#!/bin/bash

# FilterPDF Safe Installation System
# Handles all installation paths properly without creating broken symlinks

set -e

echo "==================================="
echo "FilterPDF Safe Installation System"
echo "==================================="

# Get version from Version.cs
VERSION=$(grep 'Current = "' src/Version.cs | cut -d'"' -f2)

if [ -z "$VERSION" ]; then
    echo "Error: Could not extract version from src/Version.cs"
    exit 1
fi

# Check if binary exists
if [ ! -f "publish/fpdf" ]; then
    echo "Error: publish/fpdf not found."
    echo "Run ./build-clean.sh first"
    exit 1
fi

echo "Installing FilterPDF version: $VERSION"
echo ""

# Function to safely clean installation directory
safe_clean() {
    local INSTALL_DIR=$1
    local NEED_SUDO=$2
    
    echo "Cleaning $INSTALL_DIR..."
    
    # Handle broken symlinks first
    if [ -L "$INSTALL_DIR/fpdf" ] && [ ! -e "$INSTALL_DIR/fpdf" ]; then
        echo "  - Removing broken symlink at $INSTALL_DIR/fpdf"
        if [ "$NEED_SUDO" = "true" ]; then
            sudo rm -f "$INSTALL_DIR/fpdf"
        else
            rm -f "$INSTALL_DIR/fpdf"
        fi
    fi
    
    # Handle existing symlinks
    if [ -L "$INSTALL_DIR/fpdf" ]; then
        echo "  - Removing symlink at $INSTALL_DIR/fpdf"
        if [ "$NEED_SUDO" = "true" ]; then
            sudo rm -f "$INSTALL_DIR/fpdf"
        else
            rm -f "$INSTALL_DIR/fpdf"
        fi
    fi
    
    # Backup existing binary if it exists and is a real file
    if [ -f "$INSTALL_DIR/fpdf" ] && [ ! -L "$INSTALL_DIR/fpdf" ]; then
        BACKUP_DIR="${INSTALL_DIR}/backup"
        if [ "$NEED_SUDO" = "true" ]; then
            sudo mkdir -p "$BACKUP_DIR"
        else
            mkdir -p "$BACKUP_DIR"
        fi
        
        # Try to get version from existing binary
        OLD_VERSION=$("$INSTALL_DIR/fpdf" --version 2>/dev/null | grep "FilterPDF version" | awk '{print $3}' || echo "unknown")
        BACKUP_NAME="fpdf-${OLD_VERSION}-$(date +%Y%m%d%H%M%S)"
        
        echo "  - Backing up existing fpdf to $BACKUP_DIR/$BACKUP_NAME"
        if [ "$NEED_SUDO" = "true" ]; then
            sudo mv "$INSTALL_DIR/fpdf" "$BACKUP_DIR/$BACKUP_NAME"
        else
            mv "$INSTALL_DIR/fpdf" "$BACKUP_DIR/$BACKUP_NAME"
        fi
    fi
    
    # Clean any leftover fpdf files
    if [ "$NEED_SUDO" = "true" ]; then
        sudo rm -f "$INSTALL_DIR/fpdf"
        sudo rm -f "$INSTALL_DIR/fpdf-"*
    else
        rm -f "$INSTALL_DIR/fpdf"
        rm -f "$INSTALL_DIR/fpdf-"*
    fi
    
    echo "  ✓ Directory cleaned"
}

# Function to install safely
safe_install() {
    local INSTALL_DIR=$1
    local NEED_SUDO=$2
    
    echo "Installing to: $INSTALL_DIR"
    
    # Create directory if it doesn't exist
    if [ "$NEED_SUDO" = "true" ]; then
        sudo mkdir -p "$INSTALL_DIR"
    else
        mkdir -p "$INSTALL_DIR"
    fi
    
    # Clean the directory first
    safe_clean "$INSTALL_DIR" "$NEED_SUDO"
    
    # Install the new binary (NOT as symlink, as a real file)
    echo "  - Installing fpdf binary"
    if [ "$NEED_SUDO" = "true" ]; then
        sudo cp "publish/fpdf" "$INSTALL_DIR/fpdf"
        sudo chmod +x "$INSTALL_DIR/fpdf"
        
        # Also install versioned copy
        sudo cp "publish/fpdf" "$INSTALL_DIR/fpdf-${VERSION}"
        sudo chmod +x "$INSTALL_DIR/fpdf-${VERSION}"
    else
        cp "publish/fpdf" "$INSTALL_DIR/fpdf"
        chmod +x "$INSTALL_DIR/fpdf"
        
        # Also install versioned copy
        cp "publish/fpdf" "$INSTALL_DIR/fpdf-${VERSION}"
        chmod +x "$INSTALL_DIR/fpdf-${VERSION}"
    fi
    
    # Verify installation
    if [ -f "$INSTALL_DIR/fpdf" ] && [ ! -L "$INSTALL_DIR/fpdf" ]; then
        echo "  ✓ Binary installed successfully"
        
        # Test the binary
        TEST_VERSION=$("$INSTALL_DIR/fpdf" --version 2>&1 | grep "FilterPDF version" | awk '{print $3}' || echo "")
        if [ "$TEST_VERSION" = "$VERSION" ]; then
            echo "  ✓ Version verified: $VERSION"
        else
            echo "  ⚠️  Warning: Version mismatch (expected $VERSION, got $TEST_VERSION)"
        fi
    else
        echo "  ✗ Installation failed"
        return 1
    fi
    
    echo ""
}

# Function to update PATH if needed
update_path_if_needed() {
    local DIR=$1
    
    if [[ ":$PATH:" != *":$DIR:"* ]]; then
        echo "⚠️  Note: $DIR is not in your PATH"
        echo ""
        echo "To add it permanently, add this line to your ~/.bashrc or ~/.zshrc:"
        echo "  export PATH=\"$DIR:\$PATH\""
        echo ""
    fi
}

# Install to user's local bin
USER_BIN="$HOME/.local/bin"
echo "Step 1: User installation"
echo "-------------------------"
safe_install "$USER_BIN" false
update_path_if_needed "$USER_BIN"

# Ask about system-wide installation
echo "Step 2: System installation"
echo "---------------------------"
echo "Do you want to install system-wide to /usr/local/bin? (requires sudo) [y/N]"
read -r response

if [[ "$response" =~ ^[Yy]$ ]]; then
    safe_install "/usr/local/bin" true
else
    echo "Skipping system-wide installation"
    echo ""
fi

# Clear shell hash table to ensure new binary is found
hash -r 2>/dev/null || true

# Final verification
echo "==================================="
echo "Installation Complete!"
echo "==================================="
echo ""

# Find and test the active fpdf
FPDF_PATH=$(which fpdf 2>/dev/null || echo "")

if [ -n "$FPDF_PATH" ]; then
    echo "Active fpdf: $FPDF_PATH"
    
    # Check if it's a symlink or real file
    if [ -L "$FPDF_PATH" ]; then
        REAL_PATH=$(readlink -f "$FPDF_PATH" 2>/dev/null || echo "unknown")
        echo "Type: Symlink → $REAL_PATH"
        echo "⚠️  WARNING: This is a symlink (may cause issues)"
    else
        echo "Type: Real binary file ✓"
    fi
    
    # Get file size
    if [ -f "$FPDF_PATH" ]; then
        SIZE=$(stat -c%s "$FPDF_PATH" 2>/dev/null || stat -f%z "$FPDF_PATH" 2>/dev/null || echo "0")
        SIZE_MB=$((SIZE / 1024 / 1024))
        echo "Size: ${SIZE_MB} MB"
    fi
    
    # Test version
    echo ""
    echo "Testing fpdf:"
    "$FPDF_PATH" --version 2>/dev/null | head -5 || echo "Could not get version"
    
    # Test stats command
    echo ""
    echo "Testing stats command:"
    "$FPDF_PATH" stats --help 2>/dev/null | head -3 || echo "Stats command not available"
else
    echo "⚠️  fpdf not found in PATH"
    echo ""
    echo "Installation locations:"
    
    if [ -f "$USER_BIN/fpdf" ]; then
        echo "  ✓ $USER_BIN/fpdf (installed)"
    else
        echo "  ✗ $USER_BIN/fpdf (not found)"
    fi
    
    if [ -f "/usr/local/bin/fpdf" ]; then
        echo "  ✓ /usr/local/bin/fpdf (installed)"
    else
        echo "  ✗ /usr/local/bin/fpdf (not found)"
    fi
    
    echo ""
    echo "Make sure one of these directories is in your PATH:"
    echo "  • $USER_BIN"
    echo "  • /usr/local/bin"
fi

echo ""
echo "To use FilterPDF:"
echo "  fpdf --help"
echo "  fpdf stats --images  # Analyze images in PDFs"
echo "  fpdf cache list      # List cached PDFs"