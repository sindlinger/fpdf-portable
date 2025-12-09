using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using iTextSharp.text.pdf;
using FilterPDF.Options;
using FilterPDF.Commands;


namespace FilterPDF
{
    /// <summary>
    /// Filter Fonts Command - Find fonts that match specific criteria
    /// </summary>
    public class FpdfFontsCommand
    {
        private Dictionary<string, string> filterOptions = new Dictionary<string, string>();
        private Dictionary<string, string> outputOptions = new Dictionary<string, string>();
        private PDFAnalysisResult analysisResult = new PDFAnalysisResult();
        private string inputFilePath = "";
        private bool isUsingCache = false;
        
        // Classes internas para resultados
        public class FontMatch
        {
            public FontDetails FontDetails { get; set; } = new FontDetails();
            public List<string> MatchReasons { get; set; } = new List<string>();
        }
        
        public class FontDetails
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public bool IsEmbedded { get; set; }
            public List<float> SizesUsed { get; set; } = new List<float>();
            public int UsageCount { get; set; }
            public List<int> PagesUsed { get; set; } = new List<int>();
        }
        
        public void Execute(string inputFile, PDFAnalysisResult analysis, Dictionary<string, string> filters, Dictionary<string, string> outputs)
        {
            inputFilePath = inputFile;
            analysisResult = analysis;
            filterOptions = filters;
            outputOptions = outputs;
            isUsingCache = inputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            
            ExecuteFontsSearch();
        }
        
        private void ExecuteFontsSearch()
        {
            Console.WriteLine($"Finding FONTS in: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            var foundFonts = new List<FontMatch>();
            
            if (isUsingCache)
            {
                // Usar fontes do cache
                var allFonts = ExtractFontsFromCache();
                
                // Filtrar fontes baseado nas opcoes
                foreach (var fontPair in allFonts)
                {
                    if (FontMatchesAllFilters(fontPair.Value))
                    {
                        foundFonts.Add(new FontMatch
                        {
                            FontDetails = fontPair.Value,
                            MatchReasons = GetFontMatchReasons(fontPair.Value)
                        });
                    }
                }
            }
            else
            {
                // Não deveria chegar aqui - FilterCommand agora força uso de cache
                Console.WriteLine("ERRO: Comando fonts requer arquivo em cache!");
                return;
            }
            
            OutputFontResults(foundFonts);
        }
        
        private Dictionary<string, FontDetails> ExtractFontsFromCache()
        {
            var allFonts = new Dictionary<string, FontDetails>();
            
            foreach (var page in analysisResult.Pages)
            {
                if (page.FontInfo != null)
                {
                    foreach (var font in page.FontInfo)
                    {
                        if (string.IsNullOrEmpty(font.BaseFont))
                            continue;
                            
                        if (!allFonts.ContainsKey(font.BaseFont))
                        {
                            allFonts[font.BaseFont] = new FontDetails
                            {
                                Name = font.BaseFont,
                                Type = font.FontType,
                                IsEmbedded = font.IsEmbedded,
                                SizesUsed = new List<float>(),
                                UsageCount = 0,
                                PagesUsed = new List<int>()
                            };
                        }
                        
                        var fontDetail = allFonts[font.BaseFont];
                        fontDetail.UsageCount++;
                        if (!fontDetail.PagesUsed.Contains(page.PageNumber))
                            fontDetail.PagesUsed.Add(page.PageNumber);
                            
                        // Adicionar tamanhos unicos
                        if (font.FontSizes != null)
                        {
                            foreach (var size in font.FontSizes)
                            {
                                if (!fontDetail.SizesUsed.Contains(size))
                                    fontDetail.SizesUsed.Add(size);
                            }
                        }
                    }
                }
            }
            
            return allFonts;
        }
        
        private bool FontMatchesAllFilters(FontDetails font)
        {
            // --embedded-only
            if (filterOptions.ContainsKey("--embedded-only") && !font.IsEmbedded)
                return false;
                
            // --missing-only
            if (filterOptions.ContainsKey("--missing-only") && font.IsEmbedded)
                return false;
                
            // --name ou --font-name
            if (filterOptions.ContainsKey("--name") || filterOptions.ContainsKey("--font-name"))
            {
                var pattern = filterOptions.ContainsKey("--name") ? filterOptions["--name"] : filterOptions["--font-name"];
                if (!WordOption.Matches(font.Name, pattern))
                    return false;
            }
            
            // --type
            if (filterOptions.ContainsKey("--type"))
            {
                var type = filterOptions["--type"];
                if (!font.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            
            // --font-size
            if (filterOptions.ContainsKey("--font-size") && float.TryParse(filterOptions["--font-size"], out float targetSize))
            {
                if (font.SizesUsed == null || !font.SizesUsed.Any(s => Math.Abs(s - targetSize) < 0.1f))
                    return false;
            }
            
            // --page ou -p
            if (filterOptions.ContainsKey("--page") || filterOptions.ContainsKey("-p"))
            {
                var pageStr = filterOptions.ContainsKey("--page") ? filterOptions["--page"] : filterOptions["-p"];
                if (int.TryParse(pageStr, out int targetPage))
                {
                    if (!font.PagesUsed.Contains(targetPage))
                        return false;
                }
            }
            
            // --page-range
            if (filterOptions.ContainsKey("--page-range"))
            {
                var range = filterOptions["--page-range"];
                var parts = range.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                {
                    if (!font.PagesUsed.Any(p => p >= start && p <= end))
                        return false;
                }
            }
            
            // --usage-count
            if (filterOptions.ContainsKey("--usage-count") && int.TryParse(filterOptions["--usage-count"], out int minUsage))
            {
                if (font.UsageCount < minUsage)
                    return false;
            }
            
            // --signature
            if (filterOptions.ContainsKey("--signature") || filterOptions.ContainsKey("-s"))
            {
                var signaturePattern = filterOptions.ContainsKey("--signature") ? filterOptions["--signature"] : filterOptions["-s"];
                if (!FontUsedOnPageWithSignature(font, signaturePattern))
                    return false;
            }
                
            return true;
        }
        
        private List<string> GetFontMatchReasons(FontDetails font)
        {
            var reasons = new List<string>();
            
            if (filterOptions.ContainsKey("--embedded-only") && font.IsEmbedded)
                reasons.Add("Font is embedded");
                
            if (filterOptions.ContainsKey("--missing-only") && !font.IsEmbedded)
                reasons.Add("Font is not embedded");
                
            if (filterOptions.ContainsKey("--name") || filterOptions.ContainsKey("--font-name"))
            {
                var pattern = filterOptions.ContainsKey("--name") ? filterOptions["--name"] : filterOptions["--font-name"];
                reasons.Add($"Font name matches '{pattern}'");
            }
                
            if (filterOptions.ContainsKey("--type"))
                reasons.Add($"Font type is {font.Type}");
                
            if (filterOptions.ContainsKey("--font-size"))
                reasons.Add($"Font has size {filterOptions["--font-size"]}");
                
            if (filterOptions.ContainsKey("--page") || filterOptions.ContainsKey("-p"))
            {
                var page = filterOptions.ContainsKey("--page") ? filterOptions["--page"] : filterOptions["-p"];
                reasons.Add($"Font used on page {page}");
            }
                
            if (filterOptions.ContainsKey("--page-range"))
                reasons.Add($"Font used in page range {filterOptions["--page-range"]}");
                
            if (filterOptions.ContainsKey("--usage-count"))
                reasons.Add($"Font usage count >= {filterOptions["--usage-count"]}");
                
            if (filterOptions.ContainsKey("--signature") || filterOptions.ContainsKey("-s"))
            {
                var signaturePattern = filterOptions.ContainsKey("--signature") ? filterOptions["--signature"] : filterOptions["-s"];
                if (signaturePattern.Contains("&"))
                {
                    string[] terms = signaturePattern.Split('&');
                    reasons.Add($"Font used on page with all signatures: {string.Join(" AND ", terms.Select(t => $"'{t.Trim()}'"))}");
                }
                else if (signaturePattern.Contains("|"))
                {
                    string[] terms = signaturePattern.Split('|');
                    reasons.Add($"Font used on page with any signature: {string.Join(" OR ", terms.Select(t => $"'{t.Trim()}'"))}");
                }
                else
                {
                    reasons.Add($"Font used on page with signature: '{signaturePattern}'");
                }
            }
                
            return reasons;
        }
        
        private void ShowActiveFilters()
        {
            if (filterOptions.Count == 0)
            {
                Console.WriteLine("No filters specified - showing all results");
            }
            else
            {
                Console.WriteLine("Active filters:");
                foreach (var filter in filterOptions)
                {
                    Console.WriteLine($"   {GetFilterDescription(filter.Key, filter.Value)}");
                }
            }
        }
        
        private string GetFilterDescription(string key, string value)
        {
            switch (key)
            {
                case "--embedded-only":
                    return "Show only embedded fonts";
                case "--missing-only":
                    return "Show only missing (not embedded) fonts";
                case "--name":
                case "--font-name":
                    return $"Font name matches: \"{value}\"";
                case "--type":
                    return $"Font type: {value}";
                case "--font-size":
                    return $"Font size: {value}";
                case "--page":
                case "-p":
                    return $"Used on page: {value}";
                case "--page-range":
                    return $"Used in page range: {value}";
                case "--usage-count":
                    return $"Minimum usage count: {value}";
                default:
                    return $"{key}: {value}";
            }
        }
        
        private void OutputFontResults(List<FontMatch> fonts)
        {
            if (outputOptions.ContainsKey("--count"))
            {
                Console.WriteLine(fonts.Count);
                return;
            }
            
            string output = "";
            string format = "txt";
            
            if (outputOptions.ContainsKey("-F"))
            {
                format = outputOptions["-F"].ToLower();
            }
            
            switch (format)
            {
                case "json":
                    output = FormatFontsAsJson(fonts);
                    break;
                case "xml":
                    output = FormatFontsAsXml(fonts);
                    break;
                case "csv":
                    output = FormatFontsAsCsv(fonts);
                    break;
                case "md":
                    output = FormatFontsAsMarkdown(fonts);
                    break;
                case "raw":
                    output = FormatFontsAsRaw(fonts);
                    break;
                case "count":
                    output = fonts.Count.ToString();
                    break;
                case "png":
                    OutputFontsAsPng(fonts);
                    return; // Return early since PNG handling is done
                case "txt":
                default:
                    output = FormatFontsAsText(fonts);
                    break;
            }
            
            Console.WriteLine(output);
        }
        
        private string FormatFontsAsText(List<FontMatch> fonts)
        {
            var sb = new StringBuilder();
            
            if (fonts.Count == 0)
            {
                sb.AppendLine("No fonts found matching the specified criteria.");
                return sb.ToString();
            }
            
            sb.AppendLine($"Found {fonts.Count} font(s) matching criteria:");
            sb.AppendLine();
            
            foreach (var match in fonts)
            {
                var font = match.FontDetails;
                sb.AppendLine($"FONT: {font.Name}");
                sb.AppendLine($"  Type: {font.Type}");
                sb.AppendLine($"  Embedded: {(font.IsEmbedded ? "Yes" : "No")}");
                sb.AppendLine($"  Usage Count: {font.UsageCount}");
                
                if (font.PagesUsed.Count > 0)
                {
                    sb.AppendLine($"  Pages Used: {string.Join(", ", font.PagesUsed.OrderBy(p => p))}");
                }
                
                if (font.SizesUsed.Count > 0)
                {
                    sb.AppendLine($"  Sizes Used: {string.Join(", ", font.SizesUsed.OrderBy(s => s).Select(s => s.ToString("F1")))}");
                }
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine("  Match Reasons:");
                    foreach (var reason in match.MatchReasons)
                    {
                        sb.AppendLine($"    - {reason}");
                    }
                }
                
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private string FormatFontsAsJson(List<FontMatch> fonts)
        {
            var result = new
            {
                totalFonts = fonts.Count,
                fonts = fonts.Select(f => new
                {
                    name = f.FontDetails.Name,
                    type = f.FontDetails.Type,
                    embedded = f.FontDetails.IsEmbedded,
                    usageCount = f.FontDetails.UsageCount,
                    pagesUsed = f.FontDetails.PagesUsed.OrderBy(p => p).ToArray(),
                    sizesUsed = f.FontDetails.SizesUsed.OrderBy(s => s).ToArray(),
                    matchReasons = f.MatchReasons
                }).ToArray()
            };
            
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }
        
        private string FormatFontsAsXml(List<FontMatch> fonts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<FontResults>");
            sb.AppendLine($"  <TotalFonts>{fonts.Count}</TotalFonts>");
            sb.AppendLine("  <Fonts>");
            
            foreach (var match in fonts)
            {
                var font = match.FontDetails;
                sb.AppendLine($"    <Font name=\"{System.Security.SecurityElement.Escape(font.Name)}\">");
                sb.AppendLine($"      <Type>{System.Security.SecurityElement.Escape(font.Type)}</Type>");
                sb.AppendLine($"      <Embedded>{font.IsEmbedded}</Embedded>");
                sb.AppendLine($"      <UsageCount>{font.UsageCount}</UsageCount>");
                
                if (font.PagesUsed.Count > 0)
                {
                    sb.AppendLine("      <PagesUsed>");
                    foreach (var page in font.PagesUsed.OrderBy(p => p))
                    {
                        sb.AppendLine($"        <Page>{page}</Page>");
                    }
                    sb.AppendLine("      </PagesUsed>");
                }
                
                if (font.SizesUsed.Count > 0)
                {
                    sb.AppendLine("      <SizesUsed>");
                    foreach (var size in font.SizesUsed.OrderBy(s => s))
                    {
                        sb.AppendLine($"        <Size>{size:F1}</Size>");
                    }
                    sb.AppendLine("      </SizesUsed>");
                }
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine("      <MatchReasons>");
                    foreach (var reason in match.MatchReasons)
                    {
                        sb.AppendLine($"        <Reason>{System.Security.SecurityElement.Escape(reason)}</Reason>");
                    }
                    sb.AppendLine("      </MatchReasons>");
                }
                
                sb.AppendLine("    </Font>");
            }
            
            sb.AppendLine("  </Fonts>");
            sb.AppendLine("</FontResults>");
            
            return sb.ToString();
        }
        
        private string FormatFontsAsCsv(List<FontMatch> fonts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FontName,Type,Embedded,UsageCount,PagesUsed,SizesUsed,MatchReasons");
            
            foreach (var match in fonts)
            {
                var font = match.FontDetails;
                var name = font.Name.Replace("\"", "\"\"");
                var type = font.Type.Replace("\"", "\"\"");
                var pages = string.Join(";", font.PagesUsed.OrderBy(p => p));
                var sizes = string.Join(";", font.SizesUsed.OrderBy(s => s).Select(s => s.ToString("F1")));
                var reasons = string.Join(";", match.MatchReasons).Replace("\"", "\"\"");
                
                sb.AppendLine($"\"{name}\",\"{type}\",{font.IsEmbedded},{font.UsageCount},\"{pages}\",\"{sizes}\",\"{reasons}\"");
            }
            
            return sb.ToString();
        }
        
        private string FormatFontsAsMarkdown(List<FontMatch> fonts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Font Search Results");
            sb.AppendLine();
            sb.AppendLine($"**Total Fonts Found:** {fonts.Count}");
            sb.AppendLine();
            
            if (fonts.Count == 0)
            {
                sb.AppendLine("No fonts found matching the specified criteria.");
                return sb.ToString();
            }
            
            // Summary table
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Font Name | Type | Embedded | Usage Count | Pages |");
            sb.AppendLine("|-----------|------|----------|-------------|-------|");
            
            foreach (var match in fonts)
            {
                var font = match.FontDetails;
                var pageCount = font.PagesUsed.Count;
                sb.AppendLine($"| {font.Name} | {font.Type} | {(font.IsEmbedded ? "Yes" : "No")} | {font.UsageCount} | {pageCount} |");
            }
            sb.AppendLine();
            
            // Details
            sb.AppendLine("## Font Details");
            sb.AppendLine();
            
            foreach (var match in fonts)
            {
                var font = match.FontDetails;
                sb.AppendLine($"### {font.Name}");
                sb.AppendLine();
                sb.AppendLine($"- **Type:** {font.Type}");
                sb.AppendLine($"- **Embedded:** {(font.IsEmbedded ? "Yes" : "No")}");
                sb.AppendLine($"- **Usage Count:** {font.UsageCount}");
                
                if (font.PagesUsed.Count > 0)
                {
                    sb.AppendLine($"- **Pages Used:** {string.Join(", ", font.PagesUsed.OrderBy(p => p))}");
                }
                
                if (font.SizesUsed.Count > 0)
                {
                    sb.AppendLine($"- **Sizes Used:** {string.Join(", ", font.SizesUsed.OrderBy(s => s).Select(s => s.ToString("F1")))}pt");
                }
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine("- **Match Reasons:**");
                    foreach (var reason in match.MatchReasons)
                    {
                        sb.AppendLine($"  - {reason}");
                    }
                }
                
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private void OutputFontsAsPng(List<FontMatch> fonts)
        {
            Console.WriteLine($"🖼️  Iniciando extração PNG para {fonts.Count} fonte(s) encontrada(s)...");
            Console.WriteLine();
            
            try 
            {
                // Converter FontMatch para PageMatch para usar o OptimizedPngExtractor
                var pageMatches = ConvertFontsToPageMatches(fonts);
                
                if (pageMatches.Count == 0)
                {
                    Console.WriteLine("⚠️  Nenhuma fonte encontrada possui páginas válidas para extração PNG");
                    return;
                }
                
                // Usar o OptimizedPngExtractor existente
                OptimizedPngExtractor.ExtractPagesAsPng(
                    pageMatches, 
                    outputOptions, 
                    analysisResult?.FilePath,  // PDF path from analysis
                    inputFilePath,             // Input file path
                    isUsingCache               // Whether using cache
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro durante extração PNG: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Detalhes: {ex.InnerException.Message}");
                }
            }
        }
        
        private List<PageMatch> ConvertFontsToPageMatches(List<FontMatch> fonts)
        {
            var pageMatches = new List<PageMatch>();
            var processedPages = new HashSet<int>();
            
            foreach (var font in fonts)
            {
                // Fontes têm lista de páginas onde são usadas
                if (font.FontDetails.PagesUsed != null && font.FontDetails.PagesUsed.Count > 0)
                {
                    foreach (var pageNum in font.FontDetails.PagesUsed)
                    {
                        if (!processedPages.Contains(pageNum))
                        {
                            // Criar PageMatch correspondente
                            var pageMatch = new PageMatch
                            {
                                PageNumber = pageNum,
                                MatchReasons = new List<string> 
                                {
                                    $"Usa fonte '{font.FontDetails.Name}' ({font.FontDetails.Type})"
                                }
                            };
                            
                            // Tentar preencher PageInfo se disponível no analysis result
                            if (analysisResult?.Pages != null)
                            {
                                var pageInfo = analysisResult.Pages.FirstOrDefault(p => p.PageNumber == pageNum);
                                if (pageInfo != null)
                                {
                                    pageMatch.PageInfo = pageInfo;
                                }
                            }
                            
                            pageMatches.Add(pageMatch);
                            processedPages.Add(pageNum);
                        }
                        else
                        {
                            // Adicionar informação da fonte à página já existente
                            var existingMatch = pageMatches.FirstOrDefault(p => p.PageNumber == pageNum);
                            if (existingMatch != null)
                            {
                                existingMatch.MatchReasons.Add($"Usa fonte '{font.FontDetails.Name}' ({font.FontDetails.Type})");
                            }
                        }
                    }
                }
            }
            
            return pageMatches.OrderBy(p => p.PageNumber).ToList();
        }
        
        private string FormatFontsAsRaw(List<FontMatch> fonts)
        {
            var sb = new StringBuilder();
            foreach (var match in fonts)
            {
                sb.AppendLine(match.FontDetails.Name);
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Check if font is used on a page with signature patterns
        /// </summary>
        private bool FontUsedOnPageWithSignature(FontDetails font, string signaturePattern)
        {
            if (font.PagesUsed == null || font.PagesUsed.Count == 0)
                return false;
            
            // Check each page where this font is used
            foreach (int pageNumber in font.PagesUsed)
            {
                // Find the corresponding page in the analysis result
                var page = analysisResult.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
                if (page?.TextInfo?.PageText != null && PageContainsSignature(page.TextInfo.PageText, signaturePattern))
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if page contains signature patterns
        /// Searches in the last 30% of page text where signatures usually appear
        /// Supports AND (&) and OR (|) operators like WordOption
        /// </summary>
        private bool PageContainsSignature(string pageText, string signaturePattern)
        {
            if (string.IsNullOrEmpty(pageText) || string.IsNullOrEmpty(signaturePattern))
                return false;
            
            // Get last 30% of page text where signatures usually appear
            int startIndex = (int)(pageText.Length * 0.7);
            string signatureArea = pageText.Substring(startIndex);
            
            // Handle OR operator (|)
            if (signaturePattern.Contains("|"))
            {
                string[] orTerms = signaturePattern.Split('|');
                foreach (string term in orTerms)
                {
                    string trimmedTerm = term.Trim();
                    if (string.IsNullOrEmpty(trimmedTerm))
                        continue;
                        
                    if (ContainsSignatureTerm(pageText, signatureArea, trimmedTerm))
                        return true;
                }
                return false;
            }
            // Handle AND operator (&)
            else if (signaturePattern.Contains("&"))
            {
                string[] andTerms = signaturePattern.Split('&');
                foreach (string term in andTerms)
                {
                    string trimmedTerm = term.Trim();
                    if (string.IsNullOrEmpty(trimmedTerm))
                        continue;
                        
                    if (!ContainsSignatureTerm(pageText, signatureArea, trimmedTerm))
                        return false;
                }
                return true;
            }
            // Single term
            else
            {
                return ContainsSignatureTerm(pageText, signatureArea, signaturePattern);
            }
        }
        
        /// <summary>
        /// Check if signature area contains a specific signature term
        /// Searches for names, signature patterns, and common signature indicators
        /// </summary>
        private bool ContainsSignatureTerm(string fullPageText, string signatureArea, string term)
        {
            term = term.Trim();
            if (string.IsNullOrEmpty(term))
                return false;
            
            // Common signature patterns to search for
            var signaturePatterns = new List<string>
            {
                term, // The exact term provided
                $"Assinado por {term}",
                $"Signed by {term}",
                $"{term} (assinado)",
                $"{term} (signed)",
                $"{term} (digitally signed)"
            };
            
            // Also check for common signature line patterns
            var commonSignaturePatterns = new List<string>
            {
                "____", // Underscores for signature lines
                "Assinado por",
                "Signed by",
                "Assinatura",
                "Signature",
                "Digital signature",
                "Digital certificate"
            };
            
            // Search in signature area first (last 30% of page)
            foreach (var pattern in signaturePatterns)
            {
                if (signatureArea.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            
            // If looking for a specific name, also search the full page for that name
            // but only if we also find signature indicators
            if (!string.IsNullOrEmpty(term) && term.Length > 2)
            {
                bool hasNameInFullPage = fullPageText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasNameInFullPage)
                {
                    // Check if page also contains signature indicators
                    foreach (var indicator in commonSignaturePatterns)
                    {
                        if (fullPageText.IndexOf(indicator, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            
            // If looking for common signature patterns, search full page
            if (commonSignaturePatterns.Any(p => term.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return fullPageText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            
            return false;
        }
    }
}