import json
import os
from pathlib import Path
from typing import List, Dict, Any
from datetime import datetime

from docling.document_converter import DocumentConverter
from docling.datamodel.base_models import InputFormat
from docling.datamodel.pipeline_options import PdfPipelineOptions
from docling.backend.pypdfium2_backend import PyPdfiumDocumentBackend

from ..models.models import Document, ProcessedDocument, PageContent


class DoclingService:
    def __init__(self, data_path: str = "/app/data"):
        self.data_path = Path(data_path)
        self.processed_path = self.data_path / "processed"
        self.uploads_path = self.data_path / "uploads"
        
        # Ensure directories exist
        self.processed_path.mkdir(parents=True, exist_ok=True)
        (self.processed_path / "documents").mkdir(parents=True, exist_ok=True)
        
        # Initialize the document converter
        self.converter = DocumentConverter()

    def process_document(self, document: Document) -> tuple[ProcessedDocument, List[PageContent]]:
        """Process a document using docling and return processed document and pages"""
        try:
            # Get the full file path
            file_path = self.uploads_path / document.file_path
            
            if not file_path.exists():
                raise FileNotFoundError(f"Document file not found: {file_path}")
            
            # Convert the document
            doc_result = self.converter.convert(str(file_path))
            docling_doc = doc_result.document
            
            # Create document-specific directory
            doc_dir = self.processed_path / "documents" / str(document.id)
            doc_dir.mkdir(parents=True, exist_ok=True)
            
            # Save the full docling document
            document_json_path = doc_dir / "document.json"
            with open(document_json_path, 'w', encoding='utf-8') as f:
                json.dump(docling_doc.export_to_dict(), f, indent=2, ensure_ascii=False)
            
            # Create pages directory
            pages_dir = doc_dir / "pages"
            pages_dir.mkdir(exist_ok=True)
            
            # Extract pages and content
            pages = self._extract_pages(docling_doc, pages_dir)
            
            # Create metadata
            metadata = {
                "converter_version": "docling",
                "processing_timestamp": datetime.now().isoformat(),
                "file_size": document.file_size,
                "mime_type": document.mime_type,
                "total_pages": len(pages)
            }
            
            # Save metadata
            metadata_path = doc_dir / "metadata.json"
            with open(metadata_path, 'w', encoding='utf-8') as f:
                json.dump(metadata, f, indent=2)
            
            # Create processed document record
            processed_doc = ProcessedDocument(
                document_id=document.id,
                docling_document_path=str(document_json_path),
                total_pages=len(pages),
                processing_date=datetime.now(),
                processing_metadata=metadata
            )
            
            return processed_doc, pages
            
        except Exception as e:
            raise RuntimeError(f"Failed to process document {document.id}: {str(e)}")

    def _extract_pages(self, docling_doc, pages_dir: Path) -> List[PageContent]:
        """Extract individual pages from the docling document"""
        pages = []
        
        # Get page content from docling document
        page_items = []
        for item in docling_doc.iterate_items():
            if hasattr(item, 'page') and item.page is not None:
                page_items.append((item.page, item))
        
        # Group by page number
        pages_dict = {}
        for page_num, item in page_items:
            if page_num not in pages_dict:
                pages_dict[page_num] = []
            pages_dict[page_num].append(item)
        
        # Process each page
        for page_num in sorted(pages_dict.keys()):
            page_content = ""
            page_metadata = {
                "page_number": page_num,
                "items_count": len(pages_dict[page_num])
            }
            
            # Extract text content from page items
            for item in pages_dict[page_num]:
                if hasattr(item, 'text') and item.text:
                    page_content += item.text + "\n"
            
            # Create page content object
            page = PageContent(
                page_number=page_num,
                content=page_content.strip(),
                metadata=page_metadata
            )
            
            # Save individual page
            page_file = pages_dir / f"page_{page_num:03d}.json"
            with open(page_file, 'w', encoding='utf-8') as f:
                json.dump({
                    "page_number": page_num,
                    "content": page_content.strip(),
                    "metadata": page_metadata
                }, f, indent=2, ensure_ascii=False)
            
            pages.append(page)
        
        return pages

    def get_document_path(self, doc_id: int) -> Path:
        """Get the path to a processed document directory"""
        return self.processed_path / "documents" / str(doc_id)

    def load_processed_document(self, doc_id: int) -> Dict[str, Any]:
        """Load a processed docling document"""
        doc_path = self.get_document_path(doc_id) / "document.json"
        
        if not doc_path.exists():
            raise FileNotFoundError(f"Processed document not found: {doc_path}")
        
        with open(doc_path, 'r', encoding='utf-8') as f:
            return json.load(f)

    def load_page_content(self, doc_id: int, page_number: int) -> PageContent:
        """Load specific page content"""
        page_path = self.get_document_path(doc_id) / "pages" / f"page_{page_number:03d}.json"
        
        if not page_path.exists():
            raise FileNotFoundError(f"Page not found: {page_path}")
        
        with open(page_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
            return PageContent(**data)