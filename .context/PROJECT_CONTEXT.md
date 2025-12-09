# FilterPDF (fpdf) - Comprehensive Project Context

## 🎯 Project Overview

### Core Identity
- **Application Name**: FilterPDF (fpdf)
- **Version**: 3.7.1
- **Original Name**: LayoutPDF (completely refactored and renamed)
- **Purpose**: Advanced PDF analysis and filtering tool with cache-based processing
- **Developer**: Eduardo Candeia Gonçalves

### Technology Stack
- **Language**: C# (.NET 6.0)
- **Platform**: Linux/WSL2
- **Key Dependencies**: 
  - Newtonsoft.Json
  - System.Text.Encoding
- **Build System**: dotnet CLI
- **Deployment**: Binary at `/mnt/b/dev-2/fpdf/bin/fpdf`

### Architectural Foundations
- **Design Pattern**: Command pattern architecture
- **Processing Model**: Cache-first approach for performance
- **Security Model**: Path validation with SecurityValidator
- **Data Storage**: JSON-based cache files
- **Command Structure**: Direct syntax (no "filter" intermediary)

## 📊 Current State

### Cache Statistics
- **Total PDFs Cached**: 3,209
- **Cache Size**: 3.2 GB
- **Cache Location**: Configured via FPDF_CACHE_DIR
- **Processing Mode**: images-only extraction completed

### Recent Implementations
1. **StatsCommand**: Statistical analysis for single PDFs and ranges
2. **ProgressTracker**: Real-time progress feedback for batch operations
3. **Automatic Symbolic Link**: Post-build link creation to /usr/local/bin
4. **Security Enhancements**: Path traversal protection
5. **Command Refactor**: Eliminated command duplication

### Project Status
- **Stability**: Production-ready (v3.7.1)
- **Known Issues**: None identified
- **Technical Debt**: Resolved through recent refactor
- **Performance**: Optimized for batch operations with progress tracking

## 🏗️ Architecture & Design

### Command Architecture
```
FilterPDFCLI (Entry Point)
    ├── CommandRegistry (Registration & Discovery)
    ├── CommandExecutor (Execution & Error Handling)
    └── Commands/
        ├── Base: ExtractCommand, LoadCommand, CacheCommand
        ├── Analysis: PagesCommand, DocumentsCommand, WordsCommand
        ├── Metadata: BookmarksCommand, AnnotationsCommand
        ├── Resources: ImagesCommand, FontsCommand, ObjectsCommand
        ├── Special: StatsCommand, OCRCommand, ScannedCommand
        └── Forensic: ModificationsCommand, StructureCommand
```

### Cache Structure
```json
{
  "Pages": [
    {
      "PageNumber": int,
      "Width": decimal,
      "Height": decimal,
      "Text": string,
      "Words": [...],
      "Images": [...],
      "Links": [...]
    }
  ],
  "TextInfo": {...},
  "Resources": {...},
  "Metadata": {...}
}
```

### Security Implementation
- **SecurityValidator Class**: Core validation logic
- **Path Traversal Protection**: Enabled by default
- **Allowed Directories**: Configured via .env or fpdf.config.json
- **Environment Variables**: FPDF_* prefix for all settings

## 💻 Code Patterns & Conventions

### Namespace Structure
```csharp
namespace FilterPDF
{
    namespace Commands { }
    namespace Options { }
    namespace Services { }
    namespace Security { }
    namespace Utils { }
    namespace Configuration { }
}
```

### Command Pattern Implementation
```csharp
public abstract class BaseCommand : ICommand
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract void Execute(string[] args);
}
```

### Options Parsing Pattern
```csharp
Dictionary<string, string> options = ParseOptions(args);
bool hasWord = options.ContainsKey("word");
string format = options.GetValueOrDefault("format", "json");
```

### Error Handling Strategy
```csharp
try
{
    // Command execution
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Environment.Exit(1);
}
```

## 🚨 Critical Anti-Reversion Rules

### NEVER Use These Patterns
1. ❌ **layoutpdf** or **LayoutPDF** references
2. ❌ **lpdf** or **LPDF** abbreviations
3. ❌ **filter** as command intermediary
4. ❌ **LAYOUTPDF_*** environment variables
5. ❌ **LayoutPDFC#.csproj** project file

### ALWAYS Use These Patterns
1. ✅ **fpdf** as executable name
2. ✅ **FilterPDF** namespace
3. ✅ **Direct command syntax**: `fpdf [file/range] [command] [options]`
4. ✅ **FPDF_*** environment variables
5. ✅ **fpdf.csproj** project file

## 📋 Command Reference

### Core Commands
- **extract**: `fpdf file.pdf extract [options]`
- **load**: `fpdf load [mode] --input-dir /path --num-workers 16`
- **cache**: `fpdf cache [list|clear|info|stats]`
- **stats**: `fpdf [file/range] stats [options]`

### Analysis Commands
- **pages**: Extract page information
- **documents**: Filter documents
- **words**: Extract and filter words
- **bookmarks**: Extract bookmarks
- **annotations**: Extract annotations
- **objects**: Analyze PDF objects
- **fonts**: Extract font information
- **metadata**: Extract metadata
- **structure**: Analyze document structure
- **modifications**: Track document changes
- **images**: Extract images
- **base64**: Extract base64 content
- **scanned**: Detect scanned pages

### Load Modes
- **ultra**: Full forensic analysis
- **text**: Text-only extraction
- **custom**: Balanced extraction
- **images-only**: Extract only image pages
- **base64-only**: Extract only base64 content

## 🔧 Configuration

### Environment Variables
```bash
FPDF_DEBUG=true                    # Enable debug mode
FPDF_ALLOWED_DIRS=/path1:/path2   # Allowed directories
FPDF_DEFAULT_WORKERS=16            # Worker threads
FPDF_CACHE_DIR=/custom/cache      # Cache location
```

### Configuration File (fpdf.config.json)
```json
{
  "allowedDirectories": ["/mnt/b", "/home/user"],
  "defaultWorkers": 16,
  "cacheDirectory": "/var/cache/fpdf",
  "debugMode": false
}
```

## 📈 Performance Metrics

### Current Benchmarks
- **Cache Size**: 3.2 GB for 3,209 PDFs
- **Processing Speed**: Batch operations with progress tracking
- **Memory Usage**: Optimized for large PDF collections
- **Concurrency**: Configurable worker threads (default: 16)

### Optimization Opportunities
- Parallel processing for large batches (partially implemented)
- Stream processing for very large PDFs
- Incremental cache updates
- Memory-mapped file access for cache

## 🗺️ Future Considerations

### Potential Enhancements
- OCR integration (OCRCommand scaffolded)
- Directory-level statistics
- Advanced search with regex patterns
- Cache compression options
- Export to multiple formats

### Maintenance Notes
- Regular cache cleanup recommended
- Monitor cache growth in production
- Update symbolic links after deployment
- Verify security configuration regularly

## 📝 Session Context Preservation

### Key Files to Monitor
- `/mnt/b/dev-2/fpdf/CLAUDE.md` - Anti-reversion instructions
- `/mnt/b/dev-2/fpdf/fpdf.csproj` - Project configuration
- `/mnt/b/dev-2/fpdf/src/FilterPDFCLI.cs` - Main entry point
- `/mnt/b/dev-2/fpdf/src/Commands/*` - Command implementations
- `/mnt/b/dev-2/fpdf/.env` - Environment configuration

### Git Status Snapshot
- **Branch**: main
- **Last Commit**: feat(refactor): major architecture refactor
- **Clean Working Directory**: No uncommitted changes
- **Version Tag**: v3.7.1

### Critical Context for AI Agents
1. **Identity Crisis Prevention**: This is FilterPDF, never LayoutPDF
2. **Command Syntax**: Direct commands without "filter"
3. **Namespace Consistency**: Always use FilterPDF namespace
4. **Cache-First Design**: All operations work with cached data
5. **Security by Default**: Path validation is mandatory

## 🔄 Agent Handoff Protocol

### When Resuming Work
1. Read `/mnt/b/dev-2/fpdf/CLAUDE.md` first
2. Verify project identity (FilterPDF, not LayoutPDF)
3. Check current git status
4. Review this context file
5. Validate command syntax is direct (no "filter")

### When Ending Session
1. Update this context file with changes
2. Commit any code changes with clear messages
3. Document any new patterns or decisions
4. Note any unresolved issues
5. Update version number if applicable

---
**Last Updated**: 2025-08-15
**Context Version**: 1.0.0
**Project Version**: 3.7.1
**Maintained By**: AI Assistant with Eduardo Candeia Gonçalves