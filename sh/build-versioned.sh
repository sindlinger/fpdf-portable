#!/bin/bash

# FilterPDF Versioned Build Script
# Creates versioned binaries and updates the main one

set -e

# Get version from Version.cs
VERSION=$(grep 'Current = "' src/Version.cs | cut -d'"' -f2)

if [ -z "$VERSION" ]; then
    echo "Error: Could not extract version from src/Version.cs"
    exit 1
fi

echo "==================================="
echo "Building FilterPDF v$VERSION"
echo "==================================="

# Create versioned publish directory
PUBLISH_DIR="publish/v${VERSION}"
mkdir -p "$PUBLISH_DIR"

# Clean only the specific version directory
rm -rf "$PUBLISH_DIR/*"

echo "Publishing to: $PUBLISH_DIR"

# Build and publish as single file
dotnet publish fpdf.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$PUBLISH_DIR/"

if [ $? -eq 0 ]; then
    echo "Build successful!"
    
    # Rename binary with version
    mv "$PUBLISH_DIR/fpdf" "$PUBLISH_DIR/fpdf-${VERSION}"
    
    # Create a copy without version for current
    cp "$PUBLISH_DIR/fpdf-${VERSION}" "$PUBLISH_DIR/fpdf"
    
    # Update main publish directory
    echo "Updating main publish directory..."
    cp "$PUBLISH_DIR/fpdf-${VERSION}" "publish/fpdf-${VERSION}"
    cp "$PUBLISH_DIR/fpdf" "publish/fpdf"
    
    # Make executable
    chmod +x publish/fpdf*
    chmod +x "$PUBLISH_DIR/"*
    
    # Show built files
    echo ""
    echo "Built files:"
    echo "  - $PUBLISH_DIR/fpdf-${VERSION} (versioned)"
    echo "  - $PUBLISH_DIR/fpdf (current)"
    echo "  - publish/fpdf-${VERSION} (versioned copy)"
    echo "  - publish/fpdf (current copy)"
    
    # Show version
    echo ""
    ./publish/fpdf --version | head -5
    
    echo ""
    echo "To install, run:"
    echo "  ./install-versioned.sh"
else
    echo "Build failed!"
    exit 1
fi