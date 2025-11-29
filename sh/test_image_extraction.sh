#!/bin/bash

# Test script for new image extraction functionality
# Tests the improved PNG extraction with the new ImageExtractionService

echo "🧪 Testing Image Extraction Service"
echo "=================================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test configuration
TEST_DIR="./test_extraction_output"
LOG_FILE="./test_extraction.log"

# Create test output directory
echo -e "${BLUE}📁 Creating test directory: $TEST_DIR${NC}"
mkdir -p "$TEST_DIR"

# Function to run test and check result
run_test() {
    local test_name="$1"
    local command="$2"
    local expected_files="$3"
    
    echo ""
    echo -e "${YELLOW}🔬 Test: $test_name${NC}"
    echo "Command: $command"
    echo "Expected files: $expected_files"
    echo ""
    
    # Run the command
    eval "$command" 2>&1 | tee -a "$LOG_FILE"
    local exit_code=${PIPESTATUS[0]}
    
    if [ $exit_code -eq 0 ]; then
        echo -e "${GREEN}✅ Command executed successfully${NC}"
        
        # Check if expected files were created
        if [ -n "$expected_files" ]; then
            local files_found=0
            for pattern in $expected_files; do
                local count=$(find "$TEST_DIR" -name "$pattern" | wc -l)
                files_found=$((files_found + count))
                echo "Found $count files matching: $pattern"
            done
            
            if [ $files_found -gt 0 ]; then
                echo -e "${GREEN}✅ Files created successfully ($files_found files)${NC}"
                
                # Show file details
                echo "📊 File details:"
                find "$TEST_DIR" -name "*.png" -o -name "*.jpg" | while read file; do
                    if [ -f "$file" ]; then
                        local size=$(stat -f%z "$file" 2>/dev/null || stat -c%s "$file" 2>/dev/null || echo "unknown")
                        echo "  • $(basename "$file"): ${size} bytes"
                    fi
                done
            else
                echo -e "${RED}❌ No expected files were created${NC}"
            fi
        fi
    else
        echo -e "${RED}❌ Command failed with exit code: $exit_code${NC}"
    fi
}

# Clear log file
echo "🧪 Image Extraction Test - $(date)" > "$LOG_FILE"

# Check if fpdf is available
if ! command -v ./fpdf &> /dev/null && ! command -v ./bin/fpdf &> /dev/null; then
    echo -e "${RED}❌ fpdf command not found. Please build the project first.${NC}"
    echo "Run: ./build.sh"
    exit 1
fi

# Determine fpdf command path
FPDF_CMD="./fpdf"
if [ -f "./bin/fpdf" ]; then
    FPDF_CMD="./bin/fpdf"
elif [ -f "./publish/fpdf" ]; then
    FPDF_CMD="./publish/fpdf"
fi

echo -e "${BLUE}📋 Using fpdf command: $FPDF_CMD${NC}"

# Test 1: Basic image listing (should work with existing functionality)
run_test "List images from cache index 1" \
    "$FPDF_CMD 1 images" \
    ""

# Test 2: PNG extraction with minimum height filter
run_test "Extract PNG images with min-height 1000" \
    "$FPDF_CMD 1-10 images --min-height 1000 -F png --output-dir '$TEST_DIR'" \
    "*.png"

# Test 3: PNG extraction with size range
run_test "Extract images with specific size range" \
    "$FPDF_CMD 1-5 images --min-width 700 --max-width 800 --min-height 1000 -F png --output-dir '$TEST_DIR'" \
    "*.png"

# Test 4: JSON output with PNG extraction
run_test "JSON output with PNG extraction" \
    "$FPDF_CMD 1-3 images --min-height 500 -F json --output-dir '$TEST_DIR' -o '$TEST_DIR/extraction_report.json'" \
    "extraction_report.json"

# Test 5: Count images only
run_test "Count images matching criteria" \
    "$FPDF_CMD 1-10 images --min-height 1000 -F count" \
    ""

echo ""
echo "🏁 Test Summary"
echo "==============="

# Count created files
total_images=$(find "$TEST_DIR" -name "*.png" -o -name "*.jpg" | wc -l)
total_size=$(find "$TEST_DIR" -name "*.png" -o -name "*.jpg" -exec stat -f%z {} + 2>/dev/null | awk '{sum+=$1} END {print sum}' || echo "0")

echo "📊 Results:"
echo "  • Total images extracted: $total_images"
echo "  • Total size: $total_size bytes"
echo "  • Test directory: $TEST_DIR"
echo "  • Log file: $LOG_FILE"

if [ $total_images -gt 0 ]; then
    echo -e "${GREEN}🎉 SUCCESS: Images were extracted successfully!${NC}"
    
    # Show largest files
    echo ""
    echo "📈 Largest extracted files:"
    find "$TEST_DIR" -name "*.png" -o -name "*.jpg" -exec ls -la {} + | sort -k5 -nr | head -5
    
    # Test image validation
    echo ""
    echo -e "${BLUE}🔍 Validating extracted images...${NC}"
    
    # Simple validation using file command
    invalid_count=0
    find "$TEST_DIR" -name "*.png" -o -name "*.jpg" | while read file; do
        if command -v file &> /dev/null; then
            file_type=$(file "$file" | grep -i "image\|png\|jpeg")
            if [ -z "$file_type" ]; then
                echo -e "${RED}⚠️  Invalid image: $(basename "$file")${NC}"
                invalid_count=$((invalid_count + 1))
            fi
        fi
    done
    
    if [ $invalid_count -eq 0 ]; then
        echo -e "${GREEN}✅ All extracted files appear to be valid images${NC}"
    fi
    
else
    echo -e "${YELLOW}⚠️  No images were extracted. This could be due to:${NC}"
    echo "  • No cached PDFs with images at the specified indices"
    echo "  • Filters too restrictive (min-height, size requirements)"
    echo "  • Original PDF files not accessible"
    echo "  • Images data not available in cache"
    echo ""
    echo -e "${BLUE}💡 Suggestions:${NC}"
    echo "  • Try different cache indices (fpdf 1 images to see available images)"
    echo "  • Reduce filter criteria (lower min-height)"
    echo "  • Load PDFs with image extraction enabled first"
    echo "  • Check that original PDF files are accessible"
fi

echo ""
echo -e "${BLUE}📝 Full test log available at: $LOG_FILE${NC}"
echo -e "${BLUE}🔍 Test files in directory: $TEST_DIR${NC}"

echo ""
echo "🧪 Test completed at $(date)"