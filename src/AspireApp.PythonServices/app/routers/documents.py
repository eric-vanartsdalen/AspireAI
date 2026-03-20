from fastapi import APIRouter, HTTPException, Depends
from typing import List
import logging

from ..services.database_service import DatabaseService
from ..models.models import Document, ProcessingStatus

router = APIRouter(prefix="/documents", tags=["documents"])
logger = logging.getLogger(__name__)


def get_database_service():
    return DatabaseService()


@router.get("/", response_model=List[Document])
async def list_documents(db: DatabaseService = Depends(get_database_service)):
    """Get all documents"""
    try:
        return db.list_documents()
    except Exception as e:
        logger.error(f"Error listing documents: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/unprocessed", response_model=List[Document])
async def list_unprocessed_documents(db: DatabaseService = Depends(get_database_service)):
    """Get all unprocessed documents"""
    try:
        return db.list_unprocessed_documents()
    except Exception as e:
        logger.error(f"Error listing unprocessed documents: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/{document_id}", response_model=Document)
async def get_document(document_id: int, db: DatabaseService = Depends(get_database_service)):
    """Get a specific document"""
    try:
        document = db.get_document_by_id(document_id)
        if not document:
            raise HTTPException(status_code=404, detail="Document not found")
        return document
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/{document_id}/status", response_model=ProcessingStatus)
async def get_document_status(document_id: int, db: DatabaseService = Depends(get_database_service)):
    """Get processing status of a document"""
    try:
        status = db.get_processing_status(document_id)
        if not status:
            raise HTTPException(status_code=404, detail="Document not found")
        return status
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting document status {document_id}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/health/database")
async def database_health_check(db: DatabaseService = Depends(get_database_service)):
    """Enhanced database health check for C# service monitoring"""
    try:
        health_info = db.health_check()
        
        # Determine HTTP status code based on health
        if health_info["status"] == "healthy":
            return health_info
        else:
            # Return 503 Service Unavailable for unhealthy database
            raise HTTPException(status_code=503, detail=health_info)
            
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Database health check error: {e}")
        raise HTTPException(
            status_code=503, 
            detail={
                "status": "unhealthy", 
                "error": str(e),
                "error_type": type(e).__name__
            }
        )
