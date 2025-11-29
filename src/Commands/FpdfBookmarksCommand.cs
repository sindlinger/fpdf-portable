using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Options;
using FilterPDF.Utils;
using FilterPDF.Commands;


namespace FilterPDF
{
    /// <summary>
    /// Filter Bookmarks Command - Find bookmarks that match specific criteria
    /// </summary>
    public class FpdfBookmarksCommand
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
            
            ExecuteBookmarksSearch();
        }
        
        private void ExecuteBookmarksSearch()
        {
            Console.WriteLine($"Finding BOOKMARKS in: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            var foundBookmarks = new List<BookmarkMatch>();
            
            if (analysisResult.Bookmarks?.RootItems != null)
            {
                var allBookmarks = GetAllBookmarksRecursive(analysisResult.Bookmarks.RootItems);
                
                foreach (var bookmark in allBookmarks)
                {
                    if (BookmarkMatchesAllFilters(bookmark))
                    {
                        var bookmarkMatch = new BookmarkMatch
                        {
                            Bookmark = bookmark,
                            MatchReasons = GetBookmarkMatchReasons(bookmark)
                        };
                        
                        // Objects só do cache - sem PdfReader
                        if (outputOptions.ContainsKey("--objects"))
                        {
                            bookmarkMatch.RelatedObjects = GetBookmarkRelatedObjectsFromCache(bookmark);
                        }
                        
                        foundBookmarks.Add(bookmarkMatch);
                    }
                }
            }
            
            OutputBookmarkResults(foundBookmarks);
        }
        
        private List<ObjectMatch> GetBookmarkRelatedObjectsFromCache(BookmarkItem bookmark)
        {
            // Retornar lista vazia - cache não tem objetos internos detalhados de bookmarks
            return new List<ObjectMatch>();
        }
        
        private List<BookmarkItem> GetAllBookmarksRecursive(List<BookmarkItem> items)
        {
            var allBookmarks = new List<BookmarkItem>();
            foreach (var item in items)
            {
                allBookmarks.Add(item);
                if (item.Children != null && item.Children.Count > 0)
                {
                    allBookmarks.AddRange(GetAllBookmarksRecursive(item.Children));
                }
            }
            return allBookmarks;
        }
        
        private bool BookmarkMatchesAllFilters(BookmarkItem bookmark)
        {
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--word":
                    case "-w":
                        if (!WordOption.Matches(bookmark.Title, filter.Value))
                            return false;
                        break;
                        
                    case "--not-words":
                        if (WordOption.Matches(bookmark.Title, filter.Value))
                            return false;
                        break;
                        
                    case "--regex":
                    case "-r":
                        var regex = new Regex(filter.Value, RegexOptions.IgnoreCase);
                        if (!regex.IsMatch(bookmark.Title))
                            return false;
                        break;
                        
                    case "--page":
                    case "-p":
                        int targetPage = int.Parse(filter.Value);
                        if (bookmark.Destination?.PageNumber != targetPage)
                            return false;
                        break;
                        
                    case "--page-range":
                        var range = filter.Value.Split('-');
                        if (range.Length == 2)
                        {
                            int start = int.Parse(range[0]);
                            int end = int.Parse(range[1]);
                            int bookmarkPage = bookmark.Destination?.PageNumber ?? 0;
                            if (bookmarkPage < start || bookmarkPage > end)
                                return false;
                        }
                        break;
                        
                    case "--level":
                        int targetLevel = int.Parse(filter.Value);
                        if (bookmark.Level != targetLevel)
                            return false;
                        break;
                        
                    case "--min-level":
                        int minLevel = int.Parse(filter.Value);
                        if (bookmark.Level < minLevel)
                            return false;
                        break;
                        
                    case "--max-level":
                        int maxLevel = int.Parse(filter.Value);
                        if (bookmark.Level > maxLevel)
                            return false;
                        break;
                        
                    case "--has-children":
                        bool expectedChildren = bool.Parse(filter.Value);
                        bool hasChildren = bookmark.Children != null && bookmark.Children.Count > 0;
                        if (hasChildren != expectedChildren)
                            return false;
                        break;
                        
                    case "--value":
                    case "-v":
                        if (!BrazilianCurrencyDetector.ContainsBrazilianCurrency(bookmark.Title))
                            return false;
                        break;
                        
                    case "--orientation":
                    case "-or":
                        int pageNum = bookmark.Destination?.PageNumber ?? 0;
                        if (pageNum > 0 && pageNum <= analysisResult.Pages.Count)
                        {
                            var page = analysisResult.Pages[pageNum - 1];
                            string pageOrientation = page.Size.Width > page.Size.Height ? "landscape" : "portrait";
                            if (!pageOrientation.Equals(filter.Value, StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                        else
                        {
                            return false; // Se não tem página válida, não pode filtrar por orientação
                        }
                        break;
                        
                    case "--signature":
                    case "-s":
                        int bookmarkPageNum = bookmark.Destination?.PageNumber ?? 0;
                        if (bookmarkPageNum > 0 && bookmarkPageNum <= analysisResult.Pages.Count)
                        {
                            var page = analysisResult.Pages[bookmarkPageNum - 1];
                            if (page.TextInfo?.PageText == null || !PageContainsSignature(page.TextInfo.PageText, filter.Value))
                                return false;
                        }
                        else
                        {
                            return false; // Se não tem página válida, não pode filtrar por assinatura
                        }
                        break;
                        
                }
            }
            
            return true;
        }
        
        private List<string> GetBookmarkMatchReasons(BookmarkItem bookmark)
        {
            var reasons = new List<string>();
            
            foreach (var filter in filterOptions)
            {
                switch (filter.Key)
                {
                    case "--word":
                    case "-w":
                        reasons.Add($"Contains {WordOption.GetSearchDescription(filter.Value)}");
                        break;
                        
                    case "--regex":
                    case "-r":
                        reasons.Add($"Matches regex: '{filter.Value}'");
                        break;
                        
                    case "--page":
                    case "-p":
                        reasons.Add($"On page: {filter.Value}");
                        break;
                        
                    case "--page-range":
                        reasons.Add($"In page range: {filter.Value}");
                        break;
                        
                    case "--level":
                        reasons.Add($"At level: {filter.Value}");
                        break;
                        
                    case "--min-level":
                        reasons.Add($"At or above level: {filter.Value}");
                        break;
                        
                    case "--max-level":
                        reasons.Add($"At or below level: {filter.Value}");
                        break;
                        
                    case "--has-children":
                        reasons.Add($"Has children: {filter.Value}");
                        break;
                        
                    case "--value":
                    case "-v":
                        reasons.Add("Contains Brazilian currency value");
                        break;
                        
                    case "--orientation":
                    case "-or":
                        reasons.Add($"Page orientation is {filter.Value}");
                        break;
                        
                    case "--signature":
                    case "-s":
                        if (filter.Value.Contains("&"))
                        {
                            string[] terms = filter.Value.Split('&');
                            reasons.Add($"Page contains all signatures: {string.Join(" AND ", terms.Select(t => $"'{t.Trim()}'"))}");
                        }
                        else if (filter.Value.Contains("|"))
                        {
                            string[] terms = filter.Value.Split('|');
                            reasons.Add($"Page contains any signature: {string.Join(" OR ", terms.Select(t => $"'{t.Trim()}'"))}");
                        }
                        else
                        {
                            reasons.Add($"Page contains signature: '{filter.Value}'");
                        }
                        break;
                }
            }
            
            if (reasons.Count == 0)
            {
                int pageNum = bookmark.Destination?.PageNumber ?? 0;
                reasons.Add($"Bookmark on page {pageNum}, Level: {bookmark.Level}");
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
            else
            {
                Console.WriteLine("No filters specified - showing all results");
                Console.WriteLine();
            }
        }
        
        private void OutputBookmarkResults(List<BookmarkMatch> foundBookmarks)
        {
            using (var outputManager = new OutputManager(outputOptions))
            {
                if (foundBookmarks.Count == 0)
                {
                    Console.WriteLine("No bookmarks found matching the specified criteria.");
                    return;
                }
                
                // Usar FormatManager para determinar formato
                string format = FormatManager.ExtractFormat(outputOptions, "filter");
                
                switch (format)
                {
                    case "count":
                        Console.WriteLine(foundBookmarks.Count);
                        break;
                    case "json":
                        OutputBookmarksAsJson(foundBookmarks);
                        break;
                    case "xml":
                        OutputBookmarksAsXml(foundBookmarks);
                        break;
                    case "csv":
                        OutputBookmarksAsCsv(foundBookmarks);
                        break;
                    case "md":
                        OutputBookmarksAsMarkdown(foundBookmarks);
                        break;
                    case "raw":
                        OutputBookmarksAsRaw(foundBookmarks);
                        break;
                    case "png":
                        OutputBookmarksAsPng(foundBookmarks);
                        break;
                    default:
                        OutputBookmarksAsText(foundBookmarks);
                        break;
                }
                
                outputManager.Flush();
            }
        }
        
        private void OutputBookmarksAsText(List<BookmarkMatch> foundBookmarks)
        {
            Console.WriteLine($"Found {foundBookmarks.Count} bookmark(s) matching criteria:");
            Console.WriteLine();
            
            foreach (var match in foundBookmarks)
            {
                string indent = new string(' ', match.Bookmark.Level * 2);
                Console.WriteLine($"{indent}BOOKMARK: \"{match.Bookmark.Title}\"");
                Console.WriteLine($"  Page: {match.Bookmark.Destination?.PageNumber ?? 0}");
                Console.WriteLine($"  Level: {match.Bookmark.Level}");
                
                if (match.Bookmark.Children != null && match.Bookmark.Children.Count > 0)
                {
                    Console.WriteLine($"  Children: {match.Bookmark.Children.Count}");
                }
                
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
        
        private void OutputBookmarksAsJson(List<BookmarkMatch> foundBookmarks)
        {
            var output = new
            {
                arquivo = System.IO.Path.GetFileName(analysisResult.FilePath),
                bookmarksEncontrados = foundBookmarks.Count,
                bookmarks = foundBookmarks.Select(b => {
                    var bookmarkObj = new Dictionary<string, object>
                    {
                        ["title"] = b.Bookmark.Title,
                        ["pageNumber"] = b.Bookmark.Destination?.PageNumber ?? 0,
                        ["level"] = b.Bookmark.Level,
                        ["hasChildren"] = b.Bookmark.Children != null && b.Bookmark.Children.Count > 0,
                        ["childrenCount"] = b.Bookmark.Children?.Count ?? 0,
                        ["matchReasons"] = b.MatchReasons,
                        ["relatedObjects"] = b.RelatedObjects?.Count ?? 0
                    };
                    
                    // ALWAYS add information from active filters
                    if (filterOptions.ContainsKey("-w") || filterOptions.ContainsKey("--word"))
                    {
                        bookmarkObj["searchedWords"] = filterOptions.ContainsKey("-w") ? filterOptions["-w"] : filterOptions["--word"];
                    }
                    
                    if (filterOptions.ContainsKey("-r") || filterOptions.ContainsKey("--regex"))
                    {
                        bookmarkObj["regexPattern"] = filterOptions.ContainsKey("-r") ? filterOptions["-r"] : filterOptions["--regex"];
                    }
                    
                    if (filterOptions.ContainsKey("-p") || filterOptions.ContainsKey("--page"))
                    {
                        bookmarkObj["searchedPage"] = filterOptions.ContainsKey("-p") ? filterOptions["-p"] : filterOptions["--page"];
                    }
                    
                    if (filterOptions.ContainsKey("--page-range"))
                    {
                        bookmarkObj["pageRange"] = filterOptions["--page-range"];
                    }
                    
                    if (filterOptions.ContainsKey("--level"))
                    {
                        bookmarkObj["searchedLevel"] = filterOptions["--level"];
                    }
                    
                    if (filterOptions.ContainsKey("--min-level"))
                    {
                        bookmarkObj["minLevel"] = filterOptions["--min-level"];
                    }
                    
                    if (filterOptions.ContainsKey("--max-level"))
                    {
                        bookmarkObj["maxLevel"] = filterOptions["--max-level"];
                    }
                    
                    if (filterOptions.ContainsKey("--has-children"))
                    {
                        bookmarkObj["searchedHasChildren"] = filterOptions["--has-children"];
                    }
                    
                    if (filterOptions.ContainsKey("-v") || filterOptions.ContainsKey("--value"))
                    {
                        bookmarkObj["monetaryValues"] = true;
                    }
                    
                    if (filterOptions.ContainsKey("-or") || filterOptions.ContainsKey("--orientation"))
                    {
                        // Para bookmarks, a orientação seria da página onde está o bookmark
                        int pageNum = b.Bookmark.Destination?.PageNumber ?? 0;
                        if (pageNum > 0 && pageNum <= analysisResult.Pages.Count)
                        {
                            var page = analysisResult.Pages[pageNum - 1];
                            string orientation = page.Size.Width > page.Size.Height ? "landscape" : "portrait";
                            bookmarkObj["orientation"] = orientation;
                        }
                    }
                    
                    return bookmarkObj;
                }).ToArray()
            };
            
            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            Console.WriteLine(json);
        }
        
        private void OutputBookmarksAsXml(List<BookmarkMatch> foundBookmarks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<bookmarks>");
            sb.AppendLine($"  <totalBookmarks>{foundBookmarks.Count}</totalBookmarks>");
            
            foreach (var match in foundBookmarks)
            {
                int pageNum = match.Bookmark.Destination?.PageNumber ?? 0;
                sb.AppendLine($"  <bookmark pageNumber=\"{pageNum}\" level=\"{match.Bookmark.Level}\">");
                sb.AppendLine($"    <title>{System.Security.SecurityElement.Escape(match.Bookmark.Title)}</title>");
                sb.AppendLine($"    <hasChildren>{match.Bookmark.Children != null && match.Bookmark.Children.Count > 0}</hasChildren>");
                sb.AppendLine($"    <childrenCount>{match.Bookmark.Children?.Count ?? 0}</childrenCount>");
                
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
                
                sb.AppendLine("  </bookmark>");
            }
            
            sb.AppendLine("</bookmarks>");
            Console.WriteLine(sb.ToString());
        }
        
        private void OutputBookmarksAsCsv(List<BookmarkMatch> foundBookmarks)
        {
            Console.WriteLine("Title,PageNumber,Level,HasChildren,ChildrenCount,MatchReasons,RelatedObjects");
            
            foreach (var match in foundBookmarks)
            {
                string title = match.Bookmark.Title.Replace("\"", "\"\"");
                bool hasChildren = match.Bookmark.Children != null && match.Bookmark.Children.Count > 0;
                int childrenCount = match.Bookmark.Children?.Count ?? 0;
                string reasons = string.Join("; ", match.MatchReasons).Replace("\"", "\"\"");
                int relatedObjects = match.RelatedObjects?.Count ?? 0;
                
                int pageNum = match.Bookmark.Destination?.PageNumber ?? 0;
                Console.WriteLine($"\"{title}\",{pageNum},{match.Bookmark.Level},{hasChildren},{childrenCount},\"{reasons}\",{relatedObjects}");
            }
        }
        
        private void OutputBookmarksAsMarkdown(List<BookmarkMatch> foundBookmarks)
        {
            Console.WriteLine($"# Found Bookmarks ({foundBookmarks.Count})");
            Console.WriteLine();
            
            foreach (var match in foundBookmarks)
            {
                string indent = new string('#', Math.Min(match.Bookmark.Level + 2, 6));
                Console.WriteLine($"{indent} {match.Bookmark.Title}");
                Console.WriteLine();
                Console.WriteLine($"- **Page:** {match.Bookmark.Destination?.PageNumber ?? 0}");
                Console.WriteLine($"- **Level:** {match.Bookmark.Level}");
                
                if (match.Bookmark.Children != null && match.Bookmark.Children.Count > 0)
                {
                    Console.WriteLine($"- **Children:** {match.Bookmark.Children.Count}");
                }
                
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
        
        private void OutputBookmarksAsRaw(List<BookmarkMatch> foundBookmarks)
        {
            foreach (var match in foundBookmarks)
            {
                Console.WriteLine(match.Bookmark.Title);
            }
        }
        
        private void OutputBookmarksAsPng(List<BookmarkMatch> foundBookmarks)
        {
            // Convert BookmarkMatch to PageMatch, avoiding duplicate pages
            var pagesWithBookmarks = foundBookmarks
                .Where(b => b.Bookmark.Destination?.PageNumber > 0) // Only bookmarks with valid page targets
                .GroupBy(b => b.Bookmark.Destination.PageNumber)
                .Select(group => 
                {
                    var pageNumber = group.Key;
                    var bookmarksInPage = group.ToList();
                    var pageInfo = analysisResult.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
                    
                    var bookmarkTitles = bookmarksInPage.Select(b => b.Bookmark.Title).Distinct();
                    var bookmarkLevels = bookmarksInPage.Select(b => b.Bookmark.Level).Distinct();
                    
                    return new PageMatch
                    {
                        PageNumber = pageNumber,
                        PageInfo = pageInfo ?? new PageAnalysis { PageNumber = pageNumber },
                        MatchReasons = new List<string> 
                        { 
                            $"Contains {bookmarksInPage.Count} bookmark(s): {string.Join(", ", bookmarkTitles)}",
                            $"Bookmark levels: {string.Join(", ", bookmarkLevels.OrderBy(l => l))}"
                        }
                    };
                })
                .OrderBy(p => p.PageNumber)
                .ToList();

            if (pagesWithBookmarks.Count == 0)
            {
                Console.WriteLine("⚠️  No bookmarks found with valid page targets for PNG extraction.");
                return;
            }

            Console.WriteLine($"🖼️  Iniciando extração PNG para {pagesWithBookmarks.Count} página(s) com bookmarks encontrados...");
            Console.WriteLine();
            
            try 
            {
                // Use the existing OptimizedPngExtractor to extract the filtered pages
                OptimizedPngExtractor.ExtractPagesAsPng(
                    pagesWithBookmarks, 
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
            Console.WriteLine(LanguageManager.GetBookmarksHelp());
        }
    }
}