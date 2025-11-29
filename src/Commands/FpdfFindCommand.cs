using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Options;
using FilterPDF.Utils;

namespace FilterPDF
{
    /// <summary>
    /// Comando único de busca: fpdf find
    /// AND por espaço, OR com '|', sensível com '!'.
    /// Escopos: texto (default), header/footer, docs, meta, fonts, objects.
    /// Filtros: pages, min/max words, type, limit, formato.
    /// </summary>
    public class FpdfFindCommand
    {
        public void Execute(string inputFile, PDFAnalysisResult analysis, string[] args)
        {
            string dbPath = "data/sqlite/sqlite-mcp.db";

            var terms = new List<string>();
            var headerTerms = new List<string>();
            var footerTerms = new List<string>();
            var docTerms = new List<string>();
            var metaTerms = new List<string>();
            var fontTerms = new List<string>();
            var objectTerms = new List<string>();
            string pageRange = null;
            int? minWords = null, maxWords = null, limit = 200;
            string typeFilter = null;
            string format = "txt";
            bool wantBBox = false;
            string regexPattern = null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == "--db-path" && i + 1 < args.Length) { dbPath = args[++i]; continue; }
                if (a == "--header" && i + 1 < args.Length) { headerTerms.Add(args[++i]); continue; }
                if (a == "--footer" && i + 1 < args.Length) { footerTerms.Add(args[++i]); continue; }
                if (a == "--docs" && i + 1 < args.Length) { docTerms.Add(args[++i]); continue; }
                if (a == "--meta" && i + 1 < args.Length) { metaTerms.Add(args[++i]); continue; }
                if (a == "--fonts" && i + 1 < args.Length) { fontTerms.Add(args[++i]); continue; }
                if (a == "--objects" && i + 1 < args.Length) { objectTerms.Add(args[++i]); continue; }
                if ((a == "--pages" || a == "-p") && i + 1 < args.Length) { pageRange = args[++i]; continue; }
                if (a == "--min-words" && i + 1 < args.Length && int.TryParse(args[++i], out var mw)) { minWords = mw; continue; }
                if (a == "--max-words" && i + 1 < args.Length && int.TryParse(args[++i], out var xw)) { maxWords = xw; continue; }
                if (a == "--type" && i + 1 < args.Length) { typeFilter = args[++i]; continue; }
                if (a == "--limit" && i + 1 < args.Length && int.TryParse(args[++i], out var lm)) { limit = lm; continue; }
                if (a == "-F" && i + 1 < args.Length) { format = args[++i].ToLower(); continue; }
                if (a == "--bbox") { wantBBox = true; continue; }
                if (a == "--regex" && i + 1 < args.Length) { regexPattern = args[++i]; continue; }
                terms.Add(a);
            }

            var hits = new List<Hit>();

            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
            {
                try
                {
                    SearchInSqlite(dbPath, terms, headerTerms, footerTerms, docTerms, metaTerms, fontTerms, objectTerms, pageRange, minWords, maxWords, typeFilter, limit, format, wantBBox, regexPattern);
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"(WARN) Falha ao buscar no SQLite ({dbPath}): {ex.Message}");
                    return;
                }
            }
            else
            {
                Console.WriteLine($"Banco não encontrado em {dbPath}. Rode 'fpdf ingest-db .cache --db-path {dbPath}' primeiro.");
                return;
            }
        }

        private record Hit
        {
            public int Page { get; set; }
            public string Scope { get; set; }
            public string Term { get; set; }
            public string Snippet { get; set; }
        }

        private void SearchInSqlite(string dbPath,
            List<string> terms, List<string> headerTerms, List<string> footerTerms,
            List<string> docTerms, List<string> metaTerms, List<string> fontTerms, List<string> objectTerms,
            string pageRange, int? minWords, int? maxWords, string typeFilter, int? limit, string format, bool wantBBox, string regexPattern)
        {
            var hits = new List<Hit>();
            using var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();

            var pageSet = BuildPageSet(pageRange, int.MaxValue); // sem total conhecido

            // Texto/header/footer
            if (terms.Count > 0 || regexPattern != null || headerTerms.Count > 0 || footerTerms.Count > 0)
            {
                var sql = "SELECT p.page_number, p.text FROM pages p JOIN documents d ON d.id = p.document_id";
                if (!string.IsNullOrEmpty(typeFilter)) sql += " WHERE d.doc_type LIKE @type";
                using var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn);
                if (!string.IsNullOrEmpty(typeFilter)) cmd.Parameters.AddWithValue("@type", $"%{typeFilter}%");
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int pageNum = reader.GetInt32(0);
                    if (pageSet != null && !pageSet.Contains(pageNum)) continue;
                    string text = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    int wordCount = text.Split(new[]{' ','\n','\t'}, StringSplitOptions.RemoveEmptyEntries).Length;
                    if (minWords.HasValue && wordCount < minWords.Value) continue;
                    if (maxWords.HasValue && wordCount > maxWords.Value) continue;

                    var lines = text.Split('\n');
                    if (headerTerms.Count > 0)
                    {
                        var header = string.Join("\n", lines.Take(5));
                        CollectTextHits(pageNum, "header", header, headerTerms, regexPattern, hits, limit);
                    }
                    if (footerTerms.Count > 0)
                    {
                        var footer = string.Join("\n", lines.Reverse().Take(5).Reverse());
                        CollectTextHits(pageNum, "footer", footer, footerTerms, regexPattern, hits, limit);
                    }
                    if (terms.Count > 0 && headerTerms.Count == 0 && footerTerms.Count == 0)
                    {
                        CollectTextHits(pageNum, "text", text, terms, regexPattern, hits, limit);
                    }
                    if (limit.HasValue && hits.Count >= limit.Value) break;
                }
            }

            // Docs
            if (docTerms.Count > 0)
            {
                using var cmd = new System.Data.SQLite.SQLiteCommand("SELECT name, doc_type, start_page FROM documents", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string name = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    string dtype = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    int spage = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    if (!string.IsNullOrEmpty(typeFilter) && !dtype.ToLower().Contains(typeFilter.ToLower())) continue;
                    foreach (var t in docTerms)
                    {
                        if (WordOption.Matches(name, t))
                        {
                            hits.Add(new Hit { Page = spage, Scope = "docs", Term = t, Snippet = name });
                            break;
                        }
                    }
                    if (limit.HasValue && hits.Count >= limit.Value) break;
                }
            }

            // Meta/Fonts/Objects não persistidos no schema atual -> ignorar

            Emit(hits, format);
        }

        private static HashSet<int> BuildPageSet(string range, int totalPages)
        {
            if (string.IsNullOrWhiteSpace(range)) return null;
            var set = new HashSet<int>();
            foreach (var part in range.Split(','))
            {
                var p = part.Trim();
                if (p.Contains('-'))
                {
                    var seg = p.Split('-');
                    if (seg.Length == 2 && int.TryParse(seg[0], out var a) && int.TryParse(seg[1], out var b))
                    {
                        for (int i = Math.Max(1, a); i <= Math.Min(totalPages, b); i++) set.Add(i);
                    }
                }
                else if (int.TryParse(p, out var single))
                {
                    if (single >= 1 && single <= totalPages) set.Add(single);
                }
            }
            return set;
        }

        private static bool TypeMatches(string typeFilter, PageAnalysis page)
        {
            if (string.IsNullOrEmpty(typeFilter)) return true;
            var t = (page.DocType ?? "").ToLower();
            return t.Contains(typeFilter.ToLower());
        }

        private static void CollectTextHits(int pageNumber, string scope, string text, List<string> terms, string regexPattern, List<Hit> hits, int? limit)
        {
            if (limit.HasValue && hits.Count >= limit.Value) return;
            if (string.IsNullOrEmpty(text)) return;

            if (!string.IsNullOrEmpty(regexPattern))
            {
                foreach (Match m in Regex.Matches(text, regexPattern, RegexOptions.IgnoreCase))
                {
                    hits.Add(new Hit { Page = pageNumber, Scope = scope, Term = regexPattern, Snippet = ExtractContext(text, m.Index) });
                    if (limit.HasValue && hits.Count >= limit.Value) return;
                }
                return;
            }

            // AND implícito
            foreach (var term in terms)
                if (!WordOption.Matches(text, term)) return;

            hits.Add(new Hit { Page = pageNumber, Scope = scope, Term = string.Join(" & ", terms), Snippet = ExtractContext(text, FirstTermIndex(text, terms)) });
        }

        private static IEnumerable<Hit> SearchInDocNames(PDFAnalysisResult analysis, string term, HashSet<int> pageSet, string typeFilter, int? remaining)
        {
            var list = new List<Hit>();
            var docs = analysis.Documents ?? new List<DocumentInfo>();
            foreach (var d in docs)
            {
                if (!string.IsNullOrEmpty(typeFilter) && !(d.Type ?? "").ToLower().Contains(typeFilter.ToLower())) continue;
                if (!WordOption.Matches(d.Name ?? "", term)) continue;
                list.Add(new Hit { Page = d.PageNumber > 0 ? d.PageNumber : 0, Scope = "docs", Term = term, Snippet = d.Name });
                if (remaining.HasValue && list.Count >= remaining.Value) break;
            }
            return list;
        }

        private static string ExtractContext(string text, int index)
        {
            if (index < 0) index = 0;
            int start = Math.Max(0, index - 60);
            int len = Math.Min(120, text.Length - start);
            return text.Substring(start, len).Replace('\n', ' ').Trim();
        }

        private static int FirstTermIndex(string text, List<string> terms)
        {
            int best = text.Length;
            foreach (var t in terms)
            {
                var normalizedText = WordOption.NormalizeText(text);
                var normalizedTerm = WordOption.NormalizeText(t.Trim('!', '"', '\''));
                var idx = normalizedText.IndexOf(normalizedTerm);
                if (idx >= 0 && idx < best) best = idx;
            }
            return best == text.Length ? 0 : best;
        }

        private static void Emit(List<Hit> hits, string format)
        {
            if (format == "json")
            {
                Console.WriteLine(JsonConvert.SerializeObject(hits, Formatting.Indented));
                return;
            }
            if (format == "csv")
            {
                Console.WriteLine("page,scope,term,snippet");
                foreach (var h in hits)
                {
                    var snip = h.Snippet.Replace("\"", "\"\"");
                    Console.WriteLine($"{h.Page},{h.Scope},\"{h.Term.Replace('\"',' ')}\",\"{snip}\"");
                }
                return;
            }
            if (format == "count")
            {
                Console.WriteLine(hits.Count);
                return;
            }
            Console.WriteLine($"Hits: {hits.Count}\n");
            foreach (var h in hits)
            {
                Console.WriteLine($"[{h.Scope}] page {h.Page} :: {h.Snippet}");
            }
        }
    }
}
