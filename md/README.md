# FilterPDF (fpdf) - Advanced PDF Filtering & Analysis Tool

## 📋 Overview

FilterPDF (fpdf) is a powerful PDF content filtering and analysis tool. It provides direct access to all filter commands for maximum efficiency and ease of use.

**Version**: 3.0.0  
**Author**: Eduardo Candeia Gonçalves (sindlinger@github.com)

## 🚀 Key Features

- **Promoted Filter Commands**: All filter subcommands are now main-level commands
- **Ultra-Fast Cache System**: Process PDFs once, query instantly (sub-100ms)
- **Advanced Search**: Boolean operators, wildcards, normalized search
- **Multi-Worker Processing**: Process multiple PDFs in parallel (up to 16 workers)
- **Multiple Output Formats**: JSON, XML, CSV, Markdown, Raw, Count
- **Document Boundary Detection**: Automatically identify multi-document PDFs
- **Brazilian Currency Support**: Specialized R$ detection and filtering

## 📦 Installation

### Global Installation
```bash
# Create symbolic link for global access
sudo ln -sf /mnt/b/dev-2/fpdf/bin/fpdf /usr/local/bin/fpdf
```

Now you can use `fpdf` from anywhere without `./`

## 🎯 Command Structure

### Consistent Syntax Rules
- **Cache Index Before Commands**: `fpdf 1 pages`, `fpdf 1 extract`
- **Load Command First**: `fpdf load file.pdf` (NEW SYNTAX!)
- **Cache Without Index**: `fpdf cache list`

### Command Categories

#### Filter Commands (Main Focus)
```bash
fpdf 1 pages           # Filter pages by content
fpdf 1 bookmarks       # Search bookmarks
fpdf 1 words           # Advanced word searching
fpdf 1 documents       # Detect document boundaries
fpdf 1 annotations     # Search annotations
fpdf 1 fonts           # Font analysis
fpdf 1 metadata        # Extract metadata
fpdf 1 structure       # PDF/A compliance
fpdf 1 modifications   # Detect modifications
fpdf 1 objects         # Low-level analysis
```

#### Classic Commands
```bash
fpdf load file.pdf ultra    # Load PDF to cache (NEW SYNTAX!)
fpdf 1 extract -o out.txt   # Extract text from cache
fpdf cache list              # List cached PDFs
fpdf cache clear             # Clear cache
```

## 📚 Common Workflows

### 1. Load and Search Workflow
```bash
# Load PDFs into cache with multiple workers
fpdf load *.pdf ultra --num-workers 16

# List cached PDFs
fpdf cache list

# Search for specific content
fpdf 1 pages --word "contract"
fpdf 1-10 documents --word "invoice"
```

### 2. Extract and Filter Workflow
```bash
# Load a PDF
fpdf load document.pdf ultra

# Extract all text
fpdf 1 extract -o document.txt

# Filter specific pages
fpdf 1 pages --word "important" -F json
```

### 3. Document Analysis Workflow
```bash
# Load multi-document PDF
fpdf load processo.pdf ultra

# Detect document boundaries
fpdf 1 documents

# Search within specific documents
fpdf 1 documents --word "perito"
```

## 🔍 Advanced Search Operators

### Text Search Syntax
```bash
# OR operator (|)
fpdf 1 pages --word "contract|agreement"

# AND operator (&)
fpdf 1 pages --word "invoice&2024"

# Wildcards
fpdf 1 pages --word "doc*"     # Matches document, docs, etc.
fpdf 1 pages --word "doc?"     # Matches docs but not document

# Normalized search (ignores accents/case)
fpdf 1 pages --word "~certidao~"   # Finds CERTIDÃO, certidao, etc.

# Complex combinations
fpdf 1 pages --word "~certidao~&Robson|Felipe"
```

### Negative Filtering
```bash
# Exclude pages with specific words
fpdf 1 pages --word "contract" --not-words "cancelled"

# Exclude documents
fpdf 1 documents --word "processo" --not-words "arquivado"
```

## 📊 Output Formats

All filter commands support multiple output formats:

```bash
fpdf 1 pages -F json        # JSON format
fpdf 1 pages -F xml         # XML format
fpdf 1 pages -F csv         # CSV format
fpdf 1 pages -F md          # Markdown format
fpdf 1 pages -F count       # Count only
fpdf 1 pages -F raw         # Raw data
fpdf 1 pages -F txt         # Text (default)
```

### Save to File
```bash
fpdf 1 pages --word "test" -o results.txt
fpdf 1 pages --word "test" --output-dir ./results/
```

## 🚄 Performance Optimization

### Multi-Worker Processing
```bash
# Use 16 workers for maximum speed
fpdf load *.pdf ultra --num-workers 16

# Process entire directory
fpdf load /path/to/pdfs/*.pdf ultra --num-workers 8
```

### Cache Modes
```bash
fpdf load file.pdf ultra    # Full analysis (slowest, most complete)
fpdf load file.pdf text     # Text only (fastest)
fpdf load file.pdf custom   # Custom options
```

### Cache Management
```bash
fpdf cache list              # List all cached PDFs
fpdf cache stats             # Show cache statistics
fpdf cache clear             # Clear all cache
fpdf cache remove doc        # Remove specific PDF
```

## 💡 Special Features

### Brazilian Currency Detection
```bash
# Find pages with monetary values (R$)
fpdf 1 pages --value
fpdf 1 documents --value
```

### Page Ranges (QPdf-style)
```bash
fpdf 1 pages --page-ranges "1-5,10,15-20"
fpdf 1 pages --page-ranges "1-10:even"     # Even pages
fpdf 1 pages --page-ranges "r1"            # Last page
```

### Font Analysis
```bash
fpdf 1 fonts                               # List all fonts
fpdf 1 pages --font "Times*"               # Pages using Times fonts
fpdf 1 documents --font "Arial|Helvetica"  # Documents with specific fonts
```

## 📖 Examples

### Find Contract Pages
```bash
fpdf load contracts.pdf ultra
fpdf 1 pages --word "contract" --not-words "cancelled" -F json
```

### Extract Document References
```bash
fpdf load processo.pdf ultra
fpdf 1 documents --min-pages 5 --min-confidence 0.7
```

### Search Annotations
```bash
fpdf 1 annotations --type "Highlight" --author "John"
```

### Analyze PDF Structure
```bash
fpdf 1 structure          # PDF/A compliance
fpdf 1 modifications      # Detect changes
fpdf 1 metadata          # Extract metadata
```

## 🛠️ Troubleshooting

### Cache Not Found
```bash
# Rebuild cache index
fpdf cache rebuild
```

### Performance Issues
```bash
# Use fewer workers if system is slow
fpdf load *.pdf ultra --num-workers 4

# Use text mode for faster processing
fpdf load *.pdf text
```

### Memory Issues
```bash
# Process in smaller batches
fpdf load file1.pdf ultra
fpdf load file2.pdf ultra
```

## 📝 Recent Changes

### Version 3.0.0
- ✅ Promoted all filter subcommands to main level
- ✅ New consistent `load` syntax: `fpdf load file.pdf`
- ✅ Removed inconsistent file-before-command pattern
- ✅ Global command installation support
- ✅ Improved help system and documentation

## 🔗 Related Documentation

- [TESTES_FPDF.md](testes/TESTES_FPDF.md) - Complete test documentation
- [HELP_TESTS.md](testes/HELP_TESTS.md) - Help system documentation
- [MUDANCA_LOAD.md](testes/MUDANCA_LOAD.md) - Load command syntax change
- [CLAUDE.md](CLAUDE.md) - Claude Code integration guide

## 📄 License

Created by Eduardo Candeia Gonçalves  
Uses: iTextSharp, iTextSharp.pdfa, iTextSharp.xtra, Newtonsoft.Json

---

**FilterPDF (fpdf)** - Making PDF analysis simple, fast, and powerful! 🚀