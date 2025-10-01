from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import StreamingResponse
import logging
import os

from .routers import documents, processing, rag

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(
    title="AspireAI Document Processing Service",
    description="FastAPI application for document processing using docling and Neo4j for RAG functionality.",
    summary="Document Processing and RAG Service",
    version="1.0.0"
)

# Include routers
app.include_router(documents.router)
app.include_router(processing.router)
app.include_router(rag.router)

@app.get("/")
def read_root():
    return {
        "service": "AspireAI Document Processing Service",
        "status": "Alive",
        "version": "1.0.0",
        "endpoints": [
            "/docs - API Documentation",
            "/documents - Document management",
            "/processing - Document processing",
            "/rag - RAG functionality",
            "/processing/service-info - Service capabilities"
        ]
    }

@app.get("/health")
def health_check():
    """Health check endpoint"""
    try:
        # Basic health check - can be enhanced to check services
        data_path = "/app/data"
        db_path = "/app/database"
        
        # Check service capabilities
        try:
            from .services.service_factory import get_service_info
            service_info = get_service_info()
        except Exception:
            service_info = {"error": "Could not determine service capabilities"}
        
        # Test database connection
        db_status = "unknown"
        try:
            from .services.database_service import DatabaseService
            db_service = DatabaseService()
            db_health = db_service.health_check()
            db_status = db_health.get("status", "unknown")
        except Exception as e:
            logger.warning(f"Database health check failed: {e}")
            db_status = f"unhealthy: {str(e)}"
        
        health_status = {
            "status": "healthy",
            "data_path_exists": os.path.exists(data_path),
            "database_path_exists": os.path.exists(db_path),
            "database_status": db_status,
            "service_info": service_info,
            "environment": {
                "NEO4J_URI": os.getenv("NEO4J_URI", "not_set"),
                "NEO4J_USER": os.getenv("NEO4J_USER", "not_set"),
            }
        }
        
        return health_status
    except Exception as e:
        logger.error(f"Health check error: {e}")
        return {"status": "unhealthy", "error": str(e)}

@app.exception_handler(Exception)
async def global_exception_handler(request: Request, exc: Exception):
    logger.error(f"Global exception: {exc}")
    return {"error": str(exc)}

@app.on_event("startup")
async def startup_event():
    """Initialize services on startup"""
    logger.info("Starting AspireAI Document Processing Service")
    
    # Ensure required directories exist with proper permissions
    directories = [
        "/app/data/processed/documents",
        "/app/data/uploads",
        "/app/database",
        "/tmp/aspire_database"
    ]
    
    for directory in directories:
        try:
            os.makedirs(directory, exist_ok=True)
            # Try to set permissions if we can
            try:
                os.chmod(directory, 0o755)
            except (OSError, PermissionError):
                pass  # Ignore permission errors
            logger.debug(f"Ensured directory exists: {directory}")
        except Exception as e:
            logger.warning(f"Could not create directory {directory}: {e}")
    
    # Test database initialization
    try:
        from .services.database_service import DatabaseService
        db_service = DatabaseService()
        health = db_service.health_check()
        if health["status"] == "healthy":
            logger.info("Database service initialized successfully")
        else:
            logger.warning(f"Database service health check issues: {health}")
    except Exception as e:
        logger.error(f"Database service initialization failed: {e}")
        # Don't fail startup - let the service try to recover
    
    # Log service capabilities
    try:
        from .services.service_factory import get_service_info
        service_info = get_service_info()
        logger.info(f"Service capabilities: {service_info}")
    except Exception as e:
        logger.warning(f"Could not determine service capabilities: {e}")
    
    logger.info("Service initialization completed")

@app.on_event("shutdown")
async def shutdown_event():
    """Cleanup on shutdown"""
    logger.info("Shutting down AspireAI Document Processing Service")