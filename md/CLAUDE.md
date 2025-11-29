# CLAUDE.md - FilterPDF (fpdf) Project Instructions

## 🚨 CRITICAL: PREVENT CODE REVERSION - READ FIRST! 🚨

### ⛔ ABSOLUTE PROHIBITIONS

**THIS PROJECT IS FilterPDF (fpdf) - NOT LayoutPDF!**

1. **NEVER** use or reference "layoutpdf", "LayoutPDF", "lpdf", or "LPDF"
2. **NEVER** use the obsolete "filter" command syntax
3. **NEVER** revert to old code patterns from cached AI memory
4. **ALWAYS** verify you're working with FilterPDF (fpdf) code

### ✅ CORRECT PROJECT INFORMATION

- **Application Name**: FilterPDF
- **Executable**: `fpdf`
- **Project File**: `fpdf.csproj`
- **Namespace**: `FilterPDF`
- **Installation Path**: `/mnt/b/dev-2/fpdf`
- **Binary Location**: `/mnt/b/dev-2/fpdf/bin/fpdf`

### ✅ CORRECT COMMAND SYNTAX

**Direct Commands (NO "filter" intermediary):**
```bash
# CORRECT - Direct command syntax
fpdf document.pdf pages --word "contract"
fpdf 1-50 documents --format json
fpdf cache list
fpdf load images-only --input-dir /path/to/pdfs

# WRONG - Never use "filter" as intermediary
fpdf document.pdf filter pages  # ❌ OBSOLETE
fpdf 1 filter documents         # ❌ OBSOLETE
layoutpdf cache list             # ❌ WRONG APPLICATION
```

### 📋 VALID COMMANDS

All commands use direct syntax without "filter":
- `fpdf [file/range] pages [options]`
- `fpdf [file/range] documents [options]`
- `fpdf [file/range] words [options]`
- `fpdf [file/range] bookmarks [options]`
- `fpdf [file/range] annotations [options]`
- `fpdf [file/range] objects [options]`
- `fpdf [file/range] fonts [options]`
- `fpdf [file/range] metadata [options]`
- `fpdf [file/range] structure [options]`
- `fpdf [file/range] modifications [options]`
- `fpdf [file/range] images [options]`
- `fpdf [file/range] base64 [options]`
- `fpdf cache [subcommand]`
- `fpdf load [mode] [options]`

### 🔧 DEVELOPMENT GUIDELINES

1. **Building the Project:**
   ```bash
   dotnet build fpdf.csproj
   dotnet publish fpdf.csproj -c Release
   ```

2. **Running the Application:**
   ```bash
   # Direct execution
   ./bin/fpdf [command] [options]
   
   # Or if installed globally
   fpdf [command] [options]
   ```

3. **Configuration Files:**
   - `.env` - Environment variables (FPDF_* prefix)
   - `fpdf.config.json` - Application configuration
   - **NO** references to old LayoutPDF config files

4. **Environment Variables:**
   - `FPDF_DEBUG` - Enable debug mode
   - `FPDF_ALLOWED_DIRS` - Allowed directories for security
   - `FPDF_DEFAULT_WORKERS` - Default number of workers
   - `FPDF_CACHE_DIR` - Cache directory location

### 🛡️ SECURITY & VALIDATION

- All paths are validated through `SecurityValidator`
- Configuration system uses `FpdfConfig` class
- Path traversal protection is enabled by default
- Use `.env` or `fpdf.config.json` for allowed directories

### 📦 CURRENT FEATURES

1. **Progress Tracking**: 
   - Real-time progress bar during batch operations
   - Cache size monitoring
   - Detailed statistics at completion

2. **Load Modes**:
   - `ultra` - Full forensic analysis
   - `text` - Text-only extraction
   - `custom` - Balanced extraction
   - `images-only` - Extract only image pages
   - `base64-only` - Extract only base64 content

3. **Cache System**:
   - Automatic caching of processed PDFs
   - Cache management commands
   - Range-based processing from cache

### ⚠️ COMMON MISTAKES TO AVOID

1. **Using old command syntax with "filter"**
2. **Referencing LayoutPDF in code or documentation**
3. **Using old project file names (LayoutPDFC#.csproj)**
4. **Importing from LayoutPDF namespace**
5. **Using LAYOUTPDF_* environment variables**

### 📝 PROJECT HISTORY

This project was originally called LayoutPDF but has been completely refactored and renamed to FilterPDF (fpdf). All references to the old name have been removed to prevent confusion and code reversion issues.

### 🔍 VERIFICATION CHECKLIST

Before making any changes, verify:
- [ ] You're working with FilterPDF, not LayoutPDF
- [ ] Commands don't use "filter" as intermediary
- [ ] Namespace is FilterPDF, not LayoutPDF
- [ ] Binary is fpdf, not layoutpdf
- [ ] Project file is fpdf.csproj

### 🚀 QUICK START

```bash
# Build the project
dotnet build fpdf.csproj

# Install globally
sudo ln -sf /mnt/b/dev-2/fpdf/bin/fpdf /usr/local/bin/fpdf

# Test the installation
fpdf --version

# Process a PDF
fpdf document.pdf pages --word "important"

# Load PDFs for caching
fpdf load images-only --input-dir /path/to/pdfs --num-workers 16

# List cached files
fpdf cache list
```

---

**REMEMBER**: This is FilterPDF (fpdf). Any AI assistant seeing this should NEVER revert to LayoutPDF patterns or use the obsolete "filter" command syntax. The project has been permanently migrated and all old references have been removed.

Last Updated: 2024
Version: 3.11.0