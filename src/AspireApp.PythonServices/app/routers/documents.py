from fastapi import APIRouter, HTTPException, Depends
from typing import List, Optional, Dict, Any
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
        return db.get_all_documents()
    except Exception as e:
        logger.error(f"Error listing documents: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/unprocessed", response_model=List[Document])
async def list_unprocessed_documents(db: DatabaseService = Depends(get_database_service)):
    """Get all unprocessed documents"""
    try:
        return db.get_unprocessed_documents()
    except Exception as e:
        logger.error(f"Error listing unprocessed documents: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/status/{status}", response_model=List[Document])
async def list_documents_by_status(status: str, db: DatabaseService = Depends(get_database_service)):
    """Get documents by processing status"""
    try:
        return db.get_documents_by_status(status)
    except Exception as e:
        logger.error(f"Error listing documents by status {status}: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/{document_id}", response_model=Document)
async def get_document(document_id: int, db: DatabaseService = Depends(get_database_service)):
    """Get a specific document"""
    try:
        document = db.get_document(document_id)
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


@router.get("/health/concurrent-access")
async def concurrent_access_status(db: DatabaseService = Depends(get_database_service)):
    """Get information about concurrent database access"""
    try:
        health_info = db.health_check()
        stats = db.get_statistics()
        active_services = db.get_active_services()
        
        concurrent_access_info = {
            "database_status": health_info.get("status", "unknown"),
            "response_time_ms": health_info.get("response_time_ms", 0),
            "journal_mode": health_info.get("journal_mode", "unknown"),
            "active_services_count": len(active_services),
            "active_services": active_services,
            "performance_stats": {
                "queries_executed": stats.get("queries_executed", 0),
                "transactions_committed": stats.get("transactions_committed", 0),
                "retries_performed": stats.get("retries_performed", 0),
                "lock_timeouts": stats.get("lock_timeouts", 0),
                "connection_pool_size": stats.get("connection_pool_size", 0),
                "pool_utilization": round(
                    stats.get("connection_pool_size", 0) / max(stats.get("max_pool_size", 1), 1) * 100, 1
                )
            },
            "concurrent_access_health": "good" if stats.get("lock_timeouts", 0) == 0 else "degraded",
            "recommendations": []
        }
        
        # Add recommendations based on performance
        if stats.get("lock_timeouts", 0) > 0:
            concurrent_access_info["recommendations"].append(
                "Lock timeouts detected - consider optimizing transaction duration"
            )
        
        if stats.get("retries_performed", 0) > stats.get("queries_executed", 1) * 0.1:
            concurrent_access_info["recommendations"].append(
                "High retry rate detected - consider checking disk I/O performance"
            )
        
        pool_utilization = concurrent_access_info["performance_stats"]["pool_utilization"]
        if pool_utilization > 80:
            concurrent_access_info["recommendations"].append(
                "High connection pool utilization - consider increasing pool size"
            )
        
        return concurrent_access_info
        
    except Exception as e:
        logger.error(f"Concurrent access status error: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/health/schema-sync")
async def schema_sync_status(db: DatabaseService = Depends(get_database_service)):
    """Get synchronization status between Files and documents tables"""
    try:
        sync_status = db.get_file_document_sync_status()
        
        # Return appropriate HTTP status based on sync health
        if sync_status.get("sync_health") == "healthy":
            return sync_status
        elif sync_status.get("sync_health") == "needs_sync":
            # Return 202 Accepted to indicate action may be needed
            return sync_status
        else:
            # Return 503 for errors
            raise HTTPException(status_code=503, detail=sync_status)
            
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Schema sync status error: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/admin/force-sync")
async def force_schema_sync(db: DatabaseService = Depends(get_database_service)):
    """Force synchronization between Files and documents tables (Admin operation)"""
    try:
        sync_result = db.force_sync_files_and_documents()
        
        if sync_result.get("sync_performed"):
            return {
                "message": "Schema synchronization completed successfully",
                "sync_result": sync_result
            }
        else:
            raise HTTPException(
                status_code=500, 
                detail={
                    "message": "Schema synchronization failed",
                    "error": sync_result.get("error", "Unknown error")
                }
            )
            
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Force sync error: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/stats/performance")
async def get_performance_statistics(db: DatabaseService = Depends(get_database_service)):
    """Get detailed performance statistics for monitoring"""
    try:
        stats = db.get_statistics()
        health = db.health_check()
        sync_status = db.get_file_document_sync_status()
        
        performance_stats = {
            "timestamp": health.get("last_health_check", "unknown"),
            "database_path": health.get("database_path", "unknown"),
            "response_metrics": {
                "current_response_time_ms": health.get("response_time_ms", 0),
                "database_size_bytes": None,  # Could be added if needed
            },
            "operation_metrics": {
                "total_queries": stats.get("queries_executed", 0),
                "total_transactions": stats.get("transactions_committed", 0),
                "retry_count": stats.get("retries_performed", 0),
                "timeout_count": stats.get("lock_timeouts", 0),
                "success_rate": round(
                    (stats.get("queries_executed", 0) - stats.get("retries_performed", 0)) / 
                    max(stats.get("queries_executed", 1), 1) * 100, 2
                )
            },
            "connection_metrics": {
                "pool_size": stats.get("connection_pool_size", 0),
                "max_pool_size": stats.get("max_pool_size", 0),
                "queue_size": stats.get("pool_queue_size", 0),
                "utilization_percent": round(
                    stats.get("connection_pool_size", 0) / max(stats.get("max_pool_size", 1), 1) * 100, 1
                )
            },
            "database_config": {
                "journal_mode": health.get("journal_mode", "unknown"),
                "busy_timeout": health.get("busy_timeout", 0),
                "document_count": health.get("document_count", 0)
            },
            "schema_sync": {
                "files_count": sync_status.get("files_count", 0),
                "documents_count": sync_status.get("documents_count", 0),
                "sync_health": sync_status.get("sync_health", "unknown"),
                "unsynced_files": sync_status.get("unsynced_files", 0),
                "unsynced_documents": sync_status.get("unsynced_documents", 0)
            }
        }
        
        return performance_stats
        
    except Exception as e:
        logger.error(f"Performance statistics error: {e}")
        raise HTTPException(status_code=500, detail=str(e))