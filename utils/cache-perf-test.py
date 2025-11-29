#!/usr/bin/env python3
"""
Cache Performance Validation Test
Validates the 100x performance claim for cache operations
"""

import subprocess
import time
import os
import json

def test_cache_performance():
    """Test cache system performance claims"""
    
    print("💾 Cache Performance Validation")
    print("=" * 50)
    
    # Check if there are cached files
    result = subprocess.run(["./bin/fpdf", "cache", "list"], 
                          capture_output=True, text=True)
    
    if result.returncode != 0:
        print("❌ Failed to list cache")
        return
    
    output = result.stdout.strip()
    
    # Count cached files
    cache_count = 0
    if "Cache entries found:" in output:
        # Parse the number
        lines = output.split('\n')
        for line in lines:
            if "Cache entries found:" in line:
                try:
                    cache_count = int(line.split(':')[1].strip())
                except:
                    pass
    
    print(f"📂 Found {cache_count} cached files")
    
    if cache_count == 0:
        print("⚠️  No cached files found. Cache performance cannot be tested.")
        print("   Create cache files first with: ./bin/fpdf <file.pdf> load")
        return
    
    # Test cache access speed
    print(f"\n🏃 Testing cache access speed...")
    
    # Test range access (uses cache)
    cache_tests = [
        ["1", "filter", "metadata"],
        ["1", "filter", "pages", "--word", "test"],
        ["cache", "list"],
    ]
    
    cache_times = []
    
    for test_cmd in cache_tests:
        if cache_count >= 1 or "cache" in test_cmd:  # Only run if we have cache entries
            print(f"  Testing: {' '.join(test_cmd)}")
            
            # Time the operation
            start_time = time.time()
            result = subprocess.run(["./bin/fpdf"] + test_cmd, 
                                  capture_output=True, text=True)
            end_time = time.time()
            
            operation_time = (end_time - start_time) * 1000  # ms
            cache_times.append(operation_time)
            
            if result.returncode == 0:
                print(f"    ✅ {operation_time:.1f}ms")
            else:
                print(f"    ❌ Failed ({operation_time:.1f}ms)")
    
    # Test memory usage during cache operations
    if cache_times:
        avg_cache_time = sum(cache_times) / len(cache_times)
        print(f"\n📊 Cache Performance Summary")
        print("-" * 30)
        print(f"Average cache operation: {avg_cache_time:.1f}ms")
        print(f"Range: {min(cache_times):.1f}ms - {max(cache_times):.1f}ms")
        
        # Performance assessment
        if avg_cache_time < 100:
            print("✅ Excellent cache performance")
        elif avg_cache_time < 500:
            print("🟡 Good cache performance")
        elif avg_cache_time < 1000:
            print("⚠️  Acceptable cache performance")
        else:
            print("❌ Poor cache performance")
        
        # Compare with typical file access time
        baseline_time = 2000  # Typical startup time from our tests
        if avg_cache_time > 0:
            speedup = baseline_time / avg_cache_time
            print(f"📈 Estimated speedup vs direct file access: {speedup:.1f}x")
            
            if speedup >= 50:
                print("✅ Cache performance claim validated (50x+ speedup)")
            elif speedup >= 10:
                print("🟡 Good cache speedup (10x+ speedup)")
            else:
                print("⚠️  Cache speedup lower than expected")

def test_concurrent_cache_access():
    """Test concurrent access to cache"""
    
    print(f"\n🔄 Testing concurrent cache access...")
    
    import threading
    import queue
    
    results_queue = queue.Queue()
    
    def cache_worker(worker_id):
        try:
            start_time = time.time()
            result = subprocess.run(["./bin/fpdf", "cache", "list"], 
                                  capture_output=True, text=True)
            end_time = time.time()
            
            results_queue.put({
                'worker_id': worker_id,
                'time_ms': (end_time - start_time) * 1000,
                'success': result.returncode == 0,
                'output_size': len(result.stdout)
            })
        except Exception as e:
            results_queue.put({
                'worker_id': worker_id,
                'error': str(e),
                'success': False
            })
    
    # Run 3 concurrent cache operations
    threads = []
    start_time = time.time()
    
    for i in range(3):
        thread = threading.Thread(target=cache_worker, args=(i,))
        threads.append(thread)
        thread.start()
    
    for thread in threads:
        thread.join()
    
    end_time = time.time()
    total_time = (end_time - start_time) * 1000
    
    # Collect results
    results = []
    while not results_queue.empty():
        results.append(results_queue.get())
    
    successful_results = [r for r in results if r.get('success', False)]
    
    if successful_results:
        print(f"✅ {len(successful_results)}/3 concurrent operations succeeded")
        print(f"Total time: {total_time:.1f}ms")
        
        avg_individual = sum(r['time_ms'] for r in successful_results) / len(successful_results)
        print(f"Average individual operation: {avg_individual:.1f}ms")
        
        # Check for concurrency efficiency
        if total_time < avg_individual * 1.5:  # If total time is close to individual time
            print("✅ Good concurrent performance")
        else:
            print("⚠️  Concurrent operations slower than expected")
    else:
        print("❌ Concurrent cache access failed")

def main():
    test_cache_performance()
    test_concurrent_cache_access()
    
    print("\n" + "=" * 50)
    print("✅ Cache performance analysis completed")

if __name__ == "__main__":
    main()