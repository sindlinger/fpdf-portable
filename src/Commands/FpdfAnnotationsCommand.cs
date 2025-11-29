using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using iTextSharp.text.pdf;
using Newtonsoft.Json;
using FilterPDF.Options;
using FilterPDF.Utils;
using FilterPDF.Commands;


namespace FilterPDF
{
    public class FpdfAnnotationsCommand
    {
        private string inputFilePath = "";
        private PDFAnalysisResult analysisResult = new PDFAnalysisResult();
        private bool isUsingCache = false;
        private Dictionary<string, string> filterOptions = new Dictionary<string, string>();
        private Dictionary<string, string> outputOptions = new Dictionary<string, string>();
        
        // Classes internas para resultados
        public class AnnotationMatch
        {
            public int PageNumber { get; set; }
            public Annotation Annotation { get; set; } = new Annotation();
            public List<string> MatchReasons { get; set; } = new List<string>();
            public List<ObjectMatch> RelatedObjects { get; set; } = new List<ObjectMatch>();
        }
        
        public class ObjectMatch
        {
            public int ObjectNumber { get; set; }
            public string ObjectType { get; set; } = "";
            public PageAnalysis ContainingPage { get; set; } = new PageAnalysis();
        }
        
        public void Execute(string inputFile, PDFAnalysisResult analysis, Dictionary<string, string> filters, Dictionary<string, string> outputs)
        {
            inputFilePath = inputFile;
            analysisResult = analysis;
            filterOptions = filters;
            outputOptions = outputs;
            isUsingCache = inputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            
            ExecuteAnnotationsSearch();
        }
        
        // Manter o método antigo para compatibilidade se necessário
        public void Execute(string[] args)
        {
            if (args.Length < 2)
            {
                ShowInstanceHelp();
                return;
            }
            
            inputFilePath = args[0];
            
            // Parse arguments
            ParseArguments(args.Skip(2).ToArray());
            
            // Validar arquivo
            if (!File.Exists(inputFilePath))
            {
                Console.WriteLine($"Arquivo '{inputFilePath}' nao encontrado");
                return;
            }
            
            // Check if it's JSON (cache) or PDF
            isUsingCache = inputFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            
            if (isUsingCache)
            {
                var cacheJson = File.ReadAllText(inputFilePath);
                analysisResult = JsonConvert.DeserializeObject<PDFAnalysisResult>(cacheJson) ?? new PDFAnalysisResult();
            }
            else
            {
                // Não deveria chegar aqui - FilterCommand agora força uso de cache
                Console.WriteLine("ERRO: Comando annotations requer arquivo em cache!");
                return;
            }
            
            ExecuteAnnotationsSearch();
        }
        
        private void ExecuteAnnotationsSearch()
        {
            Console.WriteLine($"Finding ANNOTATIONS in: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            // Primeiro verificar se o documento atende aos filtros universais
            if (!DocumentMatchesUniversalFilters())
            {
                Console.WriteLine("Document does not match filter criteria.");
                OutputAnnotationResults(new List<AnnotationMatch>());
                return;
            }
            
            var foundAnnotations = new List<AnnotationMatch>();
            
            foreach (var page in analysisResult.Pages)
            {
                if (PageMatchesLocationFilters(page) && page.Annotations.Count > 0)
                {
                    foreach (var annotation in page.Annotations)
                    {
                        if (AnnotationMatchesFilters(annotation, page))
                        {
                            var annotationMatch = new AnnotationMatch
                            {
                                PageNumber = page.PageNumber,
                                Annotation = annotation,
                                MatchReasons = GetAnnotationMatchReasons(annotation, page)
                            };
                            
                            // Objects so do cache - sem PdfReader
                            if (outputOptions.ContainsKey("--objects"))
                            {
                                annotationMatch.RelatedObjects = GetAnnotationRelatedObjectsFromCache(page, annotation);
                            }
                            
                            foundAnnotations.Add(annotationMatch);
                        }
                    }
                }
            }
            
            OutputAnnotationResults(foundAnnotations);
        }
        
        private bool DocumentMatchesUniversalFilters()
        {
            // Filtros que se aplicam ao documento inteiro
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--created-before":
                        if (!IsCreatedBefore(filter.Value))
                            return false;
                        break;
                    case "--created-after":
                        if (!IsCreatedAfter(filter.Value))
                            return false;
                        break;
                    case "--modified-before":
                        if (!IsModifiedBefore(filter.Value))
                            return false;
                        break;
                    case "--modified-after":
                        if (!IsModifiedAfter(filter.Value))
                            return false;
                        break;
                }
            }
            return true;
        }
        
        private bool PageMatchesLocationFilters(PageAnalysis page)
        {
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--page":
                    case "-p":
                        if (page.PageNumber != int.Parse(filter.Value))
                            return false;
                        break;
                    case "--page-range":
                        var parts = filter.Value.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                        {
                            if (page.PageNumber < start || page.PageNumber > end)
                                return false;
                        }
                        break;
                }
            }
            return true;
        }
        
        private bool AnnotationMatchesFilters(Annotation annotation, PageAnalysis page)
        {
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--word":
                    case "-w":
                        if (string.IsNullOrEmpty(annotation.Contents) || !WordOption.Matches(annotation.Contents, filter.Value))
                            return false;
                        break;
                        
                    case "--not-words":
                        if (!string.IsNullOrEmpty(annotation.Contents) && WordOption.Matches(annotation.Contents, filter.Value))
                            return false;
                        break;
                        
                    case "--regex":
                    case "-r":
                        var regex = new Regex(filter.Value, RegexOptions.IgnoreCase);
                        if (!regex.IsMatch(annotation.Contents ?? ""))
                            return false;
                        break;
                        
                    case "--type":
                        if (!annotation.Type.ToLower().Contains(filter.Value.ToLower()))
                            return false;
                        break;
                        
                    case "--author":
                        if (annotation.Author != null && !WordOption.Matches(annotation.Author, filter.Value))
                            return false;
                        break;
                        
                    case "--comment":
                        if (annotation.Contents != null && !WordOption.Matches(annotation.Contents, filter.Value))
                            return false;
                        break;
                        
                    case "--subject":
                        if (annotation.Subject != null && !WordOption.Matches(annotation.Subject, filter.Value))
                            return false;
                        break;
                        
                    case "--has-reply":
                        bool expectedReply = ParseBooleanValue(filter.Value);
                        // InReplyTo nao esta disponivel no modelo atual
                        bool hasReply = false;
                        if (hasReply != expectedReply)
                            return false;
                        break;
                        
                    case "--value":
                    case "-v":
                        if (!BrazilianCurrencyDetector.ContainsBrazilianCurrency(annotation.Contents ?? ""))
                            return false;
                        break;
                        
                    case "--signature":
                    case "-s":
                        if (page.TextInfo?.PageText == null || !PageContainsSignature(page.TextInfo.PageText, filter.Value))
                            return false;
                        break;
                }
            }
            return true;
        }
        
        private bool ParseBooleanValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return value.ToLower() == "true" || value == "1";
        }
        
        private bool IsCreatedBefore(string dateStr)
        {
            if (DateTime.TryParse(dateStr, out DateTime targetDate))
            {
                if (analysisResult.Metadata?.CreationDate != null)
                {
                    return analysisResult.Metadata.CreationDate.Value < targetDate;
                }
            }
            return false;
        }
        
        private bool IsCreatedAfter(string dateStr)
        {
            if (DateTime.TryParse(dateStr, out DateTime targetDate))
            {
                if (analysisResult.Metadata?.CreationDate != null)
                {
                    return analysisResult.Metadata.CreationDate.Value > targetDate;
                }
            }
            return false;
        }
        
        private bool IsModifiedBefore(string dateStr)
        {
            if (DateTime.TryParse(dateStr, out DateTime targetDate))
            {
                if (analysisResult.Metadata?.ModificationDate != null)
                {
                    return analysisResult.Metadata.ModificationDate.Value < targetDate;
                }
            }
            return false;
        }
        
        private bool IsModifiedAfter(string dateStr)
        {
            if (DateTime.TryParse(dateStr, out DateTime targetDate))
            {
                if (analysisResult.Metadata?.ModificationDate != null)
                {
                    return analysisResult.Metadata.ModificationDate.Value > targetDate;
                }
            }
            return false;
        }
        
        private List<string> GetAnnotationMatchReasons(Annotation annotation, PageAnalysis page)
        {
            var reasons = new List<string>();
            reasons.Add($"Annotation on page {page.PageNumber}");
            reasons.Add($"Type: {annotation.Type}");
            
            if (!string.IsNullOrEmpty(annotation.Contents))
            {
                var preview = annotation.Contents.Length > 50 ? 
                    annotation.Contents.Substring(0, 50) + "..." : 
                    annotation.Contents;
                reasons.Add($"Content: {preview}");
            }
            
            return reasons;
        }
        
        private List<ObjectMatch> GetAnnotationRelatedObjectsFromCache(PageAnalysis page, Annotation annotation)
        {
            // Retornar lista vazia - cache nao tem objetos internos detalhados
            return new List<ObjectMatch>();
        }
        
        private void OutputAnnotationResults(List<AnnotationMatch> foundAnnotations)
        {
            using (var outputManager = new OutputManager(outputOptions))
            {
                if (foundAnnotations.Count == 0)
                {
                    Console.WriteLine("No annotations found matching the specified criteria.");
                    return;
                }
                
                // Usar FormatManager para determinar formato
                string format = FormatManager.ExtractFormat(outputOptions, "filter");
                
                string output = "";
                switch (format)
                {
                    case "json":
                        output = FormatAnnotationsAsJson(foundAnnotations);
                        break;
                        
                    case "xml":
                        output = FormatAnnotationsAsXml(foundAnnotations);
                        break;
                        
                    case "csv":
                        output = FormatAnnotationsAsCsv(foundAnnotations);
                        break;
                        
                    case "md":
                        output = FormatAnnotationsAsMarkdown(foundAnnotations);
                        break;
                        
                    case "count":
                        Console.WriteLine(foundAnnotations.Count);
                        return;
                        
                    case "raw":
                        output = FormatAnnotationsAsRaw(foundAnnotations);
                        break;
                        
                    case "png":
                        OutputAnnotationsAsPng(foundAnnotations);
                        return; // Early return to avoid console output
                        
                    default: // txt
                        output = FormatAnnotationsAsText(foundAnnotations);
                        break;
                }
                
                Console.WriteLine(output);
                outputManager.Flush();
            }
        }
        
        private string FormatAnnotationsAsText(List<AnnotationMatch> annotations)
        {
            var sb = new StringBuilder();
            
            foreach (var match in annotations)
            {
                sb.AppendLine($"ANNOTATION (Page {match.PageNumber}):");
                sb.AppendLine($"  Type: {match.Annotation.Type}");
                
                if (!string.IsNullOrEmpty(match.Annotation.Contents))
                    sb.AppendLine($"  Content: {match.Annotation.Contents}");
                    
                if (!string.IsNullOrEmpty(match.Annotation.Author))
                    sb.AppendLine($"  Author: {match.Annotation.Author}");
                    
                if (match.Annotation.ModificationDate.HasValue)
                    sb.AppendLine($"  Modified: {match.Annotation.ModificationDate}");
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine($"  Match reasons: {string.Join(", ", match.MatchReasons)}");
                }
                
                if (match.RelatedObjects?.Count > 0)
                {
                    sb.AppendLine($"  Related PDF Objects ({match.RelatedObjects.Count}):");
                    foreach (var obj in match.RelatedObjects)
                    {
                        sb.AppendLine($"    - Object #{obj.ObjectNumber} ({obj.ObjectType})");
                    }
                }
                
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private string FormatAnnotationsAsJson(List<AnnotationMatch> annotations)
        {
            var result = new
            {
                totalResults = annotations.Count,
                annotations = annotations.Select(a => new
                {
                    pageNumber = a.PageNumber,
                    type = a.Annotation.Type,
                    contents = a.Annotation.Contents,
                    author = a.Annotation.Author,
                    modificationDate = a.Annotation.ModificationDate,
                    subject = "", // Subject nao disponivel no modelo
                    inReplyTo = "", // InReplyTo nao disponivel no modelo
                    matchReasons = a.MatchReasons,
                    relatedObjects = a.RelatedObjects?.Select(o => new
                    {
                        objectNumber = o.ObjectNumber,
                        objectType = o.ObjectType
                    }).ToArray() ?? new object[0]
                }).ToArray()
            };
            
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }
        
        private string FormatAnnotationsAsXml(List<AnnotationMatch> annotations)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<AnnotationResults>");
            sb.AppendLine($"  <TotalResults>{annotations.Count}</TotalResults>");
            sb.AppendLine("  <Annotations>");
            
            foreach (var match in annotations)
            {
                sb.AppendLine($"    <Annotation pageNumber=\"{match.PageNumber}\">");
                sb.AppendLine($"      <Type>{System.Security.SecurityElement.Escape(match.Annotation.Type)}</Type>");
                
                if (!string.IsNullOrEmpty(match.Annotation.Contents))
                    sb.AppendLine($"      <Contents>{System.Security.SecurityElement.Escape(match.Annotation.Contents)}</Contents>");
                    
                if (!string.IsNullOrEmpty(match.Annotation.Author))
                    sb.AppendLine($"      <Author>{System.Security.SecurityElement.Escape(match.Annotation.Author)}</Author>");
                    
                if (match.Annotation.ModificationDate.HasValue)
                    sb.AppendLine($"      <ModificationDate>{match.Annotation.ModificationDate}</ModificationDate>");
                    
                // Subject nao disponivel no modelo atual
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine("      <MatchReasons>");
                    foreach (var reason in match.MatchReasons)
                    {
                        sb.AppendLine($"        <Reason>{System.Security.SecurityElement.Escape(reason)}</Reason>");
                    }
                    sb.AppendLine("      </MatchReasons>");
                }
                
                if (match.RelatedObjects?.Count > 0)
                {
                    sb.AppendLine("      <RelatedObjects>");
                    foreach (var obj in match.RelatedObjects)
                    {
                        sb.AppendLine($"        <Object number=\"{obj.ObjectNumber}\" type=\"{obj.ObjectType}\"/>");
                    }
                    sb.AppendLine("      </RelatedObjects>");
                }
                
                sb.AppendLine("    </Annotation>");
            }
            
            sb.AppendLine("  </Annotations>");
            sb.AppendLine("</AnnotationResults>");
            
            return sb.ToString();
        }
        
        private string FormatAnnotationsAsCsv(List<AnnotationMatch> annotations)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PageNumber,Type,Contents,Author,ModificationDate,Subject,MatchReasons");
            
            foreach (var match in annotations)
            {
                var contents = (match.Annotation.Contents ?? "").Replace("\"", "\"\"");
                var author = (match.Annotation.Author ?? "").Replace("\"", "\"\"");
                var subject = ""; // Subject nao disponivel no modelo
                var reasons = string.Join("; ", match.MatchReasons).Replace("\"", "\"\"");
                var modDate = match.Annotation.ModificationDate?.ToString() ?? "";
                
                sb.AppendLine($"{match.PageNumber},\"{match.Annotation.Type}\",\"{contents}\",\"{author}\",\"{modDate}\",\"{subject}\",\"{reasons}\"");
            }
            
            return sb.ToString();
        }
        
        private string FormatAnnotationsAsMarkdown(List<AnnotationMatch> annotations)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Annotation Search Results");
            sb.AppendLine();
            sb.AppendLine($"**Total Results:** {annotations.Count}");
            sb.AppendLine();
            
            foreach (var match in annotations)
            {
                sb.AppendLine($"## Annotation on Page {match.PageNumber}");
                sb.AppendLine();
                sb.AppendLine($"- **Type:** {match.Annotation.Type}");
                
                if (!string.IsNullOrEmpty(match.Annotation.Contents))
                {
                    sb.AppendLine($"- **Content:** {match.Annotation.Contents}");
                }
                
                if (!string.IsNullOrEmpty(match.Annotation.Author))
                {
                    sb.AppendLine($"- **Author:** {match.Annotation.Author}");
                }
                
                if (match.Annotation.ModificationDate.HasValue)
                {
                    sb.AppendLine($"- **Modified:** {match.Annotation.ModificationDate}");
                }
                
                // Subject nao disponivel no modelo atual
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine($"- **Match Reasons:** {string.Join(", ", match.MatchReasons)}");
                }
                
                if (match.RelatedObjects?.Count > 0)
                {
                    sb.AppendLine("- **Related Objects:**");
                    foreach (var obj in match.RelatedObjects)
                    {
                        sb.AppendLine($"  - Object #{obj.ObjectNumber} ({obj.ObjectType})");
                    }
                }
                
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private string FormatAnnotationsAsRaw(List<AnnotationMatch> annotations)
        {
            var sb = new StringBuilder();
            
            foreach (var match in annotations)
            {
                if (!string.IsNullOrEmpty(match.Annotation.Contents))
                {
                    sb.AppendLine(match.Annotation.Contents);
                }
            }
            
            return sb.ToString();
        }
        
        private void OutputAnnotationsAsPng(List<AnnotationMatch> foundAnnotations)
        {
            // Convert AnnotationMatch to PageMatch, avoiding duplicate pages
            var pagesWithAnnotations = foundAnnotations
                .Where(a => a.PageNumber > 0) // Only annotations with valid page numbers
                .GroupBy(a => a.PageNumber)
                .Select(group => 
                {
                    var pageNumber = group.Key;
                    var annotationsInPage = group.ToList();
                    var pageInfo = analysisResult.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
                    
                    var annotationTypes = annotationsInPage.Select(a => a.Annotation.Type).Distinct();
                    var annotationCount = annotationsInPage.Count;
                    var hasContent = annotationsInPage.Any(a => !string.IsNullOrEmpty(a.Annotation.Contents));
                    
                    var reasons = new List<string>
                    {
                        $"Contains {annotationCount} annotation(s): {string.Join(", ", annotationTypes)}"
                    };
                    
                    if (hasContent)
                    {
                        var contentPreviews = annotationsInPage
                            .Where(a => !string.IsNullOrEmpty(a.Annotation.Contents))
                            .Select(a => a.Annotation.Contents.Length > 30 ? 
                                a.Annotation.Contents.Substring(0, 30) + "..." : 
                                a.Annotation.Contents)
                            .Take(3);
                        reasons.Add($"Content samples: {string.Join("; ", contentPreviews)}");
                    }
                    
                    return new PageMatch
                    {
                        PageNumber = pageNumber,
                        PageInfo = pageInfo ?? new PageAnalysis { PageNumber = pageNumber },
                        MatchReasons = reasons
                    };
                })
                .OrderBy(p => p.PageNumber)
                .ToList();

            if (pagesWithAnnotations.Count == 0)
            {
                Console.WriteLine("⚠️  No annotations found with valid page targets for PNG extraction.");
                return;
            }

            Console.WriteLine($"🖼️  Iniciando extração PNG para {pagesWithAnnotations.Count} página(s) com annotations encontradas...");
            Console.WriteLine();
            
            try 
            {
                // Use the existing OptimizedPngExtractor to extract the filtered pages
                OptimizedPngExtractor.ExtractPagesAsPng(
                    pagesWithAnnotations, 
                    outputOptions, 
                    analysisResult?.FilePath,  // PDF path from analysis
                    inputFilePath,             // Cache file path if using cache
                    isUsingCache               // Whether we're using cache
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro durante extração PNG: {ex.Message}");
            }
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
                case "--word":
                case "-w":
                    return $"Contains {WordOption.GetSearchDescription(value)}";
                case "--regex":
                case "-r":
                    return $"Matches regex: \"{value}\"";
                case "--page":
                case "-p":
                    return $"On page: {value}";
                case "--page-range":
                    return $"Page range: {value}";
                case "--type":
                    return $"Annotation type: \"{value}\"";
                case "--author":
                    return $"Author {WordOption.GetSearchDescription(value)}";
                case "--comment":
                    return $"Comment {WordOption.GetSearchDescription(value)}";
                case "--subject":
                    return $"Subject {WordOption.GetSearchDescription(value)}";
                case "--has-reply":
                    return $"Has reply: {value}";
                case "--created-before":
                    return $"Created before: {value}";
                case "--created-after":
                    return $"Created after: {value}";
                case "--modified-before":
                    return $"Modified before: {value}";
                case "--modified-after":
                    return $"Modified after: {value}";
                default:
                    return $"{key}: {value}";
            }
        }
        
        private void ParseArguments(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                
                // Output format options
                if (arg == "-F" && i + 1 < args.Length)
                {
                    outputOptions["-F"] = args[++i];
                }
                else if (arg == "--count" || arg == "-c")
                {
                    outputOptions["--count"] = "true";
                }
                else if (arg == "--objects")
                {
                    outputOptions["--objects"] = "true";
                }
                // Filter options
                else if ((arg == "--word" || arg == "-w") && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if ((arg == "--regex" || arg == "-r") && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if ((arg == "--page" || arg == "-p") && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--page-range" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--type" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--author" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--comment" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--subject" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--has-reply")
                {
                    filterOptions[arg] = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[++i] : "true";
                }
                else if (arg == "--created-before" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--created-after" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--modified-before" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (arg == "--modified-after" && i + 1 < args.Length)
                {
                    filterOptions[arg] = args[++i];
                }
                else if (!arg.StartsWith("-"))
                {
                    Console.Error.WriteLine($"Warning: Unexpected argument '{arg}'");
                }
                else
                {
                    Console.Error.WriteLine($"Warning: Unknown option '{arg}' for annotations command");
                }
            }
        }
        
        private void ShowInstanceHelp()
        {
            FpdfAnnotationsCommand.ShowHelp();
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
        
        public static void ShowHelp()
        {
            Console.WriteLine(LanguageManager.GetAnnotationsHelp());
        }
    }
}