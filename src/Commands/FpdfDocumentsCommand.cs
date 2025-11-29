using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Options;
using FilterPDF.Utils;
using FilterPDF.Services;
using FilterPDF.Commands;


namespace FilterPDF
{
    /// <summary>
    /// Filter Documents Command - Identifica e separa documentos em PDFs multi-documento
    /// </summary>
    public class FpdfDocumentsCommand
    {
        public static void ShowHelp()
        {
            Console.WriteLine(LanguageManager.GetDocumentsHelp());
        }
        
        public void Execute(string inputFile, PDFAnalysisResult analysis, 
                          Dictionary<string, string> filterOptions, 
                          Dictionary<string, string> outputOptions)
        {
            try
            {
                ExecuteSingleFile(inputFile, analysis, filterOptions, outputOptions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR in FpdfDocumentsCommand: {ex.Message}");
                Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }
        
        private void ExecuteSingleFile(string inputFile, PDFAnalysisResult analysis, 
                                      Dictionary<string, string> filterOptions, 
                                      Dictionary<string, string> outputOptions)
        {
            Console.Error.WriteLine($"[DEBUG] ExecuteSingleFile started at {DateTime.Now:HH:mm:ss.fff}");
            Console.Error.WriteLine($"[DEBUG] Processing file: {Path.GetFileName(inputFile)}");
            Console.Error.WriteLine($"[DEBUG] Total pages in cache: {analysis?.Pages?.Count ?? 0}");
            
            // Configurar segmentador
            var config = new DocumentSegmentationConfig();
            
            // Aplicar opções de filtro
            if (filterOptions.ContainsKey("--min-pages"))
            {
                if (int.TryParse(filterOptions["--min-pages"], out int minPages))
                    config.MinDocumentPages = minPages;
            }
            
            if (filterOptions.ContainsKey("--min-confidence"))
            {
                if (double.TryParse(filterOptions["--min-confidence"], out double minConf))
                    config.MinConfidenceScore = minConf;
            }
            
            // Opções de saída
            config.Verbose = outputOptions.ContainsKey("-v") || outputOptions.ContainsKey("--verbose");
            
            // Usar FormatManager para determinar formato
            string format = FormatManager.ExtractFormat(outputOptions, "filter");
            // Console.Error.WriteLine($"[DEBUG] FpdfDocumentsCommand format detected: '{format}'");
            
            // Executar segmentação
            var segmenter = new DocumentSegmenter(config);
            var documents = segmenter.FindDocuments(analysis);
            
            // DEBUG: Mostrar filtros ativos
            // Console.Error.WriteLine($"[DEBUG] FilterOptions count: {filterOptions.Count}");
            // foreach (var kvp in filterOptions)
            // {
            //     Console.Error.WriteLine($"[DEBUG] Filter: {kvp.Key} = {kvp.Value}");
            // }
            
            // Aplicar filtros de conteúdo (-w, -f, -r, --value)
            documents = FilterDocumentsByContent(documents, analysis, filterOptions);
            
            // Exibir resultados
            // Se outputOptions está vazio, significa que já existe um OutputManager ativo (range processing)
            bool useExistingOutput = outputOptions.Count == 0;
            
            if (useExistingOutput)
            {
                // Usar Console.Out que já foi redirecionado pelo OutputManager do range
                OutputResults(format, documents, analysis, filterOptions, outputOptions);
            }
            else
            {
                // Use OutputManager for consistent output handling
                using (var outputManager = new OutputManager(outputOptions))
                {
                    OutputResults(format, documents, analysis, filterOptions, outputOptions);
                }
            }
        }
        
        private void OutputResults(string format, List<DocumentBoundary> documents, PDFAnalysisResult analysis, Dictionary<string, string> filterOptions, Dictionary<string, string> outputOptions)
        {
            switch (format)
            {
                case "txt":
                    OutputTxt(documents, analysis);
                    break;
                case "raw":
                    OutputRaw(documents);
                    break;
                case "json":
                    OutputJson(documents, analysis, filterOptions);
                    break;
                case "xml":
                    OutputXml(documents, analysis);
                    break;
                case "csv":
                    OutputCsv(documents);
                    break;
                case "md":
                    OutputMarkdown(documents, analysis);
                    break;
                case "count":
                    Console.WriteLine(documents.Count);
                    break;
                case "detailed":
                    OutputDetailed(documents, analysis, false); // verbose será passado como parâmetro se necessário
                    break;
                case "ocr":
                    OutputDocumentsWithOCR(documents, analysis, ""); // inputPath will be retrieved from analysis.FilePath
                    break;
                case "png":
                    OutputDocumentsAsPng(documents, analysis, outputOptions);
                    break;
                default:
                    OutputSummary(documents, analysis, false); // verbose será passado como parâmetro se necessário
                    break;
            }
        }
        
        private void OutputSummary(List<DocumentBoundary> documents, PDFAnalysisResult analysis, bool verbose)
        {
            Console.WriteLine($"Found {documents.Count} document(s):");
            Console.WriteLine();
            
            foreach (var doc in documents)
            {
                Console.WriteLine($"DOCUMENT {doc.Number}:");
                Console.WriteLine($"  Pages: {doc.StartPage}-{doc.EndPage} ({doc.PageCount} pages)");
                Console.WriteLine($"  Confidence: {doc.Confidence:P0}");
                Console.WriteLine($"  Type detected: {doc.DetectedType}");
                
                if (doc.Fonts.Count > 0)
                {
                    Console.WriteLine($"  Fonts: {doc.Fonts.Count} ({string.Join(", ", doc.Fonts.Take(3))}{(doc.Fonts.Count > 3 ? "..." : "")})");
                }
                
                if (!string.IsNullOrEmpty(doc.PageSize))
                {
                    Console.WriteLine($"  Page size: {doc.PageSize}");
                }
                
                Console.WriteLine($"  Words: {doc.TotalWords:N0} (avg {doc.AverageWordsPerPage:F0}/page)");
                
                if (doc.HasSignatureImage)
                {
                    Console.WriteLine($"  Has signature image: Yes");
                }
                
                if (verbose)
                {
                    if (doc.StartIndicators.Count > 0)
                        Console.WriteLine($"  Start indicators: {string.Join(", ", doc.StartIndicators)}");
                    if (doc.EndIndicators.Count > 0)
                        Console.WriteLine($"  End indicators: {string.Join(", ", doc.EndIndicators)}");
                }
                
                // TEXTO COMPLETO - SEM LIMITAÇÕES
                if (!string.IsNullOrEmpty(doc.FirstPageText))
                {
                    Console.WriteLine($"  First page text:");
                    Console.WriteLine(doc.FirstPageText);
                }
                if (!string.IsNullOrEmpty(doc.LastPageText))
                {
                    Console.WriteLine($"  Last page text:");
                    Console.WriteLine(doc.LastPageText);
                }
                
                Console.WriteLine();
            }
            
            // Resumo final
            Console.WriteLine("SUMMARY:");
            Console.WriteLine($"  Total pages in PDF: {analysis.DocumentInfo.TotalPages}");
            Console.WriteLine($"  Documents found: {documents.Count}");
            if (documents.Count > 0)
            {
                Console.WriteLine($"  Average document size: {documents.Average(d => d.PageCount):F1} pages");
                Console.WriteLine($"  Page coverage: {documents.Sum(d => d.PageCount)}/{analysis.DocumentInfo.TotalPages} ({100.0 * documents.Sum(d => d.PageCount) / analysis.DocumentInfo.TotalPages:F0}%)");
            }
        }
        
        private void OutputJson(List<DocumentBoundary> documents, PDFAnalysisResult analysis, Dictionary<string, string> filterOptions)
        {
            // Criar output simplificado focado no que foi encontrado
            var output = new
            {
                arquivo = System.IO.Path.GetFileName(analysis.FilePath),
                documentosEncontrados = documents.Count,
                documentos = documents.Select(d => {
                    var docObj = new Dictionary<string, object>
                    {
                        ["documentNumber"] = d.Number,
                        ["pages"] = $"{d.StartPage}-{d.EndPage}",
                        ["documentName"] = ExtractDocumentName(d),
                        ["content"] = CleanTextForReading(d.FullText ?? d.FirstPageText ?? "")
                    };
                    
                    // ALWAYS add information from active filters
                    if (filterOptions.ContainsKey("-w") || filterOptions.ContainsKey("--word"))
                    {
                        docObj["searchedWords"] = filterOptions.ContainsKey("-w") ? filterOptions["-w"] : filterOptions["--word"];
                    }
                    
                    if (filterOptions.ContainsKey("--not-words"))
                    {
                        docObj["excludedWords"] = filterOptions["--not-words"];
                    }
                    
                    if (filterOptions.ContainsKey("-f") || filterOptions.ContainsKey("--font"))
                    {
                        docObj["searchedFont"] = filterOptions.ContainsKey("-f") ? filterOptions["-f"] : filterOptions["--font"];
                        docObj["fontsFound"] = d.Fonts.ToArray();
                    }
                    
                    if (filterOptions.ContainsKey("-r") || filterOptions.ContainsKey("--regex"))
                    {
                        docObj["regexPattern"] = filterOptions.ContainsKey("-r") ? filterOptions["-r"] : filterOptions["--regex"];
                    }
                    
                    if (filterOptions.ContainsKey("-v") || filterOptions.ContainsKey("--value") || filterOptions.ContainsKey("value"))
                    {
                        docObj["monetaryValues"] = true;
                    }
                    
                    if (filterOptions.ContainsKey("-s") || filterOptions.ContainsKey("--signature"))
                    {
                        string signatureFilter = filterOptions.ContainsKey("-s") ? filterOptions["-s"] : filterOptions["--signature"];
                        docObj["searchedSignature"] = signatureFilter;
                        docObj["hasSignaturePatterns"] = true;
                    }
                    
                    return docObj;
                }).ToArray()
            };
            
            Console.WriteLine(JsonConvert.SerializeObject(output, Formatting.Indented));
        }
        
        private void OutputDetailed(List<DocumentBoundary> documents, PDFAnalysisResult analysis, bool verbose)
        {
            Console.WriteLine("DOCUMENT BOUNDARIES ANALYSIS");
            Console.WriteLine(new string('=', 80));
            Console.WriteLine();
            Console.WriteLine($"File: {Path.GetFileName(analysis.FilePath)}");
            Console.WriteLine($"Total pages: {analysis.DocumentInfo.TotalPages}");
            Console.WriteLine($"Documents found: {documents.Count}");
            Console.WriteLine();
            
            foreach (var doc in documents)
            {
                Console.WriteLine(new string('-', 80));
                Console.WriteLine($"DOCUMENT {doc.Number}");
                Console.WriteLine(new string('-', 80));
                Console.WriteLine($"Pages: {doc.StartPage} to {doc.EndPage} ({doc.PageCount} pages)");
                Console.WriteLine($"Type: {doc.DetectedType}");
                Console.WriteLine($"Confidence: {doc.Confidence:P1}");
                Console.WriteLine();
                
                Console.WriteLine("CHARACTERISTICS:");
                Console.WriteLine($"  Total words: {doc.TotalWords:N0}");
                Console.WriteLine($"  Average words/page: {doc.AverageWordsPerPage:F0}");
                Console.WriteLine($"  Page size: {doc.PageSize ?? "Unknown"}");
                Console.WriteLine($"  Signature image: {(doc.HasSignatureImage ? "Yes" : "No")}");
                Console.WriteLine();
                
                Console.WriteLine($"FONTS ({doc.Fonts.Count}):");
                foreach (var font in doc.Fonts.OrderBy(f => f))
                {
                    Console.WriteLine($"  - {font}");
                }
                Console.WriteLine();
                
                if (verbose)
                {
                    Console.WriteLine("DETECTION DETAILS:");
                    if (doc.StartIndicators.Count > 0)
                    {
                        Console.WriteLine($"  Start indicators: {string.Join(", ", doc.StartIndicators)}");
                    }
                    if (doc.EndIndicators.Count > 0)
                    {
                        Console.WriteLine($"  End indicators: {string.Join(", ", doc.EndIndicators)}");
                    }
                    Console.WriteLine();
                }
                
                Console.WriteLine("FIRST PAGE TEXT:");
                if (!string.IsNullOrEmpty(doc.FirstPageText))
                {
                    Console.WriteLine(doc.FirstPageText);
                }
                
                Console.WriteLine("\nLAST PAGE TEXT:");
                if (!string.IsNullOrEmpty(doc.LastPageText))
                {
                    Console.WriteLine(doc.LastPageText);
                }
                Console.WriteLine();
            }
        }
        
        private void OutputTxt(List<DocumentBoundary> documents, PDFAnalysisResult analysis)
        {
            Console.WriteLine($"ARQUIVO: {System.IO.Path.GetFileName(analysis.FilePath)}");
            Console.WriteLine($"DOCUMENTOS ENCONTRADOS: {documents.Count}");
            Console.WriteLine();
            
            foreach (var doc in documents)
            {
                Console.WriteLine($"DOCUMENTO {doc.Number}:");
                Console.WriteLine($"Páginas: {doc.StartPage}-{doc.EndPage}");
                Console.WriteLine("CONTEÚDO COMPLETO:");
                Console.WriteLine(doc.FullText ?? doc.FirstPageText ?? "");
                Console.WriteLine();
                Console.WriteLine(new string('-', 80));
                Console.WriteLine();
            }
        }
        
        private void OutputRaw(List<DocumentBoundary> documents)
        {
            foreach (var doc in documents)
            {
                Console.WriteLine($"{doc.StartPage}-{doc.EndPage}");
            }
        }
        
        private void OutputXml(List<DocumentBoundary> documents, PDFAnalysisResult analysis)
        {
            Console.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            Console.WriteLine("<documents>");
            Console.WriteLine($"  <file>{System.Security.SecurityElement.Escape(analysis.FilePath)}</file>");
            Console.WriteLine($"  <totalPages>{analysis.DocumentInfo.TotalPages}</totalPages>");
            Console.WriteLine($"  <documentsFound>{documents.Count}</documentsFound>");
            
            foreach (var doc in documents)
            {
                Console.WriteLine($"  <document number=\"{doc.Number}\">");
                Console.WriteLine($"    <startPage>{doc.StartPage}</startPage>");
                Console.WriteLine($"    <endPage>{doc.EndPage}</endPage>");
                Console.WriteLine($"    <pageCount>{doc.PageCount}</pageCount>");
                Console.WriteLine($"    <confidence>{doc.Confidence}</confidence>");
                Console.WriteLine($"    <typeDetected>{System.Security.SecurityElement.Escape(doc.DetectedType)}</typeDetected>");
                Console.WriteLine($"    <totalWords>{doc.TotalWords}</totalWords>");
                
                if (!string.IsNullOrEmpty(doc.FirstPageText))
                {
                    Console.WriteLine($"    <firstPageText>{System.Security.SecurityElement.Escape(doc.FirstPageText)}</firstPageText>");
                }
                
                Console.WriteLine("  </document>");
            }
            
            Console.WriteLine("</documents>");
        }
        
        private void OutputCsv(List<DocumentBoundary> documents)
        {
            Console.WriteLine("Number,StartPage,EndPage,PageCount,Confidence,TypeDetected,TotalWords,FirstPageContent");
            
            foreach (var doc in documents)
            {
                string content = "";
                if (!string.IsNullOrEmpty(doc.FullText))
                {
                    content = doc.FullText
                        .Replace("\"", "\"\"").Replace("\r", " ").Trim();
                }
                else if (!string.IsNullOrEmpty(doc.FirstPageText))
                {
                    content = doc.FirstPageText
                        .Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ").Trim();
                }
                
                Console.WriteLine($"{doc.Number},{doc.StartPage},{doc.EndPage},{doc.PageCount},{doc.Confidence:F2},\"{doc.DetectedType}\",{doc.TotalWords},\"{content}\"");
            }
        }
        
        private void OutputMarkdown(List<DocumentBoundary> documents, PDFAnalysisResult analysis)
        {
            Console.WriteLine($"# Documents Found ({documents.Count})");
            Console.WriteLine();
            Console.WriteLine($"**File:** {System.IO.Path.GetFileName(analysis.FilePath)}");
            Console.WriteLine($"**Total Pages:** {analysis.DocumentInfo.TotalPages}");
            Console.WriteLine();
            
            foreach (var doc in documents)
            {
                Console.WriteLine($"## Document {doc.Number}");
                Console.WriteLine();
                Console.WriteLine($"- **Pages:** {doc.StartPage}-{doc.EndPage} ({doc.PageCount} pages)");
                Console.WriteLine($"- **Type:** {doc.DetectedType}");
                Console.WriteLine($"- **Confidence:** {doc.Confidence:P0}");
                Console.WriteLine($"- **Words:** {doc.TotalWords:N0}");
                
                if (!string.IsNullOrEmpty(doc.FullText))
                {
                    Console.WriteLine();
                    Console.WriteLine("**Content:**");
                    Console.WriteLine($"{doc.FullText}");
                }
                else if (!string.IsNullOrEmpty(doc.FirstPageText))
                {
                    Console.WriteLine();
                    Console.WriteLine("**Content:**");
                    Console.WriteLine($"{doc.FirstPageText}");
                }
                
                Console.WriteLine();
            }
        }
        
        private List<DocumentBoundary> FilterDocumentsByContent(List<DocumentBoundary> documents, 
                                                               PDFAnalysisResult analysis,
                                                               Dictionary<string, string> filterOptions)
        {
            var startTime = DateTime.Now;
            Console.Error.WriteLine($"[DEBUG] FilterDocumentsByContent started at {startTime:HH:mm:ss.fff}");
            Console.Error.WriteLine($"[DEBUG] Documents to filter: {documents.Count}");
            Console.Error.WriteLine($"[DEBUG] Active filters: {string.Join(", ", filterOptions.Keys)}");
            
            var filtered = new List<DocumentBoundary>();
            
            foreach (var doc in documents)
            {
                bool matches = true;
                
                // Verificar cada filtro
                foreach (var filter in filterOptions)
                {
                    switch (filter.Key)
                    {
                        case "-w":
                        case "--word":
                            var wordStartTime = DateTime.Now;
                            bool containsText = DocumentContainsText(doc, analysis, filter.Value);
                            var wordTime = DateTime.Now - wordStartTime;
                            Console.Error.WriteLine($"[DEBUG] Word search for '{filter.Value}' in doc {doc.StartPage}-{doc.EndPage} took {wordTime.TotalMilliseconds:F1}ms - Result: {containsText}");
                            
                            // Debug: Show sample text from document
                            var samplePage = analysis.Pages.FirstOrDefault(p => p.PageNumber == doc.StartPage);
                            if (samplePage?.TextInfo?.PageText != null)
                            {
                                var sampleText = samplePage.TextInfo.PageText.Length > 200 
                                    ? samplePage.TextInfo.PageText.Substring(0, 200) + "..." 
                                    : samplePage.TextInfo.PageText;
                                Console.Error.WriteLine($"[DEBUG] Sample text from page {doc.StartPage}: {sampleText.Replace("\n", " ")}");
                            }
                            
                            if (!containsText)
                            {
                                matches = false;
                            }
                            break;
                            
                        case "--not-words":
                            if (DocumentContainsText(doc, analysis, filter.Value))
                            {
                                matches = false;
                            }
                            break;
                            
                        case "-f":
                        case "--font":
                            if (!DocumentUsesFont(doc, filter.Value))
                            {
                                matches = false;
                            }
                            break;
                            
                        case "-r":
                        case "--regex":
                            if (!DocumentMatchesRegex(doc, analysis, filter.Value))
                            {
                                matches = false;
                            }
                            break;
                            
                        case "-v":
                        case "--value":
                        case "value": // Aceitar também sem hífen
                            if (!DocumentContainsCurrencyValue(doc, analysis))
                            {
                                matches = false;
                            }
                            break;
                            
                        case "-s":
                        case "--signature":
                            if (!DocumentContainsSignature(doc, analysis, filter.Value))
                            {
                                matches = false;
                            }
                            break;
                    }
                    
                    if (!matches) break;
                }
                
                if (matches)
                    filtered.Add(doc);
            }
            
            var totalTime = DateTime.Now - startTime;
            Console.Error.WriteLine($"[DEBUG] FilterDocumentsByContent completed in {totalTime.TotalMilliseconds:F1}ms");
            Console.Error.WriteLine($"[DEBUG] Documents filtered: {filtered.Count} of {documents.Count}");
            
            return filtered;
        }
        
        private bool DocumentContainsText(DocumentBoundary doc, PDFAnalysisResult analysis, string searchText)
        {
            // Obter todas as páginas do documento
            var pages = analysis.Pages
                .Where(p => p.PageNumber >= doc.StartPage && p.PageNumber <= doc.EndPage)
                .ToList();
            
            // Verificar se alguma página do documento contém o texto
            foreach (var page in pages)
            {
                if (page.TextInfo?.PageText != null && WordOption.Matches(page.TextInfo.PageText, searchText))
                    return true;
            }
            
            return false;
        }
        
        private bool DocumentUsesFont(DocumentBoundary doc, string fontPattern)
        {
            if (fontPattern.Contains("|"))
            {
                // OR logic
                string[] fonts = fontPattern.Split('|');
                foreach (string font in fonts)
                {
                    string trimmedFont = font.Trim();
                    if (string.IsNullOrEmpty(trimmedFont))
                        continue;
                    
                    if (DocumentUsesSingleFont(doc, trimmedFont))
                        return true;
                }
                return false;
            }
            else if (fontPattern.Contains("&"))
            {
                // AND logic
                string[] fonts = fontPattern.Split('&');
                foreach (string font in fonts)
                {
                    string trimmedFont = font.Trim();
                    if (string.IsNullOrEmpty(trimmedFont))
                        continue;
                    
                    if (!DocumentUsesSingleFont(doc, trimmedFont))
                        return false;
                }
                return true;
            }
            else
            {
                // Single font
                return DocumentUsesSingleFont(doc, fontPattern);
            }
        }
        
        private bool DocumentUsesSingleFont(DocumentBoundary doc, string fontPattern)
        {
            foreach (var fontName in doc.Fonts)
            {
                if (fontName.IndexOf(fontPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
        
        private bool DocumentMatchesRegex(DocumentBoundary doc, PDFAnalysisResult analysis, string pattern)
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                // Verificar em todas as páginas do documento
                var pages = analysis.Pages
                    .Where(p => p.PageNumber >= doc.StartPage && p.PageNumber <= doc.EndPage)
                    .ToList();
                
                foreach (var page in pages)
                {
                    if (page.TextInfo?.PageText != null && regex.IsMatch(page.TextInfo.PageText))
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool DocumentContainsCurrencyValue(DocumentBoundary doc, PDFAnalysisResult analysis)
        {
            // Obter todas as páginas do documento
            var pages = analysis.Pages
                .Where(p => p.PageNumber >= doc.StartPage && p.PageNumber <= doc.EndPage)
                .ToList();
            
            // Verificar se alguma página contém valor monetário
            foreach (var page in pages)
            {
                if (page.TextInfo?.PageText != null && 
                    BrazilianCurrencyDetector.ContainsBrazilianCurrency(page.TextInfo.PageText))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if document contains signature patterns
        /// A document matches if ANY of its pages contains the signature
        /// Uses the same logic as PageContainsSignature from FpdfPagesCommand
        /// </summary>
        private bool DocumentContainsSignature(DocumentBoundary doc, PDFAnalysisResult analysis, string signaturePattern)
        {
            // Obter todas as páginas do documento
            var pages = analysis.Pages
                .Where(p => p.PageNumber >= doc.StartPage && p.PageNumber <= doc.EndPage)
                .ToList();
            
            // Verificar se alguma página do documento contém a assinatura
            foreach (var page in pages)
            {
                if (page.TextInfo?.PageText != null && PageContainsSignature(page.TextInfo.PageText, signaturePattern))
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
                $"Por: {term}",
                $"By: {term}",
                $"{term} (assinado digitalmente)",
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
                "Assinado digitalmente",
                "Digitally signed",
                "Certificado digital",
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
        
        
        /// <summary>
        /// Extrai um nome/título significativo do documento
        /// </summary>
        private string ExtractDocumentName(DocumentBoundary doc)
        {
            string text = doc.FullText ?? doc.FirstPageText ?? "";
            
            if (string.IsNullOrWhiteSpace(text))
                return $"Documento {doc.Number}";
            
            // Pegar primeiras linhas significativas
            var lines = text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && l.Length > 5)
                .Take(8)
                .ToList();
            
            if (lines.Count == 0)
                return $"Documento {doc.Number}";
            
            // Tentar encontrar a linha mais significativa para usar como título
            string bestTitle = "";
            int bestScore = 0;
            
            foreach (var line in lines)
            {
                int score = 0;
                
                // Preferir linhas de tamanho médio (nem muito curtas nem muito longas)
                if (line.Length >= 20 && line.Length <= 80)
                    score += 3;
                else if (line.Length > 80 && line.Length <= 120)
                    score += 1;
                
                // Preferir linhas que começam com maiúscula
                if (line.Length > 0 && char.IsUpper(line[0]))
                    score += 2;
                
                // Preferir linhas que contêm números (possível número de documento/processo)
                if (Regex.IsMatch(line, @"\d"))
                    score += 2;
                
                // Penalizar linhas que são claramente parágrafos (muitas vírgulas, pontos no meio)
                int punctuationCount = line.Count(c => c == ',' || c == ';');
                if (punctuationCount > 2)
                    score -= 2;
                
                // Se tem ponto final no meio da linha, provavelmente não é título
                if (line.IndexOf('.') > 0 && line.IndexOf('.') < line.Length - 5)
                    score -= 1;
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTitle = line;
                }
            }
            
            // Se não encontrou um bom título, usar as primeiras palavras significativas
            if (string.IsNullOrEmpty(bestTitle) || bestScore < 2)
            {
                // Combinar algumas linhas curtas ou pegar parte da primeira linha longa
                var titleWords = new List<string>();
                int wordCount = 0;
                
                foreach (var line in lines)
                {
                    var words = line.Split(' ');
                    foreach (var word in words)
                    {
                        if (wordCount >= 10) break;
                        if (word.Length > 2)
                        {
                            titleWords.Add(word);
                            wordCount++;
                        }
                    }
                    if (wordCount >= 10) break;
                }
                
                bestTitle = string.Join(" ", titleWords);
                if (wordCount == 10)
                    bestTitle += "...";
            }
            
            // Limitar o tamanho do título
            if (bestTitle.Length > 100)
                bestTitle = bestTitle.Substring(0, 97) + "...";
            
            return bestTitle;
        }
        
        /// <summary>
        /// Limpa o texto removendo \n e organizando para leitura
        /// </summary>
        private string CleanTextForReading(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            
            // Substituir múltiplas quebras de linha por espaço duplo para indicar parágrafos
            text = Regex.Replace(text, @"\n\s*\n", "  ");
            
            // Substituir quebras de linha simples por espaço
            text = text.Replace("\n", " ");
            
            // Remover \r e \t
            text = text.Replace("\r", "").Replace("\t", " ");
            
            // Remover caracteres nulos e substituir por caracteres corretos
            text = text.Replace("\u0000", "");
            text = text.Replace("Pro\u0000ssão", "Profissão");
            text = text.Replace("Prossão", "Profissão");
            text = text.Replace("prossão", "profissão");
            
            // Limpar espaços múltiplos
            text = Regex.Replace(text, @"\s+", " ");
            
            // Remover espaços antes de pontuação
            text = Regex.Replace(text, @"\s+([.,;:!?])", "$1");
            
            // Adicionar espaço após pontuação se não houver
            text = Regex.Replace(text, @"([.,;:!?])([A-Za-záàâãéèêíïóôõöúçñÁÀÂÃÉÈÍÏÓÔÕÖÚÇÑ])", "$1 $2");
            
            // Quebrar em "sentenças" longas para melhor leitura
            var sentences = new List<string>();
            var currentSentence = new StringBuilder();
            var words = text.Split(' ');
            
            foreach (var word in words)
            {
                currentSentence.Append(word).Append(" ");
                
                // Se a sentença ficou muito longa ou encontrou pontuação final
                if (currentSentence.Length > 120 || 
                    word.EndsWith(".") || word.EndsWith("!") || word.EndsWith("?") ||
                    word.EndsWith(":") || word.EndsWith(";"))
                {
                    sentences.Add(currentSentence.ToString().Trim());
                    currentSentence.Clear();
                }
            }
            
            // Adicionar última sentença se houver
            if (currentSentence.Length > 0)
                sentences.Add(currentSentence.ToString().Trim());
            
            // Juntar sentenças com espaço duplo para simular parágrafos
            return string.Join("  ", sentences).Trim();
        }

        private void OutputDocumentsWithOCR(List<DocumentBoundary> documents, PDFAnalysisResult analysis, string inputPath)
        {
            Console.WriteLine($"🔍 Processando OCR para {documents.Count} documento(s) encontrado(s)...");
            Console.WriteLine();

            // Use analysis.FilePath if inputPath is empty
            string actualInputPath = string.IsNullOrEmpty(inputPath) ? analysis.FilePath : inputPath;

            for (int i = 0; i < documents.Count; i++)
            {
                var document = documents[i];
                
                Console.WriteLine($"📄 Documento {i + 1}:");
                Console.WriteLine($"   Páginas: {document.StartPage}-{document.EndPage}");
                Console.WriteLine($"   Total de páginas: {document.EndPage - document.StartPage + 1}");
                
                if (!string.IsNullOrEmpty(document.DetectedType))
                {
                    Console.WriteLine($"   Tipo: {document.DetectedType}");
                }
                
                Console.WriteLine($"   Confiança: {document.Confidence:F2}");
                Console.WriteLine();

                // Process OCR for first page of document (representative)
                var representativePage = document.StartPage;
                Console.WriteLine($"   🔍 Aplicando OCR na página {representativePage} (representativa)...");
                
                var ocrResult = UniversalOCRService.ProcessPageWithOCR(actualInputPath, analysis, representativePage);
                
                if (ocrResult.Success)
                {
                    Console.WriteLine(UniversalOCRService.FormatOCROutput(ocrResult, true));
                }
                else
                {
                    Console.WriteLine($"   ❌ Erro no OCR: {ocrResult.Error}");
                }
                
                Console.WriteLine();
                Console.WriteLine("═══════════════════════════════════════");
                Console.WriteLine();
            }

            Console.WriteLine($"🎯 OCR concluído para {documents.Count} documento(s)!");
        }

        private void OutputDocumentsAsPng(List<DocumentBoundary> documents, PDFAnalysisResult analysis, Dictionary<string, string> outputOptions)
        {
            Console.WriteLine($"🖼️  Iniciando extração PNG para {documents.Count} documento(s) filtrado(s)...");
            Console.WriteLine();
            
            try 
            {
                // Converter cada DocumentBoundary em PageMatches para usar com o OptimizedPngExtractor
                var allPageMatches = new List<PageMatch>();
                
                foreach (var document in documents)
                {
                    // Para cada documento, criar PageMatch objects para todas suas páginas
                    for (int pageNum = document.StartPage; pageNum <= document.EndPage; pageNum++)
                    {
                        var page = analysis.Pages.FirstOrDefault(p => p.PageNumber == pageNum);
                        if (page != null)
                        {
                            var pageMatch = new PageMatch
                            {
                                PageNumber = page.PageNumber,
                                PageInfo = page,
                                MatchReasons = new List<string> { $"Document {document.Number} (pages {document.StartPage}-{document.EndPage})" }
                            };
                            allPageMatches.Add(pageMatch);
                        }
                    }
                }
                
                Console.WriteLine($"📋 Convertidos {documents.Count} documentos em {allPageMatches.Count} páginas para extração...");
                Console.WriteLine();
                
                // Usar o OptimizedPngExtractor existente para extrair todas as páginas dos documentos filtrados
                OptimizedPngExtractor.ExtractPagesAsPng(
                    allPageMatches, 
                    outputOptions, 
                    analysis?.FilePath,  // PDF path from analysis
                    analysis?.FilePath,  // inputFilePath
                    true  // isUsingCache - assumindo que estamos sempre usando cache em document analysis
                );
                
                Console.WriteLine();
                Console.WriteLine($"🎯 Extração PNG concluída para {documents.Count} documento(s) ({allPageMatches.Count} páginas)!");
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
    }
}