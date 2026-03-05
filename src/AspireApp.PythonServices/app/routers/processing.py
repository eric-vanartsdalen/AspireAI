from fastapi import APIRouter, HTTPException, Depends, BackgroundTasks
from typing import List
import logging
from datetime import datetime

from ..services.database_service import DatabaseService
from ..services.service_factory import get_docling_service
from ..services.neo4j_service import Neo4jService
from ..models.models import Document, ProcessedDocument, ProcessingStatus

router = APIRouter(prefix="/processing", tags=["processing"])
logger = logging.getLogger(__name__)


def get_database_service():
    return DatabaseService()


def get_neo4j_service():
    return Neo4jService()


async def process_document_task(
    document_id: int,
    db: DatabaseService,
    docling,  # Don't type hint since it could be either service
    neo4j: Neo4jService
):
    """Background task to process a document"""
    try:
        logger.info(f"Starting processing for document {document_id}")
        
        # Update status to processing
        db.update_processing_status(document_id, "processing")
        
        # Get document
        document = db.get_document(document_id)
        if not document:
            raise Exception("Document not found")
        
        # Process with docling (full or fallback)
        processed_doc, pages = docling.process_document(document)
        
        # Save processed document to database
        processed_doc_id = db.save_processed_document(processed_doc)
        
        # Create Neo4j nodes
        try:
            # Create document node
            doc_node_id = neo4j.create_document_node(document)
            
            # Create page nodes
            page_node_ids = neo4j.create_page_nodes(pages, doc_node_id, document.id)
            
            # Create relationships
            neo4j.create_relationships(doc_node_id, page_node_ids)
            neo4j.create_sequential_relationships(page_node_ids)
            
            # Update processed document with Neo4j node ID
            processed_doc.neo4j_node_id = doc_node_id
            
        except Exception as neo4j_error:
            logger.warning(f"Neo4j processing failed for document {document_id}: {neo4j_error}")
            # Continue without Neo4j - the document is still processed
        
        # Save individual pages
        for i, page in enumerate(pages):
            db.save_document_page(
                file_id=document_id,
                page_number=page.page_number,
                content=page.content,
                metadata=page.metadata,
                neo4j_node_id=page_node_ids[i] if 'page_node_ids' in locals() else None
            )
        
        # Update status to completed
        db.update_processing_status(document_id, "completed")
        
        logger.info(f"Completed processing for document {document_id} with {len(pages)} pages")
        
    except Exception as e:
        logger.error(f"Error processing document {document_id}: {e}")
        db.update_processing_status(document_id, f"error: {str(e)}")
        raise


@router.post("/process-document/{document_id}")
async def process_document(
    document_id: int,
    background_tasks: BackgroundTasks,
    db: DatabaseService = Depends(get_database_service),
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Start processing a specific document"""
    try:
        # Check if document exists
        document = db.get_document(document_id)
        if not document:
            raise HTTPException(status_code=404, detail="Document not found")
        
        # Check if already processed
        if document.processed:
            raise HTTPException(status_code=400, detail="Document already processed")
        
        # Get the appropriate docling service
        docling = get_docling_service()
        
        # Start background processing
        background_tasks.add_task(
            process_document_task,
            document_id,
            db,
            docling,
            neo4j
        )
        
        return {"message": f"Processing started for document {document_id}"}
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error starting processing for document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/process-all")
async def process_all_documents(
    background_tasks: BackgroundTasks,
    db: DatabaseService = Depends(get_database_service),
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Start processing all unprocessed documents"""
    try:
        unprocessed_docs = db.get_unprocessed_documents()
        
        if not unprocessed_docs:
            return {"message": "No unprocessed documents found"}
        
        # Get the appropriate docling service
        docling = get_docling_service()
        
        # Start processing for each document
        for doc in unprocessed_docs:
            background_tasks.add_task(
                process_document_task,
                doc.id,
                db,
                docling,
                neo4j
            )
        
        return {
            "message": f"Started processing {len(unprocessed_docs)} documents",
            "document_ids": [doc.id for doc in unprocessed_docs]
        }
        
    except Exception as e:
        logger.error(f"Error starting batch processing: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/status/{document_id}")
async def get_processing_status(
    document_id: int,
    db: DatabaseService = Depends(get_database_service)
):
    """Get processing status of a specific document"""
    try:
        document = db.get_document(document_id)
        if not document:
            raise HTTPException(status_code=404, detail="Document not found")
        
        processed_doc = db.get_processed_document(document_id)
        
        status = ProcessingStatus(
            document_id=document_id,
            status=document.processing_status,
            total_pages=processed_doc.total_pages if processed_doc else None,
            started_at=document.upload_date,
            completed_at=processed_doc.processing_date if processed_doc else None
        )
        
        return status
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting processing status for document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/processed-documents")
async def list_processed_documents(db: DatabaseService = Depends(get_database_service)):
    """Get all processed documents"""
    try:
        all_docs = db.get_all_documents()
        processed_docs = [doc for doc in all_docs if doc.processed]
        
        result = []
        for doc in processed_docs:
            processed_doc = db.get_processed_document(doc.id)
            result.append({
                "document": doc,
                "processed_info": processed_doc
            })
        
        return result
        
    except Exception as e:
        logger.error(f"Error listing processed documents: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/service-info")
async def get_service_info():
    """Get information about the document processing service being used"""
    try:
        from ..services.service_factory import get_service_info
        return get_service_info()
    except Exception as e:
        logger.error(f"Error getting service info: {e}")
        raise HTTPException(status_code=500, detail=str(e))