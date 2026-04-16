from fastapi import APIRouter, HTTPException, Depends, Query
from typing import List, Dict, Any, Optional
import logging

from ..services.database_service import DatabaseService
from ..services.embedding_service import EmbeddingService
from ..services.neo4j_service import Neo4jService
from ..brain.knowledge import BrainKnowledgeRetriever, LightRagRetriever
from ..contracts import BrainQueryRequest, IKnowledgeRetriever, KnowledgeResult
from ..models.models import SemanticQuery, LightRagQueryRequest

router = APIRouter(prefix="/rag", tags=["rag"])
logger = logging.getLogger(__name__)


def get_database_service():
    return DatabaseService()


def get_neo4j_service():
    return Neo4jService()


def get_embedding_service():
    return EmbeddingService()


def get_knowledge_retriever(
    neo4j: Neo4jService = Depends(get_neo4j_service),
) -> IKnowledgeRetriever:
    return LightRagRetriever(neo4j_service=neo4j)


def get_brain_knowledge_retriever(
    neo4j: Neo4jService = Depends(get_neo4j_service),
    embedding: EmbeddingService = Depends(get_embedding_service),
) -> IKnowledgeRetriever:
    return BrainKnowledgeRetriever(neo4j_service=neo4j, embedding_service=embedding)


@router.get("/search-documents")
async def search_documents(
    query: str = Query(..., description="Search query"),
    limit: int = Query(10, description="Maximum number of results"),
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Search for documents containing the query text"""
    try:
        results = neo4j.search_similar_content(query, limit)
        return {
            "query": query,
            "results": results,
            "count": len(results)
        }
    except Exception as e:
        logger.error(f"Error searching documents: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/document-context/{document_id}")
async def get_document_context(
    document_id: int,
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Get full context for a document including all pages"""
    try:
        context = neo4j.get_document_context(document_id)
        if not context:
            raise HTTPException(status_code=404, detail="Document not found in graph")
        return context
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting document context for {document_id}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/page-content/{document_id}/{page_number}")
async def get_page_content(
    document_id: int,
    page_number: int,
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Get specific page content"""
    try:
        content = neo4j.get_page_content(document_id, page_number)
        if not content:
            raise HTTPException(status_code=404, detail="Page not found")
        return content
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting page content for document {document_id}, page {page_number}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/surrounding-pages/{document_id}/{page_number}")
async def get_surrounding_pages(
    document_id: int,
    page_number: int,
    context_range: int = Query(2, description="Number of pages before and after to include"),
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Get surrounding pages for context"""
    try:
        pages = neo4j.get_surrounding_pages(document_id, page_number, context_range)
        return {
            "document_id": document_id,
            "target_page": page_number,
            "context_range": context_range,
            "pages": pages
        }
    except Exception as e:
        logger.error(f"Error getting surrounding pages: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/semantic-search")
async def semantic_search(
    query: SemanticQuery,
    neo4j: Neo4jService = Depends(get_neo4j_service),
    embedding: EmbeddingService = Depends(get_embedding_service),
):
    """Perform semantic search across documents using vector similarity when available."""
    try:
        query_embedding = embedding.embed_text(query.query) if embedding.is_available() else None

        if query_embedding is not None:
            results = neo4j.search_pages_vector(
                query_embedding, query.limit, query.similarity_threshold,
            )
        else:
            results = neo4j.search_similar_content(query.query, query.limit)

        if query.document_ids:
            results = [r for r in results if r["document_id"] in query.document_ids]

        return {
            "query": query.query,
            "similarity_threshold": query.similarity_threshold,
            "document_ids": query.document_ids,
            "results": results,
            "count": len(results),
            "search_mode": "vector" if query_embedding is not None else "text",
        }
    except Exception as e:
        logger.error(f"Error performing semantic search: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/lightrag-query", response_model=KnowledgeResult)
async def lightrag_query(
    query: LightRagQueryRequest,
    retriever: IKnowledgeRetriever = Depends(get_knowledge_retriever),
):
    """Query LightRAG through the contract-shaped retrieval seam."""
    try:
        return await retriever.retrieve(
            query.query,
            tenant_id=query.tenant_id,
            correlation_id=query.correlation_id,
            limit=max(query.top_k, query.chunk_top_k, 1),
            mode=query.mode,
            top_k=query.top_k,
            chunk_top_k=query.chunk_top_k,
            include_references=query.include_references,
            include_chunk_content=query.include_chunk_content,
        )
    except ValueError as e:
        logger.warning(f"Invalid LightRAG query request: {e}")
        raise HTTPException(status_code=400, detail=str(e))
    except RuntimeError as e:
        logger.error(f"LightRAG query failed: {e}")
        raise HTTPException(status_code=502, detail=str(e))
    except Exception as e:
        logger.error(f"Unexpected LightRAG query failure: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/query", response_model=KnowledgeResult)
async def query_knowledge(
    request: BrainQueryRequest,
    retriever: IKnowledgeRetriever = Depends(get_brain_knowledge_retriever),
):
    """Query the Python knowledge layer through a single contract-shaped route."""
    try:
        return await retriever.retrieve(
            request.query,
            tenant_id=request.tenant_id,
            correlation_id=request.correlation_id,
            limit=request.top_k,
            top_k=request.top_k,
            chunk_top_k=request.top_k,
            include_references=True,
            include_chunk_content=True,
        )
    except ValueError as e:
        logger.warning(f"Invalid knowledge query request: {e}")
        raise HTTPException(status_code=400, detail=str(e))
    except RuntimeError as e:
        logger.error(f"Knowledge query failed: {e}")
        raise HTTPException(status_code=502, detail=str(e))
    except Exception as e:
        logger.error(f"Unexpected knowledge query failure: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/health")
async def rag_health_check(
    db: DatabaseService = Depends(get_database_service),
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Check health of RAG services"""
    try:
        # Check database
        db_healthy = True
        try:
            db.list_documents()
        except Exception:
            db_healthy = False
        
        # Check Neo4j
        neo4j_healthy = neo4j.health_check()
        
        return {
            "database": "healthy" if db_healthy else "unhealthy",
            "neo4j": "healthy" if neo4j_healthy else "unhealthy",
            "overall": "healthy" if (db_healthy and neo4j_healthy) else "unhealthy"
        }
    except Exception as e:
        logger.error(f"Error in health check: {e}")
        return {
            "database": "unknown",
            "neo4j": "unknown",
            "overall": "unhealthy",
            "error": str(e)
        }
