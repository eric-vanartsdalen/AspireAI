#!/usr/bin/env python3
"""
Concurrent access test script for the canonical AspireAI Python database surface.
"""

from __future__ import annotations

import random
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from datetime import datetime

sys.path.append(str(Path(__file__).parent.parent))

from app.services.database_service import DatabaseService


class ConcurrentAccessTest:
    """Test concurrent database access scenarios using canonical file operations."""

    def __init__(self, num_threads: int = 5, operations_per_thread: int = 10):
        self.num_threads = num_threads
        self.operations_per_thread = operations_per_thread
        self.results = []
        self.errors = []
        self.lock = threading.Lock()

    def log_result(self, thread_id: int, operation: str, success: bool, duration: float, error: str = None):
        with self.lock:
            result = {
                "thread_id": thread_id,
                "operation": operation,
                "success": success,
                "duration_ms": round(duration * 1000, 2),
                "timestamp": datetime.now().isoformat(),
                "error": error,
            }
            self.results.append(result)
            if not success:
                self.errors.append(result)

    def worker_thread(self, thread_id: int):
        db_service = DatabaseService()

        for op_num in range(self.operations_per_thread):
            operation_type = random.choice(
                [
                    "read_files",
                    "read_unprocessed",
                    "write_file",
                    "update_status",
                    "read_status",
                    "health_check",
                ]
            )

            start_time = time.time()
            success = True
            error = None

            try:
                if operation_type == "read_files":
                    db_service.get_all_files()
                    time.sleep(random.uniform(0.01, 0.05))

                elif operation_type == "read_unprocessed":
                    db_service.get_unprocessed_files()
                    time.sleep(random.uniform(0.01, 0.03))

                elif operation_type == "write_file":
                    db_service.create_file_record(
                        file_name=f"test_doc_{thread_id}_{op_num}.pdf",
                        original_file_name=f"test_doc_{thread_id}_{op_num}.pdf",
                        file_path="uploads",
                        file_size=random.randint(1000, 10000),
                        mime_type="application/pdf",
                        status="uploaded",
                    )
                    time.sleep(random.uniform(0.02, 0.08))

                elif operation_type == "update_status":
                    files = db_service.get_all_files()
                    if files:
                        file_record = random.choice(files)
                        new_status = random.choice(["processing", "processed", "error"])
                        error_message = "simulated error" if new_status == "error" else None
                        db_service.update_file_status(file_record["id"], new_status, error_message)
                    time.sleep(random.uniform(0.01, 0.04))

                elif operation_type == "read_status":
                    files = db_service.get_all_files()
                    if files:
                        file_record = random.choice(files)
                        db_service.get_processing_status(file_record["id"])
                    time.sleep(random.uniform(0.01, 0.03))

                elif operation_type == "health_check":
                    db_service.health_check()
                    time.sleep(random.uniform(0.001, 0.01))

            except Exception as e:
                success = False
                error = str(e)

            duration = time.time() - start_time
            self.log_result(thread_id, operation_type, success, duration, error)
            time.sleep(random.uniform(0.001, 0.01))

    def run_test(self):
        print("🚀 Starting concurrent access test...")
        print(f"   Threads: {self.num_threads}")
        print(f"   Operations per thread: {self.operations_per_thread}")
        print(f"   Total operations: {self.num_threads * self.operations_per_thread}")
        print("=" * 60)

        start_time = time.time()
        with ThreadPoolExecutor(max_workers=self.num_threads) as executor:
            futures = [executor.submit(self.worker_thread, thread_id) for thread_id in range(self.num_threads)]
            for future in as_completed(futures):
                try:
                    future.result()
                except Exception as e:
                    print(f"❌ Thread failed: {e}")

        self.generate_report(time.time() - start_time)

    def generate_report(self, total_time: float):
        print("\n" + "=" * 60)
        print("📊 CONCURRENT ACCESS TEST RESULTS")
        print("=" * 60)

        total_ops = len(self.results)
        successful_ops = sum(1 for r in self.results if r["success"])
        failed_ops = len(self.errors)

        print(f"⏱️  Total test time: {total_time:.2f} seconds")
        print(f"📈 Total operations: {total_ops}")
        print(f"✅ Successful operations: {successful_ops} ({successful_ops / total_ops * 100:.1f}%)")
        print(f"❌ Failed operations: {failed_ops} ({failed_ops / total_ops * 100:.1f}%)")

        durations = [r["duration_ms"] for r in self.results if r["success"]]
        if durations:
            print(f"📉 Average operation time: {sum(durations) / len(durations):.2f}ms")
            print(f"🐢 Slowest operation: {max(durations):.2f}ms")
            print(f"⚡ Fastest operation: {min(durations):.2f}ms")
            print(f"🔁 Operations per second: {successful_ops / total_time:.2f}")

        if self.errors:
            print(f"\n❌ Error Analysis ({len(self.errors)} errors):")
            error_types = {}
            for error in self.errors:
                error_msg = error["error"] or "Unknown error"
                error_type = error_msg.split(":")[0] if ":" in error_msg else error_msg
                error_types[error_type] = error_types.get(error_type, 0) + 1
            for error_type, count in error_types.items():
                print(f"  {error_type[:50]:50}: {count:3} occurrences")

        print("\n🩺 Final Database Health Check:")
        try:
            db_service = DatabaseService()
            health = db_service.health_check()
            print(f"  Status: {health.get('status', 'unknown')}")
            print(f"  Files: {len(db_service.get_all_files())}")

            stats = db_service.get_statistics()
            print(f"  Connection pool size: {stats.get('connection_pool_size', 0)}")
            print(f"  Max pool size: {stats.get('max_pool_size', 0)}")
        except Exception as e:
            print(f"  ❌ Health check failed: {e}")


def run_read_only_test(duration_seconds: int = 30):
    """Run a read-heavy test to simulate UI reads while Python writes."""
    print(f"🔍 Running read-only concurrent test for {duration_seconds} seconds...")

    results = []
    errors = []
    lock = threading.Lock()

    def reader_thread(thread_id: int):
        db_service = DatabaseService()
        end_time = time.time() + duration_seconds
        while time.time() < end_time:
            start_time = time.time()
            try:
                files = db_service.get_all_files()
                db_service.get_unprocessed_files()
                if files:
                    file_record = random.choice(files)
                    db_service.get_processing_status(file_record["id"])

                duration = time.time() - start_time
                with lock:
                    results.append(
                        {
                            "thread_id": thread_id,
                            "operation": "read_simulation",
                            "success": True,
                            "duration_ms": duration * 1000,
                        }
                    )
            except Exception as e:
                with lock:
                    errors.append({"thread_id": thread_id, "error": str(e)})
            time.sleep(random.uniform(0.01, 0.1))

    def writer_thread():
        db_service = DatabaseService()
        operations = 0
        end_time = time.time() + duration_seconds
        while time.time() < end_time:
            try:
                file_id = db_service.create_file_record(
                    file_name=f"concurrent_test_{operations}.pdf",
                    original_file_name=f"concurrent_test_{operations}.pdf",
                    file_path="uploads",
                    file_size=random.randint(1000, 5000),
                    mime_type="application/pdf",
                    status="uploaded",
                )
                if random.random() < 0.3:
                    db_service.update_file_status(file_id, random.choice(["processing", "processed"]))
                operations += 1
            except Exception as e:
                with lock:
                    errors.append({"thread_id": "writer", "error": str(e)})
            time.sleep(random.uniform(0.05, 0.2))

    with ThreadPoolExecutor(max_workers=4) as executor:
        futures = [executor.submit(reader_thread, i) for i in range(3)]
        futures.append(executor.submit(writer_thread))
        for future in as_completed(futures):
            future.result()

    print("\n📊 Read-Only Test Results:")
    print(f"  Total read operations: {len(results)}")
    print(f"  Total errors: {len(errors)}")
    if results:
        durations = [r["duration_ms"] for r in results]
        print(f"  Average read time: {sum(durations) / len(durations):.2f}ms")
        print(f"  Reads per second: {len(results) / duration_seconds:.2f}")
    if errors:
        print("  ❌ Errors encountered:")
        for error in errors[:5]:
            print(f"    {error['error']}")


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="Test concurrent database access")
    parser.add_argument("--threads", "-t", type=int, default=5, help="Number of concurrent threads")
    parser.add_argument("--operations", "-o", type=int, default=10, help="Operations per thread")
    parser.add_argument("--read-only", "-r", action="store_true", help="Run read-only test")
    parser.add_argument("--duration", "-d", type=int, default=30, help="Read-only test duration in seconds")
    args = parser.parse_args()

    if args.read_only:
        run_read_only_test(args.duration)
    else:
        ConcurrentAccessTest(args.threads, args.operations).run_test()
