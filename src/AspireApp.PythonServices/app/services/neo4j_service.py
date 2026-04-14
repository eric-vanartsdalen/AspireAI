import os
from typing import List, Dict, Any, Optional
from neo4j import GraphDatabase, Driver
import json

from ..contracts import CanonicalDocument
from ..models.models import Document, DocumentPage, PageContent


class Neo4jService:
    def __init__(self, uri: str = None, user: str = None, password: str = None):
        # Get connection details from environment or use defaults
        self.uri = uri or os.getenv("NEO4J_URI", "bolt://localhost:7687")
        self.user = user or os.getenv("NEO4J_USER", "neo4j")
        self.password = password or os.getenv("NEO4J_PASSWORD", "neo4j@secret")
        
        self._driver: Optional[Driver] = None

    def get_driver(self) -> Driver:
        """Get or create Neo4j driver"""
        if self._driver is None:
            self._driver = GraphDatabase.driver(self.uri, auth=(self.user, self.password))
            self._ensure_constraints()
        return self._driver

    def close(self):
        """Close the Neo4j driver"""
        if self._driver:
            self._driver.close()

    def _ensure_constraints(self):
        """Ensure required constraints exist"""
        try:
            with self.get_driver().session() as session:
                # Create constraints for unique IDs
                constraints = [
                    "CREATE CONSTRAINT doc_id IF NOT EXISTS FOR (d:Document) REQUIRE d.id IS UNIQUE",
                    "CREATE CONSTRAINT page_id IF NOT EXISTS FOR (p:Page) REQUIRE p.id IS UNIQUE",
                    "CREATE CONSTRAINT chunk_id IF NOT EXISTS FOR (c:Chunk) REQUIRE c.id IS UNIQUE"
                ]
                
                for constraint in constraints:
                    try:
                        session.run(constraint)
                    except Exception as e:
                        # Constraint might already exist
                        print(f"Constraint creation info: {e}")
        except Exception as e:
            print(f"Warning: Could not create Neo4j constraints: {e}")

    def create_document_node(self, document: Document | CanonicalDocument) -> str:
        """Create a document node in Neo4j and return the node ID"""
        payload = self._build_document_payload(document)
        with self.get_driver().session() as session:
            result = session.run("""
                CREATE (d:Document {
                    id: $id,
                    filename: $filename,
                    original_filename: $original_filename,
                    file_path: $file_path,
                    file_size: $file_size,
                    mime_type: $mime_type,
                    upload_date: $upload_date,
                    processed: $processed,
                    tenant_id: $tenant_id,
                    source_type: $source_type,
                    source_confidence: $source_confidence
                })
                RETURN elementId(d) as node_id
            """, payload)
             
            return result.single()["node_id"]

    def _build_document_payload(self, document: Document | CanonicalDocument) -> dict[str, Any]:
        if isinstance(document, CanonicalDocument):
            metadata = document.metadata or {}
            return {
                "id": document.document_id,
                "filename": metadata.get("file_name", f"document-{document.document_id}"),
                "original_filename": metadata.get("original_file_name", metadata.get("file_name", f"document-{document.document_id}")),
                "file_path": metadata.get("file_path", ""),
                "file_size": metadata.get("file_size"),
                "mime_type": metadata.get("mime_type"),
                "upload_date": metadata.get("upload_date"),
                "processed": True,
                "tenant_id": document.tenant_id,
                "source_type": document.source_type,
                "source_confidence": document.source_confidence,
            }

        return {
            "id": document.id,
            "filename": document.filename,
            "original_filename": document.original_filename,
            "file_path": document.file_path,
            "file_size": document.file_size,
            "mime_type": document.mime_type,
            "upload_date": document.upload_date.isoformat(),
            "processed": document.processed,
            "tenant_id": getattr(document, "tenant_id", "default"),
            "source_type": getattr(document, "source_type", "upload"),
            "source_confidence": getattr(document, "source_confidence", 0.7),
        }

    def create_page_nodes(self, pages: List[PageContent], doc_node_id: str, document_id: int) -> List[str]:
        """Create page nodes and return their IDs"""
        page_node_ids = []
        
        with self.get_driver().session() as session:
            for page in pages:
                result = session.run("""
                    CREATE (p:Page {
                        id: $page_id,
                        document_id: $document_id,
                        page_number: $page_number,
                        content: $content,
                        metadata: $metadata
                    })
                    RETURN elementId(p) as node_id
                """, {
                    "page_id": f"{document_id}_{page.page_number}",
                    "document_id": document_id,
                    "page_number": page.page_number,
                    "content": page.content,
                    "metadata": json.dumps(page.metadata) if page.metadata else "{}"
                })
                
                page_node_ids.append(result.single()["node_id"])
        
        return page_node_ids

    def create_relationships(self, doc_node_id: str, page_node_ids: List[str]):
        """Create relationships between document and pages"""
        with self.get_driver().session() as session:
            for page_node_id in page_node_ids:
                session.run("""
                    MATCH (d:Document), (p:Page)
                    WHERE elementId(d) = $doc_id AND elementId(p) = $page_id
                    CREATE (d)-[:CONTAINS]->(p)
                """, {
                    "doc_id": doc_node_id,
                    "page_id": page_node_id
                })

    def create_sequential_relationships(self, page_node_ids: List[str]):
        """Create PRECEDES relationships between sequential pages"""
        with self.get_driver().session() as session:
            for i in range(len(page_node_ids) - 1):
                session.run("""
                    MATCH (p1:Page), (p2:Page)
                    WHERE elementId(p1) = $page1_id AND elementId(p2) = $page2_id
                    CREATE (p1)-[:PRECEDES]->(p2)
                """, {
                    "page1_id": page_node_ids[i],
                    "page2_id": page_node_ids[i + 1]
                })

    def delete_document_graph(self, document_id: int) -> Dict[str, int]:
        """Delete a document graph and any page nodes for the document."""
        with self.get_driver().session() as session:
            document_row = session.run(
                """
                MATCH (d:Document {id: $document_id})
                RETURN count(d) AS deleted_documents
                """,
                {"document_id": document_id},
            ).single()
            session.run(
                """
                MATCH (d:Document {id: $document_id})
                DETACH DELETE d
                """,
                {"document_id": document_id},
            )
            page_row = session.run(
                """
                MATCH (p:Page {document_id: $document_id})
                RETURN count(p) AS deleted_pages
                """,
                {"document_id": document_id},
            ).single()
            session.run(
                """
                MATCH (p:Page {document_id: $document_id})
                DETACH DELETE p
                """,
                {"document_id": document_id},
            )

            return {
                "deleted_documents": document_row["deleted_documents"] if document_row else 0,
                "deleted_pages": page_row["deleted_pages"] if page_row else 0,
            }

    def search_similar_content(self, query: str, limit: int = 10) -> List[Dict[str, Any]]:
        """Search for similar content (basic text matching for now)"""
        with self.get_driver().session() as session:
            result = session.run("""
                MATCH (p:Page)
                WHERE p.content CONTAINS $query
                MATCH (p)<-[:CONTAINS]-(d:Document)
                RETURN p.content as content, 
                       p.page_number as page_number, 
                       d.filename as filename,
                       d.id as document_id,
                       d.source_confidence as source_confidence,
                       coalesce(d.source_confidence, 0.5) as confidence,
                       coalesce(d.source_confidence, 0.5) as relevance_score
                ORDER BY p.page_number
                LIMIT $limit
            """, {
                "query": query,
                "limit": limit
            })
            
            return [dict(record) for record in result]

    def get_document_context(self, document_id: int) -> Dict[str, Any]:
        """Get full context for a document including all pages"""
        with self.get_driver().session() as session:
            result = session.run("""
                MATCH (d:Document {id: $document_id})-[:CONTAINS]->(p:Page)
                RETURN d.filename as filename,
                       d.original_filename as original_filename,
                       d.upload_date as upload_date,
                       collect({
                           page_number: p.page_number,
                           content: p.content,
                           metadata: p.metadata
                       }) as pages
                ORDER BY p.page_number
            """, {
                "document_id": document_id
            })
            
            record = result.single()
            if record:
                return dict(record)
            return {}

    def get_page_content(self, document_id: int, page_number: int) -> Optional[Dict[str, Any]]:
        """Get specific page content"""
        with self.get_driver().session() as session:
            result = session.run("""
                MATCH (d:Document {id: $document_id})-[:CONTAINS]->(p:Page {page_number: $page_number})
                RETURN p.content as content,
                       p.page_number as page_number,
                       p.metadata as metadata,
                       d.filename as filename
            """, {
                "document_id": document_id,
                "page_number": page_number
            })
            
            record = result.single()
            if record:
                return dict(record)
            return None

    def get_surrounding_pages(self, document_id: int, page_number: int, context_range: int = 2) -> List[Dict[str, Any]]:
        """Get surrounding pages for context"""
        with self.get_driver().session() as session:
            result = session.run("""
                MATCH (d:Document {id: $document_id})-[:CONTAINS]->(p:Page)
                WHERE p.page_number >= $start_page AND p.page_number <= $end_page
                RETURN p.content as content,
                       p.page_number as page_number,
                       p.metadata as metadata
                ORDER BY p.page_number
            """, {
                "document_id": document_id,
                "start_page": max(1, page_number - context_range),
                "end_page": page_number + context_range
            })
            
            return [dict(record) for record in result]

    def health_check(self) -> bool:
        """Check if Neo4j connection is healthy"""
        try:
            with self.get_driver().session() as session:
                result = session.run("RETURN 1 as test")
                return result.single()["test"] == 1
        except Exception:
            return False
