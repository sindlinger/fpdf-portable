#!/usr/bin/env python3
"""
Quick Performance Baseline Tests for FilterPDF
"""

import subprocess
import time
import os
import sys

def time_command(cmd_args, iterations=3):
    """Time a command execution"""
    times = []
    fpdf_path = "./bin/fpdf"
    
    for i in range(iterations):
        start = time.time()
        try:
            result = subprocess.run([fpdf_path] + cmd_args, 
                                 capture_output=True, 
                                 text=True, 
                                 timeout=10)
            end = time.time()
            if result.returncode == 0:
                times.append(end - start)
            else:
                print(f"Error in iteration {i+1}: {result.stderr}")
        except subprocess.TimeoutExpired:
            print(f"Timeout in iteration {i+1}")
        except Exception as e:
            print(f"Exception in iteration {i+1}: {e}")
    
    if times:
        avg_time = sum(times) / len(times)
        return avg_time, min(times), max(times)
    return None, None, None

def main():
    print("🚀 Quick Performance Baseline Test")
    print("=" * 50)
    
    # Test 1: Startup Time (Help Command)
    print("📊 Testing startup time...")
    avg, min_t, max_t = time_command(["--help"])
    if avg:
        print(f"  Average: {avg*1000:.1f}ms")
        print(f"  Range: {min_t*1000:.1f}ms - {max_t*1000:.1f}ms")
    
    # Test 2: Version Command (Minimal operation)
    print("\n📋 Testing version command...")
    avg, min_t, max_t = time_command(["--version"])
    if avg:
        print(f"  Average: {avg*1000:.1f}ms")
        print(f"  Range: {min_t*1000:.1f}ms - {max_t*1000:.1f}ms")
    
    # Test 3: Cache list (DI-heavy operation)
    print("\n💾 Testing cache list...")
    avg, min_t, max_t = time_command(["cache", "list"])
    if avg:
        print(f"  Average: {avg*1000:.1f}ms")
        print(f"  Range: {min_t*1000:.1f}ms - {max_t*1000:.1f}ms")
    
    # Test 4: Check if any test PDFs exist
    test_pdfs = ["test.pdf", "tests/pdfs/simple.pdf"]
    test_pdf = None
    for pdf in test_pdfs:
        if os.path.exists(pdf):
            test_pdf = pdf
            break
    
    if test_pdf:
        print(f"\n📄 Testing with {test_pdf}...")
        
        # Test extract command
        print("  Extract command...")
        avg, min_t, max_t = time_command([test_pdf, "extract"])
        if avg:
            print(f"    Average: {avg*1000:.1f}ms")
            print(f"    Range: {min_t*1000:.1f}ms - {max_t*1000:.1f}ms")
        
        # Test filter metadata (lightweight)
        print("  Filter metadata...")
        avg, min_t, max_t = time_command([test_pdf, "filter", "metadata"])
        if avg:
            print(f"    Average: {avg*1000:.1f}ms")
            print(f"    Range: {min_t*1000:.1f}ms - {max_t*1000:.1f}ms")
    else:
        print("\n⚠️  No test PDF found, skipping PDF-specific tests")
    
    print("\n" + "=" * 50)
    print("✅ Quick performance test completed")

if __name__ == "__main__":
    main()