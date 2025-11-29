#!/usr/bin/env python3
"""
Memory Usage Analysis for FilterPDF
"""

import subprocess
import psutil
import time
import os

def measure_memory_usage(cmd_args, duration=5):
    """Measure memory usage during command execution"""
    fpdf_path = "./bin/fpdf"
    
    # Get baseline memory
    baseline_memory = psutil.virtual_memory().used / 1024 / 1024  # MB
    
    # Start process
    process = subprocess.Popen([fpdf_path] + cmd_args, 
                             stdout=subprocess.PIPE, 
                             stderr=subprocess.PIPE)
    
    peak_memory = baseline_memory
    memory_samples = []
    start_time = time.time()
    
    try:
        while process.poll() is None and (time.time() - start_time) < duration:
            try:
                # Get process memory
                proc = psutil.Process(process.pid)
                proc_memory = proc.memory_info().rss / 1024 / 1024  # MB
                current_total = psutil.virtual_memory().used / 1024 / 1024  # MB
                
                memory_samples.append(proc_memory)
                peak_memory = max(peak_memory, current_total)
                
                time.sleep(0.1)  # Sample every 100ms
                
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                break
        
        # Wait for process to complete
        stdout, stderr = process.communicate(timeout=10)
        
    except subprocess.TimeoutExpired:
        process.kill()
        stdout, stderr = process.communicate()
    
    if memory_samples:
        avg_process_memory = sum(memory_samples) / len(memory_samples)
        max_process_memory = max(memory_samples)
        
        return {
            'success': process.returncode == 0,
            'avg_process_memory_mb': avg_process_memory,
            'max_process_memory_mb': max_process_memory,
            'peak_system_memory_mb': peak_memory,
            'baseline_memory_mb': baseline_memory,
            'memory_increase_mb': peak_memory - baseline_memory,
            'stdout_length': len(stdout) if stdout else 0,
            'stderr_length': len(stderr) if stderr else 0
        }
    
    return None

def main():
    print("🧠 Memory Usage Analysis")
    print("=" * 50)
    
    tests = [
        (["--help"], "Help Command"),
        (["--version"], "Version Command"),
        (["cache", "list"], "Cache List"),
    ]
    
    # Add PDF tests if available
    if os.path.exists("test.pdf"):
        tests.extend([
            (["test.pdf", "extract"], "Extract Command"),
            (["test.pdf", "filter", "metadata"], "Filter Metadata"),
        ])
    
    for cmd_args, test_name in tests:
        print(f"\n📊 {test_name}")
        print("-" * 30)
        
        result = measure_memory_usage(cmd_args)
        
        if result:
            if result['success']:
                print(f"✅ Success")
            else:
                print(f"❌ Failed")
            
            print(f"Process Memory (Avg): {result['avg_process_memory_mb']:.1f}MB")
            print(f"Process Memory (Peak): {result['max_process_memory_mb']:.1f}MB")
            print(f"System Memory Increase: {result['memory_increase_mb']:.1f}MB")
            print(f"Output Size: {result['stdout_length']} chars")
            
            if result['stderr_length'] > 0:
                print(f"Error Output: {result['stderr_length']} chars")
        else:
            print("❌ Could not measure memory usage")
    
    print("\n" + "=" * 50)
    print("✅ Memory analysis completed")

if __name__ == "__main__":
    main()