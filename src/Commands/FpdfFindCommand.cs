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
            string dbPath = Utils.SqliteCacheStore.DefaultDbPath;
            string cacheFilter = null;

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
            bool? hasMoney = null;
            bool? hasCpf = null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == "--db-path" && i + 1 < args.Length) { dbPath = args[++i]; continue; }
                if (a == "--cache" && i + 1 < args.Length) { cacheFilter = args[++i]; continue; }
                if (a == "--text" && i + 1 < args.Length)
                {
                    var t = args[++i];
                    terms.AddRange(t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    continue;
                }
                if (a == "--header" && i + 1 < args.Length) { headerTerms.Add(args[++i]); continue; }
                if (a == "--footer" && i + 1 < args.Length) { footerTerms.Add(args[++i]); continue; }
                if (a == "--docs" && i + 1 < args.Length) { docTerms.Add(args[++i]); continue; }
                if (a == "--meta" && i + 1 < args.Length) { metaTerms.Add(args[++i]); continue; }
                if (a == "--fonts" && i + 1 < args.Length) { fontTerms.Add(args[++i]); continue; }
                if (a == "--objects" && i + 1 < args.Length) { objectTerms.Add(args[++i]); continue; }
                if (a == "--has-money") { hasMoney = true; continue; }
                if (a == "--has-cpf") { hasCpf = true; continue; }
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

            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
            {
                try
                {
                    SearchInSqlite(dbPath,
                        terms, headerTerms, footerTerms,
                        docTerms, metaTerms, fontTerms, objectTerms,
                        pageRange, minWords, maxWords, typeFilter, limit, format, wantBBox, regexPattern,
                        hasMoney, hasCpf);
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
            public string Cache { get; set; }
            public int Page { get; set; }
            public string Scope { get; set; }
            public string Term { get; set; }
            public string Snippet { get; set; }
        }

        private void SearchInSqlite(string dbPath,
            List<string> terms, List<string> headerTerms, List<string> footerTerms,
            List<string> docTerms, List<string> metaTerms, List<string> fontTerms, List<string> objectTerms,
            string pageRange, int? minWords, int? maxWords, string typeFilter, int? limit, string format, bool wantBBox, string regexPattern,
            bool? hasMoney, bool? hasCpf)
        {
            Utils.SqliteCacheStore.EnsureDatabase(dbPath);
            var hits = new List<Hit>();
            using var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();

            var pageSet = BuildPageSet(pageRange, int.MaxValue); // sem total conhecido

            // Texto/header/footer
            if (terms.Count > 0 || regexPattern != null || headerTerms.Count > 0 || footerTerms.Count > 0)
            {
                bool ftsTried = false;

                if (regexPattern == null && headerTerms.Count == 0 && footerTerms.Count == 0 && terms.Count > 0)
                {
                    // Prefer FTS5 for plain term search
                    try
                    {
                        ftsTried = true;
                        var ftsQuery = string.Join(" AND ", terms.Select(t => t.Replace('"', ' ').Replace("'", " ")));
                        using var cmdFts = new System.Data.SQLite.SQLiteCommand("SELECT f.page_number, f.text, c.name FROM page_fts f JOIN caches c ON c.id = f.cache_id WHERE f MATCH @q", conn);
                        cmdFts.Parameters.AddWithValue("@q", ftsQuery);
                        using var reader = cmdFts.ExecuteReader();
                        while (reader.Read())
                        {
                            int pageNum = reader.GetInt32(0);
                            if (pageSet != null && !pageSet.Contains(pageNum)) continue;
                            string text = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            string cacheName = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            int wc = text.Split(new[]{' ','\n','\t'}, StringSplitOptions.RemoveEmptyEntries).Length;
                            if (minWords.HasValue && wc < minWords.Value) continue;
                            if (maxWords.HasValue && wc > maxWords.Value) continue;
                            bool textOk = MatchesTerms(text, terms, regexPattern);
                            if (textOk)
                                CollectTextHits(cacheName, pageNum, "text", text, terms, regexPattern, hits, limit);
                            if (limit.HasValue && hits.Count >= limit.Value) break;
                        }
                    }
                    catch
                    {
                        // fallback to scan
                    }
                }

                if (!ftsTried || (limit.HasValue && hits.Count < limit.Value))
                {
                    var sql = "SELECT p.page_number, p.text, p.has_money, p.has_cpf, p.fonts, c.name FROM pages p JOIN caches c ON c.id = p.cache_id";
                    using var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        int pageNum = reader.GetInt32(0);
                        if (pageSet != null && !pageSet.Contains(pageNum)) continue;
                        string text = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        int pageHasMoney = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        int pageHasCpf = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                        string pageFonts = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        string cacheName = reader.IsDBNull(5) ? "" : reader.GetString(5);

                        if (hasMoney == true && pageHasMoney == 0) continue;
                        if (hasCpf == true && pageHasCpf == 0) continue;

                        int wordCount = text.Split(new[]{' ','\n','\t'}, StringSplitOptions.RemoveEmptyEntries).Length;
                        if (minWords.HasValue && wordCount < minWords.Value) continue;
                        if (maxWords.HasValue && wordCount > maxWords.Value) continue;

                        var lines = text.Split('\n');
                        var header = string.Join("\n", lines.Take(5));
                        var footer = string.Join("\n", lines.Reverse().Take(5).Reverse());

                        bool headerOk = MatchesTerms(header, headerTerms, regexPattern);
                        bool footerOk = MatchesTerms(footer, footerTerms, regexPattern);
                        bool textOk = MatchesTerms(text, terms, regexPattern);

                        if (fontTerms.Count > 0)
                        {
                            bool fontOk = true;
                            foreach (var fterm in fontTerms)
                            {
                                var ors = fterm.Split('|', StringSplitOptions.RemoveEmptyEntries);
                                bool any = ors.Any(o => WordOption.Matches(pageFonts ?? "", o));
                                if (!any) { fontOk = false; break; }
                            }
                            if (!fontOk) continue;
                        }

                        // Aplica AND entre header/footer/text/fonts/money/cpf
                        if (headerOk && footerOk && textOk)
                        {
                            if (headerTerms.Count > 0) CollectTextHits(cacheName, pageNum, "header", header, headerTerms, regexPattern, hits, limit);
                            if (footerTerms.Count > 0) CollectTextHits(cacheName, pageNum, "footer", footer, footerTerms, regexPattern, hits, limit);
                            if (terms.Count > 0 || regexPattern != null || (headerTerms.Count == 0 && footerTerms.Count == 0))
                                CollectTextHits(cacheName, pageNum, "text", text, terms, regexPattern, hits, limit);
                        }
                        if (limit.HasValue && hits.Count >= limit.Value) break;
                    }
                }
            }

            // Docs
            if (docTerms.Count > 0)
            {
                using var cmd = new System.Data.SQLite.SQLiteCommand("SELECT name FROM caches", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string name = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    foreach (var t in docTerms)
                    {
                        if (WordOption.Matches(name, t))
                        {
                            hits.Add(new Hit { Cache = name, Page = 0, Scope = "docs", Term = t, Snippet = name });
                            break;
                        }
                    }
                    if (limit.HasValue && hits.Count >= limit.Value) break;
                }
            }

            // Meta/Fonts/Objects não persistidos no schema atual -> ignorar

            Emit(hits, format);
        }

        private static bool MatchesTerms(string text, List<string> terms, string regexPattern)
        {
            if (terms == null || terms.Count == 0)
            {
                if (regexPattern == null) return true;
                return Regex.IsMatch(text ?? string.Empty, regexPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            }

            foreach (var term in terms)
            {
                if (!WordOption.Matches(text ?? string.Empty, term))
                    return false;
            }

            if (regexPattern != null && !Regex.IsMatch(text ?? string.Empty, regexPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase))
                return false;

            return true;
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

        private static void CollectTextHits(string cache, int pageNumber, string scope, string text, List<string> terms, string regexPattern, List<Hit> hits, int? limit)
        {
            if (limit.HasValue && hits.Count >= limit.Value) return;
            if (string.IsNullOrEmpty(text)) return;

            if (!string.IsNullOrEmpty(regexPattern))
            {
                foreach (Match m in Regex.Matches(text, regexPattern, RegexOptions.IgnoreCase))
                {
                    hits.Add(new Hit { Cache = cache, Page = pageNumber, Scope = scope, Term = regexPattern, Snippet = ExtractContext(text, m.Index) });
                    if (limit.HasValue && hits.Count >= limit.Value) return;
                }
                return;
            }

            // AND implícito
            foreach (var term in terms)
                if (!WordOption.Matches(text, term)) return;

            hits.Add(new Hit { Cache = cache, Page = pageNumber, Scope = scope, Term = string.Join(" & ", terms), Snippet = ExtractContext(text, FirstTermIndex(text, terms)) });
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
            foreach (var raw in terms)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var (mode, clean) = WordOption.DetectMode(raw);
                int idx = mode == WordOption.MatchMode.Exact
                    ? text.IndexOf(clean)
                    : WordOption.NormalizeText(text).IndexOf(WordOption.NormalizeText(clean.Trim('~')));

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
