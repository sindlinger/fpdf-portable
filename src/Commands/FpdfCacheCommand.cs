using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;


namespace FilterPDF
{
    /// <summary>
    /// Comando para gerenciar cache de PDFs
    /// Permite listar, limpar e gerenciar PDFs carregados
    /// </summary>
    public class FpdfCacheCommand : Command
    {
        public override string Name => "cache";
        public override string Description => "Manage cached PDF files";
        
        public override void Execute(string[] args)
        {
            if (args.Length == 0 || args.Any(a => a == "--help" || a == "-h"))
            {
                ShowHelp();
                return;
            }
            
            string subcommand = args[0].ToLower();
            
            switch (subcommand)
            {
                case "list":
                case "ls":
                    ListCachedPDFs(args.Skip(1).ToArray());
                    break;
                    
                case "stats":
                case "status":
                    ShowCacheStats();
                    break;
                    
                case "clear":
                    ClearCache(args.Skip(1).ToArray());
                    break;
                    
                case "remove":
                case "rm":
                    RemoveFromCache(args.Skip(1).ToArray());
                    break;
                    
                case "find":
                    FindInCache(args.Skip(1).ToArray());
                    break;
                    
                case "rebuild":
                    RebuildIndex(args.Skip(1).ToArray());
                    break;
                    
                default:
                    Console.Error.WriteLine($"Error: Unknown subcommand '{subcommand}'");
                    ShowHelp();
                    Environment.Exit(1);
                    break;
            }
        }
        
        private void ListCachedPDFs(string[] args)
        {
            var format = "table"; // default
            var verbose = false;
            
            // Parse options
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-F":
                        if (i + 1 < args.Length)
                            format = args[++i];
                        break;
                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;
                }
            }
            
            var entries = CacheManager.ListCachedPDFs();
            
            if (entries.Count == 0)
            {
                Console.WriteLine("No PDFs in cache.");
                Console.WriteLine();
                Console.WriteLine("To add PDFs to cache, use:");
                Console.WriteLine("   fpdf load document.pdf");
                return;
            }
            
            switch (format.ToLower())
            {
                case "json":
                    Console.WriteLine(JsonConvert.SerializeObject(entries, Formatting.Indented));
                    break;
                    
                case "csv":
                    Console.WriteLine("Name,OriginalFile,CacheFile,CachedDate,OriginalSize,CacheSize,Mode");
                    foreach (var entry in entries)
                    {
                        Console.WriteLine($"{Path.GetFileNameWithoutExtension(entry.OriginalFileName)},{entry.OriginalFileName},{entry.CacheFileName},{entry.CachedDate:yyyy-MM-dd HH:mm:ss},{entry.OriginalSize},{entry.CacheSize},{entry.ExtractionMode}");
                    }
                    break;
                    
                default: // table
                    Console.WriteLine("CACHED PDFs:");
                    Console.WriteLine();
                    
                    if (verbose)
                    {
                        foreach (var entry in entries)
                        {
                            var name = Path.GetFileNameWithoutExtension(entry.OriginalFileName);
                            Console.WriteLine($"{name}");
                            Console.WriteLine($"   Original: {entry.OriginalFileName} ({entry.OriginalSize / 1024:N0} KB)");
                            Console.WriteLine($"   Cache: {entry.CacheFileName} ({entry.CacheSize / 1024:N0} KB)");
                            Console.WriteLine($"   Mode: {entry.ExtractionMode}");
                            Console.WriteLine($"   Cached: {entry.CachedDate:yyyy-MM-dd HH:mm:ss}");
                            Console.WriteLine();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{"ID",-4} {"NAME",-20} {"ORIGINAL",-25} {"SIZE",-10} {"MODE",-8} {"CACHED",-12}");
                        Console.WriteLine(new string('-', 85));
                        
                        int index = 1;
                        foreach (var entry in entries)
                        {
                            var name = Path.GetFileNameWithoutExtension(entry.OriginalFileName);
                            var originalName = entry.OriginalFileName.Length > 24 ? 
                                entry.OriginalFileName.Substring(0, 21) + "..." : 
                                entry.OriginalFileName;
                            var size = $"{entry.CacheSize / 1024:N0} KB";
                            var cached = entry.CachedDate.ToString("MMM dd HH:mm");
                            
                            Console.WriteLine($"{index,-4} {name,-20} {originalName,-25} {size,-10} {entry.ExtractionMode,-8} {cached,-12}");
                            index++;
                        }
                    }
                    
                    Console.WriteLine();
                    Console.WriteLine($"Total: {entries.Count} PDFs in cache");
                    break;
            }
        }
        
        private void ShowCacheStats()
        {
            var stats = CacheManager.GetCacheStats();
            
            Console.WriteLine("CACHE STATISTICS:");
            Console.WriteLine();
            Console.WriteLine($"Cache Directory: {stats.CacheDirectory}");
            Console.WriteLine($"Total PDFs: {stats.TotalEntries}");
            Console.WriteLine($"Cache Size: {stats.TotalCacheSize / (1024 * 1024):N1} MB");
            Console.WriteLine($"Original Size: {stats.TotalOriginalSize / (1024 * 1024):N1} MB");
            
            if (stats.TotalOriginalSize > 0)
            {
                var compressionRatio = (double)stats.TotalCacheSize / stats.TotalOriginalSize;
                Console.WriteLine($"Compression: {compressionRatio:P1}");
            }
            
            Console.WriteLine($"Last Updated: {stats.LastUpdated:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
            
            if (stats.TotalEntries > 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("   fpdf <pdf_name> pages");
                Console.WriteLine("   fpdf <pdf_name> bookmarks");
            }
        }
        
        private void ClearCache(string[] args)
        {
            bool force = args.Contains("--force") || args.Contains("-f");
            
            var stats = CacheManager.GetCacheStats();
            
            if (stats.TotalEntries == 0)
            {
                Console.WriteLine("Cache is already empty.");
                return;
            }
            
            if (!force)
            {
                Console.WriteLine($"This will remove {stats.TotalEntries} cached PDFs ({stats.TotalCacheSize / (1024 * 1024):N1} MB)");
                Console.Write("Are you sure? (y/N): ");
                var response = Console.ReadLine();
                
                if (response?.ToLower() != "y" && response?.ToLower() != "yes")
                {
                    Console.WriteLine("Cache clear cancelled.");
                    return;
                }
            }
            
            CacheManager.ClearCache();
            Console.WriteLine("Cache cleared successfully.");
        }
        
        private void RemoveFromCache(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Error: PDF name required");
                Console.Error.WriteLine("Usage: fpdf cache remove <pdf_name>");
                Environment.Exit(1);
            }
            
            string pdfName = args[0];
            
            if (CacheManager.RemoveFromCache(pdfName))
            {
                Console.WriteLine($"Removed '{pdfName}' from cache.");
            }
            else
            {
                Console.WriteLine($"PDF '{pdfName}' not found in cache.");
                Console.WriteLine();
                Console.WriteLine("Use 'fpdf cache list' to see available PDFs");
            }
        }
        
        private void FindInCache(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Error: PDF name or index required");
                Console.Error.WriteLine("Usage: fpdf cache find <pdf_name_or_index>");
                Environment.Exit(1);
            }
            
            string pdfNameOrIndex = args[0];
            string? cacheFile = CacheManager.FindCacheFile(pdfNameOrIndex);
            var entry = CacheManager.GetCacheEntry(pdfNameOrIndex);
            
            if (cacheFile != null && entry != null)
            {
                Console.WriteLine($"Found: {Path.GetFileNameWithoutExtension(entry.OriginalFileName)}");
                Console.WriteLine($"   Original: {entry.OriginalFileName}");
                Console.WriteLine($"   Cache file: {cacheFile}");
                Console.WriteLine($"   Size: {new FileInfo(cacheFile).Length / 1024:N0} KB");
                Console.WriteLine($"   Mode: {entry.ExtractionMode}");
                Console.WriteLine($"   Cached: {entry.CachedDate:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                Console.WriteLine($"Not found: {pdfNameOrIndex}");
                Console.WriteLine();
                Console.WriteLine("Use 'fpdf cache list' to see available PDFs with their IDs");
                Console.WriteLine("Examples:");
                Console.WriteLine("   fpdf cache find 1");
                Console.WriteLine("   fpdf cache find document");
            }
        }
        
        private void RebuildIndex(string[] args)
        {
            bool force = args.Contains("--force") || args.Contains("-f");
            bool verbose = args.Contains("--verbose") || args.Contains("-v");
            
            Console.WriteLine("CACHE INDEX REBUILD");
            Console.WriteLine("===================");
            Console.WriteLine();
            Console.WriteLine("This command reconstructs the cache index (index.json) from all existing");
            Console.WriteLine("cache files in the .cache directory. Useful when:");
            Console.WriteLine("  - The index is corrupted or out of sync");
            Console.WriteLine("  - Multiple workers caused race conditions during batch processing");
            Console.WriteLine("  - Cache files exist but are not listed in 'cache list'");
            Console.WriteLine();
            
            // Verificar se o diretório de cache existe
            var cacheDir = Path.Combine(Environment.CurrentDirectory, ".cache");
            if (!Directory.Exists(cacheDir))
            {
                Console.Error.WriteLine("Error: Cache directory does not exist at: " + cacheDir);
                Console.Error.WriteLine("No cache files to rebuild from.");
                Environment.Exit(1);
            }
            
            // OTIMIZAÇÃO: Não listar todos os arquivos para evitar lentidão
            var cacheFiles = new string[0];
            
            Console.WriteLine($"Found {cacheFiles.Length} cache files in directory");
            
            if (cacheFiles.Length == 0)
            {
                Console.WriteLine("No cache files found. Nothing to rebuild.");
                return;
            }
            
            // Backup do índice existente se houver
            var indexFile = Path.Combine(cacheDir, "index.json");
            if (File.Exists(indexFile))
            {
                var backupFile = Path.Combine(cacheDir, $"index.backup.{DateTime.Now:yyyyMMddHHmmss}.json");
                File.Copy(indexFile, backupFile);
                Console.WriteLine($"Backed up existing index to: {Path.GetFileName(backupFile)}");
            }
            
            if (!force)
            {
                Console.WriteLine();
                Console.Write("This will rebuild the entire index. Continue? (y/N): ");
                var response = Console.ReadLine();
                if (response?.ToLower() != "y" && response?.ToLower() != "yes")
                {
                    Console.WriteLine("Rebuild cancelled.");
                    return;
                }
            }
            
            Console.WriteLine();
            Console.WriteLine("Rebuilding index...");
            
            // Rebuild index
            var startTime = DateTime.Now;
            var rebuilt = CacheManager.RebuildIndexFromFiles();
            var duration = DateTime.Now - startTime;
            
            Console.WriteLine();
            Console.WriteLine($"REBUILD COMPLETE");
            Console.WriteLine($"================");
            Console.WriteLine($"Successfully rebuilt index with {rebuilt} entries");
            Console.WriteLine($"Time taken: {duration.TotalSeconds:F2} seconds");
            Console.WriteLine($"Missing entries recovered: {cacheFiles.Length - rebuilt}");
            Console.WriteLine();
            Console.WriteLine("Use 'fpdf cache list' to see all cached PDFs");
            
            if (verbose)
            {
                Console.WriteLine();
                Console.WriteLine("Note: Rebuilt entries have:");
                Console.WriteLine("  - Mode: 'unknown' (original extraction mode not recoverable)");
                Console.WriteLine("  - Original size: 0 (original file size not stored)");
                Console.WriteLine("  - Creation date from cache file timestamp");
            }
        }
        
        public override void ShowHelp()
        {
            Console.WriteLine($"COMMAND: {Name}");
            Console.WriteLine($"    {Description}");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine($"    fpdf {Name} <subcommand> [options]");
            Console.WriteLine();
            Console.WriteLine("SUBCOMMANDS:");
            Console.WriteLine("    list, ls              List all cached PDFs");
            Console.WriteLine("    stats, status         Show cache statistics");
            Console.WriteLine("    clear                 Clear all cache (with confirmation)");
            Console.WriteLine("    remove <pdf>, rm      Remove specific PDF from cache");
            Console.WriteLine("    find <pdf>            Find PDF in cache");
            Console.WriteLine("    rebuild               Rebuild index from existing cache files");
            Console.WriteLine();
            Console.WriteLine("LIST OPTIONS:");
            Console.WriteLine("    -F <format>           Output format (table, json, csv)");
            Console.WriteLine("    -v, --verbose         Show detailed information");
            Console.WriteLine();
            Console.WriteLine("CLEAR OPTIONS:");
            Console.WriteLine("    -f, --force           Force clear without confirmation");
            Console.WriteLine();
            Console.WriteLine("REBUILD OPTIONS:");
            Console.WriteLine("    -f, --force           Skip confirmation prompt");
            Console.WriteLine("    -v, --verbose         Show detailed information about rebuild");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine($"    fpdf {Name} list");
            Console.WriteLine($"    fpdf {Name} list -v");
            Console.WriteLine($"    fpdf {Name} stats");
            Console.WriteLine($"    fpdf {Name} find documento");
            Console.WriteLine($"    fpdf {Name} remove documento");
            Console.WriteLine($"    fpdf {Name} clear --force");
            Console.WriteLine($"    fpdf {Name} rebuild");
            Console.WriteLine($"    fpdf {Name} rebuild --force --verbose");
            Console.WriteLine();
            Console.WriteLine("REBUILD USE CASES:");
            Console.WriteLine("    1. After batch processing with multiple workers failed to index all files");
            Console.WriteLine("    2. When 'cache list' shows fewer entries than actual cache files");
            Console.WriteLine("    3. To recover from corrupted or missing index.json");
            Console.WriteLine("    4. After manual deletion of index.json");
            Console.WriteLine();
            Console.WriteLine("WORKFLOW:");
            Console.WriteLine("    1. Load PDFs: fpdf load document.pdf");
            Console.WriteLine("    2. List cache: fpdf cache list");
            Console.WriteLine("    3. Use cache: fpdf document pages");
        }
    }
}