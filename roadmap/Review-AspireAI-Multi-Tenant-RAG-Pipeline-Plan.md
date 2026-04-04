# AspireAI Multi-Tenant RAG Pipeline Plan

We will build on the existing AspireAI solution by incrementally adding robust document ingestion, retrieval, and multi-tenant features.

## 1. Inventory Repo & Docling Outputs (Ingestion Mapping)

- Audit the codebase and data sources to list supported formats such as PDFs, DOCX, and HTML, and to understand how Docling is invoked.
- Confirm where user books, websites, and transcripts will enter the pipeline.
- Inspect Docling outputs: Docling parses each document into a DoclingDocument, a structured JSON/Pydantic format that captures hierarchy, layout, and metadata.
- Generate sample Docling JSON for a typical PDF to see how headings and paragraphs appear.
- Define ingestion requirements: include main text, captions, and tables; likely exclude headers and footers.
- Record metadata such as page number and document ID for later filtering.
- Capture section titles so chunks retain semantic context.

## 2. Semantic Chunking & Document Graph for PDFs

- Use hierarchical chunking instead of fixed token counts.
- Treat each section or subsection as a parent node and split it into smaller child chunks based on length limits.
- Configure chunk size around 1000 tokens with overlap for continuity.
- Attach metadata to each chunk, including page number, section title, and document ID.
- Build a parent-child graph in LightRAG by linking child chunks to parent section or page nodes.
- Use the graph for two-level retrieval and for answers that need broader context.

## 3. Harrier Embeddings & LightRAG Indexing

- Use Microsoft Harrier-OSS-v1 embedding models.
- Start with the 270M or 0.6B model for consumer hardware.
- Encode document chunks with Harrier and verify vectors are L2-normalized.
- Configure LightRAG with the embedding function and a vector store such as Chroma or Qdrant.
- Ensure the same Harrier model and embedding dimension are used for indexing and querying.
- Add nodes and edges to LightRAG’s graph for each chunk and parent segment.

## 4. Query Service with Tenant/Group Filtering

- The query service receives chat queries together with user identity after login.
- Tag every chunk with tenant_id and group_id at ingestion.
- Filter vector search by tenant and group so users only retrieve permitted content.
- LightRAG workspace isolation can be used, but metadata filtering is more flexible.
- Retrieve the top relevant chunks and feed them into the Gemma4 prompt.
- Log queries and returned documents for audit if needed.
- Return a no-answer response when a user has no accessible documents.

## 5. Gemma4 Chat UI with Login & Access Control

- Secure the chat interface with authentication such as OpenID Connect via Azure AD or Microsoft Entra ID.
- Map users to groups using Azure AD groups, ASP.NET Identity, or a local database.
- Include tenant and group claims in API calls.
- Enforce authorization on the backend so tampered or missing tokens are rejected.
- Configure the chat LLM to use Google’s Gemma4 model locally or through Ollama.
- Connect the UI to a Chat API that performs retrieval and then calls Gemma4.

## Implementation Notes

- The plan is to be documented in plan.md and implemented incrementally.
- Core tools referenced include Docling, LightRAG, Semantic Kernel, Gemma4, and Harrier. Note: Harrier may not be usable on my consumer hardware, so we default to bge-m3. Please do advise if there is a better embedding model for use on an 8Gig nVidia cuda card.
- The overall goal is a multi-tenant RAG system with ingestion, semantic chunking, vector search, graph support, and access control.

