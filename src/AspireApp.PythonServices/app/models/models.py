from pydantic import BaseModel
from datetime import datetime
from typing import Optional, List, Dict, Any


class Document(BaseModel):
    id: int
    filename: str
    original_filename: str
    file_path: str
    file_size: Optional[int] = None
    mime_type: Optional[str] = None
    upload_date: datetime
    processed: bool = False
    processing_status: str = "pending"


class ProcessedDocument(BaseModel):
    id: Optional[int] = None
    document_id: int
    docling_document_path: str
    total_pages: int
    processing_date: datetime
    processing_metadata: Optional[Dict[str, Any]] = None
    neo4j_node_id: Optional[str] = None


class DocumentPage(BaseModel):
    id: Optional[int] = None
    processed_document_id: int
    page_number: int
    content: str
    page_metadata: Optional[Dict[str, Any]] = None
    neo4j_node_id: Optional[str] = None


class PageContent(BaseModel):
    page_number: int
    content: str
    metadata: Optional[Dict[str, Any]] = None


class ProcessingStatus(BaseModel):
    document_id: int
    status: str
    total_pages: Optional[int] = None
    processed_pages: Optional[int] = None
    error_message: Optional[str] = None
    started_at: Optional[datetime] = None
    completed_at: Optional[datetime] = None


class SemanticQuery(BaseModel):
    query: str
    document_ids: Optional[List[int]] = None
    limit: int = 10
    similarity_threshold: float = 0.7