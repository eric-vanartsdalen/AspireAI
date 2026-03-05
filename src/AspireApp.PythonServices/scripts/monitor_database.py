#!/usr/bin/env python3
"""
Database monitoring script for AspireAI
Monitors concurrent access between Python and C# services
"""

import sys
import time
import json
from pathlib import Path
from datetime import datetime, timedelta

# Add the parent directory to the path to import our modules
sys.path.append(str(Path(__file__).parent.parent))

from app.services.database_service import DatabaseService


def monitor_database(duration_minutes: int = 5, check_interval: int = 10):
    """Monitor database activity for concurrent access patterns"""
    
    print(f"?? Starting database monitoring for {duration_minutes} minutes...")
    print(f"??  Checking every {check_interval} seconds")
    print("=" * 60)
    
    db_service = DatabaseService()
    start_time = datetime.now()
    end_time = start_time + timedelta(minutes=duration_minutes)
    
    monitoring_data = []
    
    try:
        while datetime.now() < end_time:
            timestamp = datetime.now()
            
            # Get health check data
            health = db_service.health_check()
            
            # Get statistics
            stats = db_service.get_statistics()
            
            # Create monitoring entry
            monitor_entry = {
                "timestamp": timestamp.isoformat(),
                "status": health.get("status"),
                "document_count": health.get("document_count", 0),
                "response_time_ms": health.get("response_time_ms", 0),
                "active_services": health.get("active_services", 0),
                "service_details": health.get("service_details", []),
                "journal_mode": health.get("journal_mode"),
                "queries_executed": stats.get("queries_executed", 0),
                "transactions_committed": stats.get("transactions_committed", 0),
                "retries_performed": stats.get("retries_performed", 0),
                "lock_timeouts": stats.get("lock_timeouts", 0),
                "connection_pool_size": stats.get("connection_pool_size", 0),
                "pool_queue_size": stats.get("pool_queue_size", 0)
            }
            
            monitoring_data.append(monitor_entry)
            
            # Print current status
            print(f"? {timestamp.strftime('%H:%M:%S')} | "
                  f"Status: {health.get('status', 'unknown'):8} | "
                  f"Docs: {health.get('document_count', 0):4} | "
                  f"Services: {health.get('active_services', 0):2} | "
                  f"Response: {health.get('response_time_ms', 0):6.1f}ms | "
                  f"Mode: {health.get('journal_mode', 'unknown'):6}")
            
            # Show service details if multiple services are active
            if health.get("active_services", 0) > 1:
                print("  ?? Active Services:")
                for service in health.get("service_details", []):
                    last_activity = service.get("last_activity", "unknown")
                    operations = service.get("operations_count", 0)
                    print(f"    • {service.get('service_name', 'unknown'):20} | "
                          f"Ops: {operations:4} | Last: {last_activity}")
            
            # Show any performance issues
            if stats.get("retries_performed", 0) > 0 or stats.get("lock_timeouts", 0) > 0:
                print(f"  ??  Retries: {stats.get('retries_performed', 0)}, "
                      f"Timeouts: {stats.get('lock_timeouts', 0)}")
            
            time.sleep(check_interval)
            
    except KeyboardInterrupt:
        print("\n?? Monitoring stopped by user")
    except Exception as e:
        print(f"\n? Monitoring error: {e}")
    
    # Generate summary report
    print("\n" + "=" * 60)
    print("?? MONITORING SUMMARY")
    print("=" * 60)
    
    if monitoring_data:
        total_entries = len(monitoring_data)
        healthy_entries = sum(1 for entry in monitoring_data if entry["status"] == "healthy")
        
        avg_response_time = sum(entry["response_time_ms"] for entry in monitoring_data) / total_entries
        max_response_time = max(entry["response_time_ms"] for entry in monitoring_data)
        
        total_retries = monitoring_data[-1]["retries_performed"] - monitoring_data[0]["retries_performed"]
        total_timeouts = monitoring_data[-1]["lock_timeouts"] - monitoring_data[0]["lock_timeouts"]
        
        max_concurrent_services = max(entry["active_services"] for entry in monitoring_data)
        
        print(f"?? Total checks: {total_entries}")
        print(f"? Healthy checks: {healthy_entries} ({healthy_entries/total_entries*100:.1f}%)")
        print(f"?? Average response time: {avg_response_time:.2f}ms")
        print(f"?? Max response time: {max_response_time:.2f}ms")
        print(f"?? Total retries during monitoring: {total_retries}")
        print(f"? Total lock timeouts: {total_timeouts}")
        print(f"?? Max concurrent services: {max_concurrent_services}")
        
        # Detect concurrent access patterns
        concurrent_periods = [entry for entry in monitoring_data if entry["active_services"] > 1]
        if concurrent_periods:
            print(f"?? Concurrent access detected in {len(concurrent_periods)} checks")
            print(f"   ({len(concurrent_periods)/total_entries*100:.1f}% of monitoring period)")
        else:
            print("?? No concurrent access detected (only single service active)")
    
    # Save detailed monitoring data
    report_file = f"database_monitoring_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
    try:
        with open(report_file, 'w') as f:
            json.dump({
                "monitoring_period": {
                    "start": start_time.isoformat(),
                    "end": datetime.now().isoformat(),
                    "duration_minutes": duration_minutes,
                    "check_interval_seconds": check_interval
                },
                "data": monitoring_data
            }, f, indent=2)
        print(f"?? Detailed report saved to: {report_file}")
    except Exception as e:
        print(f"??  Could not save detailed report: {e}")


def show_current_status():
    """Show current database status"""
    print("?? Current Database Status")
    print("=" * 30)
    
    try:
        db_service = DatabaseService()
        health = db_service.health_check()
        
        print(f"Status: {health.get('status', 'unknown')}")
        print(f"Database Path: {health.get('database_path', 'unknown')}")
        print(f"Document Count: {health.get('document_count', 0)}")
        print(f"Response Time: {health.get('response_time_ms', 0):.2f}ms")
        print(f"Journal Mode: {health.get('journal_mode', 'unknown')}")
        print(f"Active Services: {health.get('active_services', 0)}")
        
        if health.get("service_details"):
            print("\nService Details:")
            for service in health.get("service_details", []):
                print(f"  • {service.get('service_name', 'unknown')}")
                print(f"    Operations: {service.get('operations_count', 0)}")
                print(f"    Last Activity: {service.get('last_activity', 'unknown')}")
        
        stats = db_service.get_statistics()
        print(f"\nStatistics:")
        print(f"  Queries: {stats.get('queries_executed', 0)}")
        print(f"  Transactions: {stats.get('transactions_committed', 0)}")
        print(f"  Retries: {stats.get('retries_performed', 0)}")
        print(f"  Timeouts: {stats.get('lock_timeouts', 0)}")
        print(f"  Pool Size: {stats.get('connection_pool_size', 0)}")
        
    except Exception as e:
        print(f"? Error getting status: {e}")


if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Monitor AspireAI database for concurrent access")
    parser.add_argument("--duration", "-d", type=int, default=5, 
                       help="Monitoring duration in minutes (default: 5)")
    parser.add_argument("--interval", "-i", type=int, default=10,
                       help="Check interval in seconds (default: 10)")
    parser.add_argument("--status", "-s", action="store_true",
                       help="Show current status only (no monitoring)")
    
    args = parser.parse_args()
    
    if args.status:
        show_current_status()
    else:
        monitor_database(args.duration, args.interval)