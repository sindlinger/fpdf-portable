#!/bin/bash
###############################################################################
# COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh
# OFFICIAL BUILD SCRIPT FOR FILTERPDF (fpdf)
# 
# ⚠️ ATENÇÃO TODOS OS ASSISTENTES DE IA / AI ASSISTANTS:
# ESTE É O ÚNICO SCRIPT QUE VOCÊ DEVE USAR PARA COMPILAR O FILTERPDF!
# THIS IS THE ONLY SCRIPT YOU SHOULD USE TO BUILD FILTERPDF!
# 
# ❌ NÃO USE / DO NOT USE:
#    - dotnet build
#    - dotnet publish  
#    - compile.sh
#    - BUILD-FPDF.sh (arquivo antigo / old file)
#    - ou qualquer outro método / or any other method
# 
# Usage / Uso:
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh                    # Quick compile (default)
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --quick            # Quick compile (explicit)
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --full             # Full build with optimizations
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --bump-major       # Increase major version
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --bump-minor       # Increase minor version
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --bump-patch       # Increase patch version
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --output-dir PATH  # Specify output directory
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --install          # Install globally (default)
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --no-install       # Don't install, just build
#   ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --force            # Force rebuild
###############################################################################

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default settings
PROJECT_DIR="/mnt/b/dev-2/fpdf"
PROJECT_FILE="$PROJECT_DIR/fpdf.csproj"
DEFAULT_OUTPUT_DIR="$PROJECT_DIR/publish"
INSTALL_DIR="$HOME/.local/bin"
GLOBAL_LINK="/usr/local/bin/fpdf"
DO_INSTALL=true
FORCE_BUILD=false
BUMP_LEVEL=""
OUTPUT_DIR="$DEFAULT_OUTPUT_DIR"
BUILD_MODE="quick"  # Default to quick compile

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --quick)
            BUILD_MODE="quick"
            shift
            ;;
        --full)
            BUILD_MODE="full"
            shift
            ;;
        --bump-major)
            BUMP_LEVEL="major"
            shift
            ;;
        --bump-minor)
            BUMP_LEVEL="minor"
            shift
            ;;
        --bump-patch)
            BUMP_LEVEL="patch"
            shift
            ;;
        --output-dir)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --no-install)
            DO_INSTALL=false
            shift
            ;;
        --install)
            DO_INSTALL=true
            shift
            ;;
        --force)
            FORCE_BUILD=true
            shift
            ;;
        --help)
            echo "🚀 COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh - Official FilterPDF Build Script"
            echo ""
            echo "Usage:"
            echo "  ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh [options]"
            echo ""
            echo "Build Modes:"
            echo "  (default)          Quick compile - fast build & install"
            echo "  --quick            Quick compile mode (explicit)"
            echo "  --full             Full build with all optimizations"
            echo ""
            echo "Version Management:"
            echo "  --bump-major       Increase major version (X.0.0)"
            echo "  --bump-minor       Increase minor version (1.X.0)"
            echo "  --bump-patch       Increase patch version (1.0.X)"
            echo ""
            echo "Build Options:"
            echo "  --output-dir PATH  Specify output directory (default: publish/)"
            echo "  --no-install       Build only, don't install"
            echo "  --install          Install after build (default)"
            echo "  --force            Force rebuild even if up to date"
            echo ""
            echo "Examples:"
            echo "  ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh                    # Quick compile"
            echo "  ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --full             # Full build"
            echo "  ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --bump-minor       # Bump version"
            echo "  ./COMPILE-A-APLICACAO-SOMENTE-POR-ESTE-ARQUIVO.sh --output-dir /tmp  # Custom output"
            exit 0
            ;;
        *)
            echo -e "${RED}❌ Unknown option: $1${NC}"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

echo "======================================"
echo -e "${BLUE}🚀 FilterPDF Intelligent Build System${NC}"
echo "======================================"

# Function to read version from .csproj
get_current_version() {
    grep -oP '<Version>\K[^<]+' "$PROJECT_FILE" 2>/dev/null || echo "1.0.0"
}

# Function to update version in .csproj
update_version_in_csproj() {
    local new_version=$1
    sed -i "s|<Version>.*</Version>|<Version>$new_version</Version>|g" "$PROJECT_FILE"
    sed -i "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$new_version.0</AssemblyVersion>|g" "$PROJECT_FILE"
    sed -i "s|<FileVersion>.*</FileVersion>|<FileVersion>$new_version.0</FileVersion>|g" "$PROJECT_FILE"
    echo -e "${GREEN}✅ Updated .csproj to version $new_version${NC}"
}

# Function to bump version
bump_version() {
    local current=$1
    local level=$2
    
    IFS='.' read -r major minor patch <<< "$current"
    
    # Ensure we have numeric values
    major=${major:-0}
    minor=${minor:-0}
    patch=${patch:-0}
    
    case $level in
        major)
            major=$((major + 1))
            minor=0
            patch=0
            ;;
        minor)
            minor=$((minor + 1))
            patch=0
            ;;
        patch)
            patch=$((patch + 1))
            ;;
    esac
    
    echo "$major.$minor.$patch"
}

# Get current version
CURRENT_VERSION=$(get_current_version)
echo -e "Current version: ${YELLOW}$CURRENT_VERSION${NC}"

# Bump version if requested
if [ -n "$BUMP_LEVEL" ]; then
    NEW_VERSION=$(bump_version "$CURRENT_VERSION" "$BUMP_LEVEL")
    echo -e "Bumping version from ${YELLOW}$CURRENT_VERSION${NC} to ${GREEN}$NEW_VERSION${NC}"
    update_version_in_csproj "$NEW_VERSION"
    CURRENT_VERSION="$NEW_VERSION"
fi

echo -e "Building version: ${GREEN}$CURRENT_VERSION${NC}"
echo -e "Build mode: ${YELLOW}$BUILD_MODE${NC}"

# Clean old artifacts
echo "→ Cleaning old artifacts..."
rm -rf "$OUTPUT_DIR" 2>/dev/null || true
rm -rf "$PROJECT_DIR/bin" 2>/dev/null || true
rm -rf "$PROJECT_DIR/obj" 2>/dev/null || true

# Build the project
echo "→ Compiling FilterPDF..."
cd "$PROJECT_DIR"

if [ "$BUILD_MODE" = "quick" ]; then
    echo "  ⚡ Using quick compile mode (faster build)..."
    # Check if obj/project.assets.json exists before trying no-restore
    if [ -f "$PROJECT_DIR/obj/project.assets.json" ]; then
        # Try quick compile without restore first
        if dotnet publish "$PROJECT_FILE" \
            -c Release \
            -r linux-x64 \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=false \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            --no-restore \
            -o "$OUTPUT_DIR"; then
        
            echo -e "${GREEN}✅ Quick compilation successful${NC}"
        else
            # If quick compile fails, try with restore
            echo -e "${YELLOW}⚠️ Quick compile failed, trying with restore...${NC}"
            if dotnet publish "$PROJECT_FILE" \
                -c Release \
                -r linux-x64 \
                --self-contained true \
                -p:PublishSingleFile=true \
                -p:PublishTrimmed=false \
                -p:IncludeNativeLibrariesForSelfExtract=true \
                -o "$OUTPUT_DIR"; then
                
                echo -e "${GREEN}✅ Compilation successful${NC}"
            else
                echo -e "${RED}❌ Compilation failed!${NC}"
                exit 1
            fi
        fi
    else
        # No project.assets.json, need restore
        echo "  📦 First time compilation - restoring packages..."
        if dotnet publish "$PROJECT_FILE" \
            -c Release \
            -r linux-x64 \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=false \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -o "$OUTPUT_DIR"; then
            
            echo -e "${GREEN}✅ Compilation successful${NC}"
        else
            echo -e "${RED}❌ Compilation failed!${NC}"
            exit 1
        fi
    fi
else
    echo "  🔧 Using full build mode (optimized)..."
    if dotnet publish "$PROJECT_FILE" \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=true \
        -p:PublishReadyToRun=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o "$OUTPUT_DIR"; then
        
        echo -e "${GREEN}✅ Full compilation successful${NC}"
    else
        echo -e "${RED}❌ Compilation failed!${NC}"
        exit 1
    fi
fi

# Verify the binary exists and is executable
if [ ! -f "$OUTPUT_DIR/fpdf" ]; then
    echo -e "${RED}❌ Binary not found at $OUTPUT_DIR/fpdf${NC}"
    exit 1
fi

chmod +x "$OUTPUT_DIR/fpdf"

# Test the binary
echo "→ Running basic tests..."
if "$OUTPUT_DIR/fpdf" --version | grep -q "$CURRENT_VERSION"; then
    echo -e "  ${GREEN}✅ Version check passed${NC}"
else
    echo -e "  ${YELLOW}⚠️ Version check warning - binary may be using cached version${NC}"
fi

if "$OUTPUT_DIR/fpdf" --help > /dev/null 2>&1; then
    echo -e "  ${GREEN}✅ Help command passed${NC}"
else
    echo -e "  ${RED}❌ Help command failed${NC}"
    exit 1
fi

# Install if requested
if [ "$DO_INSTALL" = true ]; then
    echo "→ Installing version $CURRENT_VERSION..."
    
    # Create local bin directory if it doesn't exist
    mkdir -p "$INSTALL_DIR"
    
    # Install main binary
    cp "$OUTPUT_DIR/fpdf" "$INSTALL_DIR/fpdf"
    chmod +x "$INSTALL_DIR/fpdf"
    echo -e "  ${GREEN}✅ Installed: $INSTALL_DIR/fpdf${NC}"
    
    # Create versioned copy
    cp "$OUTPUT_DIR/fpdf" "$INSTALL_DIR/fpdf-$CURRENT_VERSION"
    chmod +x "$INSTALL_DIR/fpdf-$CURRENT_VERSION"
    echo -e "  ${GREEN}✅ Versioned: $INSTALL_DIR/fpdf-$CURRENT_VERSION${NC}"
    
    # Create/update global symlink
    echo "→ Updating global symlink..."
    sudo rm -f "$GLOBAL_LINK" 2>/dev/null || true
    sudo ln -sf "$INSTALL_DIR/fpdf" "$GLOBAL_LINK"
    echo -e "  ${GREEN}✅ Global link: $GLOBAL_LINK → $INSTALL_DIR/fpdf${NC}"
    
    # Update PATH if needed
    if ! echo "$PATH" | grep -q "$INSTALL_DIR"; then
        echo -e "${YELLOW}⚠️ $INSTALL_DIR is not in your PATH${NC}"
        echo "Add this to your ~/.bashrc:"
        echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
    fi
fi

# Final verification
echo ""
echo "======================================"
echo -e "${BLUE}🔍 FINAL VERIFICATION${NC}"
echo "======================================"

# Check which fpdf is active
ACTIVE_FPDF=$(which fpdf 2>/dev/null || echo "not found")
echo "→ Active fpdf location: $ACTIVE_FPDF"

# Test the active version
if command -v fpdf > /dev/null 2>&1; then
    ACTIVE_VERSION=$(fpdf --version 2>&1 | grep -oP 'version \K[0-9.]+' | head -1 || echo "unknown")
    echo "→ Active version: $ACTIVE_VERSION"
    
    if [ "$ACTIVE_VERSION" = "$CURRENT_VERSION" ]; then
        echo -e "  ${GREEN}✅ Version confirmed: $CURRENT_VERSION${NC}"
    else
        echo -e "  ${YELLOW}⚠️ Version mismatch - run 'hash -r' to refresh${NC}"
    fi
else
    echo -e "  ${RED}❌ fpdf command not found in PATH${NC}"
fi

echo ""
echo "======================================"
echo -e "${GREEN}✅ Build Complete!${NC}"
echo "======================================"
echo ""
echo "📦 Build output: $OUTPUT_DIR/fpdf (v$CURRENT_VERSION)"

if [ "$DO_INSTALL" = true ]; then
    echo "📦 Installed at: $INSTALL_DIR/fpdf"
    echo "📦 Versioned at: $INSTALL_DIR/fpdf-$CURRENT_VERSION"
    echo "🔗 Global link: $GLOBAL_LINK"
fi

echo ""
echo -e "${BLUE}FilterPDF version $CURRENT_VERSION${NC}"
echo "Author: Eduardo Candeia Gonçalves (sindlinger@github.com)"
echo "Copyright (c) 2024 - Advanced PDF Processing Tool"
echo ""

# Final instructions
if [ "$DO_INSTALL" = true ]; then
    echo -e "${GREEN}✅ FilterPDF is ready to use!${NC}"
    echo ""
    echo "🎯 Next steps:"
    echo "  1. Run: hash -r"
    echo "  2. Test: fpdf --version"
    echo "  3. Use: fpdf --help"
else
    echo -e "${GREEN}✅ Build complete!${NC}"
    echo ""
    echo "🎯 To test the binary:"
    echo "  $OUTPUT_DIR/fpdf --version"
fi

echo ""
echo "======================================"
echo -e "${GREEN}🎉 SUCCESS!${NC}"
echo "======================================"