import asyncio
import json
import shutil
import os
import threading
from pathlib import Path

from fastapi import APIRouter, HTTPException, Depends, BackgroundTasks
import logging

from ..brain.ingestion import build_canonical_document
from ..services.database_service import DatabaseService
from ..services.service_factory import get_docling_service
from ..services.lightrag_handoff_service import LightRagHandoffService
from ..services.neo4j_service import Neo4jService
from ..services.claim_extraction_service import ClaimExtractionService
from ..services.embedding_service import EmbeddingService
from ..services.url_content_fetcher import get_url_content_fetcher
from ..models.models import BatchProcessingStartResponse, DocumentCleanupResponse, ProcessingStartResponse, ProcessingStatus

router = APIRouter(prefix="/processing", tags=["processing"])
logger = logging.getLogger(__name__)

_youtube_transcript_queue_thread_lock = threading.Lock()
_youtube_transcript_queue_thread: threading.Thread | None = None
_youtube_transcript_queue_stop_event = threading.Event()


def get_database_service():
    return DatabaseService()


def get_neo4j_service():
    return Neo4jService()


def get_lightrag_handoff_service():
    return LightRagHandoffService()


def _is_youtube_transcript_document(document) -> bool:
    return (
        getattr(document, "source_type", None) == "youtube_video"
        and bool(getattr(document, "source_url", None))
    )


def _ensure_youtube_transcript_queue_drainer() -> None:
    global _youtube_transcript_queue_thread

    with _youtube_transcript_queue_thread_lock:
        if _youtube_transcript_queue_thread is not None and _youtube_transcript_queue_thread.is_alive():
            return

        _youtube_transcript_queue_stop_event.clear()
        _youtube_transcript_queue_thread = threading.Thread(
            target=_youtube_transcript_queue_worker_loop,
            name="youtube-transcript-queue",
            daemon=True,
        )
        _youtube_transcript_queue_thread.start()


def stop_youtube_transcript_queue_drainer() -> None:
    _youtube_transcript_queue_stop_event.set()


def _youtube_transcript_queue_worker_loop() -> None:
    logger.info("YouTube transcript queue drainer started")

    while not _youtube_transcript_queue_stop_event.is_set():
        try:
            db = DatabaseService()
            wait_seconds = db.get_youtube_transcript_queue_wait_seconds()

            if wait_seconds is None:
                logger.info("YouTube transcript queue drainer is idle")
                return

            if wait_seconds > 0:
                _youtube_transcript_queue_stop_event.wait(timeout=wait_seconds)
                continue

            queued_attempt = db.claim_next_youtube_transcript()
            if queued_attempt is None:
                _youtube_transcript_queue_stop_event.wait(timeout=5.0)
                continue

            file_id = queued_attempt["file_id"]
            document = db.get_document_by_id(file_id)
            if document is None:
                db.mark_youtube_transcript_completed(file_id)
                continue

            if document.processing_status == "processing":
                _youtube_transcript_queue_stop_event.wait(timeout=1.0)
                continue

            db.update_file_status(file_id, "processing")
            _process_document_task_sync(
                file_id,
                db,
                get_docling_service(),
                Neo4jService(),
                LightRagHandoffService(),
                mark_processing_started=False,
            )
        except Exception as exc:
            logger.error("YouTube transcript queue drainer error: %s", exc, exc_info=True)
            _youtube_transcript_queue_stop_event.wait(timeout=5.0)


async def process_document_task(
    document_id: int,
    db: DatabaseService,
    docling,  # Don't type hint since it could be either service
    neo4j: Neo4jService,
    lightrag_handoff: LightRagHandoffService | None = None,
    mark_processing_started: bool = True,
):
    await asyncio.to_thread(
        _process_document_task_sync,
        document_id,
        db,
        docling,
        neo4j,
        lightrag_handoff,
        mark_processing_started,
    )


def _normalize_processing_status(status: str | None) -> str:
    normalized = (status or "uploaded").lower()
    status_map = {
        "pending": "uploaded",
        "uploaded": "uploaded",
        "processing": "processing",
        "completed": "processed",
        "processed": "processed",
        "failed": "error",
        "error": "error",
    }
    return status_map.get(normalized, normalized)


def _collect_child_document_ids(document, content, db: DatabaseService, fetcher) -> list[int]:
    child_document_ids: list[int] = []

    for child_url in content.child_urls or []:
        try:
            child_source_type = _classify_url_source_type(fetcher, child_url)
            existing = db.find_duplicate_by_url(child_url, document.tenant_id)
            if existing:
                existing_status = _normalize_processing_status(existing.get("status"))
                existing_id = existing.get("id")
                if existing_status in {"processing", "processed"}:
                    logger.info(
                        "Skipping child URL %s because existing document %s is already %s",
                        child_url,
                        existing_id,
                        existing_status,
                    )
                    continue
                if existing_id is not None:
                    logger.info(
                        "Reusing existing child URL %s as document %s with status %s",
                        child_url,
                        existing_id,
                        existing_status,
                    )
                    child_document_ids.append(existing_id)
                    if child_source_type == "youtube_video":
                        db.enqueue_youtube_transcript(
                            file_id=existing_id,
                            source_url=child_url,
                            tenant_id=document.tenant_id,
                        )
                    continue

            child_document_id = db.add_url_datasource(
                source_name=_build_source_name_from_url(child_url),
                source_url=child_url,
                source_type=child_source_type,
                status="uploaded",
                tenant_id=document.tenant_id,
            )
            child_document_ids.append(child_document_id)
            if child_source_type == "youtube_video":
                db.enqueue_youtube_transcript(
                    file_id=child_document_id,
                    source_url=child_url,
                    tenant_id=document.tenant_id,
                )
            logger.info("Queued child URL for processing: %s (document %s)", child_url, child_document_id)
        except Exception as e:
            logger.warning(f"Failed to queue child URL {child_url}: {e}")

    return child_document_ids


def _fetch_url_content_sync(
    document,
    db: DatabaseService,
) -> tuple[Path, list[int]]:
    """
    Fetch content from a URL source and save it as a text file for processing.
    
    Returns the saved content file path plus queued child document IDs.
    """
    import asyncio
    
    data_path = os.getenv("ASPIRE_DATA_PATH", "/app/data")
    fetcher = get_url_content_fetcher(data_path)

    async def _fetch_async():
        return await fetcher.fetch(document.source_url)
    
    # Run async fetch in sync context
    content = asyncio.run(_fetch_async())
    
    # Handle child URLs (e.g., from YouTube channels)
    child_document_ids: list[int] = []
    if content.has_children:
        logger.info(f"URL {document.source_url} has {len(content.child_urls)} child URLs to ingest")
        child_document_ids = _collect_child_document_ids(document, content, db, fetcher)
        if child_document_ids:
            _ensure_youtube_transcript_queue_drainer()
    
    # Save fetched content to a text file for docling processing
    data_path = Path(os.getenv("ASPIRE_DATA_PATH", "/app/data"))
    url_content_dir = data_path / "url_content"
    url_content_dir.mkdir(parents=True, exist_ok=True)
    
    content_file = url_content_dir / f"url_{document.id}.md"
    content_text = content.text.strip()
    if content.has_children and not content_text:
        child_urls = "\n".join(f"- {child_url}" for child_url in (content.child_urls or []))
        content_text = f"Queued child URLs for processing:\n{child_urls}"

    if not content_text:
        raise RuntimeError(f"URL content extraction returned no text for {document.source_url}")
    
    # Write content with metadata header
    with open(content_file, "w", encoding="utf-8") as f:
        f.write(f"# Source: {document.source_url}\n")
        f.write(f"# Type: {content.metadata.get('source_type', content.content_type)}\n")
        if content.metadata.get("title"):
            f.write(f"# Title: {content.metadata['title']}\n")
        f.write("\n---\n\n")
        f.write(content_text)
    
    logger.info(f"Saved URL content to {content_file} ({len(content_text)} chars)")
    
    return content_file, child_document_ids


def _process_document_task_sync(
    document_id: int,
    db: DatabaseService,
    docling,  # Don't type hint since it could be either service
    neo4j: Neo4jService,
    lightrag_handoff: LightRagHandoffService | None = None,
    mark_processing_started: bool = True,
):
    """Background task to process a document"""
    child_document_ids: list[int] = []
    document = None
    is_youtube_transcript = False
    try:
        logger.info(f"Starting processing for document {document_id}")

        # Get document
        document = db.get_document_by_id(document_id)
        if not document:
            raise Exception("Document not found")
        is_youtube_transcript = _is_youtube_transcript_document(document)

        if mark_processing_started:
            db.update_file_status(document_id, "processing")

        # Handle URL sources - fetch content before docling processing
        document_source_url = getattr(document, "source_url", None)
        document_file_path = getattr(document, "file_path", None)
        if document_source_url and not document_file_path:
            resolved_file_path, child_document_ids = _fetch_url_content_sync(document, db)
        else:
            resolved_file_path = db.resolve_upload_path(document)
        
        # Process with docling (full or fallback)
        processed_doc, pages = docling.process_document(document, resolved_file_path)
        canonical_document = build_canonical_document(document, pages)
        _persist_canonical_document(processed_doc, canonical_document)

        # Create Neo4j nodes and generate embeddings BEFORE triggering LightRAG.
        # LightRAG ingestion also calls Ollama; running both concurrently saturates
        # the single Ollama instance and causes embedding timeouts that push total
        # processing time past the test polling window.
        page_node_ids = []
        try:
            # Create document node
            doc_node_id = neo4j.create_document_node(canonical_document)
            
            # Initialize embedding service for P2-C population
            embedding_service = EmbeddingService()
            embedding_available = embedding_service.is_available()
            
            # Create page nodes and populate embeddings
            page_node_ids = neo4j.create_page_nodes(canonical_document.pages, doc_node_id, document.id)
            
            # P2-C: Populate page embeddings during ingestion
            if embedding_available:
                page_texts = [page.content for page in canonical_document.pages]
                try:
                    page_embeddings = embedding_service.embed_batch(page_texts)
                except Exception as embed_error:
                    page_embeddings = None
                    logger.warning(f"Failed to generate page embeddings for document {document_id}: {embed_error}")

                if page_embeddings:
                    for i, page in enumerate(canonical_document.pages):
                        if i < len(page_node_ids) and i < len(page_embeddings):
                            page_node_id = page_node_ids[i]
                            page_embedding = page_embeddings[i]
                            if page_embedding:
                                neo4j.populate_page_embedding(page_node_id, page_embedding)
                                logger.debug(
                                    f"Populated embedding for page {page.page_number} of document {document_id}"
                                )
                else:
                    logger.warning(f"Page embedding generation returned no embeddings for document {document_id}")
            else:
                logger.info(f"Embedding service unavailable - skipping embedding population for document {document_id}")
            
            # Create relationships
            neo4j.create_relationships(doc_node_id, page_node_ids)
            neo4j.create_sequential_relationships(page_node_ids)
            
            # Extract and persist claims from pages
            claim_extractor = ClaimExtractionService()
            for i, page in enumerate(canonical_document.pages):
                if i < len(page_node_ids):
                    page_node_id = page_node_ids[i]
                    # Extract claims from page content
                    claims = claim_extractor.extract_claims(
                        content=page.content,
                        source_confidence=canonical_document.source_confidence,
                        source_type=canonical_document.source_type
                    )
                    
                    # Persist claims to Neo4j
                    if claims:
                        claim_node_ids = neo4j.create_claim_nodes(
                            claims=claims,
                            page_node_id=page_node_id,
                            document_id=document.id,
                            page_number=page.page_number
                        )
                        
                        # P2-C: Populate claim embeddings during ingestion
                        if embedding_available:
                            claim_texts = [claim.get("text", "") for claim in claims]
                            try:
                                claim_embeddings = embedding_service.embed_batch(claim_texts)
                            except Exception as embed_error:
                                claim_embeddings = None
                                logger.warning(
                                    f"Failed to generate claim embeddings for document {document_id}, page {page.page_number}: {embed_error}"
                                )

                            if claim_embeddings:
                                for j, claim_node_id in enumerate(claim_node_ids):
                                    if j < len(claim_embeddings):
                                        claim_embedding = claim_embeddings[j]
                                        if claim_embedding:
                                            neo4j.populate_claim_embedding(claim_node_id, claim_embedding)
                                            logger.debug(
                                                f"Populated embedding for claim {j} on page {page.page_number}"
                                            )
                            else:
                                logger.warning(
                                    f"Claim embedding generation returned no embeddings for document {document_id}, page {page.page_number}"
                                )
                        
                        logger.info(
                            f"Extracted and persisted {len(claims)} claims from page {page.page_number} of document {document_id}"
                        )
            
            # Update processed document with Neo4j node ID
            processed_doc.neo4j_node_id = doc_node_id
            
        except Exception as neo4j_error:
            logger.warning(f"Neo4j processing failed for document {document_id}: {neo4j_error}")
            # Continue without Neo4j - the document is still processed

        # All Ollama-dependent work is finished.  Hand off to LightRAG now so its
        # own Ollama calls (LLM entity extraction + embedding) don't compete with
        # the page/claim embedding batches above.
        _attempt_lightrag_handoff(
            document=document,
            processed_doc=processed_doc,
            lightrag_handoff=lightrag_handoff or LightRagHandoffService(),
        )
        _persist_processing_metadata(processed_doc)

        db.update_file_ingestion_metadata(
            file_id=document_id,
            tenant_id=canonical_document.tenant_id,
            source_type=canonical_document.source_type,
            source_confidence=canonical_document.source_confidence,
        )
        db.update_file_processing_results(
            file_id=document_id,
            docling_path=processed_doc.docling_document_path,
            total_pages=processed_doc.total_pages,
            neo4j_node_id=processed_doc.neo4j_node_id,
        )
        
        # Save individual pages
        for i, page in enumerate(canonical_document.pages):
            db.save_document_page(
                file_id=document_id,
                page_number=page.page_number,
                content=page.content,
                metadata=page.metadata,
                neo4j_node_id=page_node_ids[i] if i < len(page_node_ids) else None
            )
        
        # Update status to processed
        db.update_file_status(document_id, "processed")
        if is_youtube_transcript:
            db.mark_youtube_transcript_completed(document_id)
        
        logger.info(f"Completed processing for document {document_id} with {len(pages)} pages")
        
    except Exception as e:
        logger.error(f"Error processing document {document_id}: {e}")
        db.update_file_status(document_id, "error", str(e))
        if is_youtube_transcript:
            try:
                db.mark_youtube_transcript_failed(document_id, str(e))
            except Exception as queue_error:
                logger.warning(
                    "Failed to record YouTube transcript queue error for document %s: %s",
                    document_id,
                    queue_error,
                )
        raise
    finally:
        if child_document_ids:
            _ensure_youtube_transcript_queue_drainer()


def _attempt_lightrag_handoff(document, processed_doc, lightrag_handoff: LightRagHandoffService) -> None:
    metadata = getattr(processed_doc, "processing_metadata", None) or {}
    markdown_path = metadata.get("markdown_path")

    if not markdown_path:
        logger.info(
            "Skipping LightRAG handoff for document %s because no markdown export was produced",
            document.id,
        )
        return

    try:
        handoff_result = lightrag_handoff.handoff_document(document, markdown_path)
    except Exception as exc:
        logger.warning(
            "LightRAG handoff failed for document %s: %s",
            document.id,
            exc,
        )
        metadata["lightrag"] = {
            "scan_requested": False,
            "error": str(exc),
        }
    else:
        metadata["lightrag"] = handoff_result

    processed_doc.processing_metadata = metadata


def _persist_processing_metadata(processed_doc) -> None:
    metadata = getattr(processed_doc, "processing_metadata", None)
    document_path = getattr(processed_doc, "docling_document_path", None)
    if not metadata or not document_path:
        return

    metadata_path = Path(document_path).with_name("metadata.json")
    if not metadata_path.parent.exists():
        return

    metadata_path.write_text(
        json.dumps(metadata, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )


def _persist_canonical_document(processed_doc, canonical_document) -> None:
    document_path = getattr(processed_doc, "docling_document_path", None)
    if not document_path:
        return

    canonical_document_path = Path(document_path).with_name("canonical_document.json")
    if not canonical_document_path.parent.exists():
        return

    canonical_document_path.write_text(
        canonical_document.model_dump_json(indent=2),
        encoding="utf-8",
    )

    metadata = getattr(processed_doc, "processing_metadata", None) or {}
    metadata["canonical_document_path"] = str(canonical_document_path)
    metadata["tenant_id"] = canonical_document.tenant_id
    metadata["source_type"] = canonical_document.source_type
    metadata["source_confidence"] = canonical_document.source_confidence
    metadata["correlation_id"] = canonical_document.correlation_id
    processed_doc.processing_metadata = metadata


def _load_processing_metadata(docling_document_path: str | None) -> dict:
    if not docling_document_path:
        return {}

    metadata_path = Path(docling_document_path).with_name("metadata.json")
    if not metadata_path.exists():
        return {}

    try:
        return json.loads(metadata_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        logger.warning("Processing metadata at %s is not valid JSON", metadata_path)
        return {}


def _resolve_processed_document_directory(document_id: int, docling_document_path: str | None) -> Path:
    if docling_document_path:
        return Path(docling_document_path).resolve().parent

    data_root = Path(os.getenv("ASPIRE_DATA_PATH", "/app/data"))
    return data_root / "processed" / "documents" / str(document_id)


def _classify_url_source_type(fetcher, url: str) -> str:
    handler = fetcher.get_handler(url)
    if handler is None:
        return "url"

    return "url" if handler.handler_name == "webpage" else handler.handler_name


def _build_source_name_from_url(url: str) -> str:
    safe_value = url.replace("https://", "").replace("http://", "").strip("/")
    safe_value = safe_value.replace("/", "_").replace("?", "_").replace("&", "_").replace("=", "_")
    safe_value = safe_value or "url"
    return safe_value[:120]


def _delete_processed_document_directory(document_directory: Path) -> list[str]:
    if not document_directory.exists():
        return []

    shutil.rmtree(document_directory)
    return [str(document_directory)]


@router.post("/process-document/{document_id}", response_model=ProcessingStartResponse)
async def process_document(
    document_id: int,
    background_tasks: BackgroundTasks,
    db: DatabaseService = Depends(get_database_service),
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Start processing a specific document"""
    try:
        # Check if document exists
        document = db.get_document_by_id(document_id)
        if not document:
            raise HTTPException(status_code=404, detail="Document not found")
        
        # Check if already processed
        if document.processing_status == "processed":
            raise HTTPException(status_code=400, detail="Document already processed")

        if document.processing_status == "processing":
            raise HTTPException(status_code=409, detail="Document is already processing")

        if _is_youtube_transcript_document(document):
            db.enqueue_youtube_transcript(
                file_id=document_id,
                source_url=document.source_url,
                tenant_id=document.tenant_id,
            )
            _ensure_youtube_transcript_queue_drainer()
            return ProcessingStartResponse(message=f"YouTube transcript queued for document {document_id}")
         
        # Get the appropriate docling service
        docling = get_docling_service()

        # Persist the state transition before returning so polling clients can
        # immediately observe that processing has been queued.
        db.update_file_status(document_id, "processing")
        
        # Start background processing
        background_tasks.add_task(
            process_document_task,
            document_id,
            db,
            docling,
            neo4j,
            mark_processing_started=False,
        )
        
        return ProcessingStartResponse(message=f"Processing started for document {document_id}")
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error starting processing for document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/process-all", response_model=BatchProcessingStartResponse)
async def process_all_documents(
    background_tasks: BackgroundTasks,
    db: DatabaseService = Depends(get_database_service),
    neo4j: Neo4jService = Depends(get_neo4j_service)
):
    """Start processing all uploaded or retryable documents."""
    try:
        unprocessed_docs = db.list_unprocessed_documents()
        
        if not unprocessed_docs:
            return BatchProcessingStartResponse(
                message="No uploaded or failed documents are ready for processing",
                document_ids=[],
            )
        
        queued_document_ids = []
        queued_youtube_ids = []
        docling = None
         
        # Start processing for each document
        for doc in unprocessed_docs:
            if _is_youtube_transcript_document(doc):
                db.enqueue_youtube_transcript(
                    file_id=doc.id,
                    source_url=doc.source_url,
                    tenant_id=doc.tenant_id,
                )
                queued_document_ids.append(doc.id)
                queued_youtube_ids.append(doc.id)
                continue

            if docling is None:
                docling = get_docling_service()

            db.update_file_status(doc.id, "processing")
            background_tasks.add_task(
                process_document_task,
                doc.id,
                db,
                docling,
                neo4j,
                mark_processing_started=False,
            )
            queued_document_ids.append(doc.id)

        if queued_youtube_ids:
            _ensure_youtube_transcript_queue_drainer()

        queued_message_parts: list[str] = []
        immediate_count = len(queued_document_ids) - len(queued_youtube_ids)
        if immediate_count:
            queued_message_parts.append(f"Started processing {immediate_count} documents")
        if queued_youtube_ids:
            queued_message_parts.append(f"Queued {len(queued_youtube_ids)} YouTube transcripts")
         
        return BatchProcessingStartResponse(
            message="; ".join(queued_message_parts) or "No documents were queued",
            document_ids=queued_document_ids,
        )
        
    except Exception as e:
        logger.error(f"Error starting batch processing: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.delete("/cleanup-document/{document_id}", response_model=DocumentCleanupResponse)
async def cleanup_document(
    document_id: int,
    db: DatabaseService = Depends(get_database_service),
    neo4j: Neo4jService = Depends(get_neo4j_service),
    lightrag_handoff: LightRagHandoffService = Depends(get_lightrag_handoff_service),
):
    """Delete external processing artifacts before the Web frontend removes the source row."""
    try:
        document = db.get_document_by_id(document_id)
        if not document:
            raise HTTPException(status_code=404, detail="Document not found")

        if document.processing_status == "processing":
            raise HTTPException(status_code=409, detail="Document is still processing and cannot be deleted yet")

        file_record = db.get_file_by_id(document_id)
        if file_record is None:
            raise HTTPException(status_code=404, detail="Document not found")

        metadata = _load_processing_metadata(file_record.get("docling_document_path"))
        lightrag_metadata = metadata.get("lightrag", {}) if isinstance(metadata.get("lightrag"), dict) else {}
        staged_input_path = lightrag_metadata.get("staged_input_path")

        cleanup_result = lightrag_handoff.cleanup_document(
            document,
            staged_input_path=staged_input_path,
        )
        neo4j.delete_document_graph(document_id)
        removed_paths = cleanup_result.get("removed_paths", [])
        source_path = metadata.get("source_path")
        if file_record.get("source_url") and isinstance(source_path, str):
            source_path_value = Path(source_path)
            if source_path_value.exists():
                source_path_value.unlink()
                removed_paths.append(str(source_path_value))
        removed_paths.extend(
            _delete_processed_document_directory(
                _resolve_processed_document_directory(document_id, file_record.get("docling_document_path"))
            )
        )

        return DocumentCleanupResponse(
            message=f"Cleanup completed for document {document_id}",
            lightrag_doc_ids=cleanup_result.get("doc_ids", []),
            removed_paths=removed_paths,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error cleaning up document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/status/{document_id}", response_model=ProcessingStatus)
async def get_processing_status(
    document_id: int,
    db: DatabaseService = Depends(get_database_service)
):
    """Get processing status of a specific document"""
    try:
        status = db.get_processing_status(document_id)
        if not status:
            raise HTTPException(status_code=404, detail="Document not found")
        return status
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting processing status for document {document_id}: {e}")
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
