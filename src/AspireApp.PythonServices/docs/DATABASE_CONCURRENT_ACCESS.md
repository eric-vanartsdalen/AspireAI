# Enhanced DatabaseService for Concurrent Access

This document describes the improvements made to the `DatabaseService` class to enable effective concurrent access between the Python service and C# Blazor application.

## Key Improvements

### 1. Connection Pooling
- **Thread-safe connection pool** with configurable size (default: 10 connections)
- **Connection reuse** to reduce overhead and improve performance
- **Automatic connection health checking** before returning to pool
- **Graceful degradation** when pool is exhausted

### 2. Enhanced Transaction Management
- **Immediate write locks** (`BEGIN IMMEDIATE`) to reduce deadlocks
- **Read-only transaction optimization** for better concurrency
- **Automatic transaction rollback** on errors
- **Context managers** for proper resource cleanup

### 3. Improved Error Handling
- **Exponential backoff** for retry logic (up to 5 retries)
- **Lock timeout detection** and reporting
- **Detailed error categorization** (database locked, I/O errors, etc.)
- **Graceful fallback paths** for critical failures

### 4. Concurrent Access Monitoring
- **Service activity tracking** in `service_metrics` table
- **Real-time monitoring** of active services
- **Performance statistics** collection
- **Health check enhancements** with timing and service details

### 5. Database Optimizations
- **WAL mode** for better concurrent read/write performance
- **Optimized PRAGMA settings** for concurrent access
- **Enhanced indexing** including processing status index
- **Connection-level busy timeout** (30 seconds)

## Configuration

### Environment Variables
- `ASPIRE_DB_PATH`: Primary database path
- `ASPIRE_DB_BACKUP_PATH`: Backup location for database copies

### Database Paths (Priority Order)
1. `/app/database/data-resources.db` (Docker volume - preferred)
2. `/app/host-database/data-resources.db` (Host bind mount - fallback)
3. `/tmp/aspire_database/data-resources.db` (Temp fallback)
4. `/tmp/data-resources.db` (Last resort)

## Concurrent Access Patterns

### Python Service Operations
- Document processing and status updates
- Bulk document ingestion
- Processing metadata storage
- Page content extraction

### C# Service Operations
- Document metadata queries
- Status monitoring
- File upload coordination
- User interface data retrieval

## Monitoring and Testing

### Database Monitoring
```bash
# Monitor database activity for 5 minutes
python scripts/monitor_database.py --duration 5

# Show current status
python scripts/monitor_database.py --status

# Extended monitoring with frequent checks
python scripts/monitor_database.py --duration 10 --interval 5
```

### Concurrent Access Testing
```bash
# Basic concurrent access test
python scripts/test_concurrent_access.py --threads 5 --operations 20

# Simulate C# service reading while Python writes
python scripts/test_concurrent_access.py --read-only --duration 60

# Stress test with many threads
python scripts/test_concurrent_access.py --threads 10 --operations 50
```

## Performance Characteristics

### Expected Performance
- **Read operations**: 1-5ms typical response time
- **Write operations**: 5-20ms typical response time
- **Concurrent readers**: No blocking between read operations
- **Mixed read/write**: Minimal blocking with WAL mode

### Bottlenecks to Monitor
- **Lock contention**: Watch for retry counts and timeouts
- **I/O performance**: Monitor response times for disk issues
- **Connection pool exhaustion**: Check pool utilization
- **Memory usage**: Monitor for excessive connection creation

## Database Schema

### Core Tables
- `documents`: Main document metadata
- `processed_documents`: Document processing results
- `document_pages`: Individual page content
- `service_metrics`: Concurrent access monitoring

### Key Indexes
- `idx_documents_processed`: For status filtering
- `idx_documents_upload_date`: For chronological queries
- `idx_documents_status`: For processing status queries
- `idx_processed_documents_document_id`: For relationship queries

## Best Practices

### For Python Service
```python
# Use the enhanced service
db_service = DatabaseService()

# Batch operations when possible
documents = db_service.get_unprocessed_documents()
for doc in documents:
    # Process document
    db_service.update_processing_status(doc.id, "processing")
    # ... processing logic ...
    db_service.update_processing_status(doc.id, "completed")
```

### For C# Service
- Use Entity Framework's connection pooling
- Leverage read-only transactions where possible
- Implement proper retry logic for transient failures
- Monitor database health through the Python service endpoints

### General Guidelines
- **Keep transactions short** to minimize lock time
- **Use read-only operations** when data modification isn't needed
- **Implement circuit breakers** for resilience
- **Monitor metrics regularly** to detect issues early

## Troubleshooting

### Common Issues

#### Database Locked Errors
- **Cause**: Long-running transactions or deadlocks
- **Solution**: Check for hung processes, restart services if needed
- **Prevention**: Use shorter transactions, implement timeouts

#### High Response Times
- **Cause**: Disk I/O issues or connection pool exhaustion
- **Solution**: Check disk performance, increase pool size
- **Prevention**: Monitor regularly, use SSDs for database storage

#### Connection Pool Exhaustion
- **Cause**: Too many concurrent operations
- **Solution**: Increase `max_connections` parameter
- **Prevention**: Implement request throttling

### Diagnostic Commands

```bash
# Check current database status
python scripts/monitor_database.py --status

# Monitor for lock contention
python scripts/monitor_database.py --duration 2 --interval 5

# Test under load
python scripts/test_concurrent_access.py --threads 8 --operations 30
```

## Integration with C# Service

The enhanced `DatabaseService` is designed to work seamlessly with the C# `UploadDbContext`. Both services:

- **Share the same database file** via Docker volumes
- **Use compatible schema** with matching table and column names
- **Support concurrent operations** through WAL mode and proper locking
- **Provide health monitoring** for operational visibility

### Coordination Points
- **File uploads**: C# creates records, Python processes them
- **Status updates**: Python updates processing status, C# displays them
- **Health monitoring**: Python provides detailed metrics via API endpoints
- **Backup coordination**: Both services can trigger backup operations

This enhanced architecture ensures reliable, performant concurrent access while maintaining data integrity and providing operational visibility.