#!/usr/bin/env python3
"""
Measure DI Container Overhead by analyzing initialization times
"""

import subprocess
import re
import time
from datetime import datetime

def analyze_log_timing():
    """Analyze the timing between log entries to measure DI overhead"""
    
    log_file = "logs/fpdf-20250812.txt"
    
    try:
        with open(log_file, 'r') as f:
            lines = f.readlines()
        
        # Find recent entries with service initialization
        init_times = []
        start_times = []
        
        # Pattern to parse log timestamps
        timestamp_pattern = r'(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})'
        
        for line in lines[-50:]:  # Last 50 lines
            if "services initialized successfully" in line:
                match = re.search(timestamp_pattern, line)
                if match:
                    timestamp_str = match.group(1)
                    timestamp = datetime.strptime(timestamp_str, "%Y-%m-%d %H:%M:%S.%f")
                    init_times.append(timestamp)
            
            elif "application starting..." in line:
                match = re.search(timestamp_pattern, line)
                if match:
                    timestamp_str = match.group(1)
                    timestamp = datetime.strptime(timestamp_str, "%Y-%m-%d %H:%M:%S.%f")
                    start_times.append(timestamp)
        
        # Calculate DI initialization overhead
        di_overheads = []
        
        for i in range(min(len(init_times), len(start_times))):
            if i < len(start_times):
                # Time between service init and app start
                overhead = (start_times[i] - init_times[i]).total_seconds() * 1000
                if 0 < overhead < 1000:  # Reasonable range (0-1000ms)
                    di_overheads.append(overhead)
        
        if di_overheads:
            avg_overhead = sum(di_overheads) / len(di_overheads)
            max_overhead = max(di_overheads)
            min_overhead = min(di_overheads)
            
            return {
                'average_di_overhead_ms': avg_overhead,
                'min_di_overhead_ms': min_overhead,
                'max_di_overhead_ms': max_overhead,
                'samples': len(di_overheads)
            }
        
    except Exception as e:
        print(f"Error analyzing logs: {e}")
    
    return None

def measure_startup_components():
    """Measure different components of startup time"""
    
    # Clear log file to get clean measurements
    with open("logs/fpdf-20250812.txt", "w") as f:
        f.write("")
    
    print("🔍 Measuring startup components...")
    
    # Run a simple command and analyze the log
    start_time = time.time()
    result = subprocess.run(["./bin/fpdf", "--help"], 
                          capture_output=True, text=True)
    end_time = time.time()
    
    total_time = (end_time - start_time) * 1000
    
    # Wait a moment for logs to flush
    time.sleep(0.1)
    
    # Analyze logs for DI timing
    log_analysis = analyze_log_timing()
    
    return {
        'total_startup_time_ms': total_time,
        'command_success': result.returncode == 0,
        'log_analysis': log_analysis
    }

def main():
    print("⚙️  DI Container Overhead Analysis")
    print("=" * 50)
    
    # Run multiple measurements
    measurements = []
    
    for i in range(3):
        print(f"Running measurement {i+1}/3...")
        measurement = measure_startup_components()
        measurements.append(measurement)
        time.sleep(1)  # Space out measurements
    
    # Calculate averages
    total_times = [m['total_startup_time_ms'] for m in measurements if m['command_success']]
    
    if total_times:
        avg_total = sum(total_times) / len(total_times)
        print(f"\n📊 Startup Performance Summary")
        print("-" * 30)
        print(f"Average Total Startup: {avg_total:.1f}ms")
        print(f"Min/Max Range: {min(total_times):.1f}ms - {max(total_times):.1f}ms")
        
        # Analyze DI overhead from recent logs
        recent_analysis = analyze_log_timing()
        if recent_analysis:
            print(f"\n⚙️  DI Container Analysis")
            print("-" * 30)
            print(f"Average DI Overhead: {recent_analysis['average_di_overhead_ms']:.1f}ms")
            print(f"Range: {recent_analysis['min_di_overhead_ms']:.1f}ms - {recent_analysis['max_di_overhead_ms']:.1f}ms")
            print(f"Samples: {recent_analysis['samples']}")
            
            # Calculate percentage
            if avg_total > 0:
                di_percentage = (recent_analysis['average_di_overhead_ms'] / avg_total) * 100
                print(f"DI Overhead: {di_percentage:.1f}% of total startup time")
        
        # Performance assessment
        print(f"\n📈 Performance Assessment")
        print("-" * 30)
        
        if avg_total < 100:
            print("✅ Excellent startup performance")
        elif avg_total < 500:
            print("🟡 Good startup performance")
        elif avg_total < 1000:
            print("⚠️  Acceptable startup performance")
        else:
            print("❌ Poor startup performance - needs optimization")
        
        if recent_analysis:
            if recent_analysis['average_di_overhead_ms'] < 50:
                print("✅ Low DI container overhead")
            elif recent_analysis['average_di_overhead_ms'] < 200:
                print("🟡 Moderate DI container overhead")
            else:
                print("⚠️  High DI container overhead")
    
    print("\n" + "=" * 50)
    print("✅ DI overhead analysis completed")

if __name__ == "__main__":
    main()