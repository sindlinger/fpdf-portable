#!/bin/bash

# FilterPDF Test Runner Script
# Comprehensive test execution with reporting and validation

set -e

# Configuration
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_PROJECT="FilterPDF.Tests.csproj"
RESULTS_DIR="$PROJECT_ROOT/TestResults"
COVERAGE_DIR="$PROJECT_ROOT/Coverage"
LOG_FILE="$RESULTS_DIR/test-execution.log"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging function
log() {
    echo -e "${BLUE}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1" | tee -a "$LOG_FILE"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1" | tee -a "$LOG_FILE"
}

success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1" | tee -a "$LOG_FILE"
}

warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1" | tee -a "$LOG_FILE"
}

# Create directories
create_directories() {
    log "Creating test directories..."
    mkdir -p "$RESULTS_DIR"
    mkdir -p "$COVERAGE_DIR"
    mkdir -p "$PROJECT_ROOT/Tests/TestData"
}

# Clean previous results
clean_previous_results() {
    log "Cleaning previous test results..."
    rm -rf "$RESULTS_DIR"/*
    rm -rf "$COVERAGE_DIR"/*
    rm -f "$PROJECT_ROOT/bin/TestResults"/*
}

# Check prerequisites
check_prerequisites() {
    log "Checking prerequisites..."
    
    # Check .NET 6.0
    if ! dotnet --version | grep -q "6\."; then
        error ".NET 6.0 is required but not found"
        exit 1
    fi
    
    # Check if test project exists
    if [ ! -f "$PROJECT_ROOT/$TEST_PROJECT" ]; then
        error "Test project $TEST_PROJECT not found"
        exit 1
    fi
    
    # Check if main project compiles
    log "Verifying main project compiles..."
    if ! dotnet build "$PROJECT_ROOT/FilterPDFC#.csproj" --configuration Release --verbosity quiet; then
        error "Main project compilation failed"
        exit 1
    fi
    
    success "Prerequisites check passed"
}

# Restore packages
restore_packages() {
    log "Restoring NuGet packages..."
    dotnet restore "$PROJECT_ROOT/$TEST_PROJECT" --verbosity quiet
    success "Package restoration completed"
}

# Build test project
build_tests() {
    log "Building test project..."
    if ! dotnet build "$PROJECT_ROOT/$TEST_PROJECT" --configuration Release --no-restore --verbosity quiet; then
        error "Test project build failed"
        exit 1
    fi
    success "Test project build completed"
}

# Run unit tests
run_unit_tests() {
    log "Running unit tests..."
    
    dotnet test "$PROJECT_ROOT/$TEST_PROJECT" \
        --configuration Release \
        --no-build \
        --logger "trx;LogFileName=unit-tests.trx" \
        --results-directory "$RESULTS_DIR" \
        --filter "Category!=Integration" \
        --collect:"XPlat Code Coverage" \
        --settings:"$PROJECT_ROOT/Tests/coverlet.runsettings" \
        --verbosity normal
    
    local exit_code=$?
    if [ $exit_code -eq 0 ]; then
        success "Unit tests passed"
    else
        error "Unit tests failed with exit code $exit_code"
        return $exit_code
    fi
}

# Run integration tests
run_integration_tests() {
    log "Running integration tests..."
    
    # Build main executable first
    log "Building main executable for integration tests..."
    dotnet publish "$PROJECT_ROOT/FilterPDFC#.csproj" -c Release --verbosity quiet
    
    if [ ! -f "$PROJECT_ROOT/bin/fpdf" ]; then
        error "Main executable not found after build"
        return 1
    fi
    
    dotnet test "$PROJECT_ROOT/$TEST_PROJECT" \
        --configuration Release \
        --no-build \
        --logger "trx;LogFileName=integration-tests.trx" \
        --results-directory "$RESULTS_DIR" \
        --filter "Category=Integration" \
        --verbosity normal
    
    local exit_code=$?
    if [ $exit_code -eq 0 ]; then
        success "Integration tests passed"
    else
        error "Integration tests failed with exit code $exit_code"
        return $exit_code
    fi
}

# Run security tests
run_security_tests() {
    log "Running security tests..."
    
    dotnet test "$PROJECT_ROOT/$TEST_PROJECT" \
        --configuration Release \
        --no-build \
        --logger "trx;LogFileName=security-tests.trx" \
        --results-directory "$RESULTS_DIR" \
        --filter "FullyQualifiedName~SecurityTests" \
        --verbosity normal
    
    local exit_code=$?
    if [ $exit_code -eq 0 ]; then
        success "Security tests passed"
    else
        error "Security tests failed with exit code $exit_code"
        return $exit_code
    fi
}

# Run performance tests
run_performance_tests() {
    log "Running performance tests..."
    
    dotnet test "$PROJECT_ROOT/$TEST_PROJECT" \
        --configuration Release \
        --no-build \
        --logger "trx;LogFileName=performance-tests.trx" \
        --results-directory "$RESULTS_DIR" \
        --filter "Category=Performance" \
        --verbosity normal
    
    local exit_code=$?
    if [ $exit_code -eq 0 ]; then
        success "Performance tests passed"
    else
        warning "Performance tests failed with exit code $exit_code"
        # Don't fail the entire test suite for performance tests
        return 0
    fi
}

# Generate coverage report
generate_coverage_report() {
    log "Generating coverage report..."
    
    # Find coverage files
    COVERAGE_FILES=$(find "$RESULTS_DIR" -name "coverage.cobertura.xml" | head -1)
    
    if [ -z "$COVERAGE_FILES" ]; then
        warning "No coverage files found"
        return 0
    fi
    
    # Install reportgenerator if not available
    if ! command -v reportgenerator &> /dev/null; then
        log "Installing ReportGenerator..."
        dotnet tool install --global dotnet-reportgenerator-globaltool --verbosity quiet
    fi
    
    # Generate HTML report
    reportgenerator \
        -reports:"$COVERAGE_FILES" \
        -targetdir:"$COVERAGE_DIR" \
        -reporttypes:"Html;Cobertura;TextSummary" \
        -verbosity:Warning
    
    success "Coverage report generated at $COVERAGE_DIR"
    
    # Display coverage summary
    if [ -f "$COVERAGE_DIR/Summary.txt" ]; then
        log "Coverage Summary:"
        cat "$COVERAGE_DIR/Summary.txt" | tee -a "$LOG_FILE"
    fi
}

# Validate test results
validate_results() {
    log "Validating test results..."
    
    local total_tests=0
    local passed_tests=0
    local failed_tests=0
    
    # Parse TRX files for results
    for trx_file in "$RESULTS_DIR"/*.trx; do
        if [ -f "$trx_file" ]; then
            local file_total=$(grep -o 'total="[0-9]*"' "$trx_file" | grep -o '[0-9]*' || echo "0")
            local file_passed=$(grep -o 'passed="[0-9]*"' "$trx_file" | grep -o '[0-9]*' || echo "0")
            local file_failed=$(grep -o 'failed="[0-9]*"' "$trx_file" | grep -o '[0-9]*' || echo "0")
            
            total_tests=$((total_tests + file_total))
            passed_tests=$((passed_tests + file_passed))
            failed_tests=$((failed_tests + file_failed))
        fi
    done
    
    log "Test Results Summary:"
    log "  Total Tests: $total_tests"
    log "  Passed: $passed_tests"
    log "  Failed: $failed_tests"
    
    if [ $failed_tests -gt 0 ]; then
        error "Test validation failed: $failed_tests test(s) failed"
        return 1
    else
        success "All tests passed successfully"
        return 0
    fi
}

# Cleanup test artifacts
cleanup() {
    log "Cleaning up test artifacts..."
    
    # Remove temporary test data
    find "$PROJECT_ROOT" -name "*FilterPDFTest*" -type d -exec rm -rf {} + 2>/dev/null || true
    find "/tmp" -name "*FilterPDF*" -type d -exec rm -rf {} + 2>/dev/null || true
    
    success "Cleanup completed"
}

# Main execution function
run_all_tests() {
    log "Starting FilterPDF comprehensive test suite..."
    
    local start_time=$(date +%s)
    local overall_result=0
    
    # Setup
    create_directories
    clean_previous_results
    check_prerequisites
    restore_packages
    build_tests
    
    # Execute test suites
    if ! run_unit_tests; then
        overall_result=1
    fi
    
    if ! run_integration_tests; then
        overall_result=1
    fi
    
    if ! run_security_tests; then
        overall_result=1
    fi
    
    # Performance tests (non-blocking)
    run_performance_tests
    
    # Generate reports
    generate_coverage_report
    
    # Validate and cleanup
    if ! validate_results; then
        overall_result=1
    fi
    
    cleanup
    
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    
    log "Test suite completed in ${duration} seconds"
    
    if [ $overall_result -eq 0 ]; then
        success "All test suites passed successfully!"
        success "Test results available in: $RESULTS_DIR"
        success "Coverage report available in: $COVERAGE_DIR/index.html"
    else
        error "One or more test suites failed"
        error "Check logs in: $LOG_FILE"
    fi
    
    return $overall_result
}

# Script argument handling
case "${1:-all}" in
    "unit")
        create_directories
        check_prerequisites
        restore_packages
        build_tests
        run_unit_tests
        ;;
    "integration")
        create_directories
        check_prerequisites
        restore_packages
        build_tests
        run_integration_tests
        ;;
    "security")
        create_directories
        check_prerequisites
        restore_packages
        build_tests
        run_security_tests
        ;;
    "performance")
        create_directories
        check_prerequisites
        restore_packages
        build_tests
        run_performance_tests
        ;;
    "coverage")
        generate_coverage_report
        ;;
    "clean")
        clean_previous_results
        cleanup
        ;;
    "all"|*)
        run_all_tests
        ;;
esac

exit $?