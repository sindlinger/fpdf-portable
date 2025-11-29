#!/bin/bash

# FilterPDF Clean Build System
# Ensures proper build without conflicts or broken symlinks

set -e

echo "==================================="
echo "FilterPDF Clean Build System"
echo "==================================="

# Get version from Version.cs
VERSION=$(grep 'Current = "' src/Version.cs | cut -d'"' -f2)

if [ -z "$VERSION" ]; then
    echo "Error: Could not extract version from src/Version.cs"
    exit 1
fi

echo "Building version: $VERSION"
echo ""

# Step 1: Clean ALL old artifacts
echo "Step 1: Cleaning old artifacts..."
rm -rf bin/
rm -rf obj/
rm -rf publish/

echo "  ✓ Cleaned build directories"

# Step 2: Build fresh
echo ""
echo "Step 2: Building FilterPDF..."
dotnet publish fpdf.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o publish/

if [ $? -ne 0 ]; then
    echo "  ✗ Build failed!"
    exit 1
fi

echo "  ✓ Build successful"

# Step 3: Create versioned copies
echo ""
echo "Step 3: Creating versioned binaries..."
cd publish/
cp fpdf "fpdf-${VERSION}"
chmod +x fpdf*
cd ..

echo "  ✓ Created fpdf-${VERSION}"

# Step 4: Verify build
echo ""
echo "Step 4: Verifying build..."
if [ ! -f "publish/fpdf" ]; then
    echo "  ✗ Error: publish/fpdf not found"
    exit 1
fi

FILE_SIZE=$(stat -c%s "publish/fpdf" 2>/dev/null || stat -f%z "publish/fpdf" 2>/dev/null)
FILE_SIZE_MB=$((FILE_SIZE / 1024 / 1024))

if [ $FILE_SIZE_MB -lt 50 ]; then
    echo "  ✗ Error: Binary too small (${FILE_SIZE_MB}MB), expected >50MB"
    exit 1
fi

echo "  ✓ Binary size: ${FILE_SIZE_MB}MB"

# Test the binary
TEST_OUTPUT=$(./publish/fpdf --version 2>&1 | head -1 || echo "")
if [[ "$TEST_OUTPUT" == *"$VERSION"* ]]; then
    echo "  ✓ Version check passed: $VERSION"
else
    echo "  ✗ Error: Version mismatch or binary not working"
    echo "    Expected: $VERSION"
    echo "    Got: $TEST_OUTPUT"
    exit 1
fi

# Test stats command
STATS_TEST=$(./publish/fpdf stats --help 2>&1 | grep "Analyzes PDF statistics" || echo "")
if [ -n "$STATS_TEST" ]; then
    echo "  ✓ Stats command available"
else
    echo "  ⚠️  Warning: Stats command not found"
fi

# Step 5: Create distribution package
echo ""
echo "Step 5: Creating distribution package..."
DIST_DIR="dist/fpdf-${VERSION}"
rm -rf dist/
mkdir -p "$DIST_DIR"

cp publish/fpdf "$DIST_DIR/"
cp publish/fpdf-${VERSION} "$DIST_DIR/"
cp README.md "$DIST_DIR/" 2>/dev/null || true
cp LICENSE "$DIST_DIR/" 2>/dev/null || true

# Create install script for distribution
cat > "$DIST_DIR/install.sh" << 'EOF'
#!/bin/bash
# FilterPDF Installation Script

VERSION=$(./fpdf --version 2>&1 | grep "FilterPDF version" | awk '{print $3}')
echo "Installing FilterPDF $VERSION"

# Install to user directory
INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"

# Remove any old installation
rm -f "$INSTALL_DIR/fpdf"
rm -f "$INSTALL_DIR/fpdf-"*

# Copy new version
cp fpdf "$INSTALL_DIR/"
cp fpdf-* "$INSTALL_DIR/" 2>/dev/null || true
chmod +x "$INSTALL_DIR/fpdf"*

echo "Installed to: $INSTALL_DIR/fpdf"
echo ""
echo "Make sure $INSTALL_DIR is in your PATH"
echo "Run: fpdf --version"
EOF

chmod +x "$DIST_DIR/install.sh"

echo "  ✓ Distribution package created: $DIST_DIR"

# Final summary
echo ""
echo "==================================="
echo "Build Complete!"
echo "==================================="
echo ""
echo "Built files:"
echo "  • publish/fpdf (main binary)"
echo "  • publish/fpdf-${VERSION} (versioned)"
echo "  • dist/fpdf-${VERSION}/ (distribution package)"
echo ""
echo "To install locally:"
echo "  ./install-safe.sh"
echo ""
echo "To distribute:"
echo "  Share the dist/fpdf-${VERSION}/ directory"
echo ""
./publish/fpdf --version | head -5