using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Options;


namespace FilterPDF
{
    /// <summary>
    /// Find modifications and edits in PDF documents
    /// </summary>
    class FpdfModificationsCommand : Command
    {
        public override string Name => "modifications";
        public override string Description => "Find modifications and edits in PDF";
        
        private Dictionary<string, string> filterOptions = new Dictionary<string, string>();
        private Dictionary<string, string> outputOptions = new Dictionary<string, string>();
        private PDFAnalysisResult analysisResult = new PDFAnalysisResult();
        private string inputFilePath = "";
        private bool isUsingCache = false;
        
        // Nova assinatura para compatibilidade com FilterCommand
        public void Execute(string inputFile, PDFAnalysisResult analysis, Dictionary<string, string> filters, Dictionary<string, string> outputs)
        {
            inputFilePath = inputFile;
            analysisResult = analysis;
            filterOptions = filters;
            outputOptions = outputs;
            isUsingCache = inputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            
            ExecuteModificationsSearch();
        }
        
        public override void Execute(string[] args)
        {
            if (args.Length < 1)
            {
                ShowHelp();
                return;
            }
            
            inputFilePath = args[0];
            
            // Validate file
            if (!File.Exists(inputFilePath))
            {
                Console.WriteLine($"File '{inputFilePath}' not found");
                Environment.Exit(1);
            }
            
            // Check if cache or PDF
            bool isCache = inputFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            
            if (!isCache)
            {
                // Verify it's a valid PDF
                try
                {
                    using (var fs = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read))
                    {
                        byte[] header = new byte[4];
                        fs.Read(header, 0, 4);
                        if (header[0] != '%' || header[1] != 'P' || header[2] != 'D' || header[3] != 'F')
                        {
                            Console.WriteLine($"'{inputFilePath}' is not a valid PDF file");
                            Environment.Exit(1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error verifying file: {ex.Message}");
                    Environment.Exit(1);
                }
            }
            
            // Parse options
            ParseOptions(args);
            
            // Load analysis data
            if (isCache)
            {
                isUsingCache = true;
                LoadFromCache();
            }
            else
            {
                RunPDFAnalysis();
            }
            
            // Execute modifications search
            ExecuteModificationsSearch();
        }
        
        public override void ShowHelp()
        {
            Console.WriteLine("Usage: fpdf <pdf-file> modifications [options]");
            Console.WriteLine();
            Console.WriteLine("Modification Detection Options:");
            Console.WriteLine("  --show-context        Show surrounding context");
            Console.WriteLine("  --page <n>           Search specific page");
            Console.WriteLine("  --page-range <n-m>   Search page range");
            Console.WriteLine();
            Console.WriteLine("Filter Options:");
            Console.WriteLine("  --modified-by <pattern>  Filter by modifier name");
            Console.WriteLine("  --reason <pattern>       Filter by modification reason");
            Console.WriteLine();
            Console.WriteLine("Output Formats:");
            Console.WriteLine("  -F txt               Text output (default)");
            Console.WriteLine("  -F json              JSON output");
            Console.WriteLine("  -F xml               XML output");
            Console.WriteLine("  -F csv               CSV output");
            Console.WriteLine("  -F md                Markdown output");
            Console.WriteLine("  -F raw               Raw output");
            Console.WriteLine("  -F count             Count only");
            Console.WriteLine();
            Console.WriteLine("Note: This command detects areas that may have been modified.");
        }
        
        private void ParseOptions(string[] args)
        {
            filterOptions = new Dictionary<string, string>();
            outputOptions = new Dictionary<string, string>();
            
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                
                // Output format
                if (arg == "-F" && i + 1 < args.Length)
                {
                    outputOptions["-F"] = args[i + 1];
                    i++;
                }
                // Show context option
                else if (arg == "--show-context")
                {
                    filterOptions[arg] = "true";
                }
                // Page filters
                else if ((arg == "--page" || arg == "-p" || arg == "--page-range") && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[i + 1];
                    i++;
                }
                // Filter options with values
                else if ((arg == "--modified-by" || arg == "--reason") && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[i + 1];
                    i++;
                }
            }
        }
        
        private void LoadFromCache()
        {
            try
            {
                string jsonContent = File.ReadAllText(inputFilePath);
                analysisResult = JsonConvert.DeserializeObject<PDFAnalysisResult>(jsonContent) ?? new PDFAnalysisResult();
                
                if (analysisResult == null)
                {
                    Console.WriteLine("Error: Invalid cache file format");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading cache: {ex.Message}");
                Environment.Exit(1);
            }
        }
        
        private void RunPDFAnalysis()
        {
            try
            {
                var analyzer = new PDFAnalyzer(inputFilePath);
                analysisResult = analyzer.AnalyzeFull();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing PDF: {ex.Message}");
                Environment.Exit(1);
            }
        }
        
        private void ExecuteModificationsSearch()
        {
            Console.WriteLine($"Finding MODIFICATIONS in: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            var foundModifications = new List<ModificationMatch>();
            
            // SEMPRE exigir cache - modificações não são suportadas via análise direta
            Console.WriteLine("⚠️  AVISO: Detecção de modificações não está disponível via cache.");
            Console.WriteLine("    O cache não contém informações de baixo nível necessárias para detectar modificações.");
            Console.WriteLine();
            Console.WriteLine("    Esta funcionalidade pode ser adicionada futuramente ao processo de cache.");
            
            // Apply filters to any existing modifications
            var filteredModifications = foundModifications.Where(ModificationMatchesAllFilters).ToList();
            OutputModificationResults(filteredModifications);
            return;
        }
        
        private bool PageMatchesFilters(int pageNum)
        {
            // Check --page filter
            if (filterOptions.ContainsKey("--page") || filterOptions.ContainsKey("-p"))
            {
                var pageStr = filterOptions.ContainsKey("--page") ? filterOptions["--page"] : filterOptions["-p"];
                if (int.TryParse(pageStr, out int targetPage))
                {
                    return pageNum == targetPage;
                }
            }
            
            // Check --page-range filter
            if (filterOptions.ContainsKey("--page-range"))
            {
                var range = filterOptions["--page-range"];
                var parts = range.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                {
                    return pageNum >= start && pageNum <= end;
                }
            }
            
            return true; // No page filters, include all pages
        }
        
        private bool ModificationMatchesAllFilters(ModificationMatch modification)
        {
            // Check page filters
            if (!PageMatchesFilters(modification.Modification.PageNumber))
                return false;
                
            // Check --modified-by filter
            if (filterOptions.ContainsKey("--modified-by"))
            {
                var pattern = filterOptions["--modified-by"];
                // Note: ModifiedBy field needs to be added to ModificationArea class
                // For now, this will check the Description field as a fallback
                var modifiedBy = GetModificationModifiedBy(modification.Modification);
                if (modifiedBy != null && !WordOption.Matches(modifiedBy, pattern))
                    return false;
            }
            
            // Check --reason filter
            if (filterOptions.ContainsKey("--reason"))
            {
                var pattern = filterOptions["--reason"];
                // Note: Reason field needs to be added to ModificationArea class
                // For now, this will check the Description field as a fallback
                var reason = GetModificationReason(modification.Modification);
                if (reason != null && !WordOption.Matches(reason, pattern))
                    return false;
            }
            
            return true;
        }
        
        private string GetModificationModifiedBy(ModificationArea modification)
        {
            // TODO: When ModifiedBy field is added to ModificationArea, return modification.ModifiedBy
            // For now, return null as this field doesn't exist yet
            return null;
        }
        
        private string GetModificationReason(ModificationArea modification)
        {
            // TODO: When Reason field is added to ModificationArea, return modification.Reason
            // For now, use Description field as a fallback
            return modification.Description;
        }
        
        private void ShowActiveFilters()
        {
            if (filterOptions.Count > 0)
            {
                Console.WriteLine("Active filters:");
                foreach (var filter in filterOptions)
                {
                    if (filter.Value == "true")
                        Console.WriteLine($"  {filter.Key}");
                    else
                        Console.WriteLine($"  {filter.Key}: {filter.Value}");
                }
            }
        }
        
        private void OutputModificationResults(List<ModificationMatch> modifications)
        {
            string output = "";
            string format = "txt";
            
            if (outputOptions.ContainsKey("-F"))
            {
                format = outputOptions["-F"].ToLower();
            }
            
            switch (format)
            {
                case "json":
                    output = FormatModificationsAsJson(modifications);
                    break;
                case "xml":
                    output = FormatModificationsAsXml(modifications);
                    break;
                case "csv":
                    output = FormatModificationsAsCsv(modifications);
                    break;
                case "md":
                    output = FormatModificationsAsMarkdown(modifications);
                    break;
                case "raw":
                    output = FormatModificationsAsRaw(modifications);
                    break;
                case "count":
                    output = modifications.Count.ToString();
                    break;
                case "png":
                    Console.WriteLine("⚠️ Formato PNG não é aplicável para modificações pois retorna apenas dados de análise, não páginas.");
                    Console.WriteLine("💡 Usando formato JSON como alternativa...");
                    Console.WriteLine();
                    output = FormatModificationsAsJson(modifications);
                    break;
                case "txt":
                default:
                    output = FormatModificationsAsText(modifications);
                    break;
            }
            
            Console.WriteLine(output);
        }
        
        // Format methods
        private string FormatModificationsAsJson(List<ModificationMatch> modifications)
        {
            var result = new
            {
                totalModifications = modifications.Count,
                modificationsByPage = modifications.GroupBy(m => m.Modification.PageNumber)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        pageNumber = g.Key,
                        modifications = g.Select(m => new
                        {
                            type = m.Modification.Type,
                            description = m.Modification.Description,
                            x = m.Modification.X,
                            y = m.Modification.Y,
                            width = m.Modification.Width,
                            height = m.Modification.Height,
                            objectNumber = m.Modification.ObjectNumber,
                            generation = m.Modification.Generation
                        })
                    })
            };
            
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }
        
        private string FormatModificationsAsXml(List<ModificationMatch> modifications)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<modifications>");
            sb.AppendLine($"  <totalCount>{modifications.Count}</totalCount>");
            
            foreach (var pageGroup in modifications.GroupBy(m => m.Modification.PageNumber).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  <page number=\"{pageGroup.Key}\">");
                foreach (var mod in pageGroup)
                {
                    sb.AppendLine($"    <modification type=\"{mod.Modification.Type}\">");
                    sb.AppendLine($"      <description>{mod.Modification.Description}</description>");
                    sb.AppendLine($"      <location x=\"{mod.Modification.X}\" y=\"{mod.Modification.Y}\" width=\"{mod.Modification.Width}\" height=\"{mod.Modification.Height}\"/>");
                    sb.AppendLine($"      <object number=\"{mod.Modification.ObjectNumber}\" generation=\"{mod.Modification.Generation}\"/>");
                    sb.AppendLine($"    </modification>");
                }
                sb.AppendLine($"  </page>");
            }
            
            sb.AppendLine("</modifications>");
            return sb.ToString();
        }
        
        private string FormatModificationsAsCsv(List<ModificationMatch> modifications)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Page,Type,Description,X,Y,Width,Height,Object,Generation");
            
            foreach (var mod in modifications.OrderBy(m => m.Modification.PageNumber))
            {
                sb.AppendLine($"{mod.Modification.PageNumber}," +
                             $"\"{mod.Modification.Type}\"," +
                             $"\"{mod.Modification.Description}\"," +
                             $"{mod.Modification.X}," +
                             $"{mod.Modification.Y}," +
                             $"{mod.Modification.Width}," +
                             $"{mod.Modification.Height}," +
                             $"{mod.Modification.ObjectNumber}," +
                             $"{mod.Modification.Generation}");
            }
            
            return sb.ToString();
        }
        
        private string FormatModificationsAsMarkdown(List<ModificationMatch> modifications)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PDF Modifications Report");
            sb.AppendLine();
            sb.AppendLine($"**Total Modifications Found:** {modifications.Count}");
            sb.AppendLine();
            
            if (modifications.Count == 0)
            {
                sb.AppendLine("No modifications detected.");
                return sb.ToString();
            }
            
            foreach (var pageGroup in modifications.GroupBy(m => m.Modification.PageNumber).OrderBy(g => g.Key))
            {
                sb.AppendLine($"## Page {pageGroup.Key}");
                sb.AppendLine();
                sb.AppendLine("| Type | Description | Location | Object |");
                sb.AppendLine("|------|-------------|----------|---------|");
                
                foreach (var mod in pageGroup)
                {
                    var location = $"({mod.Modification.X:F0}, {mod.Modification.Y:F0})";
                    var obj = $"{mod.Modification.ObjectNumber} {mod.Modification.Generation} R";
                    sb.AppendLine($"| {mod.Modification.Type} | {mod.Modification.Description} | {location} | {obj} |");
                }
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private string FormatModificationsAsRaw(List<ModificationMatch> modifications)
        {
            var sb = new StringBuilder();
            foreach (var mod in modifications)
            {
                sb.AppendLine($"PAGE:{mod.Modification.PageNumber}|TYPE:{mod.Modification.Type}|DESC:{mod.Modification.Description}|X:{mod.Modification.X}|Y:{mod.Modification.Y}|OBJ:{mod.Modification.ObjectNumber}");
            }
            return sb.ToString();
        }
        
        private string FormatModificationsAsText(List<ModificationMatch> modifications)
        {
            var sb = new StringBuilder();
            
            if (modifications.Count == 0)
            {
                sb.AppendLine("No modifications detected.");
                return sb.ToString();
            }
            
            sb.AppendLine($"Found {modifications.Count} modification(s):");
            sb.AppendLine();
            
            foreach (var pageGroup in modifications.GroupBy(m => m.Modification.PageNumber).OrderBy(g => g.Key))
            {
                sb.AppendLine($"Page {pageGroup.Key}:");
                foreach (var mod in pageGroup)
                {
                    sb.AppendLine($"  - {mod.Modification.Type}: {mod.Modification.Description}");
                    sb.AppendLine($"    Location: ({mod.Modification.X:F0}, {mod.Modification.Y:F0}) Size: {mod.Modification.Width:F0}x{mod.Modification.Height:F0}");
                    sb.AppendLine($"    Object: {mod.Modification.ObjectNumber} {mod.Modification.Generation} R");
                    
                    if (filterOptions.ContainsKey("--show-context") && mod.PageInfo != null)
                    {
                        sb.AppendLine($"    Page Size: {mod.PageInfo.Size.Width:F0}x{mod.PageInfo.Size.Height:F0}");
                        sb.AppendLine($"    Word Count: {mod.PageInfo.TextInfo.WordCount}");
                    }
                }
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
    }
}
