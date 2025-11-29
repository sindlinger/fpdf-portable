#!/usr/bin/env python3
"""
Comprehensive Performance Analysis for FilterPDF

Measures:
1. Startup time and memory usage
2. DI container resolution overhead  
3. Logging performance impact
4. Cache operations performance
5. Filter commands performance
6. Memory usage patterns
7. Concurrent access patterns
"""

import subprocess
import time
import json
import os
import sys
import psutil
from typing import Dict, List, Tuple, Optional
from datetime import datetime
from pathlib import Path
import tempfile
import shutil

class PerformanceMetrics:
    """Container for performance measurement results"""
    
    def __init__(self):
        self.startup_time = 0.0
        self.memory_peak = 0
        self.memory_initial = 0
        self.cpu_usage = 0.0
        self.operation_time = 0.0
        self.throughput = 0.0
        self.error_count = 0
        self.success_count = 0
        
    def to_dict(self) -> Dict:
        return {
            'startup_time_ms': round(self.startup_time * 1000, 2),
            'memory_peak_mb': round(self.memory_peak / 1024 / 1024, 2),
            'memory_initial_mb': round(self.memory_initial / 1024 / 1024, 2),
            'cpu_usage_percent': round(self.cpu_usage, 2),
            'operation_time_ms': round(self.operation_time * 1000, 2),
            'throughput_ops_per_sec': round(self.throughput, 2),
            'error_count': self.error_count,
            'success_count': self.success_count
        }

class FilterPDFPerformanceAnalyzer:
    """Main performance analysis class"""
    
    def __init__(self, fpdf_path: str = "./bin/fpdf"):
        self.fpdf_path = fpdf_path
        self.test_pdf = None
        self.results: Dict[str, PerformanceMetrics] = {}
        
        # Verify fpdf executable exists
        if not os.path.exists(fpdf_path):
            raise FileNotFoundError(f"fpdf executable not found at {fpdf_path}")
            
        # Create test PDF if it doesn't exist
        self._create_test_pdf()
        
    def _create_test_pdf(self):
        """Create a simple test PDF if none exists"""
        test_files = [
            "test.pdf",
            "tests/pdfs/simple.pdf", 
            "tests/pdfs/test-document.pdf"
        ]
        
        for test_file in test_files:
            if os.path.exists(test_file):
                self.test_pdf = test_file
                break
                
        if not self.test_pdf:
            print("Warning: No test PDF found. Some tests may fail.")
            self.test_pdf = "test.pdf"  # Use anyway for command structure tests
    
    def _run_command(self, args: List[str], timeout: int = 30) -> Tuple[subprocess.CompletedProcess, PerformanceMetrics]:
        """Run a command and measure performance"""
        metrics = PerformanceMetrics()
        
        # Record initial memory
        initial_memory = psutil.virtual_memory().used
        metrics.memory_initial = initial_memory
        
        start_time = time.time()
        
        try:
            # Start process
            process = subprocess.Popen(
                [self.fpdf_path] + args,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True
            )
            
            # Monitor resource usage
            peak_memory = initial_memory
            cpu_samples = []
            
            while process.poll() is None:
                try:
                    proc_info = psutil.Process(process.pid)
                    current_memory = proc_info.memory_info().rss + psutil.virtual_memory().used
                    peak_memory = max(peak_memory, current_memory)
                    
                    cpu_samples.append(proc_info.cpu_percent())
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    break
                    
                time.sleep(0.01)  # 10ms sampling
            
            # Wait for completion
            stdout, stderr = process.communicate(timeout=timeout)
            
            end_time = time.time()
            
            # Calculate metrics
            metrics.operation_time = end_time - start_time
            metrics.memory_peak = peak_memory
            metrics.cpu_usage = sum(cpu_samples) / len(cpu_samples) if cpu_samples else 0
            
            if process.returncode == 0:
                metrics.success_count = 1
            else:
                metrics.error_count = 1
            
            # Create result object
            result = subprocess.CompletedProcess(
                args=[self.fpdf_path] + args,
                returncode=process.returncode,
                stdout=stdout,
                stderr=stderr
            )
            
            return result, metrics
            
        except subprocess.TimeoutExpired:
            metrics.error_count = 1
            metrics.operation_time = timeout
            
            result = subprocess.CompletedProcess(
                args=[self.fpdf_path] + args,
                returncode=-1,
                stdout="",
                stderr=f"Timeout after {timeout}s"
            )
            
            return result, metrics
            
    def measure_startup_performance(self) -> PerformanceMetrics:
        """Measure application startup time and memory usage"""
        print("📊 Measuring startup performance...")
        
        metrics = PerformanceMetrics()
        iterations = 5
        
        startup_times = []
        memory_peaks = []
        
        for i in range(iterations):
            print(f"  Iteration {i+1}/{iterations}")
            
            result, iter_metrics = self._run_command(["--help"])
            
            if iter_metrics.success_count > 0:
                startup_times.append(iter_metrics.operation_time)
                memory_peaks.append(iter_metrics.memory_peak)
            
        if startup_times:
            metrics.startup_time = sum(startup_times) / len(startup_times)
            metrics.memory_peak = sum(memory_peaks) / len(memory_peaks)
            metrics.success_count = len(startup_times)
            metrics.error_count = iterations - len(startup_times)
        
        return metrics
    
    def measure_di_container_overhead(self) -> PerformanceMetrics:
        """Measure DI container resolution overhead"""
        print("⚙️  Measuring DI container overhead...")
        
        # Compare commands that heavily use DI vs simple operations
        metrics = PerformanceMetrics()
        
        # Test cache list (uses DI services)
        result1, metrics1 = self._run_command(["cache", "list"])
        
        # Test help (minimal DI usage)
        result2, metrics2 = self._run_command(["--help"])
        
        # Calculate overhead as difference
        if metrics1.success_count > 0 and metrics2.success_count > 0:
            metrics.operation_time = metrics1.operation_time - metrics2.operation_time
            metrics.memory_peak = metrics1.memory_peak - metrics2.memory_peak
            metrics.success_count = 1
        else:
            metrics.error_count = 1
        
        return metrics
    
    def measure_logging_performance(self) -> PerformanceMetrics:
        """Measure Serilog logging performance impact"""
        print("📝 Measuring logging performance impact...")
        
        metrics = PerformanceMetrics()
        
        # Test with verbose logging
        if os.path.exists(self.test_pdf):
            result, metrics = self._run_command([self.test_pdf, "extract", "--verbose"])
        else:
            # Fallback to cache command
            result, metrics = self._run_command(["cache", "list"])
        
        return metrics
    
    def measure_cache_performance(self) -> PerformanceMetrics:
        """Measure cache system performance"""
        print("💾 Measuring cache system performance...")
        
        metrics = PerformanceMetrics()
        operations = []
        
        # Test cache list
        result1, metrics1 = self._run_command(["cache", "list"])
        if metrics1.success_count > 0:
            operations.append(metrics1.operation_time)
        
        # Test cache clear (if safe)
        # Skip cache clear to avoid disrupting real cache
        
        # Test load operation if test PDF exists
        if os.path.exists(self.test_pdf):
            result2, metrics2 = self._run_command([self.test_pdf, "load", "--fast"])
            if metrics2.success_count > 0:
                operations.append(metrics2.operation_time)
        
        if operations:
            metrics.operation_time = sum(operations) / len(operations)
            metrics.success_count = len(operations)
            # Calculate throughput as operations per second
            if metrics.operation_time > 0:
                metrics.throughput = 1.0 / metrics.operation_time
        
        return metrics
    
    def measure_filter_performance(self) -> PerformanceMetrics:
        """Measure filter command performance"""
        print("🔍 Measuring filter performance...")
        
        metrics = PerformanceMetrics()
        
        if not os.path.exists(self.test_pdf):
            print(f"  Skipping filter tests - no test PDF found")
            return metrics
        
        filter_operations = [
            [self.test_pdf, "filter", "pages", "--word", "test"],
            [self.test_pdf, "filter", "metadata"],
        ]
        
        operation_times = []
        success_count = 0
        error_count = 0
        
        for operation in filter_operations:
            print(f"  Testing: {' '.join(operation[2:])}")
            result, op_metrics = self._run_command(operation)
            
            if op_metrics.success_count > 0:
                operation_times.append(op_metrics.operation_time)
                success_count += 1
            else:
                error_count += 1
        
        if operation_times:
            metrics.operation_time = sum(operation_times) / len(operation_times)
            metrics.success_count = success_count
            metrics.error_count = error_count
            
            # Calculate throughput
            if metrics.operation_time > 0:
                metrics.throughput = 1.0 / metrics.operation_time
        
        return metrics
    
    def measure_memory_usage_patterns(self) -> PerformanceMetrics:
        """Measure memory usage patterns"""
        print("🧠 Measuring memory usage patterns...")
        
        metrics = PerformanceMetrics()
        
        # Test memory-intensive operations
        if os.path.exists(self.test_pdf):
            # Load operation (memory intensive)
            result, load_metrics = self._run_command([self.test_pdf, "load", "--ultra"])
            
            if load_metrics.success_count > 0:
                metrics.memory_peak = load_metrics.memory_peak
                metrics.operation_time = load_metrics.operation_time
                metrics.success_count = 1
            else:
                metrics.error_count = 1
        
        return metrics
    
    def measure_concurrent_performance(self) -> PerformanceMetrics:
        """Measure concurrent access patterns"""
        print("🔄 Measuring concurrent performance...")
        
        metrics = PerformanceMetrics()
        
        # Simple concurrent test - multiple help commands
        import threading
        
        results = []
        errors = []
        
        def run_command():
            try:
                result, cmd_metrics = self._run_command(["--help"])
                results.append(cmd_metrics.operation_time)
            except Exception as e:
                errors.append(str(e))
        
        # Run 3 concurrent instances
        threads = []
        start_time = time.time()
        
        for _ in range(3):
            thread = threading.Thread(target=run_command)
            threads.append(thread)
            thread.start()
        
        for thread in threads:
            thread.join()
        
        end_time = time.time()
        
        if results:
            metrics.operation_time = end_time - start_time
            metrics.success_count = len(results)
            metrics.error_count = len(errors)
            metrics.throughput = len(results) / metrics.operation_time if metrics.operation_time > 0 else 0
        
        return metrics
    
    def run_comprehensive_analysis(self) -> Dict[str, PerformanceMetrics]:
        """Run all performance tests"""
        print("🚀 Starting comprehensive performance analysis...")
        print(f"Using executable: {self.fpdf_path}")
        print(f"Test PDF: {self.test_pdf}")
        print()
        
        tests = [
            ("startup", self.measure_startup_performance),
            ("di_container", self.measure_di_container_overhead),
            ("logging", self.measure_logging_performance),
            ("cache", self.measure_cache_performance),
            ("filter", self.measure_filter_performance),
            ("memory", self.measure_memory_usage_patterns),
            ("concurrent", self.measure_concurrent_performance),
        ]
        
        results = {}
        
        for test_name, test_func in tests:
            try:
                print(f"Running {test_name} tests...")
                metrics = test_func()
                results[test_name] = metrics
                print(f"✅ {test_name} tests completed")
                print()
            except Exception as e:
                print(f"❌ {test_name} tests failed: {e}")
                results[test_name] = PerformanceMetrics()  # Empty metrics
                results[test_name].error_count = 1
                print()
        
        self.results = results
        return results
    
    def generate_report(self) -> str:
        """Generate comprehensive performance report"""
        if not self.results:
            return "No performance data available. Run analysis first."
        
        report = []
        report.append("# FilterPDF Performance Analysis Report")
        report.append(f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        report.append("")
        
        # Executive Summary
        report.append("## Executive Summary")
        report.append("")
        
        total_successes = sum(m.success_count for m in self.results.values())
        total_errors = sum(m.error_count for m in self.results.values())
        success_rate = (total_successes / (total_successes + total_errors)) * 100 if (total_successes + total_errors) > 0 else 0
        
        report.append(f"- **Overall Success Rate**: {success_rate:.1f}%")
        
        # Get startup time
        startup_metrics = self.results.get('startup')
        if startup_metrics and startup_metrics.startup_time > 0:
            startup_ms = startup_metrics.startup_time * 1000
            report.append(f"- **Application Startup Time**: {startup_ms:.1f}ms")
            
            # Evaluate startup performance
            if startup_ms < 100:
                report.append("  - ✅ Excellent startup performance")
            elif startup_ms < 500:
                report.append("  - 🟡 Good startup performance")
            else:
                report.append("  - ⚠️  Slow startup performance")
        
        # Memory usage
        memory_metrics = self.results.get('memory')
        if memory_metrics and memory_metrics.memory_peak > 0:
            memory_mb = memory_metrics.memory_peak / 1024 / 1024
            report.append(f"- **Peak Memory Usage**: {memory_mb:.1f}MB")
            
            if memory_mb < 100:
                report.append("  - ✅ Excellent memory efficiency")
            elif memory_mb < 500:
                report.append("  - 🟡 Good memory efficiency")
            else:
                report.append("  - ⚠️  High memory usage")
        
        report.append("")
        
        # Detailed Results
        report.append("## Detailed Performance Metrics")
        report.append("")
        
        for test_name, metrics in self.results.items():
            report.append(f"### {test_name.title()} Performance")
            report.append("")
            
            metrics_dict = metrics.to_dict()
            
            for key, value in metrics_dict.items():
                if value > 0 or key in ['error_count', 'success_count']:
                    formatted_key = key.replace('_', ' ').title()
                    report.append(f"- **{formatted_key}**: {value}")
            
            # Add performance assessment
            if test_name == 'startup' and metrics.startup_time > 0:
                startup_ms = metrics.startup_time * 1000
                if startup_ms < 100:
                    report.append("- **Assessment**: ✅ Excellent")
                elif startup_ms < 500:
                    report.append("- **Assessment**: 🟡 Good")
                else:
                    report.append("- **Assessment**: ⚠️  Needs improvement")
            
            elif test_name == 'di_container' and metrics.operation_time > 0:
                overhead_ms = metrics.operation_time * 1000
                if overhead_ms < 10:
                    report.append("- **Assessment**: ✅ Minimal DI overhead")
                elif overhead_ms < 50:
                    report.append("- **Assessment**: 🟡 Acceptable DI overhead")
                else:
                    report.append("- **Assessment**: ⚠️  High DI overhead")
            
            elif test_name == 'cache' and metrics.throughput > 0:
                if metrics.throughput > 10:
                    report.append("- **Assessment**: ✅ High cache performance")
                elif metrics.throughput > 1:
                    report.append("- **Assessment**: 🟡 Good cache performance")
                else:
                    report.append("- **Assessment**: ⚠️  Low cache performance")
            
            report.append("")
        
        # Recommendations
        report.append("## Performance Recommendations")
        report.append("")
        
        # Analyze startup performance
        startup_metrics = self.results.get('startup')
        if startup_metrics and startup_metrics.startup_time > 0.5:
            report.append("### Startup Optimization")
            report.append("- Consider lazy loading of non-critical services")
            report.append("- Optimize DI container configuration")
            report.append("- Review assembly loading patterns")
            report.append("")
        
        # Analyze DI overhead
        di_metrics = self.results.get('di_container')
        if di_metrics and di_metrics.operation_time > 0.05:  # 50ms
            report.append("### DI Container Optimization")
            report.append("- Review service registration patterns")
            report.append("- Consider singleton vs transient lifetimes")
            report.append("- Optimize service resolution paths")
            report.append("")
        
        # Analyze memory usage
        memory_metrics = self.results.get('memory')
        if memory_metrics and memory_metrics.memory_peak > 500 * 1024 * 1024:  # 500MB
            report.append("### Memory Optimization")
            report.append("- Implement streaming for large PDF operations")
            report.append("- Review cache size limitations")
            report.append("- Consider memory pooling for frequent allocations")
            report.append("")
        
        # Cache performance
        cache_metrics = self.results.get('cache')
        if cache_metrics and cache_metrics.throughput < 1:
            report.append("### Cache Performance")
            report.append("- Validate cache hit/miss ratios")
            report.append("- Consider cache warming strategies")
            report.append("- Review cache serialization performance")
            report.append("")
        
        return '\n'.join(report)
    
    def save_results(self, output_file: str = "performance-analysis-results.json"):
        """Save raw results to JSON file"""
        if not self.results:
            print("No results to save")
            return
        
        output_data = {
            'timestamp': datetime.now().isoformat(),
            'fpdf_executable': self.fpdf_path,
            'test_pdf': self.test_pdf,
            'system_info': {
                'cpu_count': os.cpu_count(),
                'memory_total_gb': round(psutil.virtual_memory().total / 1024 / 1024 / 1024, 2),
                'platform': sys.platform
            },
            'results': {name: metrics.to_dict() for name, metrics in self.results.items()}
        }
        
        with open(output_file, 'w') as f:
            json.dump(output_data, f, indent=2)
        
        print(f"Results saved to: {output_file}")


def main():
    """Main entry point"""
    
    # Check if fpdf executable exists
    fpdf_path = "./bin/fpdf"
    if not os.path.exists(fpdf_path):
        print(f"Error: fpdf executable not found at {fpdf_path}")
        print("Please build the application first:")
        print("  dotnet publish FilterPDFC#.csproj -c Release")
        sys.exit(1)
    
    try:
        # Initialize analyzer
        analyzer = FilterPDFPerformanceAnalyzer(fpdf_path)
        
        # Run comprehensive analysis
        results = analyzer.run_comprehensive_analysis()
        
        # Generate and display report
        report = analyzer.generate_report()
        print("=" * 80)
        print(report)
        
        # Save results
        analyzer.save_results()
        
        # Save report to file
        with open("performance-analysis-report.md", "w") as f:
            f.write(report)
        
        print("\n" + "=" * 80)
        print("📊 Performance analysis completed!")
        print("📁 Results saved to: performance-analysis-results.json")
        print("📄 Report saved to: performance-analysis-report.md")
        
    except Exception as e:
        print(f"Error during performance analysis: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()