#!/bin/bash

# FilterPDF Versioned Installation Script
# Properly manages versions and prevents conflicts

set -e

echo "==================================="
echo "FilterPDF Versioned Installation"
echo "==================================="

# Get version from Version.cs
VERSION=$(grep 'Current = "' src/Version.cs | cut -d'"' -f2)

if [ -z "$VERSION" ]; then
    echo "Error: Could not extract version from src/Version.cs"
    exit 1
fi

# Check if versioned binary exists
if [ ! -f "publish/fpdf-${VERSION}" ]; then
    echo "Error: publish/fpdf-${VERSION} not found."
    echo "Run ./build-versioned.sh first"
    exit 1
fi

echo "Installing FilterPDF version: $VERSION"
echo ""

# Function to install with proper versioning
install_versioned() {
    local INSTALL_DIR=$1
    local NEED_SUDO=$2
    
    echo "Installing to: $INSTALL_DIR"
    
    # Create backup directory
    BACKUP_DIR="${INSTALL_DIR}/backup"
    if [ "$NEED_SUDO" = "true" ]; then
        sudo mkdir -p "$BACKUP_DIR"
    else
        mkdir -p "$BACKUP_DIR"
    fi
    
    # Check current installation
    if [ -f "$INSTALL_DIR/fpdf" ]; then
        # Get current version if possible
        CURRENT_VERSION=$("$INSTALL_DIR/fpdf" --version 2>/dev/null | grep "FilterPDF version" | awk '{print $3}' || echo "unknown")
        
        if [ "$CURRENT_VERSION" != "$VERSION" ]; then
            echo "  - Current version: $CURRENT_VERSION"
            echo "  - Backing up to: $BACKUP_DIR/fpdf-${CURRENT_VERSION}"
            
            if [ "$NEED_SUDO" = "true" ]; then
                sudo cp "$INSTALL_DIR/fpdf" "$BACKUP_DIR/fpdf-${CURRENT_VERSION}"
            else
                cp "$INSTALL_DIR/fpdf" "$BACKUP_DIR/fpdf-${CURRENT_VERSION}"
            fi
        fi
        
        # Remove old binary (not symlink this time!)
        echo "  - Removing old fpdf"
        if [ "$NEED_SUDO" = "true" ]; then
            sudo rm -f "$INSTALL_DIR/fpdf"
        else
            rm -f "$INSTALL_DIR/fpdf"
        fi
    fi
    
    # Remove any symlinks
    if [ -L "$INSTALL_DIR/fpdf" ]; then
        echo "  - Removing symlink at $INSTALL_DIR/fpdf"
        if [ "$NEED_SUDO" = "true" ]; then
            sudo rm -f "$INSTALL_DIR/fpdf"
        else
            rm -f "$INSTALL_DIR/fpdf"
        fi
    fi
    
    # Install versioned binary
    echo "  - Installing fpdf-${VERSION}"
    if [ "$NEED_SUDO" = "true" ]; then
        sudo cp "publish/fpdf-${VERSION}" "$INSTALL_DIR/fpdf-${VERSION}"
        sudo chmod +x "$INSTALL_DIR/fpdf-${VERSION}"
        
        # Create main fpdf as a COPY, not symlink
        echo "  - Creating main fpdf executable"
        sudo cp "publish/fpdf-${VERSION}" "$INSTALL_DIR/fpdf"
        sudo chmod +x "$INSTALL_DIR/fpdf"
    else
        cp "publish/fpdf-${VERSION}" "$INSTALL_DIR/fpdf-${VERSION}"
        chmod +x "$INSTALL_DIR/fpdf-${VERSION}"
        
        # Create main fpdf as a COPY, not symlink
        echo "  - Creating main fpdf executable"
        cp "publish/fpdf-${VERSION}" "$INSTALL_DIR/fpdf"
        chmod +x "$INSTALL_DIR/fpdf"
    fi
    
    echo "  ✓ Installed successfully"
    echo ""
}

# Install to user's local bin
if [ -d "$HOME/.local/bin" ]; then
    install_versioned "$HOME/.local/bin" false
fi

# Ask about system-wide installation
echo "Do you want to install system-wide to /usr/local/bin? (requires sudo) [y/N]"
read -r response
if [[ "$response" =~ ^[Yy]$ ]]; then
    install_versioned "/usr/local/bin" true
fi

# Verify installation
echo "==================================="
echo "Installation Complete!"
echo "==================================="
echo ""
echo "Installed versions:"
echo "  - fpdf (main executable) -> v$VERSION"
echo "  - fpdf-${VERSION} (versioned backup)"
echo ""

# Test the installation
echo "Testing installation..."
FPDF_PATH=$(which fpdf 2>/dev/null || echo "not found")

if [ "$FPDF_PATH" != "not found" ]; then
    echo "Active fpdf: $FPDF_PATH"
    
    # Force hash table refresh
    hash -r
    
    # Test version
    echo "Version test:"
    $FPDF_PATH --version 2>/dev/null | grep "FilterPDF version" || echo "Could not determine version"
    
    # Check if it's the right file
    if [ -f "$FPDF_PATH" ]; then
        SIZE=$(stat -c%s "$FPDF_PATH" 2>/dev/null || stat -f%z "$FPDF_PATH" 2>/dev/null || echo "0")
        echo "Binary size: $(( SIZE / 1024 / 1024 )) MB"
        
        # Verify it's not a symlink
        if [ -L "$FPDF_PATH" ]; then
            echo "⚠️  WARNING: $FPDF_PATH is a symlink to $(readlink -f $FPDF_PATH)"
        fi
    fi
else
    echo "⚠️  fpdf not found in PATH"
fi

echo ""
echo "Backups stored in:"
echo "  - ~/.local/bin/backup/ (user backups)"
echo "  - /usr/local/bin/backup/ (system backups)"