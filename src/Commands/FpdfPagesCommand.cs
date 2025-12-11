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
using FilterPDF.Utils;
using FilterPDF.Services;
using FilterPDF.Commands;


namespace FilterPDF
{
    /// <summary>
    /// Filter Pages Command - Find pages that match specific criteria
    /// </summary>
    public class FpdfPagesCommand
    {
        public static void ShowHelp()
        {
            Console.WriteLine(LanguageManager.GetPagesHelp());
        }
        
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
            
            ExecutePagesSearch();
        }

        public void ExecuteFromPg(int processId, Dictionary<string, string> filters, Dictionary<string, string> outputs)
        {
            filterOptions = filters;
            outputOptions = outputs;

            var processes = PgAnalysisLoader.ListProcesses();
            var row = processes.FirstOrDefault(r => r.Id == processId);
            if (row == null)
            {
                Console.WriteLine($"Processo {processId} não encontrado no Postgres.");
                return;
            }

            var pages = PgAnalysisLoader.ListPages(processId);
            Console.WriteLine($"Finding PAGES in: {row.ProcessNumber} (Postgres)");
            ShowActiveFilters();
            Console.WriteLine();

            var matches = new List<PgAnalysisLoader.PageRow>();
            foreach (var p in pages)
            {
                if (PgPageMatchesFilters(p))
                    matches.Add(p);
            }

            if (matches.Count == 0)
            {
                Console.WriteLine("Nenhuma página encontrada com os filtros informados.");
                return;
            }

            foreach (var m in matches)
            {
                Console.WriteLine($"Página {m.PageNumber}");
                if (!string.IsNullOrWhiteSpace(m.Header)) Console.WriteLine($"  Header: {m.Header}");
                if (!string.IsNullOrWhiteSpace(m.Footer)) Console.WriteLine($"  Footer: {m.Footer}");
                var snippet = m.Text;
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    if (snippet.Length > 400) snippet = snippet.Substring(0, 400) + "...";
                    Console.WriteLine($"  Texto: {snippet}");
                }
                Console.WriteLine($"  Palavras: {m.Words}  Imagens: {m.ImageCount}  Fontes: {m.FontCount}  Anotações: {m.AnnotationCount}");
                Console.WriteLine();
            }
        }

        private bool PgPageMatchesFilters(PgAnalysisLoader.PageRow page)
        {
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--word":
                    case "-w":
                        if (!WordOption.Matches(page.Text ?? "", filter.Value))
                            return false;
                        break;
                    case "--not-words":
                        if (WordOption.Matches(page.Text ?? "", filter.Value))
                            return false;
                        break;
                    case "--regex":
                    case "-r":
                        if (!Regex.IsMatch(page.Text ?? "", filter.Value, RegexOptions.IgnoreCase))
                            return false;
                        break;
                    case "--page":
                    case "-p":
                        if (int.TryParse(filter.Value, out var pnum) && page.PageNumber != pnum)
                            return false;
                        break;
                    case "--page-range":
                        var parts = filter.Value.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
                        {
                            if (page.PageNumber < a || page.PageNumber > b) return false;
                        }
                        break;
                    case "--min-words":
                        if (page.Words < int.Parse(filter.Value)) return false;
                        break;
                    case "--max-words":
                        if (page.Words > int.Parse(filter.Value)) return false;
                        break;
                    case "--image":
                    case "-i":
                        {
                            var expected = bool.Parse(filter.Value);
                            if ((page.ImageCount > 0) != expected) return false;
                            break;
                        }
                    case "--annotations":
                    case "-a":
                        {
                            var expected = bool.Parse(filter.Value);
                            if ((page.AnnotationCount > 0) != expected) return false;
                            break;
                        }
                    case "--signature":
                    case "-s":
                        if (!PageContainsSignature(page.Text ?? "", filter.Value)) return false;
                        break;
                }
            }
            return true;
        }
        
        private void ExecutePagesSearch()
        {
            Console.WriteLine($"Finding PAGES in: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            var foundPages = new List<PageMatch>();
            // REMOVIDO: PdfReader não é mais usado - sempre cache!
            
            try
            {
                foreach (var page in analysisResult.Pages)
                {
                    if (PageMatchesAllFilters(page))
                    {
                        var pageMatch = new PageMatch 
                        { 
                            PageNumber = page.PageNumber,
                            PageInfo = page,
                            MatchReasons = GetPageMatchReasons(page)
                        };
                        
                        // Objects só do cache - sem PdfReader
                        if (outputOptions.ContainsKey("--objects"))
                        {
                            pageMatch.RelatedObjects = GetPageRelatedObjectsFromCache(page);
                        }
                        
                        foundPages.Add(pageMatch);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            OutputPageResults(foundPages);
        }
        
        private List<ObjectMatch> GetPageRelatedObjectsFromCache(PageAnalysis pageInfo)
        {
            // Retornar lista vazia - cache não tem objetos internos detalhados de páginas
            return new List<ObjectMatch>();
        }
        
        private bool PageMatchesAllFilters(PageAnalysis page)
        {
            // Verificar filtros de localização (página específica, range, etc.)
            if (!PageMatchesLocationFilters(page))
                return false;
            
            // Verificar outros filtros específicos
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--word":
                    case "-w":
                        if (page.TextInfo?.PageText == null || !WordOption.Matches(page.TextInfo.PageText, filter.Value))
                            return false;
                        break;
                        
                    case "--not-words":
                        if (page.TextInfo?.PageText != null && WordOption.Matches(page.TextInfo.PageText, filter.Value))
                            return false;
                        break;
                        
                    case "--regex":
                    case "-r":
                        if (!PageMatchesRegex(page, filter.Value))
                            return false;
                        break;
                        
                    case "--font":
                    case "-f":
                        if (!PageUsesFont(page, filter.Value))
                            return false;
                        break;
                        
                    case "--image":
                    case "-i":
                        bool expectedImages = bool.Parse(filter.Value);
                        if ((page.Resources.Images.Count > 0) != expectedImages)
                            return false;
                        break;
                        
                    case "--value":
                    case "-v":
                        if (!PageContainsCurrencyValue(page))
                            return false;
                        break;
                        
                    case "--min-words":
                        int minWords = int.Parse(filter.Value);
                        if (page.TextInfo == null || page.TextInfo.WordCount < minWords)
                            return false;
                        break;
                        
                    case "--max-words":
                        int maxWords = int.Parse(filter.Value);
                        if (page.TextInfo == null || page.TextInfo.WordCount > maxWords)
                            return false;
                        break;
                        
                    case "--orientation":
                    case "-or":
                        string expectedOrientation = filter.Value.ToLower();
                        string actualOrientation = page.Size.Width > page.Size.Height ? "landscape" : "portrait";
                        if (actualOrientation != expectedOrientation)
                            return false;
                        break;
                        
                    case "--blank":
                        if (page.TextInfo != null && page.TextInfo.WordCount > 10) // Não é página em branco
                            return false;
                        break;
                        
                    case "--annotations":
                    case "-a":
                        bool expectedAnnotations = bool.Parse(filter.Value);
                        if ((page.Annotations.Count > 0) != expectedAnnotations)
                            return false;
                        break;
                        
                    case "--tables":
                        bool expectedTables = bool.Parse(filter.Value);
                        if (page.TextInfo == null || page.TextInfo.HasTables != expectedTables)
                            return false;
                        break;
                        
                    case "--columns":
                        bool expectedColumns = bool.Parse(filter.Value);
                        if (page.TextInfo == null || page.TextInfo.HasColumns != expectedColumns)
                            return false;
                        break;
                        
                    case "--signature":
                    case "-s":
                        if (page.TextInfo?.PageText == null || !PageContainsSignature(page.TextInfo.PageText, filter.Value))
                            return false;
                        break;
                        
                    case "--font-bold":
                        bool expectBold = ParseBooleanValue(filter.Value);
                        bool hasBold = page.TextInfo?.Fonts?.Any(f => f.IsBold) ?? false;
                        if (hasBold != expectBold)
                            return false;
                        break;
                        
                    case "--font-italic":
                        bool expectItalic = ParseBooleanValue(filter.Value);
                        bool hasItalic = page.TextInfo?.Fonts?.Any(f => f.IsItalic) ?? false;
                        if (hasItalic != expectItalic)
                            return false;
                        break;
                        
                    case "--font-mono":
                        bool expectMono = ParseBooleanValue(filter.Value);
                        bool hasMono = page.TextInfo?.Fonts?.Any(f => f.IsMonospace) ?? false;
                        if (hasMono != expectMono)
                            return false;
                        break;
                        
                    case "--font-serif":
                        bool expectSerif = ParseBooleanValue(filter.Value);
                        bool hasSerif = page.TextInfo?.Fonts?.Any(f => f.IsSerif) ?? false;
                        if (hasSerif != expectSerif)
                            return false;
                        break;
                        
                    case "--font-sans":
                        bool expectSans = ParseBooleanValue(filter.Value);
                        bool hasSans = page.TextInfo?.Fonts?.Any(f => f.IsSansSerif) ?? false;
                        if (hasSans != expectSans)
                            return false;
                        break;
                        
                    // PAGE SIZE FILTERS
                    case "--paper-size":
                        string expectedPaperSize = filter.Value.ToUpper();
                        string actualPaperSize = page.Size.GetPaperSize().ToUpper();
                        if (actualPaperSize != expectedPaperSize)
                            return false;
                        break;
                        
                    case "--width":
                        double expectedWidth = double.Parse(filter.Value);
                        if (Math.Abs(page.Size.Width - expectedWidth) > 1.0) // Tolerance of 1 point
                            return false;
                        break;
                        
                    case "--height":
                        double expectedHeight = double.Parse(filter.Value);
                        if (Math.Abs(page.Size.Height - expectedHeight) > 1.0) // Tolerance of 1 point
                            return false;
                        break;
                        
                    case "--min-width":
                        double minWidth = double.Parse(filter.Value);
                        if (page.Size.Width < minWidth)
                            return false;
                        break;
                        
                    case "--max-width":
                        double maxWidth = double.Parse(filter.Value);
                        if (page.Size.Width > maxWidth)
                            return false;
                        break;
                        
                    case "--min-height":
                        double minHeight = double.Parse(filter.Value);
                        if (page.Size.Height < minHeight)
                            return false;
                        break;
                        
                    case "--max-height":
                        double maxHeight = double.Parse(filter.Value);
                        if (page.Size.Height > maxHeight)
                            return false;
                        break;
                        
                    // FILE SIZE FILTERS
                    case "--min-size-mb":
                        double minSizeMB = double.Parse(filter.Value);
                        if (page.FileSizeMB < minSizeMB)
                            return false;
                        break;
                        
                    case "--max-size-mb":
                        double maxSizeMB = double.Parse(filter.Value);
                        if (page.FileSizeMB > maxSizeMB)
                            return false;
                        break;
                        
                    case "--min-size-kb":
                        double minSizeKB = double.Parse(filter.Value);
                        if (page.FileSizeKB < minSizeKB)
                            return false;
                        break;
                        
                    case "--max-size-kb":
                        double maxSizeKB = double.Parse(filter.Value);
                        if (page.FileSizeKB > maxSizeKB)
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
                    case "-p":
                    case "--page":
                        // -p e --page são para busca de texto (pode ter & ou |)
                        if (page.TextInfo?.PageText == null || !WordOption.Matches(page.TextInfo.PageText, filter.Value))
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
                        
                    case "--page-ranges":
                    case "--pr":
                        // Novo PageRangeParser com sintaxe qpdf
                        int totalPagesCount = analysisResult.Pages.Count;
                        var validPages = PageRangeParser.Parse(filter.Value, totalPagesCount);
                        if (!validPages.Contains(page.PageNumber))
                            return false;
                        break;
                        
                    // NOTE: --first and --last are now handled AFTER sorting in OutputPageResults
                    // They are no longer filtered here to allow proper sorting
                    case "--first":
                    case "--last":
                        // Skip these - they'll be applied after sorting
                        break;
                }
            }
            return true;
        }
        
        private bool ParseBooleanValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return value.ToLower() == "true" || value == "1" || value.ToLower() == "yes";
        }
        
        private bool MatchesWildcard(string text, string pattern)
        {
            // Se não tem wildcards, busca simples case-insensitive
            if (!pattern.Contains("*") && !pattern.Contains("?"))
            {
                return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            
            // Para wildcards, precisamos buscar palavra por palavra
            string[] words = text.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '-', '(', ')', '[', ']', '{', '}', '"', '\'' }, 
                                      StringSplitOptions.RemoveEmptyEntries);
            
            foreach (string word in words)
            {
                if (MatchesPattern(word, pattern))
                    return true;
            }
            
            return false;
        }
        
        private bool MatchesPattern(string word, string pattern)
        {
            // Case-insensitive
            word = word.ToLower();
            pattern = pattern.ToLower();
            
            int wordPos = 0;
            int patternPos = 0;
            
            while (wordPos < word.Length && patternPos < pattern.Length)
            {
                char p = pattern[patternPos];
                
                if (p == '*')
                {
                    // * pode ser 0 ou mais caracteres
                    // Consumir todos os * consecutivos
                    while (patternPos < pattern.Length && pattern[patternPos] == '*')
                        patternPos++;
                    
                    // Se * é o último caractere, match
                    if (patternPos >= pattern.Length)
                        return true;
                    
                    // Procurar o próximo caractere do padrão na palavra
                    while (wordPos < word.Length)
                    {
                        if (MatchesPattern(word.Substring(wordPos), pattern.Substring(patternPos)))
                            return true;
                        wordPos++;
                    }
                    return false;
                }
                else if (p == '?')
                {
                    // ? é exatamente um caractere qualquer
                    wordPos++;
                    patternPos++;
                }
                else
                {
                    // Caractere literal deve coincidir
                    if (word[wordPos] != p)
                        return false;
                    wordPos++;
                    patternPos++;
                }
            }
            
            // Verificar se processamos todo o padrão e toda a palavra
            // Permitir * no final do padrão
            while (patternPos < pattern.Length && pattern[patternPos] == '*')
                patternPos++;
                
            return wordPos == word.Length && patternPos == pattern.Length;
        }
        
        private bool ContainsTextFuzzy(string pageText, string searchText)
        {
            // Busca normal primeiro
            if (pageText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
                
            // Se não encontrar, tenta busca com espaços entre caracteres
            // Criar padrão regex que permite espaços opcionais entre cada caractere
            var chars = searchText.ToCharArray();
            var patternParts = new List<string>();
            
            foreach (char c in chars)
            {
                if (char.IsLetterOrDigit(c))
                {
                    patternParts.Add(Regex.Escape(c.ToString()));
                }
                else
                {
                    patternParts.Add(Regex.Escape(c.ToString()));
                }
            }
            
            // Juntar com \s* (zero ou mais espaços) entre cada caractere
            string fuzzyPattern = string.Join(@"\s*", patternParts);
            var fuzzyRegex = new Regex(fuzzyPattern, RegexOptions.IgnoreCase);
            
            return fuzzyRegex.IsMatch(pageText);
        }
        
        private string CreateFuzzyWildcardPattern(string wildcardText)
        {
            var result = new StringBuilder();
            bool inWildcard = false;
            
            for (int i = 0; i < wildcardText.Length; i++)
            {
                char c = wildcardText[i];
                
                if (c == '*')
                {
                    result.Append(".*");
                    inWildcard = true;
                }
                else if (c == '?')
                {
                    result.Append(".");
                    inWildcard = true;
                }
                else
                {
                    // Add optional spaces before non-wildcard characters (except after wildcards)
                    if (i > 0 && !inWildcard && char.IsLetterOrDigit(c))
                    {
                        result.Append(@"\s*");
                    }
                    result.Append(Regex.Escape(c.ToString()));
                    inWildcard = false;
                }
            }
            
            return result.ToString();
        }
        
        private bool PageMatchesRegex(PageAnalysis page, string pattern)
        {
            if (page.TextInfo?.PageText == null)
                return false;
                
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                return regex.IsMatch(page.TextInfo.PageText);
            }
            catch
            {
                return false;
            }
        }
        
        private bool PageContainsCurrencyValue(PageAnalysis page)
        {
            if (page.TextInfo?.PageText == null)
                return false;
                
            return BrazilianCurrencyDetector.ContainsBrazilianCurrency(page.TextInfo.PageText);
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
        
        private bool PageUsesFont(PageAnalysis page, string fontPattern)
        {
            if (page.FontInfo == null || page.FontInfo.Count == 0)
                return false;
            
            // Check for OR operator
            if (fontPattern.Contains("|"))
            {
                // Multiple fonts with OR logic
                string[] fonts = fontPattern.Split('|');
                foreach (string font in fonts)
                {
                    string trimmedFont = font.Trim();
                    if (string.IsNullOrEmpty(trimmedFont))
                        continue;
                        
                    if (PageUsesSingleFont(page, trimmedFont))
                        return true;
                }
                return false;
            }
            // Check for AND operator
            else if (fontPattern.Contains("&"))
            {
                // Multiple fonts with AND logic
                string[] fonts = fontPattern.Split('&');
                foreach (string font in fonts)
                {
                    string trimmedFont = font.Trim();
                    if (string.IsNullOrEmpty(trimmedFont))
                        continue;
                        
                    if (!PageUsesSingleFont(page, trimmedFont))
                        return false;
                }
                return true;
            }
            else
            {
                // Single font
                return PageUsesSingleFont(page, fontPattern);
            }
        }
        
        private bool PageUsesSingleFont(PageAnalysis page, string fontPattern)
        {
            foreach (var font in page.FontInfo)
            {
                if (fontPattern.Contains("*") || fontPattern.Contains("?"))
                {
                    // Wildcard matching usando método simples
                    if (MatchesWildcard(font.Name, fontPattern))
                        return true;
                }
                else
                {
                    // Exact matching
                    if (font.Name.IndexOf(fontPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            
            return false;
        }
        
        private List<string> GetPageMatchReasons(PageAnalysis page)
        {
            var reasons = new List<string>();
            
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--word":
                    case "-w":
                        if (page.TextInfo?.PageText != null && WordOption.Matches(page.TextInfo.PageText, filter.Value))
                        {
                            // Mostrar detalhes específicos do filtro de palavras
                            if (filter.Value.Contains("&"))
                            {
                                string[] words = filter.Value.Split('&');
                                reasons.Add($"Contains all words: {string.Join(" AND ", words.Select(w => $"'{w.Trim()}'"))}");
                            }
                            else if (filter.Value.Contains("|"))
                            {
                                string[] words = filter.Value.Split('|');
                                reasons.Add($"Contains any word: {string.Join(" OR ", words.Select(w => $"'{w.Trim()}'"))}");
                            }
                            else
                            {
                                reasons.Add($"Contains text: '{filter.Value}'");
                            }
                        }
                        break;
                        
                    case "--regex":
                    case "-r":
                        if (PageMatchesRegex(page, filter.Value))
                        {
                            reasons.Add($"Matches regex: '{filter.Value}'");
                        }
                        break;
                        
                    case "--font":
                    case "-f":
                        if (PageUsesFont(page, filter.Value))
                        {
                            reasons.Add($"Uses font: '{filter.Value}'");
                        }
                        break;
                        
                    case "--image":
                    case "-i":
                        reasons.Add($"Has images: {filter.Value}");
                        break;
                        
                    case "--value":
                    case "-v":
                        var values = BrazilianCurrencyDetector.ExtractCurrencyValues(page.TextInfo?.PageText ?? "");
                        if (values.Count > 0)
                        {
                            reasons.Add($"Contains {values.Count} currency value(s): {string.Join(", ", values.Select(v => v.FormattedValue))}");
                        }
                        break;
                        
                    case "--min-words":
                        reasons.Add($"Has at least {filter.Value} words ({page.TextInfo?.WordCount ?? 0})");
                        break;
                        
                    case "--max-words":
                        reasons.Add($"Has at most {filter.Value} words ({page.TextInfo?.WordCount ?? 0})");
                        break;
                        
                    case "--orientation":
                    case "-or":
                        reasons.Add($"Orientation: {filter.Value}");
                        break;
                        
                    case "--blank":
                        reasons.Add("Is blank page");
                        break;
                        
                    case "--annotations":
                    case "-a":
                        reasons.Add($"Has annotations: {filter.Value}");
                        break;
                        
                    case "--tables":
                        reasons.Add($"Has tables: {filter.Value}");
                        break;
                        
                    case "--columns":
                        reasons.Add($"Has columns: {filter.Value}");
                        break;
                        
                    case "--signature":
                    case "-s":
                        if (filter.Value.Contains("&"))
                        {
                            string[] terms = filter.Value.Split('&');
                            reasons.Add($"Contains all signatures: {string.Join(" AND ", terms.Select(t => $"'{t.Trim()}'" ))}");
                        }
                        else if (filter.Value.Contains("|"))
                        {
                            string[] terms = filter.Value.Split('|');
                            reasons.Add($"Contains any signature: {string.Join(" OR ", terms.Select(t => $"'{t.Trim()}'" ))}");
                        }
                        else
                        {
                            reasons.Add($"Contains signature: '{filter.Value}'");
                        }
                        break;
                        
                    // PAGE SIZE MATCH REASONS
                    case "--paper-size":
                        reasons.Add($"Paper size: {filter.Value}");
                        break;
                        
                    case "--width":
                        reasons.Add($"Width: {filter.Value} points (actual: {page.Size.Width:F1})");
                        break;
                        
                    case "--height":
                        reasons.Add($"Height: {filter.Value} points (actual: {page.Size.Height:F1})");
                        break;
                        
                    case "--min-width":
                        reasons.Add($"Width ≥ {filter.Value} points (actual: {page.Size.Width:F1})");
                        break;
                        
                    case "--max-width":
                        reasons.Add($"Width ≤ {filter.Value} points (actual: {page.Size.Width:F1})");
                        break;
                        
                    case "--min-height":
                        reasons.Add($"Height ≥ {filter.Value} points (actual: {page.Size.Height:F1})");
                        break;
                        
                    case "--max-height":
                        reasons.Add($"Height ≤ {filter.Value} points (actual: {page.Size.Height:F1})");
                        break;
                        
                    // FILE SIZE MATCH REASONS
                    case "--min-size-mb":
                        reasons.Add($"File size ≥ {filter.Value} MB (actual: {page.FileSizeMB:F3} MB)");
                        break;
                        
                    case "--max-size-mb":
                        reasons.Add($"File size ≤ {filter.Value} MB (actual: {page.FileSizeMB:F3} MB)");
                        break;
                        
                    case "--min-size-kb":
                        reasons.Add($"File size ≥ {filter.Value} KB (actual: {page.FileSizeKB:F2} KB)");
                        break;
                        
                    case "--max-size-kb":
                        reasons.Add($"File size ≤ {filter.Value} KB (actual: {page.FileSizeKB:F2} KB)");
                        break;
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
        
        private void OutputPageResults(List<PageMatch> foundPages)
        {
            // Apply sorting if requested
            if (filterOptions.ContainsKey("--sort-by-size"))
            {
                // Sort by file size (if available) or by area (width * height)
                foundPages = foundPages.OrderByDescending(p => 
                    p.PageInfo.FileSizeBytes > 0 ? p.PageInfo.FileSizeBytes : 
                    (long)(p.PageInfo.Size.Width * p.PageInfo.Size.Height)).ToList();
            }
            else if (filterOptions.ContainsKey("--sort-by-dimensions"))
            {
                // Sort by area (width * height)
                foundPages = foundPages.OrderByDescending(p => 
                    p.PageInfo.Size.Width * p.PageInfo.Size.Height).ToList();
            }
            else if (filterOptions.ContainsKey("--sort-by-words"))
            {
                // Sort by word count
                foundPages = foundPages.OrderByDescending(p => 
                    p.PageInfo.TextInfo.WordCount).ToList();
            }
            
            // Apply --first and --last AFTER sorting
            if (filterOptions.ContainsKey("--first"))
            {
                int firstPages = int.Parse(filterOptions["--first"]);
                foundPages = foundPages.Take(firstPages).ToList();
            }
            else if (filterOptions.ContainsKey("--last"))
            {
                int lastPages = int.Parse(filterOptions["--last"]);
                foundPages = foundPages.TakeLast(lastPages).ToList();
            }
            
            // Usar FormatManager para determinar formato ANTES de criar OutputManager
            string format = FormatManager.ExtractFormat(outputOptions, "filter");
            
            // Para PNG, não usar OutputManager porque precisa salvar arquivos diretos
            if (format == "png")
            {
                // Handle empty results for PNG
                if (foundPages.Count == 0)
                {
                    Console.WriteLine("No pages found matching the specified criteria for PNG extraction.");
                    return;
                }
                
                // Call PNG extraction directly without OutputManager
                OutputPagesAsPng(foundPages);
                return;
            }
            
            // Para outros formatos, usar OutputManager normalmente
            using (var outputManager = new OutputManager(outputOptions))
            {
                // Check for unique option first
                if (outputOptions.ContainsKey("--unique") || outputOptions.ContainsKey("-u"))
                {
                    OutputPagesUniqueCharacteristics(foundPages);
                    return;
                }
                
                // Handle empty results based on format
                if (foundPages.Count == 0)
                {
                    switch (format)
                    {
                        case "json":
                            OutputPagesAsJson(foundPages); // JSON will handle empty list
                            break;
                        case "xml":
                            OutputPagesAsXml(foundPages);
                            break;
                        case "csv":
                            OutputPagesAsCsv(foundPages);
                            break;
                        case "md":
                            OutputPagesAsMarkdown(foundPages);
                            break;
                        case "raw":
                            OutputPagesAsRaw(foundPages);
                            break;
                        case "count":
                            Console.WriteLine(foundPages.Count);
                            break;
                        case "ocr":
                            OutputPagesWithOCR(foundPages);
                            break;
                        default:
                            Console.WriteLine("No pages found matching the specified criteria.");
                            break;
                    }
                    outputManager.Flush();
                    return;
                }
                
                switch (format)
                {
                    case "json":
                        OutputPagesAsJson(foundPages);
                        break;
                    case "xml":
                        OutputPagesAsXml(foundPages);
                        break;
                    case "csv":
                        OutputPagesAsCsv(foundPages);
                        break;
                    case "md":
                        OutputPagesAsMarkdown(foundPages);
                        break;
                    case "raw":
                        OutputPagesAsRaw(foundPages);
                        break;
                    case "count":
                        Console.WriteLine(foundPages.Count);
                        break;
                    case "ocr":
                        OutputPagesWithOCR(foundPages);
                        break;
                    default:
                        OutputPagesAsText(foundPages);
                        break;
                }
                
                outputManager.Flush();
            }
        }
        
        private void OutputPagesAsText(List<PageMatch> foundPages)
        {
            Console.WriteLine($"Found {foundPages.Count} page(s):");
            
            // Show sorting information if applicable
            if (filterOptions.ContainsKey("--sort-by-size"))
            {
                Console.WriteLine("(Sorted by size - largest first)");
            }
            else if (filterOptions.ContainsKey("--sort-by-dimensions"))
            {
                Console.WriteLine("(Sorted by dimensions - largest area first)");
            }
            else if (filterOptions.ContainsKey("--sort-by-words"))
            {
                Console.WriteLine("(Sorted by word count - most words first)");
            }
            
            Console.WriteLine();
            
            foreach (var match in foundPages)
            {
                Console.WriteLine($"PAGE {match.PageNumber}:");
                
                // Size information
                Console.WriteLine($"  Size: {match.PageInfo.Size.Width:F1} x {match.PageInfo.Size.Height:F1} points");
                if (match.PageInfo.Size.WidthInches > 0 && match.PageInfo.Size.HeightInches > 0)
                {
                    Console.WriteLine($"  Size (inches): {match.PageInfo.Size.WidthInches:F2} x {match.PageInfo.Size.HeightInches:F2}");
                    Console.WriteLine($"  Size (mm): {match.PageInfo.Size.WidthMM:F1} x {match.PageInfo.Size.HeightMM:F1}");
                }
                
                // Paper size detection
                string paperSize = match.PageInfo.Size.GetPaperSize();
                if (!string.IsNullOrEmpty(paperSize))
                {
                    Console.WriteLine($"  Paper size: {paperSize}");
                }
                
                // Rotation
                if (match.PageInfo.Rotation != 0)
                {
                    Console.WriteLine($"  Rotation: {match.PageInfo.Rotation}°");
                }
                
                // Text information
                Console.WriteLine($"  Words: {match.PageInfo.TextInfo?.WordCount ?? 0:N0}");
                Console.WriteLine($"  Characters: {match.PageInfo.TextInfo?.CharacterCount ?? 0:N0}");
                Console.WriteLine($"  Lines: {match.PageInfo.TextInfo?.LineCount ?? 0:N0}");
                
                // Tables and columns
                Console.WriteLine($"  Has tables: {(match.PageInfo.TextInfo?.HasTables ?? false ? "Yes" : "No")}");
                Console.WriteLine($"  Has columns: {(match.PageInfo.TextInfo?.HasColumns ?? false ? "Yes" : "No")}");
                
                // Languages
                if (match.PageInfo.TextInfo?.Languages != null && match.PageInfo.TextInfo.Languages.Count > 0)
                {
                    Console.WriteLine($"  Languages: {string.Join(", ", match.PageInfo.TextInfo.Languages)}");
                }
                
                // Resources
                Console.WriteLine($"  Images: {match.PageInfo.Resources.Images.Count}");
                if (match.PageInfo.Resources.Images.Count > 0)
                {
                    foreach (var img in match.PageInfo.Resources.Images)
                    {
                        Console.WriteLine($"    - {img.Name ?? "Image"}: {img.Width}x{img.Height}, {img.ColorSpace}, {img.CompressionType}");
                    }
                }
                
                // Fonts
                if (match.PageInfo.FontInfo != null && match.PageInfo.FontInfo.Count > 0)
                {
                    Console.WriteLine($"  Fonts: {match.PageInfo.FontInfo.Count}");
                    foreach (var font in match.PageInfo.FontInfo)
                    {
                        string styles = "";
                        if (font.IsBold) styles += " Bold";
                        if (font.IsItalic) styles += " Italic";
                        if (font.IsMonospace) styles += " Monospace";
                        if (font.IsSerif) styles += " Serif";
                        if (font.IsSansSerif) styles += " Sans-serif";
                        if (string.IsNullOrEmpty(styles)) styles = " Regular";
                        
                        Console.WriteLine($"    - {font.BaseFont} ({font.FontType}){styles}{(font.IsEmbedded ? " [embedded]" : "")}");
                    }
                }
                
                // Forms and multimedia
                Console.WriteLine($"  Has forms: {(match.PageInfo.Resources.HasForms ? "Yes" : "No")}");
                if (match.PageInfo.Resources.FormFields != null && match.PageInfo.Resources.FormFields.Count > 0)
                {
                    Console.WriteLine($"  Form fields: {match.PageInfo.Resources.FormFields.Count}");
                    foreach (var field in match.PageInfo.Resources.FormFields)
                    {
                        Console.WriteLine($"    - {field.Name} ({field.Type})");
                    }
                }
                
                Console.WriteLine($"  Has multimedia: {(match.PageInfo.Resources.HasMultimedia ? "Yes" : "No")}");
                
                // Annotations
                Console.WriteLine($"  Annotations: {match.PageInfo.Annotations.Count}");
                if (match.PageInfo.Annotations.Count > 0)
                {
                    foreach (var annot in match.PageInfo.Annotations)
                    {
                        Console.WriteLine($"    - {annot.Type}: {annot.Contents?.Substring(0, Math.Min(50, annot.Contents?.Length ?? 0))}");
                    }
                }
                
                
                // Document references
                if (match.PageInfo.DocumentReferences != null && match.PageInfo.DocumentReferences.Count > 0)
                {
                    Console.WriteLine($"  Document references: {match.PageInfo.DocumentReferences.Count}");
                    foreach (var docRef in match.PageInfo.DocumentReferences)
                    {
                        Console.WriteLine($"    - {docRef}");
                    }
                }
                
                if (match.MatchReasons.Count > 0)
                {
                    Console.WriteLine("  Match reasons:");
                    foreach (var reason in match.MatchReasons)
                    {
                        Console.WriteLine($"    - {reason}");
                    }
                }
                
                // Mostrar o texto da página original
                if (!string.IsNullOrEmpty(match.PageInfo.TextInfo?.PageText))
                {
                    Console.WriteLine("  Text content:");
                    string pageText = match.PageInfo.TextInfo.PageText;
                    
                    // Destacar palavras buscadas se houver filtro de texto
                    if (filterOptions.ContainsKey("-w") || filterOptions.ContainsKey("--word"))
                    {
                        string searchWord = filterOptions.ContainsKey("-w") ? filterOptions["-w"] : filterOptions["--word"];
                        pageText = HighlightSearchTerms(pageText, searchWord);
                    }
                    
                    // Dividir em linhas para melhor formatação
                    string[] lines = pageText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            Console.WriteLine($"    {line.Trim()}");
                        }
                    }
                }
                
                Console.WriteLine();
            }
        }
        
        private void OutputPagesAsJson(List<PageMatch> foundPages)
        {
            // Formato igual ao documents - simples e consistente
            bool noWords = filterOptions.ContainsKey("--no-words");

            var output = new
            {
                arquivo = System.IO.Path.GetFileName(analysisResult.FilePath),
                paginasEncontradas = foundPages.Count,
                paginas = foundPages.Select(p => {
                    var pageObj = new Dictionary<string, object>
                    {
                        ["pageNumber"] = p.PageNumber,
                        ["content"] = CleanTextForReading(p.PageInfo.TextInfo.PageText ?? "")
                    };
                    
                    // ALWAYS add information from active filters
                    if (filterOptions.ContainsKey("-or") || filterOptions.ContainsKey("--orientation"))
                    {
                        string orientation = p.PageInfo.Size.Width > p.PageInfo.Size.Height ? "landscape" : "portrait";
                        pageObj["orientation"] = orientation;
                    }
                    
                    if (filterOptions.ContainsKey("-w") || filterOptions.ContainsKey("--word"))
                    {
                        pageObj["searchedWords"] = filterOptions.ContainsKey("-w") ? filterOptions["-w"] : filterOptions["--word"];
                    }
                    
                    if (filterOptions.ContainsKey("--not-words"))
                    {
                        pageObj["excludedWords"] = filterOptions["--not-words"];
                    }
                    
                    if (filterOptions.ContainsKey("-f") || filterOptions.ContainsKey("--font"))
                    {
                        pageObj["searchedFont"] = filterOptions.ContainsKey("-f") ? filterOptions["-f"] : filterOptions["--font"];
                        pageObj["fontsFound"] = p.PageInfo.TextInfo.Fonts.Select(f => f.Name).Distinct().ToArray();
                    }
                    
                    if (filterOptions.ContainsKey("-v") || filterOptions.ContainsKey("--value") || filterOptions.ContainsKey("value"))
                    {
                        pageObj["monetaryValues"] = true;
                    }
                    
                    if (filterOptions.ContainsKey("-i") || filterOptions.ContainsKey("--image"))
                    {
                        pageObj["hasImages"] = p.PageInfo.Resources.Images.Count > 0;
                        pageObj["imageCount"] = p.PageInfo.Resources.Images.Count;
                    }
                    
                    if (filterOptions.ContainsKey("-a") || filterOptions.ContainsKey("--annotations"))
                    {
                        pageObj["hasAnnotations"] = p.PageInfo.Annotations.Count > 0;
                        pageObj["annotationCount"] = p.PageInfo.Annotations.Count;
                    }
                    
                    if (filterOptions.ContainsKey("--min-words"))
                    {
                        pageObj["wordCount"] = p.PageInfo.TextInfo.WordCount;
                        pageObj["minWords"] = filterOptions["--min-words"];
                    }
                    
                    if (filterOptions.ContainsKey("--max-words"))
                    {
                        pageObj["wordCount"] = p.PageInfo.TextInfo.WordCount;
                        pageObj["maxWords"] = filterOptions["--max-words"];
                    }
                    
                    if (filterOptions.ContainsKey("--tables"))
                    {
                        pageObj["hasTables"] = p.PageInfo.TextInfo.HasTables;
                    }
                    
                    if (filterOptions.ContainsKey("--columns"))
                    {
                        pageObj["hasColumns"] = p.PageInfo.TextInfo.HasColumns;
                    }
                    
                    if (filterOptions.ContainsKey("--signature") || filterOptions.ContainsKey("-s"))
                    {
                        string signatureFilter = filterOptions.ContainsKey("--signature") ? filterOptions["--signature"] : filterOptions["-s"];
                        pageObj["searchedSignature"] = signatureFilter;
                        pageObj["hasSignaturePatterns"] = true;
                    }
                    
                    // PAGE SIZE INFORMATION
                    if (filterOptions.ContainsKey("--paper-size"))
                    {
                        pageObj["paperSize"] = p.PageInfo.Size.GetPaperSize();
                        pageObj["searchedPaperSize"] = filterOptions["--paper-size"];
                    }
                    
                    if (filterOptions.ContainsKey("--width") || filterOptions.ContainsKey("--min-width") || filterOptions.ContainsKey("--max-width"))
                    {
                        pageObj["width"] = p.PageInfo.Size.Width;
                        pageObj["widthInches"] = p.PageInfo.Size.WidthInches;
                        pageObj["widthMM"] = p.PageInfo.Size.WidthMM;
                    }
                    
                    if (filterOptions.ContainsKey("--height") || filterOptions.ContainsKey("--min-height") || filterOptions.ContainsKey("--max-height"))
                    {
                        pageObj["height"] = p.PageInfo.Size.Height;
                        pageObj["heightInches"] = p.PageInfo.Size.HeightInches;
                        pageObj["heightMM"] = p.PageInfo.Size.HeightMM;
                    }
                    
                    if (filterOptions.ContainsKey("--width"))
                    {
                        pageObj["searchedWidth"] = filterOptions["--width"];
                    }
                    
                    if (filterOptions.ContainsKey("--height"))
                    {
                        pageObj["searchedHeight"] = filterOptions["--height"];
                    }
                    
                    // FILE SIZE INFORMATION
                    if (filterOptions.ContainsKey("--min-size-mb") || filterOptions.ContainsKey("--max-size-mb"))
                    {
                        pageObj["fileSizeMB"] = Math.Round(p.PageInfo.FileSizeMB, 3);
                    }
                    
                    if (filterOptions.ContainsKey("--min-size-kb") || filterOptions.ContainsKey("--max-size-kb"))
                    {
                        pageObj["fileSizeKB"] = Math.Round(p.PageInfo.FileSizeKB, 2);
                    }
                    
                    if (filterOptions.ContainsKey("--min-size-mb"))
                    {
                        pageObj["searchedMinSizeMB"] = filterOptions["--min-size-mb"];
                    }
                    
                    if (filterOptions.ContainsKey("--max-size-mb"))
                    {
                        pageObj["searchedMaxSizeMB"] = filterOptions["--max-size-mb"];
                    }
                    
                    if (filterOptions.ContainsKey("--min-size-kb"))
                    {
                        pageObj["searchedMinSizeKB"] = filterOptions["--min-size-kb"];
                    }
                    
                    if (filterOptions.ContainsKey("--max-size-kb"))
                    {
                        pageObj["searchedMaxSizeKB"] = filterOptions["--max-size-kb"];
                    }

                    // Adicionar tokens/palavras com bbox (útil para campos)
                    if (!noWords && p.PageInfo.TextInfo.Words != null && p.PageInfo.TextInfo.Words.Count > 0)
                    {
                        pageObj["words"] = p.PageInfo.TextInfo.Words.Select(w => new {
                            w.Text,
                            w.Font,
                            w.Size,
                            w.Bold,
                            w.Italic,
                            w.Underline,
                            w.RenderMode,
                            w.CharSpacing,
                            w.WordSpacing,
                            w.HorizontalScaling,
                            w.Rise,
                            w.X0,
                            w.Y0,
                            w.X1,
                            w.Y1,
                            w.NormX0,
                            w.NormY0,
                            w.NormX1,
                            w.NormY1
                        }).ToList();
                    }
                    
                    return pageObj;
                }).ToArray()
            };
            
            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            Console.WriteLine(json);
        }
        
        private string[] GetBookmarksForPage(int pageNumber, List<BookmarkItem> bookmarks)
        {
            var result = new List<string>();
            
            foreach (var bookmark in bookmarks)
            {
                if (bookmark.Destination.PageNumber == pageNumber)
                {
                    result.Add(bookmark.Title);
                }
                
                // Buscar recursivamente nos filhos
                result.AddRange(GetBookmarksForPage(pageNumber, bookmark.Children));
            }
            
            return result.ToArray();
        }
        
        private void OutputPagesAsXml(List<PageMatch> foundPages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<pages>");
            sb.AppendLine($"  <totalPages>{foundPages.Count}</totalPages>");
            
            foreach (var match in foundPages)
            {
                sb.AppendLine($"  <page number=\"{match.PageNumber}\">");
                sb.AppendLine($"    <size width=\"{match.PageInfo.Size.Width:F1}\" height=\"{match.PageInfo.Size.Height:F1}\" />");
                sb.AppendLine($"    <fileSize bytes=\"{match.PageInfo.FileSizeBytes}\" kb=\"{match.PageInfo.FileSizeKB:F2}\" mb=\"{match.PageInfo.FileSizeMB:F3}\" />");
                sb.AppendLine($"    <textInfo>");
                sb.AppendLine($"      <wordCount>{match.PageInfo.TextInfo.WordCount}</wordCount>");
                sb.AppendLine($"      <characterCount>{match.PageInfo.TextInfo.CharacterCount}</characterCount>");
                sb.AppendLine($"      <hasImages>{match.PageInfo.Resources.Images.Count > 0}</hasImages>");
                sb.AppendLine($"    </textInfo>");
                sb.AppendLine($"    <annotations>{match.PageInfo.Annotations.Count}</annotations>");
                
                if (match.MatchReasons.Count > 0)
                {
                    sb.AppendLine("    <matchReasons>");
                    foreach (var reason in match.MatchReasons)
                    {
                        sb.AppendLine($"      <reason>{System.Security.SecurityElement.Escape(reason)}</reason>");
                    }
                    sb.AppendLine("    </matchReasons>");
                }
                
                sb.AppendLine("  </page>");
            }
            
            sb.AppendLine("</pages>");
            Console.WriteLine(sb.ToString());
        }
        
        private void OutputPagesAsCsv(List<PageMatch> foundPages)
        {
            Console.WriteLine("PageNumber,Width,Height,WordCount,CharacterCount,HasImages,Annotations,MatchReasons");
            
            foreach (var match in foundPages)
            {
                string reasons = string.Join("; ", match.MatchReasons).Replace("\"", "\"\"");
                Console.WriteLine($"{match.PageNumber}," +
                    $"{match.PageInfo.Size.Width:F1}," +
                    $"{match.PageInfo.Size.Height:F1}," +
                    $"{match.PageInfo.TextInfo.WordCount}," +
                    $"{match.PageInfo.TextInfo.CharacterCount}," +
                    $"{match.PageInfo.Resources.Images.Count > 0}," +
                    $"{match.PageInfo.Annotations.Count}," +
                    $"\"{reasons}\"");
            }
        }
        
        private void OutputPagesAsMarkdown(List<PageMatch> foundPages)
        {
            Console.WriteLine($"# Found Pages ({foundPages.Count})");
            Console.WriteLine();
            
            foreach (var match in foundPages)
            {
                Console.WriteLine($"## Page {match.PageNumber}");
                Console.WriteLine();
                Console.WriteLine($"- **Size:** {match.PageInfo.Size.Width:F1} x {match.PageInfo.Size.Height:F1}");
                Console.WriteLine($"- **Words:** {match.PageInfo.TextInfo.WordCount:N0}");
                Console.WriteLine($"- **Characters:** {match.PageInfo.TextInfo.CharacterCount:N0}");
                Console.WriteLine($"- **Images:** {(match.PageInfo.Resources.Images.Count > 0 ? "Yes" : "No")}");
                Console.WriteLine($"- **Annotations:** {match.PageInfo.Annotations.Count}");
                
                if (match.MatchReasons.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("**Match Reasons:**");
                    foreach (var reason in match.MatchReasons)
                    {
                        Console.WriteLine($"- {reason}");
                    }
                }
                
                Console.WriteLine();
            }
        }
        
        private void OutputPagesAsRaw(List<PageMatch> foundPages)
        {
            foreach (var match in foundPages)
            {
                Console.WriteLine($"=== PAGE {match.PageNumber} ===");
                if (!string.IsNullOrEmpty(match.PageInfo.TextInfo?.PageText))
                {
                    Console.WriteLine(match.PageInfo.TextInfo.PageText);
                }
                Console.WriteLine();
            }
        }
        
        private void OutputPagesUniqueCharacteristics(List<PageMatch> foundPages)
        {
            Console.WriteLine($"Unique Characteristics Analysis");
            Console.WriteLine($"Total pages analyzed: {foundPages.Count}");
            Console.WriteLine($"".PadRight(80, '-'));
            Console.WriteLine();
            
            // Analyze all pages to find unique characteristics
            var allPages = foundPages.Select(p => p.PageInfo).ToList();
            
            // First, show summary of all characteristics analyzed
            ShowCharacteristicsSummary(foundPages);
            Console.WriteLine();
            Console.WriteLine($"".PadRight(80, '-'));
            Console.WriteLine();
            
            // Then show unique pages
            
            foreach (var match in foundPages)
            {
                var page = match.PageInfo;
                var uniqueFeatures = new List<string>();
                
                // Check for unique rotation
                var rotation = page.Rotation;
                if (rotation != 0 && allPages.Count(p => p.Rotation == rotation) == 1)
                {
                    uniqueFeatures.Add($"Rotated {rotation}°");
                }
                
                // Check for unique size
                var size = $"{page.Size.Width:F0}x{page.Size.Height:F0}";
                if (allPages.Count(p => $"{p.Size.Width:F0}x{p.Size.Height:F0}" == size) == 1)
                {
                    uniqueFeatures.Add($"Unique size: {size}");
                }
                
                // Check for unique orientation
                bool isLandscape = page.Size.Width > page.Size.Height;
                if (isLandscape && allPages.Count(p => p.Size.Width > p.Size.Height) == 1)
                {
                    uniqueFeatures.Add("Landscape orientation");
                }
                else if (!isLandscape && allPages.Count(p => p.Size.Width <= p.Size.Height) == 1)
                {
                    uniqueFeatures.Add("Only portrait page");
                }
                
                // Check for blank page
                if (page.TextInfo.WordCount == 0 && allPages.Count(p => p.TextInfo.WordCount == 0) == 1)
                {
                    uniqueFeatures.Add("Only blank page");
                }
                
                // Check for most/least words
                if (page.TextInfo.WordCount > 0)
                {
                    var maxWords = allPages.Max(p => p.TextInfo.WordCount);
                    var minWords = allPages.Where(p => p.TextInfo.WordCount > 0).Min(p => p.TextInfo.WordCount);
                    
                    if (page.TextInfo.WordCount == maxWords && allPages.Count(p => p.TextInfo.WordCount == maxWords) == 1)
                    {
                        uniqueFeatures.Add($"Maximum word count: {page.TextInfo.WordCount:N0}");
                    }
                    else if (page.TextInfo.WordCount == minWords && allPages.Count(p => p.TextInfo.WordCount == minWords) == 1)
                    {
                        uniqueFeatures.Add($"Minimum word count: {page.TextInfo.WordCount:N0}");
                    }
                }
                
                // Check for images
                var imageCount = page.Resources.Images.Count;
                if (imageCount > 0)
                {
                    if (allPages.Count(p => p.Resources.Images.Count > 0) == 1)
                    {
                        uniqueFeatures.Add($"Only page with images ({imageCount})");
                    }
                    else
                    {
                        var maxImages = allPages.Max(p => p.Resources.Images.Count);
                        if (imageCount == maxImages && allPages.Count(p => p.Resources.Images.Count == maxImages) == 1)
                        {
                            uniqueFeatures.Add($"Maximum image count: {imageCount}");
                        }
                    }
                }
                
                // Check for tables
                if (page.TextInfo.HasTables && allPages.Count(p => p.TextInfo.HasTables) == 1)
                {
                    uniqueFeatures.Add("Only page with tables");
                }
                
                // Check for columns
                if (page.TextInfo.HasColumns && allPages.Count(p => p.TextInfo.HasColumns) == 1)
                {
                    uniqueFeatures.Add("Only page with columns");
                }
                
                // Check for forms
                if (page.Resources.HasForms && allPages.Count(p => p.Resources.HasForms) == 1)
                {
                    uniqueFeatures.Add($"Only page with forms ({page.Resources.FormFields?.Count ?? 0} fields)");
                }
                
                // Check for annotations
                var annotCount = page.Annotations.Count;
                if (annotCount > 0)
                {
                    if (allPages.Count(p => p.Annotations.Count > 0) == 1)
                    {
                        uniqueFeatures.Add($"Only page with annotations ({annotCount})");
                    }
                    else
                    {
                        var maxAnnot = allPages.Max(p => p.Annotations.Count);
                        if (annotCount == maxAnnot && allPages.Count(p => p.Annotations.Count == maxAnnot) == 1)
                        {
                            uniqueFeatures.Add($"Maximum annotations: {annotCount}");
                        }
                    }
                }
                
                // Check for unique fonts
                if (page.FontInfo != null && page.FontInfo.Count > 0)
                {
                    var maxFonts = allPages.Max(p => p.FontInfo?.Count ?? 0);
                    if (page.FontInfo.Count == maxFonts && allPages.Count(p => (p.FontInfo?.Count ?? 0) == maxFonts) == 1)
                    {
                        uniqueFeatures.Add($"Most fonts ({page.FontInfo.Count})");
                    }
                }
                
                // Check for languages
                if (page.TextInfo.Languages != null && page.TextInfo.Languages.Count > 0)
                {
                    foreach (var lang in page.TextInfo.Languages)
                    {
                        if (allPages.Count(p => p.TextInfo.Languages != null && p.TextInfo.Languages.Contains(lang)) == 1)
                        {
                            uniqueFeatures.Add($"Only page in {lang}");
                        }
                    }
                }
                
                // Check for specific text patterns
                if (!string.IsNullOrEmpty(page.TextInfo?.PageText))
                {
                    var text = page.TextInfo.PageText.ToLower();
                    
                    // Check for CPF
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b"))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            System.Text.RegularExpressions.Regex.IsMatch(p.TextInfo.PageText, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b")) == 1)
                        {
                            uniqueFeatures.Add("CPF detected");
                        }
                    }
                    
                    // Check for CNPJ
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b"))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            System.Text.RegularExpressions.Regex.IsMatch(p.TextInfo.PageText, @"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b")) == 1)
                        {
                            uniqueFeatures.Add("CNPJ detected");
                        }
                    }
                    
                    // Check for Brazilian currency (R$)
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"r\$\s*\d{1,3}(?:\.\d{3})*(?:,\d{2})?", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            System.Text.RegularExpressions.Regex.IsMatch(p.TextInfo.PageText, @"r\$\s*\d{1,3}(?:\.\d{3})*(?:,\d{2})?", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) == 1)
                        {
                            uniqueFeatures.Add("Only page with R$ values");
                        }
                    }
                    
                    // Check for numbered headers (1. 1.1 1.1.1 etc)
                    var lines = page.TextInfo.PageText.Split('\n');
                    var hasNumberedHeaders = lines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^\d+(\.\d+)*\.?\s+[A-ZÀ-Ú]"));
                    if (hasNumberedHeaders)
                    {
                        if (allPages.Count(p => {
                            if (p.TextInfo?.PageText == null) return false;
                            var pLines = p.TextInfo.PageText.Split('\n');
                            return pLines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^\d+(\.\d+)*\.?\s+[A-ZÀ-Ú]"));
                        }) == 1)
                        {
                            uniqueFeatures.Add("Only page with numbered sections");
                        }
                    }
                    
                    // Check for email addresses
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b"))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            System.Text.RegularExpressions.Regex.IsMatch(p.TextInfo.PageText, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) == 1)
                        {
                            uniqueFeatures.Add("Only page with email");
                        }
                    }
                    
                    // Check for URLs
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"https?://"))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            System.Text.RegularExpressions.Regex.IsMatch(p.TextInfo.PageText, @"https?://", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) == 1)
                        {
                            uniqueFeatures.Add("Only page with URLs");
                        }
                    }
                    
                    // Check for phone numbers
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{2,4}[-.\s]?\d{3,4}[-.\s]?\d{3,4}\b"))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            System.Text.RegularExpressions.Regex.IsMatch(p.TextInfo.PageText, @"\b\d{2,4}[-.\s]?\d{3,4}[-.\s]?\d{3,4}\b")) == 1)
                        {
                            uniqueFeatures.Add("Only page with phone numbers");
                        }
                    }
                    
                    
                    // Check for dates
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b"))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            System.Text.RegularExpressions.Regex.IsMatch(p.TextInfo.PageText, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b")) == 1)
                        {
                            uniqueFeatures.Add("Only page with dates");
                        }
                    }
                    
                    // Check for signatures
                    if (text.Contains("assinatura") || text.Contains("signature") || text.Contains("_________"))
                    {
                        if (allPages.Count(p => p.TextInfo?.PageText != null && 
                            (p.TextInfo.PageText.ToLower().Contains("assinatura") || 
                             p.TextInfo.PageText.ToLower().Contains("signature") || 
                             p.TextInfo.PageText.Contains("_________"))) == 1)
                        {
                            uniqueFeatures.Add("Only page with signature fields");
                        }
                    }
                    
                    // Check for ALL CAPS text
                    var words = text.Split(new char[] { ' ', '\n', '\r', '\t' });
                    var capsWords = words.Where(w => w.Length > 3 && w == w.ToUpper() && System.Text.RegularExpressions.Regex.IsMatch(w, @"[A-Z]")).Count();
                    if (capsWords > 10)
                    {
                        if (allPages.Count(p => {
                            if (p.TextInfo?.PageText == null) return false;
                            var pWords = p.TextInfo.PageText.Split(new char[] { ' ', '\n', '\r', '\t' });
                            var pCapsWords = pWords.Where(w => w.Length > 3 && w == w.ToUpper() && System.Text.RegularExpressions.Regex.IsMatch(w, @"[A-Z]")).Count();
                            return pCapsWords > 10;
                        }) == 1)
                        {
                            uniqueFeatures.Add("Only page with excessive CAPS");
                        }
                    }
                }
                
                // Check for specific image types
                if (page.Resources.Images.Count > 0)
                {
                    var hasJPEG = page.Resources.Images.Any(img => img.CompressionType?.ToUpper().Contains("JPEG") ?? false);
                    var hasPNG = page.Resources.Images.Any(img => img.CompressionType?.ToUpper().Contains("PNG") ?? false);
                    
                    if (hasJPEG && allPages.Count(p => p.Resources.Images.Any(img => img.CompressionType?.ToUpper().Contains("JPEG") ?? false)) == 1)
                    {
                        uniqueFeatures.Add("Only page with JPEG images");
                    }
                    
                    if (hasPNG && allPages.Count(p => p.Resources.Images.Any(img => img.CompressionType?.ToUpper().Contains("PNG") ?? false)) == 1)
                    {
                        uniqueFeatures.Add("Only page with PNG images");
                    }
                }
                
                // Check for specific font characteristics
                if (page.FontInfo != null && page.FontInfo.Count > 0)
                {
                    var hasBold = page.FontInfo.Any(f => f.BaseFont?.ToLower().Contains("bold") ?? false);
                    var hasItalic = page.FontInfo.Any(f => f.BaseFont?.ToLower().Contains("italic") ?? false);
                    
                    if (hasBold && allPages.Count(p => p.FontInfo?.Any(f => f.BaseFont?.ToLower().Contains("bold") ?? false) ?? false) == 1)
                    {
                        uniqueFeatures.Add("Only page with bold fonts");
                    }
                    
                    if (hasItalic && allPages.Count(p => p.FontInfo?.Any(f => f.BaseFont?.ToLower().Contains("italic") ?? false) ?? false) == 1)
                    {
                        uniqueFeatures.Add("Only page with italic fonts");
                    }
                }
                
                
                // Check for specific annotation types
                if (page.Annotations.Count > 0)
                {
                    var hasHighlight = page.Annotations.Any(a => a.Type?.ToLower().Contains("highlight") ?? false);
                    var hasStamp = page.Annotations.Any(a => a.Type?.ToLower().Contains("stamp") ?? false);
                    
                    if (hasHighlight && allPages.Count(p => p.Annotations.Any(a => a.Type?.ToLower().Contains("highlight") ?? false)) == 1)
                    {
                        uniqueFeatures.Add("Only page with highlights");
                    }
                    
                    if (hasStamp && allPages.Count(p => p.Annotations.Any(a => a.Type?.ToLower().Contains("stamp") ?? false)) == 1)
                    {
                        uniqueFeatures.Add("Only page with stamps");
                    }
                }
                
                // Display results
                if (uniqueFeatures.Count > 0)
                {
                    Console.WriteLine($"PAGE {match.PageNumber}:");
                    foreach (var feature in uniqueFeatures)
                    {
                        Console.WriteLine($"  • {feature}");
                    }
                    Console.WriteLine();
                }
            }
            
            // Show pages with no unique features
            var pagesWithoutUnique = foundPages.Where(p => {
                var page = p.PageInfo;
                // Simplified check - if page has common characteristics
                return page.Rotation == 0 && 
                       page.TextInfo.WordCount > 0 &&
                       page.Resources.Images.Count == 0 &&
                       !page.TextInfo.HasTables &&
                       !page.Resources.HasForms &&
                       page.Annotations.Count == 0;
            }).ToList();
            
            if (pagesWithoutUnique.Count > 0)
            {
                Console.WriteLine($"Standard pages (no unique characteristics): {string.Join(", ", pagesWithoutUnique.Select(p => p.PageNumber))}");
            }
        }
        
        private void ShowCharacteristicsSummary(List<PageMatch> foundPages)
        {
            var allPages = foundPages.Select(p => p.PageInfo).ToList();
            
            Console.WriteLine("CHARACTERISTICS SUMMARY:");
            Console.WriteLine();
            
            // Document structure
            Console.WriteLine("Document Structure:");
            
            // Size analysis
            var sizeGroups = allPages.GroupBy(p => $"{p.Size.Width:F0}x{p.Size.Height:F0}").OrderByDescending(g => g.Count());
            Console.WriteLine($"  Page sizes: {sizeGroups.Count()} different");
            foreach (var size in sizeGroups)
            {
                var pages = string.Join(",", size.Select(p => allPages.IndexOf(p) + 1));
                Console.WriteLine($"    - {size.Key}: pages {pages}");
            }
            
            // Rotation
            var rotations = allPages.Where(p => p.Rotation != 0).ToList();
            if (rotations.Any())
            {
                Console.WriteLine($"  Rotated pages: {string.Join(",", rotations.Select(p => allPages.IndexOf(p) + 1))}");
            }
            
            // Orientation
            var landscape = allPages.Where(p => p.Size.Width > p.Size.Height).ToList();
            var portrait = allPages.Where(p => p.Size.Width <= p.Size.Height).ToList();
            Console.WriteLine($"  Orientation: {portrait.Count} portrait, {landscape.Count} landscape");
            
            Console.WriteLine();
            Console.WriteLine("Content Analysis:");
            
            // Word count
            var wordCounts = allPages.Select(p => new { Page = allPages.IndexOf(p) + 1, Words = p.TextInfo.WordCount }).OrderByDescending(p => p.Words);
            Console.WriteLine($"  Word count range: {wordCounts.Last().Words} - {wordCounts.First().Words}");
            Console.WriteLine($"    Maximum: page {wordCounts.First().Page} ({wordCounts.First().Words} words)");
            Console.WriteLine($"    Minimum: page {wordCounts.Last().Page} ({wordCounts.Last().Words} words)");
            
            // Blank pages
            var blankPages = allPages.Where(p => p.TextInfo.WordCount == 0).ToList();
            if (blankPages.Any())
            {
                Console.WriteLine($"  Blank pages: {string.Join(",", blankPages.Select(p => allPages.IndexOf(p) + 1))}");
            }
            
            // Images
            var pagesWithImages = allPages.Where(p => p.Resources.Images.Count > 0).ToList();
            if (pagesWithImages.Any())
            {
                var maxImages = pagesWithImages.Max(p => p.Resources.Images.Count);
                var pageWithMaxImages = pagesWithImages.First(p => p.Resources.Images.Count == maxImages);
                Console.WriteLine($"  Pages with images: {pagesWithImages.Count}");
                Console.WriteLine($"    Maximum: page {allPages.IndexOf(pageWithMaxImages) + 1} ({maxImages} images)");
            }
            
            // Tables and columns
            var tablesPages = allPages.Where(p => p.TextInfo.HasTables).ToList();
            var columnsPages = allPages.Where(p => p.TextInfo.HasColumns).ToList();
            if (tablesPages.Any()) Console.WriteLine($"  Pages with tables: {string.Join(",", tablesPages.Select(p => allPages.IndexOf(p) + 1))}");
            if (columnsPages.Any()) Console.WriteLine($"  Pages with columns: {string.Join(",", columnsPages.Select(p => allPages.IndexOf(p) + 1))}");
            
            // Forms
            var formsPages = allPages.Where(p => p.Resources.HasForms).ToList();
            if (formsPages.Any())
            {
                Console.WriteLine($"  Pages with forms: {string.Join(",", formsPages.Select(p => allPages.IndexOf(p) + 1))}");
            }
            
            // Annotations
            var annotPages = allPages.Where(p => p.Annotations.Count > 0).ToList();
            if (annotPages.Any())
            {
                var maxAnnot = annotPages.Max(p => p.Annotations.Count);
                var pageWithMaxAnnot = annotPages.First(p => p.Annotations.Count == maxAnnot);
                Console.WriteLine($"  Pages with annotations: {annotPages.Count}");
                Console.WriteLine($"    Maximum: page {allPages.IndexOf(pageWithMaxAnnot) + 1} ({maxAnnot} annotations)");
            }
            
            // Fonts
            var fontCounts = allPages.Where(p => p.FontInfo != null).Select(p => new { Page = allPages.IndexOf(p) + 1, Fonts = p.FontInfo.Count }).Where(p => p.Fonts > 0).OrderByDescending(p => p.Fonts);
            if (fontCounts.Any())
            {
                Console.WriteLine($"  Font variety range: {fontCounts.Last().Fonts} - {fontCounts.First().Fonts} fonts");
                Console.WriteLine($"    Maximum: page {fontCounts.First().Page} ({fontCounts.First().Fonts} fonts)");
            }
            
            // Languages
            var langPages = allPages.Where(p => p.TextInfo.Languages != null && p.TextInfo.Languages.Count > 0).ToList();
            if (langPages.Any())
            {
                var languages = langPages.SelectMany(p => p.TextInfo.Languages).Distinct();
                Console.WriteLine($"  Languages detected: {string.Join(", ", languages)}");
            }
            
            Console.WriteLine();
            Console.WriteLine("Brazilian Document Patterns:");
            
            // Check each page for patterns
            int cpfPages = 0, cnpjPages = 0, currencyPages = 0, numberedPages = 0;
            foreach (var page in allPages)
            {
                if (page.TextInfo?.PageText != null)
                {
                    var text = page.TextInfo.PageText;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b")) cpfPages++;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b")) cnpjPages++;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"r\$\s*\d{1,3}(?:\.\d{3})*(?:,\d{2})?", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) currencyPages++;
                    var lines = text.Split('\n');
                    if (lines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^\d+(\.\d+)*\.?\s+[A-ZÀ-Ú]"))) numberedPages++;
                }
            }
            
            if (cpfPages > 0) Console.WriteLine($"  CPF detected: {cpfPages} pages");
            if (cnpjPages > 0) Console.WriteLine($"  CNPJ detected: {cnpjPages} pages");
            if (currencyPages > 0) Console.WriteLine($"  Brazilian currency (R$): {currencyPages} pages");
            if (numberedPages > 0) Console.WriteLine($"  Hierarchical numbering: {numberedPages} pages");
            
            Console.WriteLine();
            Console.WriteLine("Other Patterns:");
            
            // Email, URLs, phones, dates
            int emailPages = 0, urlPages = 0, phonePages = 0, datePages = 0, signaturePages = 0;
            foreach (var page in allPages)
            {
                if (page.TextInfo?.PageText != null)
                {
                    var text = page.TextInfo.PageText.ToLower();
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) emailPages++;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"https?://")) urlPages++;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{2,4}[-.\s]?\d{3,4}[-.\s]?\d{3,4}\b")) phonePages++;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b")) datePages++;
                    if (text.Contains("assinatura") || text.Contains("signature") || text.Contains("_________")) signaturePages++;
                }
            }
            
            if (emailPages > 0) Console.WriteLine($"  Email addresses: {emailPages} pages");
            if (urlPages > 0) Console.WriteLine($"  URLs: {urlPages} pages");
            if (phonePages > 0) Console.WriteLine($"  Phone numbers: {phonePages} pages");
            if (datePages > 0) Console.WriteLine($"  Date patterns: {datePages} pages");
            if (signaturePages > 0) Console.WriteLine($"  Signature fields: {signaturePages} pages");
        }
        
        private string HighlightSearchTerms(string text, string searchTerms)
        {
            // Não modificar o texto original, apenas adicionar sublinhado
            if (string.IsNullOrEmpty(searchTerms))
                return text;
            
            // Processar termos com OR (|) e AND (&)
            if (searchTerms.Contains("|"))
            {
                // OR logic - destacar qualquer um dos termos
                string[] terms = searchTerms.Split('|');
                foreach (string term in terms)
                {
                    text = HighlightSingleTerm(text, term.Trim());
                }
            }
            else if (searchTerms.Contains("&"))
            {
                // AND logic - destacar todos os termos
                string[] terms = searchTerms.Split('&');
                foreach (string term in terms)
                {
                    text = HighlightSingleTerm(text, term.Trim());
                }
            }
            else
            {
                // Termo único
                text = HighlightSingleTerm(text, searchTerms);
            }
            
            return text;
        }
        
        private string HighlightSingleTerm(string text, string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return text;
            
            // Usar regex para encontrar o termo (case insensitive)
            // Adicionar sublinhado antes e depois da palavra encontrada
            string pattern = $@"\b{Regex.Escape(term)}\b";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            
            return regex.Replace(text, match => $"__{match.Value}__");
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
            
            return text.Trim();
        }

        private void OutputPagesWithOCR(List<PageMatch> foundPages)
        {
            Console.WriteLine($"🔍 Processando OCR para {foundPages.Count} página(s) encontrada(s)...");
            Console.WriteLine();

            foreach (var pageMatch in foundPages)
            {
                Console.WriteLine($"📄 Página {pageMatch.PageNumber}:");
                Console.WriteLine($"   Dimensões: {pageMatch.PageInfo.Size.Width:F1} x {pageMatch.PageInfo.Size.Height:F1} points");
                
                if (pageMatch.MatchReasons?.Any() == true)
                {
                    Console.WriteLine($"   Critérios atendidos: {string.Join(", ", pageMatch.MatchReasons)}");
                }
                
                Console.WriteLine();

                // Process OCR for this page
                var ocrResult = UniversalOCRService.ProcessPageWithOCR(inputFilePath, analysisResult, pageMatch.PageNumber);
                
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

            Console.WriteLine($"🎯 OCR concluído para {foundPages.Count} página(s)!");
        }
        
        private void OutputPagesAsPng(List<PageMatch> foundPages)
        {
            Console.WriteLine($"🖼️  Iniciando extração PNG para {foundPages.Count} página(s) filtrada(s)...");
            Console.WriteLine();
            
            try 
            {
                // Usar o OptimizedPngExtractor existente para extrair as páginas filtradas
                OptimizedPngExtractor.ExtractPagesAsPng(
                    foundPages, 
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
    }
}
