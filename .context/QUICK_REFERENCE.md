# FilterPDF (fpdf) - Quick Reference for AI Agents

## 🚨 CRITICAL REMINDERS

### Project Identity
- **Name**: FilterPDF (fpdf) - NEVER LayoutPDF
- **Version**: 3.11.0
- **Binary**: `/mnt/b/dev-2/fpdf/bin/fpdf`
- **Namespace**: FilterPDF

### Command Syntax (NEVER use "filter" as intermediary)
```bash
# ✅ CORRECT
fpdf 1 pages --word "contract"
fpdf 1-50 documents --format json
fpdf cache list

# ❌ WRONG
fpdf 1 filter pages
layoutpdf cache list
```

## 🎯 Common Operations

### Build & Deploy
```bash
dotnet build fpdf.csproj
dotnet publish fpdf.csproj -c Release
# Symbolic link created automatically
```

### Basic Usage
```bash
# Process single PDF
fpdf document.pdf pages --word "important"

# Load PDFs to cache
fpdf load images-only --input-dir /path --num-workers 16

# Cache operations
fpdf cache list
fpdf cache stats

# Range operations
fpdf 1-100 stats --format summary
```

### Configuration
```bash
# Environment variables (FPDF_ prefix)
export FPDF_DEBUG=true
export FPDF_ALLOWED_DIRS="/mnt/b:/home/user"
export FPDF_DEFAULT_WORKERS=16
```

## 📋 Available Commands

| Command | Purpose | Example |
|---------|---------|---------|
| extract | Basic PDF extraction | `fpdf file.pdf extract` |
| load | Batch processing to cache | `fpdf load custom --input-dir /path` |
| cache | Cache management | `fpdf cache list` |
| stats | Statistical analysis | `fpdf 1-50 stats` |
| pages | Page analysis | `fpdf 1 pages --word "text"` |
| documents | Document filtering | `fpdf 1-10 documents --format json` |
| words | Word extraction | `fpdf 1 words --min-length 5` |
| images | Image extraction | `fpdf 1 images` |

## 🔧 Load Modes

- **ultra**: Full forensic analysis
- **text**: Text-only extraction  
- **custom**: Balanced extraction
- **images-only**: Image pages only
- **base64-only**: Base64 content only

## ⚠️ Anti-Reversion Checklist

Before any code changes:
- [ ] Verify FilterPDF namespace (not LayoutPDF)
- [ ] Check command syntax is direct (no "filter")
- [ ] Confirm binary name is fpdf
- [ ] Validate FPDF_ environment variables
- [ ] Read CLAUDE.md if uncertain

## 🔍 File Locations

- **Main CLI**: `src/FilterPDFCLI.cs`
- **Commands**: `src/Commands/*.cs`
- **Configuration**: `src/Configuration/`
- **Security**: `src/Security/SecurityValidator.cs`
- **Project**: `fpdf.csproj`
- **Instructions**: `CLAUDE.md`

## 📊 Current Cache Status

- **PDFs Cached**: 3,209
- **Cache Size**: 3.2 GB
- **Mode**: images-only processing complete

---
**Remember**: This is FilterPDF (fpdf), not LayoutPDF. Always use direct command syntax without "filter" intermediary.