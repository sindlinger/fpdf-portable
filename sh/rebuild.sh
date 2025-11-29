#!/bin/bash

# FilterPDF Complete Rebuild Script
# Handles everything: clean, build, install
# Prevents ALL symlink and version issues

set -e

echo "======================================"
echo "FilterPDF Complete Rebuild System"
echo "======================================"
echo ""
echo "This script will:"
echo "  1. Clean all old artifacts"
echo "  2. Build fresh from source"
echo "  3. Install properly (no symlinks)"
echo "  4. Verify everything works"
echo ""

# Make scripts executable
chmod +x build-clean.sh 2>/dev/null || true
chmod +x install-safe.sh 2>/dev/null || true

# Step 1: Clean build
echo "Step 1: Building FilterPDF..."
echo "------------------------------"
./build-clean.sh

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ Build failed! Check errors above."
    exit 1
fi

echo ""
echo "✅ Build completed successfully!"
echo ""

# Step 2: Install
echo "Step 2: Installing FilterPDF..."
echo "--------------------------------"
./install-safe.sh

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ Installation failed! Check errors above."
    exit 1
fi

echo ""
echo "======================================"
echo "✅ Complete Rebuild Successful!"
echo "======================================"
echo ""

# Final test
echo "Final verification:"
echo "-------------------"

# Clear hash to ensure we get the fresh binary
hash -r 2>/dev/null || true

# Test the command
FPDF_CMD=$(which fpdf 2>/dev/null || echo "")

if [ -n "$FPDF_CMD" ]; then
    echo "✓ fpdf found at: $FPDF_CMD"
    
    # Check if it's a real file (not symlink)
    if [ -L "$FPDF_CMD" ]; then
        echo "⚠️  WARNING: fpdf is a symlink (this may cause issues)"
        echo "   Run ./install-safe.sh to fix this"
    else
        echo "✓ fpdf is a real binary (not a symlink)"
    fi
    
    echo ""
    echo "Version test:"
    fpdf --version 2>/dev/null | head -3 || echo "Could not get version"
    
    echo ""
    echo "Stats command test:"
    fpdf stats --help 2>/dev/null | head -3 || echo "Stats command not available"
else
    echo "⚠️  fpdf not found in PATH"
    echo ""
    echo "Try running:"
    echo "  source ~/.bashrc"
    echo "  # or"
    echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
fi

echo ""
echo "Quick usage:"
echo "  fpdf --help                    # Show help"
echo "  fpdf cache list                # List cached PDFs"
echo "  fpdf 1-10 stats --images       # Analyze images in cached PDFs 1-10"
echo "  fpdf document.pdf pages --word 'contract'  # Search for word in PDF"