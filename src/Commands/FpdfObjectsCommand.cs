using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Options;
using FilterPDF.Commands;


namespace FilterPDF
{
    /// <summary>
    /// Filter Objects Command - Find internal PDF objects
    /// </summary>
    public class FpdfObjectsCommand
    {
        private Dictionary<string, string> filterOptions = new Dictionary<string, string>();
        private Dictionary<string, string> outputOptions = new Dictionary<string, string>();
        private PDFAnalysisResult analysisResult = new PDFAnalysisResult();
        private string inputFilePath = "";
        private bool isUsingCache = false;
        
        public void Execute(string inputFile, PDFAnalysisResult analysis, Dictionary<string, string> filters, Dictionary<string, string> outputs)
        {
            inputFilePath = inputFile;
            analysisResult = analysis;
            filterOptions = filters;
            outputOptions = outputs;
            isUsingCache = inputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            
            ExecuteObjectsSearch();
        }
        
        private void ExecuteObjectsSearch()
        {
            Console.WriteLine($"Finding OBJECTS in: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            // Primeiro verificar se o documento atende aos filtros universais
            if (!DocumentMatchesUniversalFilters())
            {
                Console.WriteLine("Document does not match filter criteria.");
                OutputObjectResults(new List<ObjectMatch>()); // Resultado vazio
                return;
            }
            
            var foundObjects = new List<ObjectMatch>();
            
            // Tentar usar objetos do cache primeiro (modo ultra)
            if (isUsingCache && TryGetObjectsFromCache(out foundObjects))
            {
                OutputObjectResults(foundObjects);
                return;
            }
            
            // Se não há cache ultra, não processar objects
            Console.WriteLine("Objects search requires cache with ultra mode.");
            Console.WriteLine("Please reload the PDF using: fpdf load document.pdf --ultra");
            OutputObjectResults(new List<ObjectMatch>());
        }
        
        private bool TryGetObjectsFromCache(out List<ObjectMatch> foundObjects)
        {
            foundObjects = new List<ObjectMatch>();
            
            // Verificar se o cache tem allObjects (modo ultra)
            var cacheData = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(inputFilePath));
            
            if (cacheData == null || !cacheData.ContainsKey("allObjects"))
            {
                return false; // Cache não tem objetos
            }
            
            var allObjects = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(cacheData["allObjects"].ToString() ?? "") ?? new List<Dictionary<string, object>>();
            
            // Extrair detailedPages se disponível
            List<object>? detailedPages = null;
            if (cacheData.ContainsKey("detailedPages"))
            {
                detailedPages = JsonConvert.DeserializeObject<List<object>>(cacheData["detailedPages"].ToString() ?? "");
            }
            
            foreach (var obj in allObjects)
            {
                if (obj.ContainsKey("number") && obj.ContainsKey("type"))
                {
                    var objectMatch = new ObjectMatch
                    {
                        ObjectNumber = Convert.ToInt32(obj["number"]),
                        ObjectType = obj["type"]?.ToString() ?? "",
                        Generation = obj.ContainsKey("generation") ? Convert.ToInt32(obj["generation"]) : 0
                    };
                    
                    if (obj.ContainsKey("streamLength"))
                        objectMatch.StreamLength = Convert.ToInt64(obj["streamLength"]);
                    
                    if (obj.ContainsKey("dictionaryKeys"))
                        objectMatch.DictionaryKeys = JsonConvert.DeserializeObject<List<string>>(obj["dictionaryKeys"].ToString() ?? "") ?? new List<string>();
                    
                    if (obj.ContainsKey("indirectReferencesCount"))
                        objectMatch.IndirectReferencesCount = Convert.ToInt32(obj["indirectReferencesCount"]);
                    
                    // Adicionar detailedPages se disponível
                    if (detailedPages != null)
                    {
                        objectMatch.DetailedPages = detailedPages;
                    }
                    
                    // Aplicar filtros
                    if (!ObjectMatchesFiltersFromCache(objectMatch))
                        continue;
                    
                    // Adicionar razões de match
                    objectMatch.MatchReasons = GetObjectMatchReasons(objectMatch);
                    
                    foundObjects.Add(objectMatch);
                }
            }
            
            return true;
        }
        
        private bool ObjectMatchesFiltersFromCache(ObjectMatch obj)
        {
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--type":
                        if (filter.Value.Contains("*"))
                        {
                            // Suporte simples para wildcard * no final
                            if (filter.Value.EndsWith("*"))
                            {
                                string prefix = filter.Value.TrimEnd('*');
                                if (!obj.ObjectType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                    return false;
                            }
                            else
                            {
                                // * em qualquer lugar = contém
                                string searchText = filter.Value.Replace("*", "");
                                if (obj.ObjectType.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                                    return false;
                            }
                        }
                        else
                        {
                            if (!obj.ObjectType.Equals(filter.Value, StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                        break;
                        
                    case "--number":
                        if (obj.ObjectNumber != int.Parse(filter.Value))
                            return false;
                        break;
                        
                    case "--stream":
                        bool expectedStream = bool.Parse(filter.Value);
                        bool hasStream = obj.StreamLength > 0;
                        if (hasStream != expectedStream)
                            return false;
                        break;
                        
                    case "--min-size":
                        long minSize = long.Parse(filter.Value);
                        if (obj.StreamLength < minSize)
                            return false;
                        break;
                        
                    case "--max-size":
                        long maxSize = long.Parse(filter.Value);
                        if (obj.StreamLength > maxSize)
                            return false;
                        break;
                        
                    case "--has-key":
                        if (obj.DictionaryKeys == null || !obj.DictionaryKeys.Contains(filter.Value))
                            return false;
                        break;
                        
                    case "--indirect-refs":
                        bool expectedRefs = bool.Parse(filter.Value);
                        bool hasRefs = obj.IndirectReferencesCount > 0;
                        if (hasRefs != expectedRefs)
                            return false;
                        break;
                        
                    case "--detailed-text":
                    case "-dt":
                        if (!ObjectHasDetailedText(obj, filter.Value))
                            return false;
                        break;
                        
                    case "--signature":
                    case "-s":
                        if (!ObjectOnPageWithSignature(obj, filter.Value))
                            return false;
                        break;
                }
            }
            
            return true;
        }
        
        private bool ObjectHasDetailedText(ObjectMatch obj, string searchText)
        {
            if (obj.DetailedPages == null || obj.DetailedPages.Count == 0)
                return false;
            
            foreach (var page in obj.DetailedPages)
            {
                try
                {
                    // Converter para JSON e depois deserializar para navegar mais facilmente
                    string pageJson = JsonConvert.SerializeObject(page);
                    var pageDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(pageJson);
                    
                    if (pageDict != null && pageDict.ContainsKey("textExtractions"))
                    {
                        var textExtractionsJson = pageDict["textExtractions"].ToString();
                        if (textExtractionsJson != null)
                        {
                            var textExtractions = JsonConvert.DeserializeObject<Dictionary<string, object>>(textExtractionsJson);
                        
                        if (textExtractions != null)
                        {
                            // Procurar em todas as estratégias de extração
                            foreach (var extraction in textExtractions)
                            {
                                var extractionJson = extraction.Value.ToString();
                                if (extractionJson != null)
                                {
                                    var extractionData = JsonConvert.DeserializeObject<Dictionary<string, object>>(extractionJson);
                                
                                if (extractionData != null && extractionData.ContainsKey("text"))
                                {
                                    string text = extractionData["text"]?.ToString() ?? "";
                                    
                                    // Usar WordOption para busca de texto
                                    if (WordOption.Matches(text, searchText))
                                        return true;
                                }
                                }
                            }
                        }
                        }
                    }
                }
                catch
                {
                    // Ignorar erros na navegação da estrutura
                }
            }
            
            return false;
        }
        
        private bool DocumentMatchesUniversalFilters()
        {
            // Verificar se há filtros que se aplicam ao documento como um todo
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--min-pages":
                        int minPages = int.Parse(filter.Value);
                        if (analysisResult.Pages.Count < minPages)
                            return false;
                        break;
                        
                    case "--max-pages":
                        int maxPages = int.Parse(filter.Value);
                        if (analysisResult.Pages.Count > maxPages)
                            return false;
                        break;
                        
                    case "--has-bookmarks":
                        bool expectedBookmarks = bool.Parse(filter.Value);
                        bool hasBookmarks = analysisResult.Bookmarks?.RootItems?.Count > 0;
                        if (hasBookmarks != expectedBookmarks)
                            return false;
                        break;
                        
                    case "--has-forms":
                        bool expectedForms = bool.Parse(filter.Value);
                        if (analysisResult.DocumentInfo.HasAcroForm != expectedForms)
                            return false;
                        break;
                        
                    case "--encrypted":
                        bool expectedEncrypted = bool.Parse(filter.Value);
                        if (analysisResult.DocumentInfo.IsEncrypted != expectedEncrypted)
                            return false;
                        break;
                }
            }
            
            return true;
        }
        
        private List<string> GetObjectMatchReasons(ObjectMatch obj)
        {
            var reasons = new List<string>();
            reasons.Add($"Object #{obj.ObjectNumber} Type: {obj.ObjectType}");
            
            if (obj.StreamLength > 0)
                reasons.Add($"Has stream ({FormatSize(obj.StreamLength)})");
            
            if (obj.DictionaryKeys != null && obj.DictionaryKeys.Count > 0)
                reasons.Add($"Has {obj.DictionaryKeys.Count} dictionary keys");
                
            if (obj.IndirectReferencesCount > 0)
                reasons.Add($"Has {obj.IndirectReferencesCount} indirect references");
                
            // Verificar se foi filtrado por texto detalhado e assinatura
            foreach (var filter in filterOptions)
            {
                if ((filter.Key == "--detailed-text" || filter.Key == "-dt") && ObjectHasDetailedText(obj, filter.Value))
                {
                    reasons.Add($"Contains detailed text: '{filter.Value}'");
                }
                else if ((filter.Key == "--signature" || filter.Key == "-s") && ObjectOnPageWithSignature(obj, filter.Value))
                {
                    if (filter.Value.Contains("&"))
                    {
                        string[] terms = filter.Value.Split('&');
                        reasons.Add($"On page with all signatures: {string.Join(" AND ", terms.Select(t => $"'{t.Trim()}'"))}");
                    }
                    else if (filter.Value.Contains("|"))
                    {
                        string[] terms = filter.Value.Split('|');
                        reasons.Add($"On page with any signature: {string.Join(" OR ", terms.Select(t => $"'{t.Trim()}'"))}");
                    }
                    else
                    {
                        reasons.Add($"On page with signature: '{filter.Value}'");
                    }
                }
            }
            
            return reasons;
        }
        
        private void ShowActiveFilters()
        {
            if (filterOptions.Count > 0)
            {
                Console.WriteLine("Active filters:");
                foreach (var filter in filterOptions)
                {
                    Console.WriteLine($"  {filter.Key}: {filter.Value}");
                }
                Console.WriteLine();
            }
        }
        
        private void OutputObjectResults(List<ObjectMatch> foundObjects)
        {
            if (foundObjects.Count == 0)
            {
                Console.WriteLine("No objects found matching the specified criteria.");
                return;
            }
            
            // Determinar formato de saída
            string format = "text"; // default
            if (outputOptions.ContainsKey("-F"))
                format = outputOptions["-F"];
            else if (outputOptions.ContainsKey("--format"))
                format = outputOptions["--format"];
            
            switch (format.ToLower())
            {
                case "json":
                    OutputObjectsAsJson(foundObjects);
                    break;
                case "xml":
                    OutputObjectsAsXml(foundObjects);
                    break;
                case "csv":
                    OutputObjectsAsCsv(foundObjects);
                    break;
                case "md":
                    OutputObjectsAsMarkdown(foundObjects);
                    break;
                case "raw":
                    OutputObjectsAsRaw(foundObjects);
                    break;
                case "count":
                    Console.WriteLine(foundObjects.Count);
                    break;
                case "png":
                    OutputObjectsAsPng(foundObjects);
                    break;
                default:
                    OutputObjectsAsText(foundObjects);
                    break;
            }
        }
        
        private void OutputObjectsAsText(List<ObjectMatch> objects)
        {
            Console.WriteLine($"Found {objects.Count} object(s):");
            Console.WriteLine();
            
            foreach (var obj in objects)
            {
                Console.WriteLine($"OBJECT #{obj.ObjectNumber} (Gen {obj.Generation}):");
                Console.WriteLine($"  Type: {obj.ObjectType}");
                
                if (obj.StreamLength > 0)
                    Console.WriteLine($"  Stream Size: {FormatSize(obj.StreamLength)}");
                
                if (obj.DictionaryKeys != null && obj.DictionaryKeys.Count > 0)
                {
                    Console.WriteLine($"  Dictionary Keys ({obj.DictionaryKeys.Count}):");
                    foreach (var key in obj.DictionaryKeys.Take(10))
                    {
                        Console.WriteLine($"    - {key}");
                    }
                    if (obj.DictionaryKeys.Count > 10)
                        Console.WriteLine($"    ... and {obj.DictionaryKeys.Count - 10} more");
                }
                
                if (obj.IndirectReferencesCount > 0)
                    Console.WriteLine($"  Indirect References: {obj.IndirectReferencesCount}");
                
                if (obj.MatchReasons.Count > 0)
                {
                    Console.WriteLine("  Match reasons:");
                    foreach (var reason in obj.MatchReasons)
                    {
                        Console.WriteLine($"    - {reason}");
                    }
                }
                
                if (obj.DetailedPages != null && obj.DetailedPages.Count > 0)
                {
                    Console.WriteLine($"  Detailed Pages Available: {obj.DetailedPages.Count} pages");
                }
                
                Console.WriteLine();
            }
        }
        
        private void OutputObjectsAsJson(List<ObjectMatch> objects)
        {
            var output = new
            {
                totalObjects = objects.Count,
                objects = objects.Select(o => new
                {
                    number = o.ObjectNumber,
                    generation = o.Generation,
                    type = o.ObjectType,
                    streamSize = o.StreamLength,
                    dictionaryKeys = o.DictionaryKeys,
                    indirectReferences = o.IndirectReferencesCount,
                    matchReasons = o.MatchReasons,
                    detailedPages = o.DetailedPages
                })
            };
            
            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            Console.WriteLine(json);
        }
        
        private void OutputObjectsAsXml(List<ObjectMatch> objects)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<objects>");
            sb.AppendLine($"  <totalObjects>{objects.Count}</totalObjects>");
            
            foreach (var obj in objects)
            {
                sb.AppendLine($"  <object number=\"{obj.ObjectNumber}\" generation=\"{obj.Generation}\">");
                sb.AppendLine($"    <type>{System.Security.SecurityElement.Escape(obj.ObjectType)}</type>");
                
                if (obj.StreamLength > 0)
                    sb.AppendLine($"    <streamSize>{obj.StreamLength}</streamSize>");
                
                if (obj.DictionaryKeys != null && obj.DictionaryKeys.Count > 0)
                {
                    sb.AppendLine($"    <dictionaryKeys count=\"{obj.DictionaryKeys.Count}\">");
                    foreach (var key in obj.DictionaryKeys)
                    {
                        sb.AppendLine($"      <key>{System.Security.SecurityElement.Escape(key)}</key>");
                    }
                    sb.AppendLine("    </dictionaryKeys>");
                }
                
                if (obj.IndirectReferencesCount > 0)
                    sb.AppendLine($"    <indirectReferences>{obj.IndirectReferencesCount}</indirectReferences>");
                
                if (obj.MatchReasons.Count > 0)
                {
                    sb.AppendLine("    <matchReasons>");
                    foreach (var reason in obj.MatchReasons)
                    {
                        sb.AppendLine($"      <reason>{System.Security.SecurityElement.Escape(reason)}</reason>");
                    }
                    sb.AppendLine("    </matchReasons>");
                }
                
                sb.AppendLine("  </object>");
            }
            
            sb.AppendLine("</objects>");
            Console.WriteLine(sb.ToString());
        }
        
        private void OutputObjectsAsCsv(List<ObjectMatch> objects)
        {
            Console.WriteLine("ObjectNumber,Generation,Type,StreamSize,DictionaryKeys,IndirectReferences,MatchReasons");
            
            foreach (var obj in objects)
            {
                string type = obj.ObjectType.Replace("\"", "\"\"");
                string keys = obj.DictionaryKeys != null ? string.Join(";", obj.DictionaryKeys).Replace("\"", "\"\"") : "";
                string reasons = string.Join("; ", obj.MatchReasons).Replace("\"", "\"\"");
                
                Console.WriteLine($"{obj.ObjectNumber},{obj.Generation},\"{type}\",{obj.StreamLength},\"{keys}\",{obj.IndirectReferencesCount},\"{reasons}\"");
            }
        }
        
        private void OutputObjectsAsMarkdown(List<ObjectMatch> objects)
        {
            Console.WriteLine($"# Found Objects ({objects.Count})");
            Console.WriteLine();
            
            foreach (var obj in objects)
            {
                Console.WriteLine($"## Object #{obj.ObjectNumber} (Gen {obj.Generation})");
                Console.WriteLine();
                Console.WriteLine($"- **Type:** {obj.ObjectType}");
                
                if (obj.StreamLength > 0)
                    Console.WriteLine($"- **Stream Size:** {FormatSize(obj.StreamLength)}");
                
                if (obj.DictionaryKeys != null && obj.DictionaryKeys.Count > 0)
                {
                    Console.WriteLine($"- **Dictionary Keys:** {obj.DictionaryKeys.Count}");
                    Console.WriteLine("  ```");
                    foreach (var key in obj.DictionaryKeys.Take(10))
                    {
                        Console.WriteLine($"  {key}");
                    }
                    if (obj.DictionaryKeys.Count > 10)
                        Console.WriteLine($"  ... and {obj.DictionaryKeys.Count - 10} more");
                    Console.WriteLine("  ```");
                }
                
                if (obj.IndirectReferencesCount > 0)
                    Console.WriteLine($"- **Indirect References:** {obj.IndirectReferencesCount}");
                
                if (obj.MatchReasons.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("**Match Reasons:**");
                    foreach (var reason in obj.MatchReasons)
                    {
                        Console.WriteLine($"- {reason}");
                    }
                }
                
                Console.WriteLine();
            }
        }
        
        private void OutputObjectsAsRaw(List<ObjectMatch> objects)
        {
            foreach (var obj in objects)
            {
                Console.WriteLine($"{obj.ObjectNumber} {obj.Generation} {obj.ObjectType}");
            }
        }
        
        private void OutputObjectsAsPng(List<ObjectMatch> objects)
        {
            Console.WriteLine($"🖼️  Iniciando extração PNG para {objects.Count} objeto(s) encontrado(s)...");
            Console.WriteLine();
            
            try 
            {
                // Converter ObjectMatch para PageMatch para usar o OptimizedPngExtractor
                var pageMatches = ConvertObjectsToPageMatches(objects);
                
                if (pageMatches.Count == 0)
                {
                    Console.WriteLine("⚠️  Nenhum objeto encontrado possui páginas válidas para extração PNG");
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
        
        private List<PageMatch> ConvertObjectsToPageMatches(List<ObjectMatch> objects)
        {
            var pageMatches = new List<PageMatch>();
            var processedPages = new HashSet<int>();
            
            foreach (var obj in objects)
            {
                // Objects podem ter páginas associadas via DetailedPages
                if (obj.DetailedPages != null && obj.DetailedPages.Count > 0)
                {
                    // Extrair números de página das DetailedPages
                    foreach (var pageData in obj.DetailedPages)
                    {
                        try
                        {
                            // Converter para JSON e extrair pageNumber
                            string pageJson = JsonConvert.SerializeObject(pageData);
                            var pageDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(pageJson);
                            
                            if (pageDict != null && pageDict.ContainsKey("pageNumber"))
                            {
                                if (int.TryParse(pageDict["pageNumber"].ToString(), out int pageNum) && 
                                    !processedPages.Contains(pageNum))
                                {
                                    // Criar PageMatch correspondente
                                    var pageMatch = new PageMatch
                                    {
                                        PageNumber = pageNum,
                                        MatchReasons = new List<string> 
                                        {
                                            $"Contém objeto #{obj.ObjectNumber} tipo {obj.ObjectType}"
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
                            }
                        }
                        catch
                        {
                            // Ignorar erros na conversão
                        }
                    }
                }
                else
                {
                    // Se não há DetailedPages, assumir que o objeto pode estar em múltiplas páginas
                    // Neste caso, extrair todas as páginas disponíveis como fallback
                    if (analysisResult?.Pages != null && analysisResult.Pages.Count > 0)
                    {
                        foreach (var page in analysisResult.Pages)
                        {
                            if (!processedPages.Contains(page.PageNumber))
                            {
                                var pageMatch = new PageMatch
                                {
                                    PageNumber = page.PageNumber,
                                    PageInfo = page,
                                    MatchReasons = new List<string> 
                                    {
                                        $"Página pode conter objeto #{obj.ObjectNumber} tipo {obj.ObjectType}"
                                    }
                                };
                                pageMatches.Add(pageMatch);
                                processedPages.Add(page.PageNumber);
                            }
                        }
                    }
                }
            }
            
            return pageMatches.OrderBy(p => p.PageNumber).ToList();
        }
        
        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            
            return $"{size:F2} {sizes[order]}";
        }
        
        /// <summary>
        /// Check if object is on a page with signature patterns
        /// This is complex because objects don't directly map to pages in the cache
        /// We check if any page in the document contains the signature
        /// </summary>
        private bool ObjectOnPageWithSignature(ObjectMatch obj, string signaturePattern)
        {
            // For objects, we need to check if ANY page in the document contains the signature
            // Since we can't directly map objects to specific pages in most cases
            if (analysisResult?.Pages == null || analysisResult.Pages.Count == 0)
                return false;
            
            foreach (var page in analysisResult.Pages)
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
