#!/bin/bash

# FilterPDF Installation Script
# Ensures proper installation without symlink conflicts

set -e  # Exit on error

echo "==================================="
echo "FilterPDF Installation Script"
echo "==================================="

# First, build the project if needed
if [ ! -f "publish/fpdf" ] || [ "$1" == "--rebuild" ]; then
    echo "Building FilterPDF..."
    ./build.sh
    if [ $? -ne 0 ]; then
        echo "Build failed! Run ./build.sh manually to see errors."
        exit 1
    fi
fi

# Check if publish/fpdf exists
if [ ! -f "publish/fpdf" ]; then
    echo "Error: publish/fpdf not found. Run ./build.sh first."
    exit 1
fi

# Get version from the binary
VERSION=$(./publish/fpdf --version 2>/dev/null | grep "FilterPDF version" | awk '{print $3}' || echo "unknown")
echo "Installing FilterPDF version: $VERSION"
echo ""

# Function to install fpdf
install_fpdf() {
    local INSTALL_PATH=$1
    local INSTALL_NAME=$2
    
    echo "Installing to: $INSTALL_PATH"
    
    # Check if it's a symlink and remove it
    if [ -L "$INSTALL_PATH" ]; then
        echo "  - Removing old symlink at $INSTALL_PATH"
        rm -f "$INSTALL_PATH"
    fi
    
    # Check if it's an old version and backup
    if [ -f "$INSTALL_PATH" ]; then
        # Check if it's an old version
        OLD_VERSION=$($INSTALL_PATH --version 2>/dev/null | grep "FilterPDF version" | awk '{print $3}' || echo "unknown")
        if [ "$OLD_VERSION" != "$VERSION" ]; then
            echo "  - Backing up old version $OLD_VERSION to ${INSTALL_PATH}.backup"
            mv "$INSTALL_PATH" "${INSTALL_PATH}.backup"
        else
            echo "  - Same version already installed, replacing anyway"
            rm -f "$INSTALL_PATH"
        fi
    fi
    
    # Copy the new binary
    echo "  - Installing new version $VERSION"
    cp publish/fpdf "$INSTALL_PATH"
    chmod +x "$INSTALL_PATH"
    echo "  ✓ Installed successfully"
    echo ""
}

# Install to user's local bin if it exists
if [ -d "$HOME/.local/bin" ]; then
    install_fpdf "$HOME/.local/bin/fpdf" "user"
fi

# Ask about system-wide installation
echo "Do you want to install system-wide to /usr/local/bin? (requires sudo) [y/N]"
read -r response
if [[ "$response" =~ ^[Yy]$ ]]; then
    # Check if it's a symlink and remove it
    if [ -L "/usr/local/bin/fpdf" ]; then
        echo "  - Removing old symlink at /usr/local/bin/fpdf"
        sudo rm -f /usr/local/bin/fpdf
    fi
    
    # Install system-wide
    echo "Installing to: /usr/local/bin/fpdf"
    sudo cp publish/fpdf /usr/local/bin/fpdf
    sudo chmod +x /usr/local/bin/fpdf
    echo "  ✓ Installed system-wide successfully"
    echo ""
fi

# Verify installation
echo "==================================="
echo "Installation Complete!"
echo "==================================="
echo ""
echo "Verifying installation..."

# Find which fpdf will be used
FPDF_PATH=$(which fpdf 2>/dev/null || echo "not found")
if [ "$FPDF_PATH" != "not found" ]; then
    echo "Active fpdf: $FPDF_PATH"
    
    # Check if it's a symlink
    if [ -L "$FPDF_PATH" ]; then
        echo "⚠️  WARNING: $FPDF_PATH is still a symlink!"
        echo "   Points to: $(readlink -f $FPDF_PATH)"
        echo "   Run: rm $FPDF_PATH && cp publish/fpdf $FPDF_PATH"
    else
        # Show version
        $FPDF_PATH --version 2>/dev/null | grep "FilterPDF version" || echo "Could not determine version"
    fi
else
    echo "⚠️  fpdf not found in PATH"
    echo "   Add ~/.local/bin to your PATH or install system-wide"
fi

echo ""
echo "To rebuild and reinstall, run: ./install.sh --rebuild"