using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using FilterPDF.Strategies;
using iTextSharp.text;
using FilterPDF.Options;


namespace FilterPDF
{
    /// <summary>
    /// Analyze PDF structure including PDF/A compliance, accessibility, layers, and security
    /// </summary>
    class FpdfStructureCommand : Command
    {
        public override string Name => "structure";
        public override string Description => "Analyze PDF structure (PDF/A, accessibility, layers, security)";
        
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
            
            ExecuteStructureSearch();
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
            
            // Execute structure analysis
            ExecuteStructureSearch();
        }
        
        public override void ShowHelp()
        {
            Console.WriteLine("Usage: fpdf <pdf-file> structure [options]");
            Console.WriteLine();
            Console.WriteLine("Structure Analysis Options:");
            Console.WriteLine("  --pdfa                Show PDF/A compliance info");
            Console.WriteLine("  --accessibility       Show accessibility info");
            Console.WriteLine("  --layers              Show layer information");
            Console.WriteLine("  --color-profiles      Show color profile information");
            Console.WriteLine("  --security            Show security settings");
            Console.WriteLine();
            Console.WriteLine("Filter Options:");
            Console.WriteLine("  --tag-name <pattern>  Filter by structure tag name");
            Console.WriteLine("  --content <pattern>   Filter by structure content text");
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
            Console.WriteLine("Without options, shows all structure information.");
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
                // String options with values
                else if ((arg == "--tag-name" || arg == "--content") && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[i + 1];
                    i++;
                }
                // Boolean options
                else if (IsBooleanStructureOption(arg))
                {
                    filterOptions[arg] = "true";
                }
            }
        }
        
        private bool IsBooleanStructureOption(string option)
        {
            var booleanOpts = new[] { 
                "--pdfa", "--accessibility", "--layers", "--color-profiles", "--security"
            };
            return booleanOpts.Contains(option);
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
        
        private void ExecuteStructureSearch()
        {
            Console.WriteLine($"Analyzing STRUCTURE of: {Path.GetFileName(inputFilePath)}");
            if (filterOptions.Count == 0)
            {
                Console.WriteLine("No filters specified - showing all results");
            }
            else
            {
                ShowActiveFilters();
            }
            Console.WriteLine();
            
            var structureResult = new StructureMatch();
            bool showAllStructure = filterOptions.Count == 0;
            
            // PDF/A compliance analysis
            if (showAllStructure || filterOptions.ContainsKey("--pdfa"))
            {
                structureResult.PDFACompliance = analysisResult.PDFACompliance;
                structureResult.PDFAValidation = analysisResult.PDFAValidation;
            }
            
            // Accessibility analysis
            if (showAllStructure || filterOptions.ContainsKey("--accessibility"))
            {
                structureResult.AccessibilityInfo = analysisResult.AccessibilityInfo;
                
                // Apply structure element filtering if filters are present
                if (structureResult.AccessibilityInfo?.StructureTree != null)
                {
                    structureResult.AccessibilityInfo.StructureTree = FilterStructureElements(structureResult.AccessibilityInfo.StructureTree);
                }
            }
            
            // Layer analysis
            if (showAllStructure || filterOptions.ContainsKey("--layers"))
            {
                structureResult.Layers = analysisResult.Layers;
            }
            
            // Color profiles
            if (showAllStructure || filterOptions.ContainsKey("--color-profiles"))
            {
                structureResult.ColorProfiles = analysisResult.ColorProfiles;
            }
            
            // Security settings
            if (showAllStructure || filterOptions.ContainsKey("--security"))
            {
                structureResult.SecurityInfo = analysisResult.SecurityInfo;
            }
            
            OutputStructureResults(structureResult);
        }
        
        private void ShowActiveFilters()
        {
            if (filterOptions.Count > 0)
            {
                Console.WriteLine("Active filters:");
                foreach (var filter in filterOptions)
                {
                    Console.WriteLine($"  {filter.Key}");
                }
            }
        }
        
        private void OutputStructureResults(StructureMatch structure)
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
                    output = FormatStructureAsJson(structure);
                    break;
                case "xml":
                    output = FormatStructureAsXml(structure);
                    break;
                case "csv":
                    output = FormatStructureAsCsv(structure);
                    break;
                case "md":
                    output = FormatStructureAsMarkdown(structure);
                    break;
                case "raw":
                    output = FormatStructureAsRaw(structure);
                    break;
                case "count":
                    output = GetStructureCount(structure).ToString();
                    break;
                case "png":
                    Console.WriteLine("⚠️ Formato PNG não é aplicável para estrutura pois retorna apenas dados estruturais, não páginas.");
                    Console.WriteLine("💡 Usando formato JSON como alternativa...");
                    Console.WriteLine();
                    output = FormatStructureAsJson(structure);
                    break;
                case "txt":
                default:
                    output = FormatStructureAsText(structure);
                    break;
            }
            
            Console.WriteLine(output);
        }
        
        private int GetStructureCount(StructureMatch structure)
        {
            int count = 0;
            if (structure.PDFACompliance != null) count++;
            if (structure.AccessibilityInfo != null) count++;
            if (structure.Layers != null && structure.Layers.Count > 0) count += structure.Layers.Count;
            if (structure.ColorProfiles != null && structure.ColorProfiles.Count > 0) count += structure.ColorProfiles.Count;
            if (structure.SecurityInfo != null) count++;
            return count;
        }
        
        // Format methods
        private string FormatStructureAsJson(StructureMatch structure)
        {
            return JsonConvert.SerializeObject(structure, Formatting.Indented);
        }
        
        private string FormatStructureAsXml(StructureMatch structure)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<structure>");
            
            if (structure.PDFACompliance != null)
            {
                sb.AppendLine($"  <pdfa-compliance isPDFA=\"{structure.PDFACompliance.IsPDFA}\" level=\"{structure.PDFACompliance.ConformanceLevel}\">");
                sb.AppendLine($"    <has-embedded-fonts>{structure.PDFACompliance.HasEmbeddedFonts}</has-embedded-fonts>");
                sb.AppendLine($"    <has-transparency>{structure.PDFACompliance.HasTransparency}</has-transparency>");
                sb.AppendLine($"    <has-javascript>{structure.PDFACompliance.HasJavaScript}</has-javascript>");
                sb.AppendLine("  </pdfa-compliance>");
            }
            
            if (structure.SecurityInfo != null)
            {
                sb.AppendLine($"  <security encrypted=\"{structure.SecurityInfo.IsEncrypted}\">");
                if (structure.SecurityInfo.IsEncrypted)
                {
                    sb.AppendLine($"    <encryption-level>{structure.SecurityInfo.EncryptionLevel}</encryption-level>");
                    sb.AppendLine($"    <can-print>{structure.SecurityInfo.CanPrint}</can-print>");
                    sb.AppendLine($"    <can-copy>{structure.SecurityInfo.CanCopy}</can-copy>");
                    sb.AppendLine($"    <can-modify>{structure.SecurityInfo.CanModify}</can-modify>");
                }
                sb.AppendLine("  </security>");
            }
            
            sb.AppendLine("</structure>");
            return sb.ToString();
        }
        
        private string FormatStructureAsCsv(StructureMatch structure)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Property,Value");
            
            if (structure.PDFACompliance != null)
            {
                sb.AppendLine($"\"Is PDF/A\",\"{structure.PDFACompliance.IsPDFA}\"");
                sb.AppendLine($"\"PDF/A Level\",\"{structure.PDFACompliance.ConformanceLevel}\"");
                sb.AppendLine($"\"Has Embedded Fonts\",\"{structure.PDFACompliance.HasEmbeddedFonts}\"");
                sb.AppendLine($"\"Has Transparency\",\"{structure.PDFACompliance.HasTransparency}\"");
                sb.AppendLine($"\"Has JavaScript\",\"{structure.PDFACompliance.HasJavaScript}\"");
            }
            
            if (structure.SecurityInfo != null)
            {
                sb.AppendLine($"\"Is Encrypted\",\"{structure.SecurityInfo.IsEncrypted}\"");
                if (structure.SecurityInfo.IsEncrypted)
                {
                    sb.AppendLine($"\"Encryption Level\",\"{structure.SecurityInfo.EncryptionLevel}\"");
                    sb.AppendLine($"\"Can Print\",\"{structure.SecurityInfo.CanPrint}\"");
                    sb.AppendLine($"\"Can Copy\",\"{structure.SecurityInfo.CanCopy}\"");
                }
            }
            
            return sb.ToString();
        }
        
        private string FormatStructureAsMarkdown(StructureMatch structure)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PDF Structure Analysis");
            sb.AppendLine();
            
            if (structure.PDFACompliance != null)
            {
                sb.AppendLine("## PDF/A Compliance");
                sb.AppendLine($"- **Is PDF/A:** {(structure.PDFACompliance.IsPDFA ? "Yes" : "No")}");
                if (structure.PDFACompliance.IsPDFA)
                {
                    sb.AppendLine($"- **Conformance Level:** {structure.PDFACompliance.ConformanceLevel}");
                }
                sb.AppendLine($"- **Embedded Fonts:** {(structure.PDFACompliance.HasEmbeddedFonts ? "Yes" : "No")}");
                sb.AppendLine($"- **Transparency:** {(structure.PDFACompliance.HasTransparency ? "Yes" : "No")}");
                sb.AppendLine($"- **JavaScript:** {(structure.PDFACompliance.HasJavaScript ? "Yes" : "No")}");
                sb.AppendLine();
            }
            
            if (structure.ColorProfiles != null && structure.ColorProfiles.Count > 0)
            {
                sb.AppendLine("## Color Profiles");
                foreach (var profile in structure.ColorProfiles)
                {
                    sb.AppendLine($"- **{profile.Name}:** {profile.Type} ({profile.Components} components)");
                }
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private string FormatStructureAsRaw(StructureMatch structure)
        {
            var sb = new StringBuilder();
            
            if (structure.PDFACompliance != null)
            {
                sb.AppendLine($"PDFA:{structure.PDFACompliance.IsPDFA}");
                sb.AppendLine($"PDFA_LEVEL:{structure.PDFACompliance.ConformanceLevel}");
                sb.AppendLine($"EMBEDDED_FONTS:{structure.PDFACompliance.HasEmbeddedFonts}");
                sb.AppendLine($"TRANSPARENCY:{structure.PDFACompliance.HasTransparency}");
                sb.AppendLine($"JAVASCRIPT:{structure.PDFACompliance.HasJavaScript}");
            }
            
            if (structure.SecurityInfo != null)
            {
                sb.AppendLine($"ENCRYPTED:{structure.SecurityInfo.IsEncrypted}");
                sb.AppendLine($"ENCRYPTION_LEVEL:{structure.SecurityInfo.EncryptionLevel}");
            }
            
            return sb.ToString();
        }
        
        private string FormatStructureAsText(StructureMatch structure)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PDF Structure Analysis:");
            sb.AppendLine();
            
            if (structure.PDFACompliance != null)
            {
                sb.AppendLine("PDF/A Compliance:");
                sb.AppendLine($"  Is PDF/A: {(structure.PDFACompliance.IsPDFA ? "Yes" : "No")}");
                if (structure.PDFACompliance.IsPDFA)
                {
                    sb.AppendLine($"  Conformance Level: {structure.PDFACompliance.ConformanceLevel}");
                }
                sb.AppendLine($"  All Fonts Embedded: {(structure.PDFACompliance.HasEmbeddedFonts ? "Yes" : "No")}");
                sb.AppendLine($"  Has Transparency: {(structure.PDFACompliance.HasTransparency ? "Yes" : "No")}");
                sb.AppendLine($"  Has JavaScript: {(structure.PDFACompliance.HasJavaScript ? "Yes" : "No")}");
                sb.AppendLine($"  Has Encryption: {(structure.PDFACompliance.HasEncryption ? "Yes" : "No")}");
                sb.AppendLine();
            }
            
            if (structure.PDFAValidation != null)
            {
                sb.AppendLine("PDF/A Validation:");
                sb.AppendLine($"  XMP Metadata: {(structure.PDFAValidation.HasXMPMetadata ? "Present" : "Missing")}");
                sb.AppendLine($"  All Fonts Embedded: {(structure.PDFAValidation.AllFontsEmbedded ? "Yes" : "No")}");
                sb.AppendLine($"  Non-Embedded Fonts: {(structure.PDFAValidation.AllFontsEmbedded ? "None" : "Some fonts not embedded")}" );
                sb.AppendLine();
            }
            
            if (structure.AccessibilityInfo != null)
            {
                sb.AppendLine("Accessibility:");
                sb.AppendLine($"  Has Tagged Structure: {(structure.AccessibilityInfo.IsTaggedPDF ? "Yes" : "No")}");
                sb.AppendLine($"  Has Structure Tags: {(structure.AccessibilityInfo.HasStructureTags ? "Yes" : "No")}" );
                sb.AppendLine($"  Has Alternative Text: {(structure.AccessibilityInfo.HasAlternativeText ? "Yes" : "No")}");
                sb.AppendLine($"  Has Reading Order: {(structure.AccessibilityInfo.HasReadingOrder ? "Yes" : "No")}");
                sb.AppendLine();
            }
            
            if (structure.Layers != null && structure.Layers.Count > 0)
            {
                sb.AppendLine($"Document Layers ({structure.Layers.Count}):");
                foreach (var layer in structure.Layers)
                {
                    sb.AppendLine($"  - {layer.Name}");
                    sb.AppendLine($"    Visible: {(layer.IsVisible ? "Yes" : "No")}");
                    sb.AppendLine($"    Can Toggle: {(layer.CanToggle ? "Yes" : "No")}");
                    if (!string.IsNullOrEmpty(layer.Intent))
                    {
                        sb.AppendLine($"    Intent: {layer.Intent}");
                    }
                }
                sb.AppendLine();
            }
            
            if (structure.ColorProfiles != null && structure.ColorProfiles.Count > 0)
            {
                sb.AppendLine($"Color Profiles ({structure.ColorProfiles.Count}):");
                foreach (var profile in structure.ColorProfiles)
                {
                    sb.AppendLine($"  - {profile.Name}");
                    sb.AppendLine($"    Type: {profile.Type}");
                    sb.AppendLine($"    Components: {profile.Components}");
                    if (!string.IsNullOrEmpty(profile.Description))
                    {
                        sb.AppendLine($"    Description: {profile.Description}");
                    }
                }
                sb.AppendLine();
            }
            
            if (structure.SecurityInfo != null)
            {
                sb.AppendLine("Security Settings:");
                sb.AppendLine($"  Is Encrypted: {(structure.SecurityInfo.IsEncrypted ? "Yes" : "No")}");
                if (structure.SecurityInfo.IsEncrypted)
                {
                    sb.AppendLine($"  Encryption Level: {structure.SecurityInfo.EncryptionLevel}");
                    sb.AppendLine($"  Can Print: {(structure.SecurityInfo.CanPrint ? "Yes" : "No")}");
                    sb.AppendLine($"  Can Copy: {(structure.SecurityInfo.CanCopy ? "Yes" : "No")}");
                    sb.AppendLine($"  Can Modify: {(structure.SecurityInfo.CanModify ? "Yes" : "No")}");
                    sb.AppendLine($"  Can Assemble: {(structure.SecurityInfo.CanAssemble ? "Yes" : "No")}" );
                    sb.AppendLine($"  Can Fill Forms: {(structure.SecurityInfo.CanFillForms ? "Yes" : "No")}");
                    sb.AppendLine($"  Can Extract Content: {(structure.SecurityInfo.CanExtractContent ? "Yes" : "No")}");
                }
            }
            
            return sb.ToString();
        }
        
        private List<StructureElement> FilterStructureElements(List<StructureElement> elements)
        {
            var filteredElements = new List<StructureElement>();
            
            foreach (var element in elements)
            {
                if (StructureElementMatchesFilters(element))
                {
                    var filteredElement = new StructureElement
                    {
                        Type = element.Type,
                        Title = element.Title,
                        AlternativeText = element.AlternativeText,
                        ActualText = element.ActualText,
                        Level = element.Level,
                        Children = FilterStructureElements(element.Children)
                    };
                    filteredElements.Add(filteredElement);
                }
            }
            
            return filteredElements;
        }
        
        private bool StructureElementMatchesFilters(StructureElement element)
        {
            // Check --tag-name filter
            if (filterOptions.ContainsKey("--tag-name"))
            {
                var pattern = filterOptions["--tag-name"];
                if (element.Type != null && !WordOption.Matches(element.Type, pattern))
                    return false;
            }
            
            // Check --content filter
            if (filterOptions.ContainsKey("--content"))
            {
                var pattern = filterOptions["--content"];
                var content = GetElementContent(element);
                if (content != null && !WordOption.Matches(content, pattern))
                    return false;
            }
            
            return true;
        }
        
        private string GetElementContent(StructureElement element)
        {
            var contentParts = new List<string>();
            
            if (!string.IsNullOrEmpty(element.Title))
                contentParts.Add(element.Title);
            if (!string.IsNullOrEmpty(element.AlternativeText))
                contentParts.Add(element.AlternativeText);
            if (!string.IsNullOrEmpty(element.ActualText))
                contentParts.Add(element.ActualText);
                
            return contentParts.Count > 0 ? string.Join(" ", contentParts) : null;
        }
    }
}