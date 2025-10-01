#!/usr/bin/env python3
"""
Concurrent access test script for AspireAI database
Tests the effectiveness of the improved DatabaseService
"""

import sys
import time
import threading
import random
from pathlib import Path
from datetime import datetime
from concurrent.futures import ThreadPoolExecutor, as_completed

# Add the parent directory to the path to import our modules
sys.path.append(str(Path(__file__).parent.parent))

from app.services.database_service import DatabaseService
from app.models.models import Document, ProcessedDocument, DocumentPage


class ConcurrentAccessTest:
    """Test concurrent database access scenarios"""
    
    def __init__(self, num_threads: int = 5, operations_per_thread: int = 10):
        self.num_threads = num_threads
        self.operations_per_thread = operations_per_thread
        self.results = []
        self.errors = []
        self.lock = threading.Lock()
    
    def log_result(self, thread_id: int, operation: str, success: bool, duration: float, error: str = None):
        """Thread-safe logging of test results"""
        with self.lock:
            result = {
                "thread_id": thread_id,
                "operation": operation,
                "success": success,
                "duration_ms": round(duration * 1000, 2),
                "timestamp": datetime.now().isoformat(),
                "error": error
            }
            self.results.append(result)
            if not success:
                self.errors.append(result)
    
    def worker_thread(self, thread_id: int):
        """Worker thread that performs database operations"""
        db_service = DatabaseService()
        
        for op_num in range(self.operations_per_thread):
            operation_type = random.choice([
                "read_documents", "read_unprocessed", "write_document", 
                "update_status", "read_processed", "health_check"
            ])
            
            start_time = time.time()
            success = True
            error = None
            
            try:
                if operation_type == "read_documents":
                    documents = db_service.get_all_documents()
                    # Simulate processing time
                    time.sleep(random.uniform(0.01, 0.05))
                
                elif operation_type == "read_unprocessed":
                    unprocessed = db_service.get_unprocessed_documents()
                    time.sleep(random.uniform(0.01, 0.03))
                
                elif operation_type == "write_document":
                    doc = Document(
                        id=0,  # Will be auto-generated
                        filename=f"test_doc_{thread_id}_{op_num}.pdf",
                        original_filename=f"test_doc_{thread_id}_{op_num}.pdf",
                        file_path=f"/test/path/{thread_id}_{op_num}.pdf",
                        file_size=random.randint(1000, 10000),
                        mime_type="application/pdf",
                        upload_date=datetime.now(),
                        processed=False,
                        processing_status="pending"
                    )
                    doc_id = db_service.save_document(doc)
                    time.sleep(random.uniform(0.02, 0.08))
                
                elif operation_type == "update_status":
                    # Get a random document to update
                    documents = db_service.get_all_documents()
                    if documents:
                        doc = random.choice(documents)
                        new_status = random.choice(["pending", "processing", "completed", "error"])
                        db_service.update_processing_status(doc.id, new_status)
                    time.sleep(random.uniform(0.01, 0.04))
                
                elif operation_type == "read_processed":
                    documents = db_service.get_all_documents()
                    if documents:
                        doc = random.choice(documents)
                        processed = db_service.get_processed_document(doc.id)
                    time.sleep(random.uniform(0.01, 0.03))
                
                elif operation_type == "health_check":
                    health = db_service.health_check()
                    time.sleep(random.uniform(0.001, 0.01))
                
            except Exception as e:
                success = False
                error = str(e)
            
            duration = time.time() - start_time
            self.log_result(thread_id, operation_type, success, duration, error)
            
            # Random small delay between operations
            time.sleep(random.uniform(0.001, 0.01))
    
    def run_test(self):
        """Run the concurrent access test"""
        print(f"?? Starting concurrent access test...")
        print(f"   Threads: {self.num_threads}")
        print(f"   Operations per thread: {self.operations_per_thread}")
        print(f"   Total operations: {self.num_threads * self.operations_per_thread}")
        print("=" * 60)
        
        start_time = time.time()
        
        # Use ThreadPoolExecutor for better thread management
        with ThreadPoolExecutor(max_workers=self.num_threads) as executor:
            # Submit all worker threads
            futures = [
                executor.submit(self.worker_thread, thread_id) 
                for thread_id in range(self.num_threads)
            ]
            
            # Wait for all threads to complete
            for future in as_completed(futures):
                try:
                    future.result()
                except Exception as e:
                    print(f"? Thread failed: {e}")
        
        total_time = time.time() - start_time
        
        # Generate report
        self.generate_report(total_time)
    
    def generate_report(self, total_time: float):
        """Generate test results report"""
        print("\n" + "=" * 60)
        print("?? CONCURRENT ACCESS TEST RESULTS")
        print("=" * 60)
        
        total_ops = len(self.results)
        successful_ops = sum(1 for r in self.results if r["success"])
        failed_ops = len(self.errors)
        
        print(f"??  Total test time: {total_time:.2f} seconds")
        print(f"?? Total operations: {total_ops}")
        print(f"? Successful operations: {successful_ops} ({successful_ops/total_ops*100:.1f}%)")
        print(f"? Failed operations: {failed_ops} ({failed_ops/total_ops*100:.1f}%)")
        
        if self.results:
            durations = [r["duration_ms"] for r in self.results if r["success"]]
            if durations:
                avg_duration = sum(durations) / len(durations)
                max_duration = max(durations)
                min_duration = min(durations)
                
                print(f"?? Average operation time: {avg_duration:.2f}ms")
                print(f"?? Slowest operation: {max_duration:.2f}ms")
                print(f"? Fastest operation: {min_duration:.2f}ms")
                print(f"?? Operations per second: {successful_ops/total_time:.2f}")
        
        # Operation type breakdown
        operation_types = {}
        for result in self.results:
            op_type = result["operation"]
            if op_type not in operation_types:
                operation_types[op_type] = {"total": 0, "successful": 0, "failed": 0}
            
            operation_types[op_type]["total"] += 1
            if result["success"]:
                operation_types[op_type]["successful"] += 1
            else:
                operation_types[op_type]["failed"] += 1
        
        print("\n?? Operation Type Breakdown:")
        for op_type, stats in operation_types.items():
            success_rate = stats["successful"] / stats["total"] * 100
            print(f"  {op_type:15}: {stats['total']:3} ops, "
                  f"{stats['successful']:3} success, "
                  f"{stats['failed']:3} failed ({success_rate:5.1f}%)")
        
        # Error analysis
        if self.errors:
            print(f"\n? Error Analysis ({len(self.errors)} errors):")
            error_types = {}
            for error in self.errors:
                error_msg = error["error"] or "Unknown error"
                error_type = error_msg.split(":")[0] if ":" in error_msg else error_msg
                error_types[error_type] = error_types.get(error_type, 0) + 1
            
            for error_type, count in error_types.items():
                print(f"  {error_type[:50]:50}: {count:3} occurrences")
        
        # Thread performance analysis
        thread_stats = {}
        for result in self.results:
            thread_id = result["thread_id"]
            if thread_id not in thread_stats:
                thread_stats[thread_id] = {"total": 0, "successful": 0, "total_time": 0}
            
            thread_stats[thread_id]["total"] += 1
            if result["success"]:
                thread_stats[thread_id]["successful"] += 1
            thread_stats[thread_id]["total_time"] += result["duration_ms"]
        
        print(f"\n?? Thread Performance:")
        for thread_id, stats in thread_stats.items():
            success_rate = stats["successful"] / stats["total"] * 100
            avg_time = stats["total_time"] / stats["total"]
            print(f"  Thread {thread_id:2}: {stats['total']:3} ops, "
                  f"{success_rate:5.1f}% success, {avg_time:6.2f}ms avg")
        
        # Database health check
        print(f"\n?? Final Database Health Check:")
        try:
            db_service = DatabaseService()
            health = db_service.health_check()
            
            print(f"  Status: {health.get('status', 'unknown')}")
            print(f"  Documents: {health.get('document_count', 0)}")
            print(f"  Response time: {health.get('response_time_ms', 0):.2f}ms")
            print(f"  Journal mode: {health.get('journal_mode', 'unknown')}")
            
            stats = db_service.get_statistics()
            print(f"  Total queries: {stats.get('queries_executed', 0)}")
            print(f"  Total transactions: {stats.get('transactions_committed', 0)}")
            print(f"  Retries performed: {stats.get('retries_performed', 0)}")
            print(f"  Lock timeouts: {stats.get('lock_timeouts', 0)}")
            
        except Exception as e:
            print(f"  ? Health check failed: {e}")


def run_read_only_test(duration_seconds: int = 30):
    """Run a read-only concurrent test to simulate C# service reading while Python writes"""
    print(f"?? Running read-only concurrent test for {duration_seconds} seconds...")
    
    results = []
    errors = []
    lock = threading.Lock()
    
    def reader_thread(thread_id: int):
        """Thread that only performs read operations"""
        db_service = DatabaseService()
        operations = 0
        
        end_time = time.time() + duration_seconds
        while time.time() < end_time:
            start_time = time.time()
            try:
                # Simulate C# service operations
                documents = db_service.get_all_documents()
                unprocessed = db_service.get_unprocessed_documents()
                if documents:
                    doc = random.choice(documents)
                    processed = db_service.get_processed_document(doc.id)
                
                operations += 1
                duration = time.time() - start_time
                
                with lock:
                    results.append({
                        "thread_id": thread_id,
                        "operation": "read_simulation",
                        "success": True,
                        "duration_ms": duration * 1000,
                        "operations": operations
                    })
                
            except Exception as e:
                with lock:
                    errors.append({
                        "thread_id": thread_id,
                        "error": str(e),
                        "timestamp": datetime.now().isoformat()
                    })
            
            time.sleep(random.uniform(0.01, 0.1))  # Simulate processing time
    
    def writer_thread():
        """Thread that performs write operations"""
        db_service = DatabaseService()
        operations = 0
        
        end_time = time.time() + duration_seconds
        while time.time() < end_time:
            try:
                # Create test document
                doc = Document(
                    id=0,
                    filename=f"concurrent_test_{operations}.pdf",
                    original_filename=f"concurrent_test_{operations}.pdf",
                    file_path=f"/test/concurrent/{operations}.pdf",
                    file_size=random.randint(1000, 5000),
                    mime_type="application/pdf",
                    upload_date=datetime.now(),
                    processed=False,
                    processing_status="pending"
                )
                doc_id = db_service.save_document(doc)
                
                # Sometimes update status
                if random.random() < 0.3:
                    new_status = random.choice(["processing", "completed"])
                    db_service.update_processing_status(doc_id, new_status)
                
                operations += 1
                
            except Exception as e:
                with lock:
                    errors.append({
                        "thread_id": "writer",
                        "error": str(e),
                        "timestamp": datetime.now().isoformat()
                    })
            
            time.sleep(random.uniform(0.05, 0.2))  # Simulate processing time
    
    # Start threads
    with ThreadPoolExecutor(max_workers=4) as executor:
        # Start 3 reader threads (simulating C# service)
        reader_futures = [
            executor.submit(reader_thread, i) for i in range(3)
        ]
        
        # Start 1 writer thread (simulating Python service)
        writer_future = executor.submit(writer_thread)
        
        # Wait for completion
        for future in as_completed(reader_futures + [writer_future]):
            future.result()
    
    # Report results
    total_reads = len(results)
    total_errors = len(errors)
    
    print(f"\n?? Read-Only Test Results:")
    print(f"  Total read operations: {total_reads}")
    print(f"  Total errors: {total_errors}")
    
    if results:
        durations = [r["duration_ms"] for r in results]
        avg_duration = sum(durations) / len(durations)
        print(f"  Average read time: {avg_duration:.2f}ms")
        print(f"  Reads per second: {total_reads/duration_seconds:.2f}")
    
    if errors:
        print(f"  ? Errors encountered:")
        for error in errors[:5]:  # Show first 5 errors
            print(f"    {error['error']}")


if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Test concurrent database access")
    parser.add_argument("--threads", "-t", type=int, default=5,
                       help="Number of concurrent threads (default: 5)")
    parser.add_argument("--operations", "-o", type=int, default=10,
                       help="Operations per thread (default: 10)")
    parser.add_argument("--read-only", "-r", action="store_true",
                       help="Run read-only test simulating C# service")
    parser.add_argument("--duration", "-d", type=int, default=30,
                       help="Duration for read-only test in seconds (default: 30)")
    
    args = parser.parse_args()
    
    if args.read_only:
        run_read_only_test(args.duration)
    else:
        test = ConcurrentAccessTest(args.threads, args.operations)
        test.run_test()