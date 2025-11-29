# FilterPDF (fpdf) - Quick Start Guide

## 🚀 5-Minute Setup

### 1. Install Globally
```bash
sudo ln -sf /mnt/b/dev-2/fpdf/bin/fpdf /usr/local/bin/fpdf
```

### 2. Load Your First PDF
```bash
fpdf load document.pdf ultra
```

### 3. Start Searching
```bash
fpdf 1 pages --word "important"
```

## 📋 Essential Commands

### Loading PDFs
```bash
# Single file
fpdf load invoice.pdf ultra

# Multiple files with workers
fpdf load *.pdf ultra --num-workers 16

# Directory of PDFs
fpdf load /path/to/pdfs/*.pdf ultra
```

### Cache Management
```bash
fpdf cache list     # See what's loaded
fpdf cache stats    # Cache statistics
fpdf cache clear    # Clear everything
```

### Basic Searches
```bash
# Find pages with text
fpdf 1 pages --word "contract"

# Find bookmarks
fpdf 1 bookmarks

# Find specific words
fpdf 1 words --word "invoice"

# Detect documents in multi-doc PDFs
fpdf 1 documents
```

## 🎯 Most Common Use Cases

### 1. Find All Contracts
```bash
fpdf load *.pdf ultra --num-workers 16
fpdf 1-100 pages --word "contract" -F json -o contracts.json
```

### 2. Extract Text from Specific Pages
```bash
fpdf 1 pages --page-ranges "1-5,10" -o first_pages.txt
```

### 3. Find Documents with Values
```bash
fpdf 1-50 documents --value    # Find docs with R$ values
```

### 4. Search Multiple PDFs
```bash
# Search cache indices 1 through 20
fpdf 1-20 pages --word "payment"
```

## 💡 Pro Tips

### Use Cache Index Ranges
```bash
fpdf 1-10 pages --word "test"      # Search PDFs 1-10
fpdf 1,3,5 documents                # Check specific PDFs
fpdf 1-100:odd pages --blank       # Odd-numbered cache entries
```

### Combine Filters
```bash
# Find contracts that aren't cancelled
fpdf 1 pages --word "contract" --not-words "cancelled"

# Find pages with both invoice AND 2024
fpdf 1 pages --word "invoice&2024"
```

### Output Formats
```bash
fpdf 1 pages -F json           # Structured data
fpdf 1 pages -F count          # Just the count
fpdf 1 pages -F csv -o data.csv # Export to CSV
```

### Normalized Search (Ignore Accents)
```bash
fpdf 1 pages --word "~certidao~"   # Finds CERTIDÃO, certidao, etc.
```

## 🔍 Search Operators Quick Reference

| Operator | Example | Description |
|----------|---------|-------------|
| `\|` | `word1\|word2` | OR - finds either |
| `&` | `word1&word2` | AND - finds both |
| `*` | `doc*` | Wildcard - matches any chars |
| `?` | `doc?` | Single char wildcard |
| `~` | `~word~` | Normalized (ignore case/accents) |

## 📊 Output Examples

### JSON Output
```bash
fpdf 1 pages --word "test" -F json
```

### Save to File
```bash
fpdf 1 pages --word "important" -o results.txt
```

### Count Only
```bash
fpdf 1-100 pages --word "invoice" -F count
```

## ⚡ Performance Tips

1. **Use Workers**: Add `--num-workers 16` for parallel processing
2. **Cache First**: Load once with `ultra`, query many times
3. **Use Ranges**: Process multiple PDFs with `1-100` syntax
4. **Text Mode**: Use `fpdf load file.pdf text` for faster loading

## 🆘 Need Help?

```bash
fpdf                    # Show general help
fpdf 1 pages --help     # Help for pages command
fpdf cache --help       # Help for cache command
```

---

**Ready to filter PDFs like a pro? Start with `fpdf load *.pdf ultra`! 🚀**