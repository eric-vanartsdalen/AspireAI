# AspireAI Database Management

## Table of Contents
1. [Overview](#overview)
2. [Architecture & Design](#architecture--design)

    2.1 [Datasource Database (SQLite)](#datasource-database-sqlite)
    
    2.2 [Graph Database (Neo4j)](#graph-database-neo4j)
3. [Database Schema](#database-schema)
4. [Setup & Configuration](#setup--configuration)
5. [Workflow](#workflow)
6. [Neo4j Integration](#neo4j-integration)
7. [RAG Workflow](#rag-workflow)
8. [Migration & Legacy](#migration--legacy)
9. [Troubleshooting](#troubleshooting)
10. [Future Plans](#future-plans)

---

# Overview

AspireAI uses a unified database architecture to manage uploaded files, locator references, document processing, and graph-based retrieval. Uploaded files and resources are managed for eventual use by the UI Chat function and external API calls. The system consists of:
- **SQLite**: Tracks file and data locator metadata, processing status, and extracted referencable page content.
- **Neo4j**: Stores relationships between document contents and pages for advanced retrieval and chat functions.

This document is the single source of truth for all database details, schema, setup, and workflows. As the system evolves, extensibility and future-proofing are prioritized.

---

# Architecture & Design

## Datasource Database (SQLite)

The data sources database (SQLite) manages file and data location metadata through document or other resource extraction. It handles the processing of document sources (files, URIs, or other types), including data extraction, cataloging and referencing for ingestion into the graph database.

## Graph Database (Neo4j)

The graph database (Neo4j) ingests, stores and manages relationships within the data so that this content may be queried for a high-quality of relavent selections. It provides a query interfaces for referenceable output to consuming applications like UI chat, agentic tool calls, and other external systems.

Note: The design should allows for extensible patterns and not necessarily be tied to a particular provider or technology, ensuring flexibility and future-proofing.

---

# Data Source Database Section

This section describes the workflow for managing and processing data sources (files, web pages, APIs, etc.) in AspireAI.

- **Location & Metadata:** Each datasource is cataloged with metadata such as name, path, type, and origin.
- **Extraction & Processing:** Upon ingestion, data is extracted and processed according to its type (e.g., document parsing, web scraping).
- **Ingestion:** Processed data and extracted content are stored in the unified `datasources` and `datasource_pages` tables for downstream retrieval and graph integration.

Refer to the [Database Schema](#database-schema) section for detailed table definitions and field descriptions.

# Graph Database Section

AspireAI uses Neo4j to model and query relationships between datasources and their extracted content. The graph database is automatically updated to reflect changes in the datasource database, ensuring consistency and maintainability.

## Graph Ingestion Workflow

- **Addition:** When a new datasource is processed, a corresponding node is created in Neo4j, along with nodes for each extracted page or segment. Relationships (`CONTAINS`, `PRECEDES`) are established.
- **Deletion:** When a datasource is deleted from SQLite, the corresponding nodes and relationships are removed from Neo4j.
- **Update:** Changes to datasource metadata or content are propagated to Neo4j nodes.

## Graph Schema

- **Datasource Node:** Represents a data source (file, web page, API, etc.), linked to its SQLite ID.
- **Page Node:** Represents individual pages or segments, linked to their datasource and SQLite reference.
- **Relationships:**
    - `CONTAINS`: Datasource contains pages.
    - `PRECEDES`: Sequential relationship between pages.

Neo4j node IDs are stored in the SQLite tables for cross-reference and efficient querying.

## Design Considerations

- **Durability:** Graph updates are transactional and reflect the current state of the datasource database.
- **Performance:** Indexes and relationship types are optimized for retrieval and chat workflows.
- **Extensibility:** The schema supports future node types (e.g., semantic concepts) and relationships.

Refer to this section for all graph database implementation, schema, and synchronization details.

---

# Database Schema

AspireAI uses a unified, simplified schema for managing data sources, document processing, and graph-based retrieval. The schema is designed for clarity, maintainability, and future extensibility.

## SQLite Schema

### datasources
Tracks the complete lifecycle of ingested data sources (files, web pages, APIs, etc.).

```sql
CREATE TABLE datasources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_name TEXT NOT NULL,
    original_source_name TEXT NOT NULL,
    source_path TEXT NOT NULL,
    source_hash TEXT NOT NULL DEFAULT '',
    source_size INTEGER NOT NULL DEFAULT 0,
    mime_type TEXT,
    ingested_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    status TEXT NOT NULL DEFAULT 'uploaded',
    processing_started_at DATETIME,
    processing_completed_at DATETIME,
    processing_error TEXT,
    docling_document_path TEXT,
    total_pages INTEGER,
    neo4j_document_node_id TEXT,
    source_type TEXT NOT NULL DEFAULT 'upload',
    source_url TEXT
);

CREATE INDEX idx_datasources_status ON datasources(status);
CREATE INDEX idx_datasources_hash ON datasources(source_hash);
CREATE INDEX idx_datasources_ingested ON datasources(ingested_at);
CREATE INDEX idx_datasources_type ON datasources(source_type);
```

### datasource_pages
Stores page-level content extracted from any datasource.

```sql
CREATE TABLE datasource_pages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    datasource_id INTEGER NOT NULL,
    page_number INTEGER NOT NULL,
    content TEXT NOT NULL,
    page_metadata TEXT,
    neo4j_page_node_id TEXT,
    FOREIGN KEY (datasource_id) REFERENCES datasources(id) ON DELETE CASCADE,
    UNIQUE(datasource_id, page_number)
);

CREATE INDEX idx_pages_datasource_id ON datasource_pages(datasource_id);
CREATE INDEX idx_pages_datasource_page ON datasource_pages(datasource_id, page_number);
```

#### Status Lifecycle
- `uploaded` -> `processing` -> `processed` (or `error`)

## Neo4j Graph Schema

- **Datasource Node**: Represents a data source (file, web page, API, etc.)
- **Page Node**: Represents individual pages or segments
- **Relationships**:
    - `CONTAINS`: Datasource contains pages
    - `PRECEDES`: Sequential page relationships

Neo4j nodes store with their data the associated SQLite reference for the originating document pages for cross-reference.

## Extensibility & Legacy
- Schema is designed for future extensions (e.g., new source types, additional metadata).
- Legacy tables and bridge logic have been removed; backward compatibility is maintained via unified schema.

Refer to this section for all schema-related implementation and migration details.

---

# Setup & Configuration

This section describes how to configure and initialize the AspireAI database system for local development and deployment.

## SQLite Datasource Database

- **Database Path:** `../database/data-resources.db` (relative to the Web project)
- **Automatic Initialization:** On startup, the application creates the database file and required tables if they do not exist.
- **Data Directory:** Uploaded files and resources are stored in `../data`.
- **Connection String:**
  Configure in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../database/data-resources.db"
  }
}
```
- **File Naming:** Unique filenames are generated to prevent conflicts.
- **Supported Types:** PDF, DOCX, TXT, MD (configurable in `appsettings.json`).

## Neo4j Graph Database

- **Directory Structure:**
```
database/neo4j/
  data/     # Neo4j database files
  logs/     # Neo4j log files
  config/   # Neo4j configuration files
  plugins/  # Neo4j plugins
```
- **Access URLs:**
  - Neo4j Browser: [http://localhost:7474](http://localhost:7474)
  - Bolt Protocol: `bolt://localhost:7687`
- **Default Credentials:**
  - Username: `neo4jadmin`
  - Password: `neo4j@secret`
- **Bind Mounts:** Directories are bind-mounted to the Neo4j container for persistence.

## Initialization Steps

1. Ensure Docker is running for container orchestration.
2. Run the AspireApp.AppHost project (`dotnet run --project src/AspireApp.AppHost`).
3. The application will:
   - Create the database and data directories if missing.
   - Initialize the SQLite schema.
   - Start Neo4j and other services via Aspire orchestration.
   - Test database and service connectivity on startup.

## Workflow

This section outlines the end-to-end data flow in AspireAI, from data ingestion to graph-based retrieval.

## 1. Data Ingestion
- Users upload files or specify data sources (e.g., web pages, APIs) via the Blazor UI.
- Metadata is cataloged in the `datasources` table; files/resources are stored in the data directory.

## 2. Processing & Extraction
- The Python service detects new entries with `status='uploaded'` in the `datasources` table.
- Data is extracted and processed according to its type (e.g., document parsing, web scraping).
- Processing status is updated (`processing`, `processed`, or `error`).

## 3. Page-Level Storage
- Extracted content is saved in the `datasource_pages` table, linked to the originating datasource.
- Each page/segment includes content, metadata, and cross-references to Neo4j nodes.

## 4. Graph Database Update
- When processing completes, Neo4j nodes are created for the datasource and its pages.
- Relationships (`CONTAINS`, `PRECEDES`) are established in Neo4j.
- Deletions or updates in SQLite are propagated to Neo4j for consistency.

## 5. Retrieval & Query
- The UI and API services query both SQLite and Neo4j for document/page retrieval, semantic search, and chat context.
- RAG (Retrieval Augmented Generation) workflows use graph relationships for advanced context selection.

## Status Lifecycle
- `uploaded` ? `processing` ? `processed` (or `error`)

Refer to this section for a high-level overview of how data moves through AspireAI from upload to retrieval.

# Migration & Legacy

AspireAI uses a unified, extensible schema for all new deployments. For most development scenarios, the database will be created fresh and no migration is required.

## Migration Strategy (Future)

- **Schema Evolution:** Future changes to the schema (e.g., new columns, tables, relationships) will use migration scripts or Entity Framework migrations.
- **Legacy Support:** If legacy tables or bridge logic exist from earlier versions, migration scripts will convert data to the unified `datasources` and `datasource_pages` schema.
- **Deprecation:** Deprecated tables and sync logic will be removed in future releases. Backward compatibility will be maintained via migration scripts and transitional code.

## Recommendations

- Always back up the database before applying migrations.
- Use versioned migration scripts for production upgrades.
- Document all schema changes and migration steps in this section.

Refer to this section for future migration plans and legacy compatibility notes.

---

# Graph RAG Workflow (Neo4j)

This section describes how AspireAI leverages Neo4j for Retrieval Augmented Generation (RAG) workflows, enabling advanced document retrieval and chat context.

## RAG Pipeline Overview

- **Document/Data Ingestion:** Datasources (files, web pages, APIs) are uploaded and cataloged in SQLite.
- **Processing:** Content is extracted and segmented into pages, stored in `datasource_pages`.
- **Graph Ingestion:** Neo4j nodes are created for each datasource and page, with relationships (`CONTAINS`, `PRECEDES`) established.
- **Semantic Retrieval:** Queries traverse the graph to find relevant documents/pages using relationships and metadata.
- **Context Assembly:** Retrieved content is used to assemble context for chat, agentic tools, or API responses.

## RAG Query Strategies

- **Keyword Search:** Find datasources/pages by metadata or content.
- **Graph Traversal:** Use Neo4j relationships to follow document structure, semantic links, or page order.
- **Hybrid Retrieval:** Combine SQLite filtering with graph-based semantic search for optimal results.

## Integration Points

- **Blazor UI:** Users initiate uploads and queries; results are assembled from both SQLite and Neo4j.
- **Python Service:** Handles extraction, graph updates, and semantic search logic.
- **API Endpoints:** Expose RAG search and retrieval functions for external applications.

## Extensibility

- Future support for vector embeddings, semantic node types, and advanced relationship modeling.
- Designed for easy integration with LLMs and agentic workflows.

Refer to this section for all RAG-related graph workflows and retrieval strategies.

---

# Troubleshooting

This section provides solutions to common issues encountered during setup, development, and operation of AspireAI's database system.

## Common Issues & Solutions

- **SQLite Error 14: 'unable to open database file'**
  - Ensure the database directory exists and the application has write permissions.
  - Verify the connection string path in `appsettings.json`.
  - Check application startup logs for initialization messages.

- **Neo4j Connection Issues**
  - Ensure Docker is running and the Neo4j container is healthy.
  - Check that the Neo4j directories are bind-mounted correctly.
  - Verify access URLs: [http://localhost:7474](http://localhost:7474), `bolt://localhost:7687`.

- **File Upload Issues**
  - Check file size and type restrictions in `appsettings.json`.
  - Ensure the data directory exists and is writable.
  - Confirm supported file types are configured.

- **Service Health Checks**
  - Use Aspire Dashboard to verify all services are running and healthy.
  - Check logs for errors in Blazor, Python, or Neo4j services.

- **SDK Version Mismatch**
  - Run `dotnet --info` to verify .NET 10 SDK is installed.
  - Check `global.json` for required SDK version.

- **Python Service Build Failures**
  - Verify dependencies in `src/AspireApp.PythonServices/requirements.txt`.
  - Ensure Docker BuildKit is enabled.

## Diagnostic Tools

- **Aspire Dashboard:** View service status, logs, and health endpoints.
- **Database Health Endpoints:** Use API endpoints to check database and service health.
- **Manual Database Checks:**
  - Use `sqlite3` CLI to inspect tables and schema.
  - Use Neo4j Browser for graph inspection and queries.

Refer to this section for troubleshooting and diagnostic guidance.

---

# Future Plans

This section outlines the envisioned future developments and enhancements for the AspireAI database system.

## Immediate Next Steps
- Consolidate all Neo4j interaction in a dedicated repository layer for better separation of concerns.
- Implement comprehensive error handling and retry mechanisms for transient failures.
- Optimize blob storage integration for handling large files and binaries.

## Upcoming Features
- Direct integration of semantic kernel capabilities for kernel-based tasks and actions.
- Support for vector embeddings and semantic search in graph queries.
- Advanced relationship modeling in Neo4j to capture complex data interactions.

## Enhancements
- Improve monitoring, alerting, and diagnostics for database and service health.
- Enable user-defined functions and procedures in Neo4j for custom processing logic.
- Expand supported file types and data sources for ingestion.

## Considerations
- Assess and evolve the schema design to accommodate new features and optimizations.
- Plan for potential data migrations or transformations needed for new capabilities.
- Keep abreast of advancements in database technologies and integration patterns.

Refer to this section for all planned future developments and enhancements for AspireAI.

