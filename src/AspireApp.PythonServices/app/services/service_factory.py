"""
Service factory for document processing - automatically selects the right service
based on available dependencies
"""

import os
import logging

logger = logging.getLogger(__name__)

# Try to detect if full docling is available
DOCLING_AVAILABLE = False
try:
    from .docling_service import DoclingService
    DOCLING_AVAILABLE = True
    logger.info("? Full docling service available")
except ImportError as e:
    logger.info(f"??  Full docling not available: {e}")
    try:
        from .docling_service_fallback import DoclingService
        logger.info("? Using fallback docling service")
    except ImportError as fallback_error:
        logger.error(f"? No docling service available: {fallback_error}")
        raise ImportError("No document processing service available")

# Export the available service
__all__ = ['DoclingService', 'get_docling_service']

def get_docling_service():
    """Factory function to get the appropriate docling service"""
    return DoclingService()

def get_service_info():
    """Get information about which service is being used"""
    return {
        "docling_available": DOCLING_AVAILABLE,
        "service_type": "full" if DOCLING_AVAILABLE else "fallback",
        "capabilities": {
            "pdf_processing": True,
            "docx_processing": True,
            "advanced_layout": DOCLING_AVAILABLE,
            "table_extraction": DOCLING_AVAILABLE,
            "image_processing": DOCLING_AVAILABLE
        }
    }