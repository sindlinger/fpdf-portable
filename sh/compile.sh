#!/bin/bash

# FilterPDF Intelligent Compilation System
# Handles versioning, backups, and installation automatically

set -e

echo "======================================"
echo "FilterPDF Intelligent Build System"
echo "======================================"

# Get version from Version.cs
VERSION=$(grep 'Current = "' src/Version.cs | cut -d'"' -f2)
echo "Building version: $VERSION"

# Step 1: Clean build
echo "→ Cleaning old artifacts..."
rm -rf bin/ obj/ publish/

# Step 2: Compile
echo "→ Compiling FilterPDF..."
dotnet publish fpdf.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o publish/ 2>&1 | tee compile.log

# Check compilation results
if grep -q "error" compile.log; then
    echo ""
    echo "❌ COMPILATION FAILED!"
    echo "Errors found:"
    grep "error" compile.log | head -10
    exit 1
fi

# Show warning summary (warnings são aceitáveis)
WARNING_COUNT=$(grep -c "warning" compile.log || echo "0")
if [ "$WARNING_COUNT" -gt 0 ]; then
    echo "⚠️  Compilation completed with $WARNING_COUNT warnings (acceptable)"
else
    echo "✅ Compilation completed without warnings"
fi

# Check if binary was created
if [ ! -f "publish/fpdf" ]; then
    echo "❌ Binary not created! Check compile.log"
    exit 1
fi

echo "✅ Compilation successful"

# Step 2.5: Run basic tests before installing
echo ""
echo "→ Running basic tests..."
TEST_FAILED=0

# Test 1: Version check
if ! ./publish/fpdf --version > /dev/null 2>&1; then
    echo "  ❌ Version check failed"
    TEST_FAILED=1
else
    echo "  ✅ Version check passed"
fi

# Test 2: Help command
if ! ./publish/fpdf --help > /dev/null 2>&1; then
    echo "  ❌ Help command failed"
    TEST_FAILED=1
else
    echo "  ✅ Help command passed"
fi

# Test 3: Cache list (should work even with empty cache)
if ! ./publish/fpdf cache list > /dev/null 2>&1; then
    echo "  ❌ Cache list command failed"
    TEST_FAILED=1
else
    echo "  ✅ Cache list command passed"
fi

# Test 4: Check for critical functions
echo "  → Checking critical functions..."
if ! strings ./publish/fpdf | grep -q "CreatePngFromBase64"; then
    echo "  ⚠️  Warning: CreatePngFromBase64 function not found in binary"
fi

if [ $TEST_FAILED -eq 1 ]; then
    echo ""
    echo "❌ TESTS FAILED! Not installing."
    echo "Fix the issues and try again."
    exit 1
fi

echo "✅ All basic tests passed"

# Step 3: Manage versions
INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR/versions"

# Backup current version if exists
if [ -f "$INSTALL_DIR/fpdf" ]; then
    CURRENT_VERSION=$("$INSTALL_DIR/fpdf" --version 2>&1 | grep "version" | awk '{print $3}' || echo "unknown")
    if [ "$CURRENT_VERSION" != "$VERSION" ] && [ "$CURRENT_VERSION" != "unknown" ]; then
        echo "→ Backing up version $CURRENT_VERSION..."
        cp "$INSTALL_DIR/fpdf" "$INSTALL_DIR/versions/fpdf-${CURRENT_VERSION}-$(date +%Y%m%d)"
    fi
fi

# Step 4: Install new version with atomic replacement
echo "→ Installing version $VERSION..."

# Create versioned binary
VERSIONED_NAME="fpdf-${VERSION}"
cp publish/fpdf "$INSTALL_DIR/${VERSIONED_NAME}"
chmod +x "$INSTALL_DIR/${VERSIONED_NAME}"

# Create new fpdf atomically (to avoid issues if it's running)
cp publish/fpdf "$INSTALL_DIR/fpdf.new"
chmod +x "$INSTALL_DIR/fpdf.new"
mv -f "$INSTALL_DIR/fpdf.new" "$INSTALL_DIR/fpdf"

echo "  ✅ Installed: $INSTALL_DIR/fpdf"
echo "  ✅ Versioned: $INSTALL_DIR/${VERSIONED_NAME}"

# Step 5: Force PATH update and clear all caches
echo "→ Forcing PATH update and clearing caches..."

# Clear ALL possible hash caches for fpdf
hash -d fpdf 2>/dev/null || true
hash -r 2>/dev/null || true

# Kill any running fpdf processes that might be locking the old binary
pkill -f fpdf 2>/dev/null || true
sleep 1

# Remove ALL old system symlinks and binaries
echo "→ Removing old system binaries..."
sudo rm -f /usr/local/bin/fpdf 2>/dev/null || true
sudo rm -f /usr/bin/fpdf 2>/dev/null || true
sudo rm -f /bin/fpdf 2>/dev/null || true

# Force update system binary locations
if command -v sudo >/dev/null 2>&1; then
    # Create new symlink to the exact binary
    if sudo ln -sf "$INSTALL_DIR/fpdf" /usr/local/bin/fpdf 2>/dev/null; then
        echo "  ✅ System symlink created: /usr/local/bin/fpdf → $INSTALL_DIR/fpdf"
    else
        echo "  ⚠️  Could not create system symlink"
    fi
fi

# Step 6: FORCE shell environment update
echo "→ FORCING shell environment update..."

# Update current shell's hash table
hash -r 2>/dev/null || true
hash -d fpdf 2>/dev/null || true

# Force rehash in common shells
if [ -n "$BASH_VERSION" ]; then
    hash -r
fi
if [ -n "$ZSH_VERSION" ]; then
    rehash 2>/dev/null || true
fi

# Update environment for ALL possible shell locations
export PATH="$INSTALL_DIR:$PATH"

# Step 7: CRITICAL VERIFICATION - Ensure new version is active
echo ""
echo "======================================"
echo "🔍 CRITICAL VERIFICATION"
echo "======================================"

# FORCE immediate hash update
hash -r 2>/dev/null || true

# Test EXACTLY which fpdf is being executed
echo "→ Checking which fpdf is active..."
ACTIVE_FPDF=$(which fpdf 2>/dev/null || echo "NONE")
echo "  Active fpdf location: $ACTIVE_FPDF"

# Test the VERSION from the active binary
echo "→ Testing ACTIVE binary version..."
ACTIVE_VERSION=$(fpdf --version 2>&1 | grep "FilterPDF" | awk '{print $3}' | head -1 || echo "unknown")
echo "  Active version: $ACTIVE_VERSION"
echo "  Expected version: $VERSION"

if [ "$ACTIVE_VERSION" != "$VERSION" ]; then
    echo ""
    echo "🚨 CRITICAL ERROR: Version mismatch detected!"
    echo "Expected: $VERSION"
    echo "Active: $ACTIVE_VERSION"
    echo ""
    echo "FORCING immediate fix..."
    
    # NUCLEAR OPTION: Force update PATH and try again
    export PATH="$INSTALL_DIR:$PATH"
    hash -r 2>/dev/null || true
    
    # Test again after forcing
    ACTIVE_VERSION_2=$(fpdf --version 2>&1 | grep "FilterPDF" | awk '{print $3}' | head -1 || echo "unknown")
    
    if [ "$ACTIVE_VERSION_2" != "$VERSION" ]; then
        echo "❌ CATASTROPHIC FAILURE: Cannot update to new version!"
        echo "Manual intervention required:"
        echo "  1. Run: export PATH=\"$INSTALL_DIR:\$PATH\""
        echo "  2. Run: hash -r"
        echo "  3. Test: fpdf --version"
        exit 1
    else
        echo "✅ FIXED: Version is now correct after PATH update"
    fi
else
    echo "  ✅ Version confirmed: $VERSION"
fi

# Test the installed binary directly
echo "→ Testing installed binary directly..."
if ! "$INSTALL_DIR/fpdf" --version | grep -q "$VERSION"; then
    echo "  ❌ Direct binary version mismatch! Expected $VERSION"
    exit 1
else
    echo "  ✅ Direct binary version confirmed: $VERSION"
fi

# Quick functional test
echo "→ Running functional test..."
if "$INSTALL_DIR/fpdf" cache list >/dev/null 2>&1; then
    echo "  ✅ Functional test passed"
else
    echo "  ⚠️  Functional test warning (non-critical)"
fi

echo ""
echo "======================================"
echo "✅ Build & Installation Complete!"
echo "======================================"
echo ""
echo "📦 Installed: $INSTALL_DIR/fpdf (v$VERSION)"
echo "📦 Versioned: $INSTALL_DIR/${VERSIONED_NAME}"
if [ -f "$INSTALL_DIR/versions/fpdf-${CURRENT_VERSION}-$(date +%Y%m%d)" ]; then
    echo "📦 Backup: $INSTALL_DIR/versions/fpdf-${CURRENT_VERSION}-$(date +%Y%m%d)"
fi
echo ""

# Show version info
"$INSTALL_DIR/fpdf" --version | head -3

echo ""

# Check if ~/.local/bin is in PATH
if ! echo "$PATH" | grep -q "$HOME/.local/bin"; then
    echo "⚠️  WARNING: $HOME/.local/bin is not in your PATH!"
    echo ""
    echo "Add this line to your ~/.bashrc or ~/.zshrc:"
    echo "    export PATH=\"\$HOME/.local/bin:\$PATH\""
    echo ""
    echo "Then reload your shell configuration:"
    echo "    source ~/.bashrc"
    echo ""
else
    echo "✅ PATH is correctly configured"
fi

echo ""
echo "🎯 FINAL INSTRUCTIONS TO ENSURE NO MORE PATH ISSUES:"
echo "======================================"
echo ""
echo "1. IMMEDIATELY run this in your terminal:"
echo "    export PATH=\"$HOME/.local/bin:\$PATH\""
echo "    hash -r"
echo ""
echo "2. Then test the version:"
echo "    fpdf --version"
echo ""
echo "3. If it still shows the old version, run:"
echo "    which fpdf"
echo "    /home/chanfle/.local/bin/fpdf --version"
echo ""
echo "4. Add this line to your ~/.bashrc to make it permanent:"
echo "    export PATH=\"\$HOME/.local/bin:\$PATH\""
echo ""
echo "🚨 This ensures NUNCA MAIS ISSO ACONTEÇA!"
echo ""

# Step 8: Test all helps
echo "======================================="
echo "🧪 TESTING ALL HELP COMMANDS"
echo "======================================="
if [ -f "./test-all-helps.sh" ]; then
    ./test-all-helps.sh
    if [ $? -ne 0 ]; then
        echo ""
        echo "⚠️  WARNING: Some help commands are not working properly!"
        echo "Please check the test results above."
    fi
else
    echo "⚠️  test-all-helps.sh not found. Skipping help tests."
fi