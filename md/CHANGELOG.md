# CHANGELOG

## Important: Project Renaming
**This project has been completely renamed from LayoutPDF to FilterPDF (fpdf).**
- Current executable: `fpdf`
- Old executable name in historical entries: `layoutpdf` (no longer used)
- The "filter" command shown in old entries has been removed - all commands are now direct
- Historical entries are preserved for reference but reflect obsolete command syntax

## [3.8.0] - 2025-08-15

### 🚨 CRITICAL: Complete Codebase Cleanup
**Major refactoring to permanently eliminate all references to obsolete LayoutPDF application**

#### Breaking Changes
- **Command Syntax**: Load command now uses proper flag-based syntax
  - ❌ Old: `fpdf document.pdf load ultra`
  - ✅ New: `fpdf load ultra --input-file document.pdf`
  - ✅ New: `fpdf load ultra --input-dir /path/to/pdfs`

#### Major Improvements
- **Complete Legacy Code Removal**: 
  - Eliminated 195+ references to "layoutpdf" across 48 files
  - Removed all traces of obsolete "filter" command syntax
  - Updated all namespaces from `LayoutPDF` to `FilterPDF`
  - Cleaned all documentation, scripts, and configuration files

- **Progress Tracking System**:
  - Added real-time progress bar with percentage completion
  - Live cache size monitoring during operations
  - Processing rate display (files/second)
  - ETA calculation for batch operations
  - Detailed completion statistics

- **Configuration System**:
  - New `FpdfConfig` class for centralized configuration
  - Support for `.env` files with `FPDF_*` environment variables
  - JSON configuration file support (`fpdf.config.json`)
  - Hierarchical configuration: Environment > .env > JSON > defaults
  - Security configuration with allowed directories

#### Security & Performance
- **Path Validation**: Enhanced `SecurityValidator` with configuration support
- **Worker Configuration**: Default workers now configurable via environment/config
- **Batch Processing**: Improved parallel processing with progress tracking
- **Resource Management**: Better memory and CPU utilization

#### Documentation Updates
- **CLAUDE.md**: Created comprehensive anti-reversion instructions
- **README.md**: Removed all legacy references
- **QUICK_START.md**: Updated installation paths and examples
- **CONFIG.md**: New configuration documentation
- **MIGRATION_GUIDE.md**: Updated to reflect current state

#### Files Modified (48 total)
- **Source Code**: 19 C# files updated
- **Documentation**: 5 markdown files cleaned
- **Scripts**: 15 shell/Python scripts updated  
- **Configuration**: 9 config files corrected

#### Bug Fixes
- Fixed LoadCommand syntax errors preventing compilation
- Corrected help text to show proper command syntax
- Fixed progress tracker integration issues
- Resolved namespace conflicts

#### Technical Details
- Removed `LayoutPDFC-original.sln` file
- Updated project file references to use `fpdf.csproj`
- Changed environment variables from `LAYOUTPDF_*` to `FPDF_*`
- Eliminated `IFilterCommand` interface (obsolete)
- Renamed command methods to `ExecuteCommandDirect`

### Migration Notes
**For users upgrading from previous versions:**
1. Update all scripts to use new load command syntax
2. Replace any `LAYOUTPDF_*` environment variables with `FPDF_*`
3. Review `.env` file for new configuration options
4. The "filter" command no longer exists - use direct commands

## [3.7.1] - 2025-08-14

### Image Extraction Improvements
- **FIXED**: LoadCommand now properly extracts and indexes embedded images in PDFs
- **Added**: DetailedImageExtractor integration in all load modes (text, ultra, custom, both)
- **Enhanced**: Image metadata extraction including dimensions, color space, and compression type
- **Fixed**: Images are now correctly stored in cache for all extraction modes
- **Updated**: Both "text" and "both" modes now include complete image information

### Technical Improvements
- **LoadCommand**: Added ExtractPageImages method for consistent image extraction
- **Text Mode**: Now includes Resources field with embedded images
- **Both Mode**: Enhanced to ensure images are extracted even from PDFAnalyzer.AnalyzeFull()
- **Image Statistics**: Added proper collection of image types and sizes
- **Cache Version**: Updated to 3.7.0 for improved image handling

### Bug Fixes
- **Image Indexing**: Fixed issue where images were not accessible after PDF indexing
- **Resource Field**: Fixed empty Resources field in cached JSON files
- **ImagesInfo Field**: Now properly populated with image count and metadata

## [3.7.0] - 2025-08-13

### CRITICAL Security Fixes
- **FIXED COMMAND INJECTION**: Fixed critical command injection vulnerabilities in OCRCommand.cs and ExtractImagesCommand.cs
- **Parameterized Commands**: Replaced string concatenation with ProcessStartInfo.ArgumentList to prevent injection
- **Input Validation**: Added comprehensive input validation for all external command parameters
- **Path Sanitization**: Integrated SecurityValidator for all file path operations
- **Removed Hardcoded Paths**: Removed hardcoded user-specific paths from SecurityValidator.cs
- **Environment Configuration**: Added FPDF_ALLOWED_DIRS environment variable support for dynamic directory configuration

### Security Improvements
- **OCRCommand**: Fixed unsafe string concatenation in Tesseract and pdftoppm command execution
- **ExtractImagesCommand**: Fixed command injection in pdftoppm image extraction process
- **SecurityValidator**: Removed hardcoded "/mnt/c/Users/pichau/Desktop/geral_pdf" and similar paths
- **Dynamic Directory Management**: Current working directory automatically included in allowed paths
- **Configurable Security**: Directories can now be configured via environment variables

### Technical Details
- **Command Safety**: All external processes now use ArgumentList instead of Arguments string
- **Input Validation**: Page numbers, language codes, and file paths validated before use
- **Path Security**: All file operations validated through SecurityValidator.IsPathSafe()
- **Environment Integration**: FPDF_ALLOWED_DIRS environment variable parsed on startup
- **Error Handling**: Security exceptions properly thrown and handled for unsafe operations

### Breaking Changes
- **Environment Required**: Users must now set FPDF_ALLOWED_DIRS to access PDF files outside project directory
- **Path Validation**: All file operations now subject to security validation (may reject previously accepted paths)

## [3.6.0] - 2025-08-11

### Security Improvements
- **CRITICAL SECURITY FIX**: Implemented comprehensive security validation to prevent path traversal and command injection vulnerabilities
- **Added SecurityValidator**: New security layer that validates all file paths and user inputs
- **Path Traversal Protection**: Blocks attempts to access files outside allowed directories (e.g., /etc/passwd, ../../../)
- **Command Injection Protection**: Prevents execution of shell commands through user input (e.g., ; ls, $(whoami))
- **Whitelist Directory System**: Only allows PDF access within predefined safe directories
- **Input Sanitization**: Validates and sanitizes wildcards, cache ranges, and file paths

### Added
- **src/Security/SecurityValidator.cs**: Central security validation class with comprehensive protection mechanisms
- **Allowed Directory Whitelist**: Configurable list of directories where PDFs can be accessed
- **Dangerous Pattern Detection**: Blocks dangerous characters and patterns in user input
- **Forbidden Path Protection**: Prevents access to system directories (/etc, /proc, C:\Windows, etc.)
- **File Size Limits**: Rejects files larger than 500MB to prevent DoS attacks
- **Cache Range Validation**: Ensures cache range specifications are safe

### Changed
- **PdfAccessManager**: Now validates all PDF paths through SecurityValidator before access
- **CacheManager**: Validates both original and cache file paths before adding to cache
- **FilterPDFCLI**: Added security validation for all user inputs at entry point
- **ExpandWildcards**: Sanitizes wildcard patterns to prevent malicious expansion
- **Error Messages**: Improved security error messages with clear explanations

### Fixed
- **CVE Prevention**: Addresses potential path traversal vulnerability (CWE-22)
- **Command Injection**: Prevents shell command injection attacks (CWE-78)
- **Information Disclosure**: No longer reveals full system paths in error messages

### Security Configuration
- Default allowed directories include project folders and specific user PDF directory
- Expandable whitelist system for adding new safe directories
- Comprehensive validation for all file operations

## [3.5.0] - 2025-08-10

### Changed
- **Project Cleanup**: Moved a significant number of versioned scripts, temporary data files, and obsolete artifacts to a new `.legacy` directory. This cleans up the project's root directory, improving clarity and organization.
- **Git Ignore**: The `.legacy` directory has been added to `.gitignore` to prevent obsolete files from being tracked.

## [3.4.0] - 2025-07-21

### Major Architectural Changes
- **Complete Cache-Based Architecture**: All filter operations now work exclusively with cached data
- **Centralized PDF Access**: New PdfAccessManager centralizes all PDF reader operations
- **Wildcard Support**: Full wildcard expansion for batch PDF processing (*.pdf, doc*.pdf, etc.)
- **Load Command Flexibility**: Load command now defaults to 'ultra' mode when no subcommand specified

### Added
- **PdfAccessManager**: Centralized manager for all PDF access and reader lifecycle
- **Wildcard Expansion**: Process multiple PDFs with patterns like `*.pdf` or `doc*.pdf`
- **Default Load Mode**: `load` command now uses 'ultra' mode by default
- **Parallel Workers Support**: `--workers` option for parallel PDF processing
- **Comprehensive Test Suite**: Full test coverage for all commands and features

### Changed
- **Load Command Syntax**: Now accepts both `load` (defaults to ultra) and `load [mode]`
- **Batch Processing**: Wildcard patterns properly expand to process all matching PDFs
- **Error Handling**: Improved error messages for cache-only operations
- **Test Infrastructure**: Migrated tests to new command syntax and architecture

### Fixed
- **Load Command Error**: Fixed "No subcommand specified" error by implementing default behavior
- **JSON Date Parsing**: Improved handling of PDF date formats in cache deserialization
- **Wildcard Processing**: Quotes around patterns now properly handled (e.g., "*.pdf")
- **Worker Parallelism**: Fixed worker count display in wildcard processing

### Performance
- **Cache-Only Operations**: All filter commands now have instant access to pre-processed data
- **Parallel Loading**: Multiple PDFs can be loaded simultaneously with --workers
- **Memory Efficiency**: Centralized reader management reduces memory overhead
- **Batch Performance**: Wildcard processing optimized for large file sets

### Technical Details
- Filter commands require cache files exclusively (no direct PDF processing)
- PdfAccessManager handles reader caching and lifecycle management
- Wildcard expansion happens at CLI level before command execution
- Load command applies 'ultra' settings when no mode specified
- All test scripts updated for new architecture compatibility

### Breaking Changes
- **None**: Load command maintains backward compatibility while adding default behavior
- **Enhanced**: Wildcard support is additive and doesn't break existing functionality

## [3.2.0] - 2024-07-19

### Added
- **CacheMemoryManager**: Centralized cache loading with in-memory storage
- **Parallel Pre-loading**: Cache files are loaded in parallel using all CPU cores
- **Debug Timing**: Added detailed timing information for cache operations
- **Progress Reporting**: Shows progress during cache range processing

### Changed
- **Cache Loading Performance**: 2.3x faster for large ranges (500 files: 62s → 27s)
- **Memory Optimization**: Cache files remain in memory during execution
- **Centralized Access**: All cache loading now goes through CacheMemoryManager

### Technical Details
- Pre-loading phase uses `Parallel.ForEach` with `MaxDegreeOfParallelism = Environment.ProcessorCount`
- In-memory cache prevents reloading the same file multiple times
- Debug messages show loading source (DISK vs MEMORY) and timing
- Processing remains sequential for JSON output to avoid Console.SetOut conflicts

### Performance Improvements
- 50 files: ~5 seconds (pre-loading: 1.7s)
- 500 files: ~27.6 seconds (pre-loading: 23s)
- Individual file loading: ~125ms → cached instant access

## [3.1.0] - 2024-07-19

### BREAKING CHANGES
- **Cache-Only Filter Commands**: All filter commands now REQUIRE cache files
- **No Direct PDF Processing**: Filter commands no longer accept PDF files directly
- **Performance First**: This ensures MAXIMUM PERFORMANCE by eliminating PDF reprocessing

### Added
- **Centralized Cache Loading Message**: All commands show "🔍 Loading from CACHE" with performance note
- **Clear Error Messages**: Helpful error when trying to use PDF directly with filter commands
- **Force Cache Usage**: FilterCommand now rejects PDFs and provides migration instructions

### Changed
- **FilterCommand**: Now requires `.json` cache files exclusively
- **All Filter Subcommands**: Removed PdfReader and direct PDF processing capabilities
- **FilterModificationsCommand**: Shows warning that modification detection requires cache enhancement

### Removed
- **Direct PDF Processing**: Removed from FilterPagesCommand, FilterWordsCommand, FilterFontsCommand, FilterAnnotationsCommand
- **PdfReader Usage**: Eliminated from all filter commands (except LoadCommand)
- **PDF Validation**: Filter commands no longer validate PDF headers

### Technical Details
- Filter commands now use `isUsingCache = true` always
- Removed all `new PdfReader()` instantiations from filter commands
- Performance guaranteed: Always uses pre-processed cache data
- Only LoadCommand retains PDF processing capability

### Migration Guide
```bash
# OLD (no longer works):
layoutpdf document.pdf filter pages -w "word"

# NEW (required):
layoutpdf document.pdf load ultra              # First, load to cache
layoutpdf 1 filter pages -w "word"            # Then, use cache index
# OR
layoutpdf document.json filter pages -w "word" # Use cache file directly
```

## [3.0.2] - 2024-07-19

### Fixed
- **FilterWordsCommand**: Restored advanced search functionality with operators
- **Wildcard Support**: Fixed wildcard patterns (`*`, `?`) in word searches
- **OR Logic**: Fixed to extract words from ALL matching terms, not just the last one
- **AND Logic**: Maintained proper page-level filtering while extracting individual words

### Technical Details
- Modified `FindWordMatchesFuzzy` to properly handle wildcard patterns without escaping them
- Preserved fuzzy search capability alongside centralized WordOption matching
- Wildcard patterns now correctly convert: `*` → `.*` and `?` → `.` in regex
- All search operators (`&`, `|`, `*`, `?`, `~word~`) now work correctly in filter words

## [3.0.1] - 2024-07-18

### Changed
- **FilterFontsCommand**: Migrated to use WordOption for font name matching
- **FilterObjectsCommand**: Migrated to use WordOption for detailed text search

### Technical Details
- Completed migration of all text-searching commands to centralized WordOption
- Commands without text search (FilterModificationsCommand, FilterMetadataCommand, FilterStructureCommand) don't require migration
- Total of 7 commands now use centralized text search logic

## [3.0.0] - 2024-07-18

### Major Architecture Changes
- **Centralized Options System**: Created `src/Options/` directory for shared option logic
- **WordOption.cs**: Centralized text search logic supporting `&`, `|`, `*`, `?` operators
- **Normalized Search**: Unified `~palavra~` syntax across all commands
- **Code Reduction**: Removed ~300+ lines of duplicated search logic

### Added
- **Options/WordOption.cs**: Central text matching logic with operators and normalization
- **--not-words Support**: Added to FilterBookmarksCommand, FilterWordsCommand, FilterAnnotationsCommand
- **Normalized Search in FilterWordsCommand**: Full support for `~palavra~` syntax

### Changed
- **FilterPagesCommand**: Migrated to use WordOption.Matches()
- **FilterBookmarksCommand**: Migrated to use WordOption.Matches()
- **FilterWordsCommand**: Integrated WordOption while maintaining fuzzy search
- **FilterAnnotationsCommand**: Migrated to use WordOption.Matches()
- **FilterDocumentsCommand**: Migrated to use WordOption.Matches()

### Removed
- **filter headers Command**: Removed (didn't support cache)
- **filter footers Command**: Removed (didn't support cache)
- **filter all Command**: Removed (didn't support cache)
- **Duplicate Methods**: PageContainsText, DocumentContainsText, ProcessMultiValueText, etc.

### Technical Details
- All text search now goes through WordOption.Matches()
- Consistent operator precedence: AND operations before OR
- WordOption.GetSearchDescription() provides human-readable search descriptions
- FilterWordsCommand maintains specialized word extraction while using WordOption

## [2.36.0] - 2024-07-18

### Fixed
- **JSON Output Filtering**: Range processing now only includes files with actual results
- **Zero Results Handling**: Files with 0 matches are excluded from JSON output
- **Results Counting**: `arquivosComResultados` now correctly reflects only files with matches

### Changed
- **Range Processing**: Added result filtering logic to check for positive match counts
- **JSON Structure**: Maintains correct structure while filtering out empty results
- **Performance**: Reduced JSON output size by excluding non-matching files

### Technical Details
- Checks multiple result count fields: paginasEncontradas, documentosEncontrados, etc.
- Uses ToObject<int>() for safe JSON value extraction
- Filters results before adding to final JSON array

## [2.35.0] - 2024-07-17

### Added
- **Normalized Text Search**: New `~word~` syntax for accent and case-insensitive search
- **Flexible Search Options**: Mix normalized and exact searches in same query
- **Enhanced Documentation**: Comprehensive examples for all operator combinations
- **Operator Precedence Guide**: Created OPERATOR_PRECEDENCE.md documentation

### Changed
- **FilterPagesCommand**: Added NormalizeText() and CheckNormalizationSyntax() methods
- **Search Logic**: Updated PageContainsText() to support normalized search syntax
- **Help Documentation**: Expanded with detailed examples for each search pattern

### Fixed
- **Range Processing**: Fixed empty pages issue when using range index with -o option
- **FilterDocumentsCommand**: Implemented OutputManager for consistent file handling
- **Removed Unused Code**: Cleaned up NormalizePath() and ConvertToRelativePathIfNeeded()

### Technical Details
- Normalized search removes accents (á→a, ã→a, ç→c, etc.) and converts to lowercase
- Supports all operators with normalization: `&`, `|`, `*`, `?`
- Examples:
  - `~certidao~` finds: CERTIDÃO, Certidão, certidao, CERTIDAO
  - `~justica~&tribunal` finds: justiça (any form) AND tribunal (exact)
  - `banco|~certidao~` finds: banco (exact) OR certidão (any form)
  - `~cert*~` finds: certificado, CERTIDÃO, Certidao, etc.

## [2.34.2] - 2024-07-17

### Fixed
- **Range Processing with Output File**: Fixed empty pages when using range index with -o option
- **CLI Range Processing**: Removed -o option from individual commands in range processing
- **Output Handling**: Range processing now handles output file saving at CLI level

### Changed
- **FilterDocumentsCommand**: Implemented OutputManager for consistent output handling
- **Code Cleanup**: Removed unused NormalizePath() and ConvertToRelativePathIfNeeded() methods

## [2.34.1] - 2024-07-17

### Fixed
- **Null Character Cleanup**: Fixed null characters (`\u0000`) in JSON text output
- **Text Normalization**: Replaced malformed "Pro\u0000ssão" with proper "Profissão" 
- **Case Variations**: Added support for both "Profissão" and "profissão" corrections
- **Enhanced Text Processing**: Improved CleanTextForReading function in both FilterPagesCommand and FilterDocumentsCommand

### Technical Details
- Added null character removal (`\u0000`) to text cleaning pipeline
- Implemented specific replacements for common OCR artifacts:
  - "Pro\u0000ssão" → "Profissão"
  - "Prossão" → "Profissão" 
  - "prossão" → "profissão"
- Updated both FilterPagesCommand and FilterDocumentsCommand for consistency

## [2.34.0] - 2024-07-17

### Added
- **Complete JSON Output Standardization**: All filter subcommands now follow consistent structure
- **Comprehensive Filter Options in JSON**: When any filter option is used, it appears in BOTH search criteria AND individual results
- **Enhanced FilterBookmarksCommand**: Added support for `--orientation` (`-or`) filtering
- **Expanded Help Documentation**: All filter commands now include "FILTER OPTIONS IN JSON OUTPUT" section

### Changed
- **Standardized JSON Structure**: All filter commands now use consistent format:
  - `pages`: `{"arquivo": "...", "paginasEncontradas": N, "paginas": [...]}`
  - `documents`: `{"arquivo": "...", "documentosEncontrados": N, "documentos": [...]}`
  - `bookmarks`: `{"arquivo": "...", "bookmarksEncontrados": N, "bookmarks": [...]}`
- **Enhanced User Experience**: Filter options automatically appear in JSON output when used
- **Improved FilterCommand**: Added `-or`, `--orientation`, `-v`, `--value` to bookmarks valid options

### Fixed
- **FilterDocumentsCommand**: Fixed scope issue with filterOptions parameter passing
- **FilterBookmarksCommand**: Implemented orientation filtering logic for bookmark page detection
- **Consistent Behavior**: All filter commands now show active filter values in results

### Technical Details
- Updated `OutputJson` methods to accept `filterOptions` parameter
- Implemented page orientation detection for bookmarks based on destination page
- Added comprehensive help text explaining filter option behavior in JSON output
- Enhanced match reasons to include orientation filtering information

### Examples of New Behavior
```json
// When using -w "felipe" --orientation portrait
{
  "arquivo": "document.pdf",
  "paginasEncontradas": 5,
  "paginas": [
    {
      "pageNumber": 1,
      "content": "...",
      "searchedWords": "felipe",
      "orientation": "portrait"
    }
  ]
}
```

## [2.33.0] - 2024-07-17

### Added
- **Optional Information Flags System**: Standardized output format for all filter subcommands
- **Modular JSON Output**: Users can now choose which information to include in JSON output
- **New Optional Flags for `filter pages`**:
  - `-c, --content`: Include page content (default if no flags)
  - `-s, --size`: Include page size (width, height)
  - `-so, --show-orientation`: Include page orientation (portrait/landscape)
  - `-wc, --word-count`: Include word count
  - `-cc, --char-count`: Include character count
  - `-i, --images`: Include image information
  - `-col, --columns`: Include column information
  - `-t, --tables`: Include table information
  - `-a, --annotations`: Include annotation count
  - `-f, --fonts`: Include font list
  - `-b, --bookmarks`: Include bookmarks pointing to page
  - `-mr, --match-reasons`: Include match reasons

### Changed
- **Standardized Output Format**: All filter subcommands now follow the same pattern as `documents`
- **Default Behavior**: When no optional flags are specified, shows minimal output (pageNumber + content)
- **Enhanced User Control**: Users can now add specific information incrementally using flags

### Fixed
- **Terminal Buffer Issue**: Fixed console output hanging without pipe (`| cat`)
- **Forced Process Exit**: Added explicit Environment.Exit(0) for proper termination

### Technical Details
- Implemented recursive bookmark search for multi-level bookmark structures
- Added proper null handling for page content extraction
- Enhanced FilterCommand to recognize all new optional flags
- Improved help documentation with clear examples

### Breaking Changes
- **None**: All changes are backward compatible
- **Enhanced**: Existing commands work the same but with more options available

## [2.32.0] - 2024-07-17

### BREAKING CHANGES
- **Converted project to Linux native**: Changed from `win-x64` to `linux-x64` target
- **Executable renamed**: `layoutpdf.exe` → `layoutpdf` (Linux native binary)
- **Build output optimized**: Removed debug symbols and Windows-specific files

### Added
- Linux native self-contained executable (71MB)
- Optimized build configuration for Linux deployment
- Clean build process with minimal output artifacts

### Changed
- **Project target**: `RuntimeIdentifier` from `win-x64` to `linux-x64`
- **Build paths**: Windows paths (`\`) → Linux paths (`/`)
- **PublishDir**: `bin\` → `bin/`
- **Debug settings**: Disabled debug symbols and .pdb generation
- **Documentation**: Updated CLAUDE.md with Linux-specific instructions

### Fixed
- **CRITICAL**: Fixed Console.Out redirection loop causing range processing hang
- **CRITICAL**: Resolved `-w` option conflict between `--word` and `--whitelist`
- Fixed JSON range processing for mixed output (debug text + JSON)
- Enhanced range processing to handle all filter subcommands properly

### Technical Details
- Removed nested Console.SetOut redirection that caused infinite loops
- Fixed `--whitelist` to not conflict with `-w` (word) option
- Added `ExtractJsonFromMixedOutput` method for robust JSON parsing
- Implemented proper null-coalescing operators for nullable references
- Enhanced FilterCommand and FilterPagesCommand with `-or` support

### Removed
- Windows executable and related files (.exe, .dll, .pdb)
- Debug symbols and unnecessary build artifacts
- `-v minimal` usage (developer transparency improvement)

## [2.31.9] - 2024-07-17

### Added
- Added short option support for `--orientation` filter: `-or`
- Both `-or` and `--orientation` now work identically for page orientation filtering

### Changed
- Enhanced command-line option parsing to support both short and long forms
- Improved user experience with more concise option syntax

### Fixed
- Fixed all nullable reference warnings in the codebase without suppression
- Resolved type conversion issues in LoadCommand.cs and AdvancedPDFProcessor.cs
- Fixed JSON range processing for mixed output (debug text + JSON)
- Enhanced range processing to properly handle all filter subcommands

### Technical Details
- Added `ExtractJsonFromMixedOutput` method to parse JSON from mixed console output
- Implemented proper null-coalescing operators for nullable string handling
- Enhanced FilterCommand and FilterPagesCommand with `-or` support in both filter logic and match reasons

## [2.31.8] - 2024-07-17

### Fixed
- Fixed --value filter not being recognized in filter documents command
- Fixed documents without currency values being incorrectly included when --value filter is used
- Fixed --value filter not appearing in criteriosDeBusca section of JSON output
- Added support for "value" (without hyphens) as valid filter option
- Fixed conflict between -v (value) and -v (verbose) in filter commands

### Changed
- Enhanced FilterCommand parser to recognize --value and -v as boolean filter options
- Improved filter logic to properly apply AND conditions between word and value filters
- Documents now only appear in results if they match ALL specified criteria

### Added
- Document name extraction in JSON output - shows meaningful title from first lines of document
- Text cleaning in JSON output - removes \n characters and formats text for better readability
- Improved text organization with automatic sentence breaking for long content

## [2.31.7] - 2024-07-17

### Fixed
- **CRITICAL**: Fixed file output not working with absolute paths in WSL environment
- **CRITICAL**: Fixed individual processing OutputManager not creating files in WSL
- **CRITICAL**: Fixed range processing OutputManager not creating files with absolute paths
- Implemented automatic absolute-to-relative path conversion for WSL compatibility
- Bypassed OutputManager StreamWriter limitations with direct File.WriteAllText approach
- Fixed win-x64 executable compatibility in WSL by detecting WSL environment properly
- Enhanced WSL detection using directory existence checks (/mnt) and environment variables

### Technical Details
- OutputManager's Console.SetOut(StreamWriter) approach fails with absolute Unix paths in win-x64 executables
- Solution: StringWriter buffer + File.WriteAllText for both individual and range processing
- Automatic path conversion: `/mnt/b/dev/layoutpdf/file.txt` → `file.txt` when needed
- Maintains user experience while ensuring file creation success

### Changed
- Range processing now uses StringWriter buffering instead of OutputManager for cross-platform compatibility
- Individual processing enhanced with path normalization and automatic conversion
- Both processing types preserve concatenation behavior and output formatting

## [2.31.6] - 2024-07-17

### Fixed
- Removed all emojis from codebase that were causing compilation issues
- Fixed cache range output concatenation - now properly appends all results to single file
- Fixed relative path handling to use absolute paths from current directory
- Fixed compilation error with processedCount variable scope

### Changed  
- Removed RuntimeIdentifier from project file to enable true multi-platform support
- Application now compiles as platform-agnostic instead of win-x64 specific

## [2.31.5] - 2024-07-16

### Fixed
- Cross-platform path handling for multi-platform support
- Windows path format (B:\dev\...) no longer appears in WSL/Linux environments
- Added NormalizePath method to OutputManager for proper path conversion
- Detects runtime platform (Linux/macOS/Windows) and adjusts accordingly
- Converts Windows drive letters to WSL format (/mnt/x/) when in WSL
- Preserves relative paths exactly as provided by user

## [2.31.4] - 2024-07-16

### Added
- --output-file option as alias for -o for file output
- Both -o and --output-file now work identically

## [2.31.3] - 2024-07-16

### Fixed
- Output redirection (-o and --output-dir) now working correctly
- Files are saved in the correct location specified by user

## [2.31.2] - 2024-07-16

### Added
- Brazilian currency (R$) detection with --value option
- Filters pages/documents/words/bookmarks/annotations containing Brazilian monetary values

## [2.31.1] - 2024-07-16

### Fixed
- Restored cache range functionality (1-50 syntax) while removing only --all-cache parameter
- Cache ranges now work with direct specification: layoutpdf 1-50 filter documents

## [2.31.0] - 2024-07-16

### Removed
- Removed --all-cache parameter and FilterAllCacheCommand.cs file
- Simplified cache range syntax to direct argument specification

## [2.30.0] - 2024-07-16

### Changed
- Major refactoring of cache range functionality
- Simplified command structure for cache operations