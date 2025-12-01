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
                    Console.WriteLine("Rebuild não é necessário: cache é mantido em SQLite.");
                    break;

                case "top":
                    ShowTopSummary(args.Skip(1).ToArray());
                    break;

                case "bookmarks": // compat: delega para top --field bookmark
                    var list = args.Skip(1).ToList();
                    list.Insert(0, "--field");
                    list.Insert(1, "bookmark");
                    ShowTopSummary(list.ToArray());
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
            
            Console.WriteLine("CACHE (SQLite):");
            Console.WriteLine();
            Console.WriteLine($"DB Path: {stats.CacheDirectory}");
            Console.WriteLine($"Total caches: {stats.TotalEntries}");
            Console.WriteLine($"Bytes registrados: {stats.TotalCacheSize / (1024 * 1024):N1} MB");
            Console.WriteLine($"Last Updated: {stats.LastUpdated:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            try
            {
                var meta = CacheManager.GetMetaStats();
                Console.WriteLine("Preenchimento de metadados (caches preenchidas / total):");
                Console.WriteLine($"  meta_title         : {meta.MetaTitle}/{stats.TotalEntries}");
                Console.WriteLine($"  meta_author        : {meta.MetaAuthor}/{stats.TotalEntries}");
                Console.WriteLine($"  meta_subject       : {meta.MetaSubject}/{stats.TotalEntries}");
                Console.WriteLine($"  meta_keywords      : {meta.MetaKeywords}/{stats.TotalEntries}");
                Console.WriteLine($"  meta_creation_date : {meta.MetaCreationDate}/{stats.TotalEntries}");
                Console.WriteLine($"  stat_total_images  : {meta.StatTotalImages}/{stats.TotalEntries}");
                Console.WriteLine($"  stat_total_fonts   : {meta.StatTotalFonts}/{stats.TotalEntries}");
                Console.WriteLine($"  stat_bookmarks     : {meta.StatBookmarks}/{stats.TotalEntries}");
                Console.WriteLine($"  res_attachments    : {meta.ResAttachments}/{stats.TotalEntries}");
                Console.WriteLine($"  res_embedded_files : {meta.ResEmbeddedFiles}/{stats.TotalEntries}");
                Console.WriteLine($"  res_javascript     : {meta.ResJavascript}/{stats.TotalEntries}");
                Console.WriteLine($"  res_multimedia     : {meta.ResMultimedia}/{stats.TotalEntries}");
                Console.WriteLine($"  sec_is_encrypted   : {meta.SecIsEncrypted}/{stats.TotalEntries}");
                Console.WriteLine();
                Console.WriteLine("Totais (soma):");
                Console.WriteLine($"  total pages        : {meta.SumPages}");
                Console.WriteLine($"  total images       : {meta.SumImages}");
                Console.WriteLine($"  total bookmarks    : {meta.SumBookmarks}");
                Console.WriteLine($"  total fonts        : {meta.SumFonts}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(WARN) Não foi possível ler estatísticas de metadados: {ex.Message}");
            }
            
            if (stats.TotalEntries > 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("   fpdf <pdf_name> pages");
                Console.WriteLine("   fpdf <pdf_name> bookmarks");
            }
        }

        private void ShowTopSummary(string[] args)
        {
            int top = 5;
            int sample = 3;
            int? last = null;
            DateTime? since = null;
            string field = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--top" && i + 1 < args.Length && int.TryParse(args[++i], out var t)) top = Math.Max(1, t);
                else if (args[i] == "--sample" && i + 1 < args.Length && int.TryParse(args[++i], out var s)) sample = Math.Max(1, s);
                else if (args[i] == "--last" && i + 1 < args.Length && int.TryParse(args[++i], out var l)) last = Math.Max(1, l);
                else if (args[i] == "--since" && i + 1 < args.Length && DateTime.TryParse(args[++i], out var d)) since = d;
                else if ((args[i] == "--field" || args[i] == "-f") && i + 1 < args.Length) field = args[++i];
            }

            if (string.IsNullOrWhiteSpace(field))
            {
                Console.WriteLine("Uso: fpdf cache top --field <bookmark|meta_title|meta_author|meta_subject|meta_keywords|meta_creator|meta_producer|doc_type|mode>");
                return;
            }

            List<CacheManager.TopValueItem> summary;
            try
            {
                summary = CacheManager.GetTopValues(field, top, sample, last, since);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }
            if (summary.Count == 0)
            {
                Console.WriteLine($"Nenhum valor encontrado para o campo '{field}'.");
                return;
            }

            Console.WriteLine($"TOP {top} valores em '{field}' (caches e exemplos):");
            int rank = 1;
            foreach (var item in summary)
            {
                Console.WriteLine($"{rank}. \"{item.Value}\" - {item.Count} caches");
                if (item.Samples?.Count > 0)
                {
                    Console.WriteLine("   Exemplos: " + string.Join(", ", item.Samples));
                }
                rank++;
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
            Console.WriteLine("Rebuild não é necessário: cache está em SQLite.");
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
            Console.WriteLine("    top                   Top values (bookmarks, autores, produtor etc.)");
            Console.WriteLine("    bookmarks             Alias para: top --field bookmark");
            Console.WriteLine("    rebuild               Rebuild index from existing cache files");
            Console.WriteLine();
            Console.WriteLine("LIST OPTIONS:");
            Console.WriteLine("    -F <format>           Output format (table, json, csv)");
            Console.WriteLine("    -v, --verbose         Show detailed information");
            Console.WriteLine();
            Console.WriteLine("CLEAR OPTIONS:");
            Console.WriteLine("    -f, --force           Force clear without confirmation");
            Console.WriteLine();
            Console.WriteLine("TOP OPTIONS:");
            Console.WriteLine("    --field <f>           Campo: bookmark|meta_title|meta_author|meta_subject|meta_keywords|meta_creator|meta_producer|doc_type|mode");
            Console.WriteLine("    --top <n>             Quantos valores mostrar (default 5)");
            Console.WriteLine("    --sample <n>          Quantos exemplos por valor (default 3)");
            Console.WriteLine("    --last <n>            Considerar apenas os <n> caches mais recentes (created_at)");
            Console.WriteLine("    --since <data>        Considerar apenas caches criados após a data (yyyy-MM-dd)");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine($"    fpdf {Name} list");
            Console.WriteLine($"    fpdf {Name} list -v");
            Console.WriteLine($"    fpdf {Name} stats");
            Console.WriteLine($"    fpdf {Name} find documento");
            Console.WriteLine($"    fpdf {Name} remove documento");
            Console.WriteLine($"    fpdf {Name} clear --force");
            Console.WriteLine($"    fpdf {Name} top --field bookmark --top 10");
            Console.WriteLine();
            Console.WriteLine("WORKFLOW:");
            Console.WriteLine("    1. Load PDFs: fpdf load document.pdf");
            Console.WriteLine("    2. List cache: fpdf cache list");
            Console.WriteLine("    3. Use cache: fpdf document pages");
        }
    }
}
