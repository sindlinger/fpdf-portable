using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using FilterPDF.Options;
using FilterPDF.Services;
using FilterPDF.Commands;


namespace FilterPDF
{
    /// <summary>
    /// Filter Words Command - Find words that match specific criteria
    /// </summary>
    public class FpdfWordsCommand
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
            
            ExecuteWordsSearch();
        }
        
        private void ExecuteWordsSearch()
        {
            Console.WriteLine($"Finding WORDS in: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            // Primeiro verificar se o documento atende aos filtros universais
            if (!DocumentMatchesUniversalFilters())
            {
                Console.WriteLine("Document does not match filter criteria.");
                OutputWordResults(new List<WordMatch>()); // Resultado vazio
                return;
            }
            
            var foundWords = new List<WordMatch>();
            // REMOVIDO: PdfReader não é mais usado - sempre cache!
            
            try
            {
                // Usar somente dados do cache
                int totalPages = analysisResult.Pages.Count;
                
                for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                {
                    var pageInfo = analysisResult.Pages.FirstOrDefault(p => p.PageNumber == pageNum);
                    if (pageInfo != null && PageMatchesLocationFilters(pageInfo))
                    {
                        var wordsInPage = ExtractWordsFromPage(pageNum, pageInfo);
                        
                        // Objects só do cache - sem PdfReader
                        if (outputOptions.ContainsKey("--objects") && wordsInPage.Count > 0)
                        {
                            var pageObjects = GetPageRelatedObjectsFromCache(pageInfo);
                            foreach (var word in wordsInPage)
                            {
                                word.RelatedObjects = pageObjects;
                            }
                        }
                        
                        foundWords.AddRange(wordsInPage);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            OutputWordResults(foundWords);
        }
        
        private List<ObjectMatch> GetPageRelatedObjectsFromCache(PageAnalysis pageInfo)
        {
            // Retornar lista vazia - cache não tem objetos internos detalhados de páginas
            return new List<ObjectMatch>();
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
        
        private bool PageMatchesLocationFilters(PageAnalysis page)
        {
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--page":
                    case "-p":
                        if (int.Parse(filter.Value) != page.PageNumber)
                            return false;
                        break;
                        
                    case "--page-range":
                        var range = filter.Value.Split('-');
                        if (range.Length == 2)
                        {
                            int start = int.Parse(range[0]);
                            int end = int.Parse(range[1]);
                            if (page.PageNumber < start || page.PageNumber > end)
                                return false;
                        }
                        break;
                        
                    case "--first":
                        int firstPages = int.Parse(filter.Value);
                        if (page.PageNumber > firstPages)
                            return false;
                        break;
                        
                    case "--last":
                        int lastPages = int.Parse(filter.Value);
                        int totalPages = analysisResult.Pages.Count;
                        if (page.PageNumber <= totalPages - lastPages)
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
        
        private List<WordMatch> ExtractWordsFromPage(int pageNumber, PageAnalysis pageInfo)
        {
            var words = new List<WordMatch>();
            
            // Usar somente texto do cache
            if (!isUsingCache || pageInfo == null || pageInfo.TextInfo == null || string.IsNullOrEmpty(pageInfo.TextInfo.PageText))
            {
                return words; // Sem cache válido, não processar
            }
            
            string pageText = pageInfo.TextInfo.PageText;
            
            // Se há filtro de palavra específica
            if (filterOptions.ContainsKey("--word") || filterOptions.ContainsKey("-w"))
            {
                string searchWord = filterOptions.ContainsKey("--word") ? filterOptions["--word"] : filterOptions["-w"];
                
                // Primeiro verifica se a página contém o padrão usando WordOption
                if (!WordOption.Matches(pageText, searchWord))
                {
                    return words; // Retorna lista vazia se não houver match
                }
                
                // Se houver match, extrai as palavras individuais para mostrar
                if (searchWord.Contains("|"))
                {
                    // Multiple words with OR logic - extrai TODAS as palavras que deram match
                    string[] orWords = searchWord.Split('|');
                    foreach (string word in orWords)
                    {
                        string trimmedWord = word.Trim();
                        if (string.IsNullOrEmpty(trimmedWord))
                            continue;
                        
                        // Verifica se esta palavra específica está na página
                        if (WordOption.Matches(pageText, trimmedWord))
                        {
                            var matches = FindWordMatchesFuzzy(pageText, trimmedWord);
                            foreach (Match match in matches)
                            {
                                words.Add(new WordMatch
                                {
                                    Word = match.Value,
                                    PageNumber = pageNumber,
                                    Context = ExtractContext(pageText, match.Index),
                                    MatchReasons = BuildMatchReasons($"Contains '{trimmedWord}' (OR search)", pageText)
                                });
                            }
                        }
                    }
                }
                else if (searchWord.Contains("&"))
                {
                    // Multiple words with AND logic - all words must be present on page
                    string[] andWords = searchWord.Split('&');
                    
                    // Extract all AND words found on this page
                    foreach (string word in andWords)
                    {
                        string trimmedWord = word.Trim();
                        if (string.IsNullOrEmpty(trimmedWord))
                            continue;
                            
                        var matches = FindWordMatchesFuzzy(pageText, trimmedWord);
                        foreach (Match match in matches)
                        {
                            words.Add(new WordMatch
                            {
                                Word = match.Value,
                                PageNumber = pageNumber,
                                Context = ExtractContext(pageText, match.Index),
                                MatchReasons = BuildMatchReasons($"Contains '{trimmedWord}' (AND search)", pageText)
                            });
                        }
                    }
                }
                else
                {
                    // Simple word search
                    var matches = FindWordMatchesFuzzy(pageText, searchWord);
                    
                    foreach (Match match in matches)
                    {
                        words.Add(new WordMatch
                        {
                            Word = match.Value,
                            PageNumber = pageNumber,
                            Context = ExtractContext(pageText, match.Index),
                            MatchReasons = BuildMatchReasons($"Contains '{searchWord}'", pageText)
                        });
                    }
                }
            }
            else if (filterOptions.ContainsKey("--not-words"))
            {
                // Se há filtro de exclusão, retornar lista vazia se a palavra for encontrada
                string excludeWords = filterOptions["--not-words"];
                if (WordOption.Matches(pageText, excludeWords))
                {
                    return words; // Retorna lista vazia
                }
                // Se não encontrou palavras excluídas, continua com a extração normal
            }
            else if (filterOptions.ContainsKey("--regex") || filterOptions.ContainsKey("-r"))
            {
                string pattern = filterOptions.ContainsKey("--regex") ? filterOptions["--regex"] : filterOptions["-r"];
                var matches = Regex.Matches(pageText, pattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    words.Add(new WordMatch
                    {
                        Word = match.Value,
                        PageNumber = pageNumber,
                        Context = ExtractContext(pageText, match.Index),
                        MatchReasons = BuildMatchReasons($"Matches regex '{pattern}'", pageText)
                    });
                }
            }
            else if (filterOptions.ContainsKey("--value") || filterOptions.ContainsKey("-v"))
            {
                // Buscar valores monetários brasileiros
                var currencyValues = BrazilianCurrencyDetector.ExtractCurrencyValues(pageText);
                
                foreach (var value in currencyValues)
                {
                    words.Add(new WordMatch
                    {
                        Word = value.OriginalText,
                        PageNumber = pageNumber,
                        Context = ExtractContext(pageText, value.Position),
                        MatchReasons = BuildMatchReasons($"Brazilian currency value: {value.FormattedValue}", pageText)
                    });
                }
            }
            else
            {
                // Extrair palavras mais frequentes
                var wordCounts = new Dictionary<string, int>();
                var wordMatches = Regex.Matches(pageText, @"\b\w{4,}\b");
                
                foreach (Match match in wordMatches)
                {
                    string word = match.Value.ToLower();
                    wordCounts[word] = wordCounts.ContainsKey(word) ? wordCounts[word] + 1 : 1;
                }
                
                var topWords = wordCounts.OrderByDescending(kv => kv.Value).Take(10);
                foreach (var wordCount in topWords)
                {
                    words.Add(new WordMatch
                    {
                        Word = wordCount.Key,
                        PageNumber = pageNumber,
                        Context = $"Appears {wordCount.Value} times on page",
                        MatchReasons = BuildMatchReasons($"High frequency ({wordCount.Value} occurrences)", pageText)
                    });
                }
            }
            
            return words;
        }
        
        private string ExtractContext(string text, int position)
        {
            int start = Math.Max(0, position - 50);
            int length = Math.Min(100, text.Length - start);
            return text.Substring(start, length).Replace("\n", " ").Trim();
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
        
        private void OutputWordResults(List<WordMatch> foundWords)
        {
            using (var outputManager = new OutputManager(outputOptions))
            {
                if (foundWords.Count == 0)
                {
                    Console.WriteLine("No words found matching the specified criteria.");
                    return;
                }
                
                // Usar FormatManager para determinar formato
                string format = FormatManager.ExtractFormat(outputOptions, "filter");
                
                switch (format)
                {
                    case "json":
                        OutputWordsAsJson(foundWords);
                        break;
                    case "xml":
                        OutputWordsAsXml(foundWords);
                        break;
                    case "csv":
                        OutputWordsAsCsv(foundWords);
                        break;
                    case "md":
                        OutputWordsAsMarkdown(foundWords);
                        break;
                    case "raw":
                        OutputWordsAsRaw(foundWords);
                        break;
                    case "count":
                        Console.WriteLine(foundWords.Count);
                        break;
                    case "ocr":
                        OutputWordsWithOCR(foundWords);
                        break;
                    case "png":
                        OutputWordsAsPng(foundWords);
                        break;
                    default:
                        OutputWordsAsText(foundWords);
                        break;
                }
                
                outputManager.Flush();
            }
        }
        
        private void OutputWordsAsText(List<WordMatch> foundWords)
        {
            Console.WriteLine($"Found {foundWords.Count} word(s):");
            Console.WriteLine();
            
            foreach (var match in foundWords)
            {
                Console.WriteLine($"WORD: '{match.Word}' (Page {match.PageNumber})");
                Console.WriteLine($"  Context: {match.Context}");
                
                if (match.MatchReasons.Count > 0)
                {
                    Console.WriteLine("  Match reasons:");
                    foreach (var reason in match.MatchReasons)
                    {
                        Console.WriteLine($"    - {reason}");
                    }
                }
                
                if (match.RelatedObjects != null && match.RelatedObjects.Count > 0)
                {
                    Console.WriteLine($"  Related objects: {match.RelatedObjects.Count}");
                }
                
                Console.WriteLine();
            }
        }
        
        private void OutputWordsAsJson(List<WordMatch> foundWords)
        {
            var output = new
            {
                totalWords = foundWords.Count,
                words = foundWords.Select(w => new
                {
                    word = w.Word,
                    pageNumber = w.PageNumber,
                    context = w.Context,
                    matchReasons = w.MatchReasons,
                    relatedObjects = w.RelatedObjects?.Count ?? 0
                })
            };
            
            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            Console.WriteLine(json);
        }
        
        private void OutputWordsAsXml(List<WordMatch> foundWords)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<words>");
            sb.AppendLine($"  <totalWords>{foundWords.Count}</totalWords>");
            
            foreach (var match in foundWords)
            {
                sb.AppendLine($"  <word pageNumber=\"{match.PageNumber}\">");
                sb.AppendLine($"    <text>{System.Security.SecurityElement.Escape(match.Word)}</text>");
                sb.AppendLine($"    <context>{System.Security.SecurityElement.Escape(match.Context)}</context>");
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine("    <matchReasons>");
                    foreach (var reason in match.MatchReasons)
                    {
                        sb.AppendLine($"      <reason>{System.Security.SecurityElement.Escape(reason)}</reason>");
                    }
                    sb.AppendLine("    </matchReasons>");
                }
                
                if (match.RelatedObjects != null && match.RelatedObjects.Count > 0)
                {
                    sb.AppendLine($"    <relatedObjects>{match.RelatedObjects.Count}</relatedObjects>");
                }
                
                sb.AppendLine("  </word>");
            }
            
            sb.AppendLine("</words>");
            Console.WriteLine(sb.ToString());
        }
        
        private void OutputWordsAsCsv(List<WordMatch> foundWords)
        {
            Console.WriteLine("Word,PageNumber,Context,MatchReasons,RelatedObjects");
            
            foreach (var match in foundWords)
            {
                string word = match.Word.Replace("\"", "\"\"");
                string context = match.Context.Replace("\"", "\"\"");
                string reasons = string.Join("; ", match.MatchReasons).Replace("\"", "\"\"");
                int relatedObjects = match.RelatedObjects?.Count ?? 0;
                
                Console.WriteLine($"\"{word}\",{match.PageNumber},\"{context}\",\"{reasons}\",{relatedObjects}");
            }
        }
        
        private void OutputWordsAsMarkdown(List<WordMatch> foundWords)
        {
            Console.WriteLine($"# Found Words ({foundWords.Count})");
            Console.WriteLine();
            
            foreach (var match in foundWords)
            {
                Console.WriteLine($"## '{match.Word}' (Page {match.PageNumber})");
                Console.WriteLine();
                Console.WriteLine($"**Context:** {match.Context}");
                
                if (match.MatchReasons.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("**Match Reasons:**");
                    foreach (var reason in match.MatchReasons)
                    {
                        Console.WriteLine($"- {reason}");
                    }
                }
                
                if (match.RelatedObjects != null && match.RelatedObjects.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"**Related Objects:** {match.RelatedObjects.Count}");
                }
                
                Console.WriteLine();
            }
        }
        
        private void OutputWordsAsRaw(List<WordMatch> foundWords)
        {
            foreach (var match in foundWords)
            {
                Console.WriteLine(match.Word);
            }
        }
        
        private bool ContainsWordFuzzy(string pageText, string searchWord)
        {
            // Usa WordOption para verificar se contém a palavra (suporta ~palavra~)
            if (WordOption.Matches(pageText, searchWord))
                return true;
                
            // Se não encontrar, tenta busca fuzzy com espaços entre caracteres
            bool shouldNormalize = searchWord.StartsWith("~") && searchWord.EndsWith("~") && searchWord.Length > 2;
            string cleanWord = shouldNormalize ? searchWord.Substring(1, searchWord.Length - 2) : searchWord;
            
            string fuzzyPattern = CreateFuzzyPattern(cleanWord);
            var fuzzyRegex = new Regex(fuzzyPattern, RegexOptions.IgnoreCase);
            
            if (shouldNormalize)
            {
                string normalizedText = WordOption.NormalizeText(pageText);
                return fuzzyRegex.IsMatch(normalizedText);
            }
            
            return fuzzyRegex.IsMatch(pageText);
        }
        
        private MatchCollection FindWordMatchesFuzzy(string pageText, string searchWord)
        {
            // Verifica se deve usar busca normalizada
            bool shouldNormalize = searchWord.StartsWith("~") && searchWord.EndsWith("~") && searchWord.Length > 2;
            string cleanWord = shouldNormalize ? searchWord.Substring(1, searchWord.Length - 2) : searchWord;
            
            if (shouldNormalize)
            {
                // Busca normalizada - converte texto e padrão
                string normalizedText = WordOption.NormalizeText(pageText);
                string normalizedPattern = WordOption.NormalizeText(cleanWord);
                
                // Busca todas as ocorrências no texto normalizado
                var normalizedMatches = Regex.Matches(normalizedText, Regex.Escape(normalizedPattern), RegexOptions.IgnoreCase);
                
                // Retorna matches do texto original nas mesmas posições
                var originalMatches = new List<Match>();
                foreach (Match match in normalizedMatches)
                {
                    // Extrai o texto original na mesma posição
                    int start = match.Index;
                    int length = match.Length;
                    if (start + length <= pageText.Length)
                    {
                        string originalText = pageText.Substring(start, Math.Min(length, pageText.Length - start));
                        // Cria um Match manual (não é possível criar Match diretamente, então usamos Regex)
                        var tempRegex = new Regex(Regex.Escape(originalText));
                        var tempMatch = tempRegex.Match(pageText, start);
                        if (tempMatch.Success)
                            originalMatches.Add(tempMatch);
                    }
                }
                
                // Converte lista para MatchCollection
                if (originalMatches.Count > 0)
                {
                    var combined = string.Join("|", originalMatches.Select(m => Regex.Escape(m.Value)));
                    return Regex.Matches(pageText, combined);
                }
            }
            
            // Busca normal - verifica se tem wildcards
            string pattern;
            if (cleanWord.Contains("*") || cleanWord.Contains("?"))
            {
                // Converte wildcard para regex
                pattern = cleanWord.Replace("*", ".*").Replace("?", ".");
                pattern = $@"\b{pattern}\b"; // Word boundaries
            }
            else
            {
                pattern = Regex.Escape(cleanWord);
            }
            
            var normalMatches = Regex.Matches(pageText, pattern, RegexOptions.IgnoreCase);
            if (normalMatches.Count > 0)
                return normalMatches;
                
            // Se não encontrar, tenta busca com espaços entre caracteres
            string fuzzyPattern = CreateFuzzyPattern(cleanWord);
            return Regex.Matches(pageText, fuzzyPattern, RegexOptions.IgnoreCase);
        }
        
        private string CreateFuzzyPattern(string searchWord)
        {
            var chars = searchWord.ToCharArray();
            var patternParts = new List<string>();
            
            foreach (char c in chars)
            {
                patternParts.Add(Regex.Escape(c.ToString()));
            }
            
            // Juntar com \s* (zero ou mais espaços) entre cada caractere
            return string.Join(@"\s*", patternParts);
        }

        private void OutputWordsWithOCR(List<WordMatch> foundWords)
        {
            // Group words by page
            var wordsByPage = foundWords.GroupBy(w => w.PageNumber).OrderBy(g => g.Key);
            
            Console.WriteLine($"🔍 Processando OCR para páginas com palavras encontradas...");
            Console.WriteLine();

            foreach (var pageGroup in wordsByPage)
            {
                var pageNumber = pageGroup.Key;
                var wordsInPage = pageGroup.ToList();
                
                Console.WriteLine($"📄 Página {pageNumber}:");
                Console.WriteLine($"   Palavras encontradas: {wordsInPage.Count}");
                
                // Show found words
                var uniqueWords = wordsInPage.Select(w => w.Word).Distinct().ToList();
                Console.WriteLine($"   Termos: {string.Join(", ", uniqueWords)}");
                Console.WriteLine();

                // Process OCR for this page
                var ocrResult = UniversalOCRService.ProcessPageWithOCR(inputFilePath, analysisResult, pageNumber);
                
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

            Console.WriteLine($"🎯 OCR concluído para {wordsByPage.Count()} página(s)!");
        }

        private void OutputWordsAsPng(List<WordMatch> foundWords)
        {
            // Convert WordMatch to PageMatch, avoiding duplicate pages
            var pagesWithWords = foundWords
                .GroupBy(w => w.PageNumber)
                .Select(group => 
                {
                    var pageNumber = group.Key;
                    var wordsInPage = group.ToList();
                    var pageInfo = analysisResult.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
                    
                    return new PageMatch
                    {
                        PageNumber = pageNumber,
                        PageInfo = pageInfo ?? new PageAnalysis { PageNumber = pageNumber },
                        MatchReasons = new List<string> { $"Contains {wordsInPage.Count} matching word(s): {string.Join(", ", wordsInPage.Select(w => w.Word).Distinct())}" }
                    };
                })
                .OrderBy(p => p.PageNumber)
                .ToList();

            Console.WriteLine($"🖼️  Iniciando extração PNG para {pagesWithWords.Count} página(s) com palavras encontradas...");
            Console.WriteLine();
            
            try 
            {
                // Use the existing OptimizedPngExtractor to extract the filtered pages
                OptimizedPngExtractor.ExtractPagesAsPng(
                    pagesWithWords, 
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
        
        /// <summary>
        /// Build match reasons including signature information if signature filter is active
        /// </summary>
        private List<string> BuildMatchReasons(string baseReason, string pageText)
        {
            var reasons = new List<string> { baseReason };
            
            // Add signature information if signature filter is active
            // Since we only extract words from pages that passed the signature filter,
            // we can safely add the signature information for all words on filtered pages
            if (filterOptions.ContainsKey("--signature") || filterOptions.ContainsKey("-s"))
            {
                string signaturePattern = filterOptions.ContainsKey("--signature") ? 
                    filterOptions["--signature"] : filterOptions["-s"];
                
                reasons.Add($"Page contains signature: '{signaturePattern}'");
            }
            
            // DEBUG: Also check if any signature-related filter is present
            foreach (var filter in filterOptions.Keys)
            {
                if (filter.Contains("signature") || filter == "-s")
                {
                    reasons.Add($"DEBUG: Found signature filter: {filter}");
                    break;
                }
            }
            
            return reasons;
        }
        
        /// <summary>
        /// Check if a page contains signature patterns
        /// Uses the same logic as PageContainsSignature from FpdfPagesCommand
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
        
    }
}