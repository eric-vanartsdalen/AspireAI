# Cross-Service Orchestration: Pull vs. Push Patterns

**Skill Owner:** Jeff (Architect review recommended)  
**Context:** AspireAI upload + processing pipeline  
**Reusable Pattern:** Yes — applicable to any multi-service async workflow

## Problem

When two or more services must coordinate work across service boundaries (e.g., Web uploads, Python processes), you must choose whether the **source service triggers** the **worker service** (push) or the **worker service checks for work** (pull).

## Solution Patterns

### Pattern 1: Push Architecture (Event-Driven)

**Flow:** Web service calls Python service immediately after producing a file.

```csharp
// Web controller after saving file
var processingResponse = await pythonClient.PostAsync("/processing/process-all", null);
if (!processingResponse.IsSuccessStatusCode) 
    logger.LogWarning("Processing trigger failed");
```

**Pros:**
- Minimal latency: work starts immediately after upload
- Simple state machine: upload → (sync) trigger → process
- No polling overhead
- Easy to test: single HTTP call after upload

**Cons:**
- Tight coupling: Web service must know Python service URL/protocol
- Blocking risk: if Python service is slow, web request may timeout
- Cascading failures: if Python is down, upload endpoint may fail
- Retry complexity: need exponential backoff logic

**When to use:** Small batch sizes, tight SLAs, services in same deployment, simple workflows.

### Pattern 2: Pull Architecture (Polling-Based)

**Flow:** Python service polls the database periodically for new files.

```python
# Python service (runs as background task or explicit endpoint call)
@router.post("/processing/process-all")
async def process_all_documents():
    unprocessed = db.list_unprocessed_documents()  # WHERE status='uploaded'
    for doc in unprocessed:
        # Process in background
```

**Pros:**
- Loose coupling: Web service is unaware of Python service
- No cascading failures: if Python is down, uploads still work
- Scalability: multiple Python workers can compete for work without coordination
- Resilience: work persists in database if worker crashes mid-processing
- Retry-friendly: worker can handle dead-letter naturally

**Cons:**
- Polling latency: files may sit for N seconds until poll cycle runs
- Database load: frequent queries (e.g., every 5 seconds) add I/O
- Manual trigger needed: must expose `/process-all` endpoint or run background job
- Operational complexity: must monitor polling loops and alert on staleness

**When to use:** Large batch sizes, high availability required, worker service may be unavailable, simple status tracking.

### Pattern 3: Event Queue (Message Broker)

**Flow:** Web publishes "file uploaded" event to queue; Python subscribes and processes.

```csharp
// Web (producer)
await messageQueue.PublishAsync(new FileUploadedEvent { FileId = 123 });
```

```python
# Python (consumer)
@queue.subscribe("file-uploaded")
async def on_file_uploaded(event):
    process_document(event.file_id)
```

**Pros:**
- Decoupling: producer and consumer never interact directly
- Ordering: queue preserves event order for replay
- Multi-subscriber: multiple Python workers can process same queue
- Durability: events logged and recoverable
- Auto-scaling: add/remove workers without coordination

**Cons:**
- Infrastructure overhead: requires Redis, RabbitMQ, Azure Service Bus, etc.
- Operational complexity: new component to monitor/debug
- Cost: additional service licensing/hosting
- Debugging: harder to trace events across time

**When to use:** Complex workflows, multiple subscribers, multi-tenant systems, compliance auditing required.

## AspireAI Decision

**Current:** Pull architecture (Python endpoint `POST /processing/process-all`)

**Rationale:**
- Simple: 2 services (Web, Python), no message broker
- Resilient: Web doesn't know about Python
- Testing-friendly: explicit trigger in tests
- Low infrastructure burden

**Next step:** Add either:
1. **UI button:** User clicks "Process Files" → calls Python endpoint directly
2. **Background service:** .NET timer calls Python endpoint every N seconds
3. **Event queue:** If multi-worker scaling or replay needed later

## When to Migrate Patterns

- **Pull → Push:** When you need sub-second ingestion latency and can tolerate tight coupling
- **Pull → Queue:** When you have 3+ worker services or need event history
- **Push → Pull:** If you observe cascading failures or worker unavailability

## Testing Implications

### Pattern 1 (Push): Easy

```csharp
// Test verifies Web calls Python
_mockHttpClient.Verify(c => c.PostAsync("/processing/process-all", null), Times.Once);
```

### Pattern 2 (Pull): Requires Manual Trigger

```csharp
// Test must manually trigger Python endpoint
var processingResponse = await _pythonClient.PostAsync("/processing/process-all", null);
Assert.True(processingResponse.IsSuccessStatusCode);

// Then poll status
await Task.Delay(100);  // Let background job run
var status = await GetFileStatusAsync(fileId);
Assert.Equal("processed", status);
```

**Blazor Server caveat:** If the upload flow is implemented inside a server-side Razor component (as in AspireAI), Playwright cannot observe `/api/FileUpload` as a browser network response because the POST is issued by server-side `HttpClient`. In that case, resolve the uploaded row from API-backed state first (for example `GET /api/FileUpload` after the UI upload), then trigger and poll the worker service directly.

### Pattern 3 (Queue): Requires Queue Simulator

```csharp
// Test publishes event
await _queue.PublishAsync(new FileUploadedEvent { FileId = 123 });

// Event handler runs (may be async)
await Task.Delay(500);  // Wait for async handler

// Verify result
var status = await GetFileStatusAsync(fileId);
Assert.Equal("processed", status);
```

## Recommendation

**Start with Pattern 2 (Pull) + UI button (Pattern 1 on demand).** This gives you:
- Simple deployment (no new infrastructure)
- Explicit user control ("Process Files" button)
- Resilience (upload works even if Python is down)
- Easy testing (call endpoint → poll status)
- Path to Pattern 3 later (if multi-worker scaling needed)

---

**Applicable to:**
- Any async multi-service workflow
- Upload → process pipelines
- ETL systems
- Microservice coordination

**See also:**
- `CROSS_SERVICE_CONTRACT.md` (data shapes across services)
- `aspire-orchestration.instructions.md` (AppHost wiring)
- `testing.instructions.md` (integration test patterns)
