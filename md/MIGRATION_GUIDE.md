# FilterPDF v3.7.1 Migration Guide

## Architecture Refactoring

The FilterPDF application has been refactored to eliminate code duplication and improve maintainability.

### Key Changes

1. **Direct command access (filter command removed)**
   - Current syntax: `fpdf 1 pages --word test`
   - All filter operations are now direct commands

2. **Command Architecture**
   - Eliminated 23 duplicate command instantiations
   - Reduced main CLI file from 2164 lines to 300 lines
   - Introduced CommandRegistry for centralized command management
   - Separated command execution logic into CommandExecutor service

3. **New File Structure**
   ```
   src/
   ├── FilterPDFCLI_Refactored.cs (300 lines - new entry point)
   ├── Services/
   │   ├── CommandRegistry.cs (manages command registration)
   │   └── CommandExecutor.cs (handles command execution)
   └── Commands/ (individual command implementations)
   ```

### Migration Steps

1. **Update project file to use new entry point**
   ```xml
   <StartupObject>FilterPDF.FilterPDFCLI_Refactored</StartupObject>
   ```

2. **Update all scripts and documentation**
   - All filter operations are now direct commands
   - No intermediate "filter" command is needed

3. **Test all command paths**
   ```bash
   # Test cache commands
   fpdf 1 pages --word test
   fpdf 1-10 images
   fpdf 1,3,5 metadata
   
   # Test file commands  
   fpdf document.pdf load
   fpdf document.pdf extract
   
   # Test direct commands
   fpdf cache list
   fpdf ocr document.pdf
   ```

### Benefits

- **Maintainability**: Single source of truth for each command
- **Performance**: Reduced memory footprint and faster startup
- **Extensibility**: Easy to add new commands via registry
- **Testability**: Separated concerns allow better unit testing
- **Code Quality**: Eliminated God Class anti-pattern

### Removed Components

- The intermediate "filter" command has been completely removed
- All filter operations are now accessed directly as main commands

### Testing

Run the test suite to ensure all commands work correctly:

```bash
dotnet test
```

### Rollback Plan

If issues are encountered, the original FilterPDFCLI.cs is preserved and can be reverted to by updating the StartupObject in the project file.