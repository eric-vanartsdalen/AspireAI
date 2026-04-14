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
    file_id: int
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


class ProcessingStartResponse(BaseModel):
    message: str


class BatchProcessingStartResponse(BaseModel):
    message: str
    document_ids: List[int] = []


class DocumentCleanupResponse(BaseModel):
    message: str
    lightrag_doc_ids: List[str] = []
    removed_paths: List[str] = []


class SemanticQuery(BaseModel):
    query: str
    document_ids: Optional[List[int]] = None
    limit: int = 10
    similarity_threshold: float = 0.7


class LightRagQueryRequest(BaseModel):
    query: str
    mode: str = "mix"
    top_k: int = 10
    chunk_top_k: int = 10
    include_references: bool = True
    include_chunk_content: bool = True


class LightRagQueryResponse(BaseModel):
    status: str
    message: str
    data: Dict[str, Any]
    metadata: Dict[str, Any]
