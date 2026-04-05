# 🧠 BRAIN — Language-Agnostic Programming Contract

## 1. Overview

BRAIN is a modular, distributed system composed of loosely coupled services communicating via:
- HTTP/REST (primary)
- Events (optional, async)
- Shared schema contracts (JSON)

Each layer exposes **clear service contracts** and **data contracts**.

---

## 2. Core Service Contracts

### 2.1 Ingestion Service

#### Responsibility
Ingest, normalize, and enrich raw data into canonical format.

#### Endpoint
POST /ingest

#### Request
{
  "source": "string",
  "type": "video|article|document|api",
  "payload": {},
  "metadata": {}
}

#### Response
{
  "documents": [CanonicalDocument]
}

#### Interface Contract
- MUST return normalized documents
- MUST NOT perform heavy reasoning
- SHOULD enrich with basic metadata (tags, timestamps)

---

### 2.2 Knowledge Service

#### Responsibility
Store and retrieve knowledge via graph + vector search.

#### Endpoints

POST /knowledge/store
{
  "documents": [CanonicalDocument]
}

POST /knowledge/query
{
  "query": "string",
  "filters": {},
  "top_k": 5
}

#### Response
{
  "results": [KnowledgeResult]
}

#### Interface Contract
- MUST support semantic retrieval
- SHOULD support graph traversal queries
- MUST return source references

---

### 2.3 Validation Service (Truth Engine)

#### Responsibility
Evaluate truthfulness and assign confidence scores.

#### Endpoint
POST /validate

#### Request
{
  "documents": [CanonicalDocument]
}

#### Response
{
  "validated": [ValidatedDocument]
}

#### Interface Contract
- MUST extract claims
- MUST assign confidence score (0–1)
- SHOULD attach evidence references
- MAY flag contradictions

---

### 2.4 Reasoning Service (Agent Layer)

#### Responsibility
Perform multi-step reasoning and orchestration.

#### Endpoint
POST /reason

#### Request
{
  "goal": "string",
  "context": {},
  "constraints": {},
  "mode": "plan|answer|analyze"
}

#### Response
{
  "output": "string",
  "steps": [],
  "confidence": 0.0,
  "sources": []
}

#### Interface Contract
- MUST use knowledge service for retrieval
- SHOULD call validation service when needed
- MUST return explainable outputs

---

### 2.5 Application Service (Domain Layer)

#### Responsibility
Map core system into domain-specific workflows.

#### Example Endpoint
POST /plan

#### Request
{
  "user_goal": "string",
  "user_state": {},
  "preferences": {}
}

#### Response
{
  "plan": [],
  "skills": [],
  "resources": [],
  "confidence": 0.0
}

#### Interface Contract
- MUST orchestrate reasoning + knowledge
- MUST remain domain-specific (not generic logic)

---

### 2.6 Interface Service (Gateway / UI API)

#### Responsibility
Expose unified API to UI or external systems.

#### Endpoint
POST /chat

#### Request
{
  "message": "string",
  "context": {},
  "user_id": "string"
}

#### Response
{
  "response": "string",
  "sources": [],
  "confidence": 0.0,
  "actions": []
}

#### Interface Contract
- MUST aggregate downstream services
- MUST return user-friendly output
- SHOULD include traceability

---

## 3. Core Data Contracts

### 3.1 CanonicalDocument

{
  "id": "string",
  "content": "string",
  "source": "string",
  "author": "string",
  "timestamp": "datetime",
  "tags": ["string"],
  "metadata": {},
  "relations": []
}

---

### 3.2 Claim

{
  "id": "string",
  "text": "string",
  "source_document_id": "string"
}

---

### 3.3 ValidatedDocument

{
  "document": CanonicalDocument,
  "claims": [Claim],
  "confidence": 0.0,
  "evidence": [string],
  "contradictions": [string]
}

---

### 3.4 KnowledgeResult

{
  "content": "string",
  "source": "string",
  "score": 0.0,
  "metadata": {}
}

---

### 3.5 ReasoningStep

{
  "step": "string",
  "description": "string",
  "inputs": {},
  "outputs": {}
}

---

## 4. Communication Patterns

### 4.1 Synchronous (Default)
- REST calls between services
- Used for:
  - Query
  - Chat
  - Planning

### 4.2 Asynchronous (Optional)
- Event bus (Kafka, Azure Service Bus, etc.)

Events:
- document.ingested
- document.validated
- knowledge.updated

---

## 5. Cross-Cutting Concerns

### 5.1 Observability
- Correlation ID required across all services
- Log:
  - inputs
  - outputs
  - latency
  - confidence scores

---

### 5.2 Security
- API authentication (token-based)
- Source-level access control
- Data isolation per user/tenant

---

### 5.3 Performance
- Cache frequent queries
- Batch ingestion + validation
- Async processing where possible

---

## 6. Deployment Model

- C# Services:
  - API Gateway
  - Application Layer
  - Orchestration

- Python Services (FastAPI):
  - Ingestion
  - Validation (LLM-heavy)
  - Reasoning (optional)

- Shared:
  - Vector DB
  - Graph DB

---

## 7. Extension Points

- Add new ingestion connectors without modifying core
- Swap validation logic (rule-based ↔ LLM)
- Introduce new agents in reasoning layer
- Add domain modules (learning, QA, enterprise)

---

## 8. Acceptance Criteria

- Services are independently deployable
- Contracts are strictly adhered to
- System supports:
  ingest → validate → store → reason → respond

- Outputs always include:
  - response
  - sources
  - confidence

---

## 9. Guiding Principle

“Dumb pipes, smart endpoints.”

- Services do one job well
- Intelligence lives in reasoning + validation layers
- Everything is replaceable without breaking the system

