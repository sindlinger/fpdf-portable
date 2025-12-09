using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Options;
using FilterPDF.Utils;
using Npgsql;

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
            string pgUri = Environment.GetEnvironmentVariable("FPDF_PG_URI");
            bool usePostgres = false;
            string cacheFilter = null;

            var terms = new List<string>();
            var headerTerms = new List<string>();
            var footerTerms = new List<string>();
            var docTerms = new List<string>();
            var metaTerms = new List<string>();
            var fontTerms = new List<string>();
            var objectTerms = new List<string>();
            var bookmarkTerms = new List<string>();
            string pageRange = null;
            int? minWords = null, maxWords = null, limit = 200;
            string typeFilter = null;
            string format = "txt";
            bool wantBBox = false;
            bool? hasMoney = null;
            bool? hasCpf = null;
            int? bookmarksMin = null, bookmarksMax = null;
            int? imagesMin = null, imagesMax = null;
            var metaTitle = new List<string>();
            var metaAuthor = new List<string>();
            var metaSubject = new List<string>();
            var metaKeywords = new List<string>();
            var metaCreator = new List<string>();
            var metaProducer = new List<string>();
            DateTime? createdAfter = null, createdBefore = null;
            int? pagesMin = null, pagesMax = null, fontsMin = null, fontsMax = null;
            bool? attachments = null, embedded = null, javascript = null, multimedia = null;
            bool? encrypted = null, canCopy = null, canPrint = null, canAnnotate = null, canFillForms = null, canExtract = null, canAssemble = null, canPrintHq = null;

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
                if (a == "--bookmark" && i + 1 < args.Length) { bookmarkTerms.Add(args[++i]); continue; }
                if (a == "--bookmarks-min" && i + 1 < args.Length && int.TryParse(args[++i], out var bmin)) { bookmarksMin = bmin; continue; }
                if (a == "--bookmarks-max" && i + 1 < args.Length && int.TryParse(args[++i], out var bmax)) { bookmarksMax = bmax; continue; }
                if (a == "--images-min" && i + 1 < args.Length && int.TryParse(args[++i], out var imin)) { imagesMin = imin; continue; }
                if (a == "--images-max" && i + 1 < args.Length && int.TryParse(args[++i], out var imax)) { imagesMax = imax; continue; }
                if (a == "--has-money") { hasMoney = true; continue; }
                if (a == "--has-cpf") { hasCpf = true; continue; }
                if (a == "--meta-title" && i + 1 < args.Length) { metaTitle.Add(args[++i]); continue; }
                if (a == "--meta-author" && i + 1 < args.Length) { metaAuthor.Add(args[++i]); continue; }
                if (a == "--meta-subject" && i + 1 < args.Length) { metaSubject.Add(args[++i]); continue; }
                if (a == "--meta-keywords" && i + 1 < args.Length) { metaKeywords.Add(args[++i]); continue; }
                if (a == "--meta-creator" && i + 1 < args.Length) { metaCreator.Add(args[++i]); continue; }
                if (a == "--meta-producer" && i + 1 < args.Length) { metaProducer.Add(args[++i]); continue; }
                if (a == "--created-after" && i + 1 < args.Length && DateTime.TryParse(args[++i], out var ca)) { createdAfter = ca; continue; }
                if (a == "--created-before" && i + 1 < args.Length && DateTime.TryParse(args[++i], out var cb)) { createdBefore = cb; continue; }
                if (a == "--pages-min" && i + 1 < args.Length && int.TryParse(args[++i], out var pgmin)) { pagesMin = pgmin; continue; }
                if (a == "--pages-max" && i + 1 < args.Length && int.TryParse(args[++i], out var pgmax)) { pagesMax = pgmax; continue; }
                if (a == "--fonts-min" && i + 1 < args.Length && int.TryParse(args[++i], out var fmin)) { fontsMin = fmin; continue; }
                if (a == "--fonts-max" && i + 1 < args.Length && int.TryParse(args[++i], out var fmax)) { fontsMax = fmax; continue; }
                if (a == "--attachments") { attachments = true; continue; }
                if (a == "--embedded") { embedded = true; continue; }
                if (a == "--javascript") { javascript = true; continue; }
                if (a == "--multimedia") { multimedia = true; continue; }
                if (a == "--encrypted") { encrypted = true; continue; }
                if (a == "--not-encrypted") { encrypted = false; continue; }
                if (a == "--can-copy") { canCopy = true; continue; }
                if (a == "--can-print") { canPrint = true; continue; }
                if (a == "--can-annotate") { canAnnotate = true; continue; }
                if (a == "--can-fill-forms") { canFillForms = true; continue; }
                if (a == "--can-extract") { canExtract = true; continue; }
                if (a == "--can-assemble") { canAssemble = true; continue; }
                if (a == "--can-print-hq") { canPrintHq = true; continue; }
                if ((a == "--pages" || a == "-p") && i + 1 < args.Length) { pageRange = args[++i]; continue; }
                if (a == "--min-words" && i + 1 < args.Length && int.TryParse(args[++i], out var mw)) { minWords = mw; continue; }
                if (a == "--max-words" && i + 1 < args.Length && int.TryParse(args[++i], out var xw)) { maxWords = xw; continue; }
                if (a == "--type" && i + 1 < args.Length) { typeFilter = args[++i]; continue; }
                if (a == "--limit" && i + 1 < args.Length && int.TryParse(args[++i], out var lm)) { limit = lm; continue; }
                if (a == "-F" && i + 1 < args.Length) { format = args[++i].ToLower(); continue; }
                if (a == "--bbox") { wantBBox = true; continue; }
                if (a == "--pg-uri" && i + 1 < args.Length) { pgUri = args[++i]; usePostgres = true; continue; }
                if (a == "--pg") { usePostgres = true; continue; }
                terms.Add(a);
            }

            if (usePostgres && string.IsNullOrWhiteSpace(pgUri))
                pgUri = Utils.PgDocStore.DefaultPgUri;

            if (usePostgres)
            {
                Console.WriteLine($"[INFO] Buscando no Postgres: {pgUri}");
                try
                {
                    SearchInPostgres(pgUri, terms, limit);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"(ERRO) Busca PG falhou: {ex.Message}");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(dbPath)) dbPath = Utils.SqliteCacheStore.DefaultDbPath;

            Console.WriteLine($"[INFO] Usando banco SQLite em: {dbPath}");

            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
            {
                try
                {
                    SearchInSqlite(dbPath,
                        terms, headerTerms, footerTerms,
                        docTerms, metaTerms, fontTerms, objectTerms,
                        pageRange, minWords, maxWords, typeFilter, limit, format, wantBBox,
                        hasMoney, hasCpf, bookmarkTerms, bookmarksMin, bookmarksMax, imagesMin, imagesMax,
                        metaTitle, metaAuthor, metaSubject, metaKeywords, metaCreator, metaProducer,
                        createdAfter, createdBefore,
                        pagesMin, pagesMax, fontsMin, fontsMax,
                        attachments, embedded, javascript, multimedia,
                        encrypted, canCopy, canPrint, canAnnotate, canFillForms, canExtract, canAssemble, canPrintHq);
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
            public CacheMetaSummary Meta { get; set; }
        }

        private record CacheMetaSummary
        {
            public string Title { get; set; }
            public string Author { get; set; }
            public string Subject { get; set; }
            public string Keywords { get; set; }
            public string Creator { get; set; }
            public string Producer { get; set; }
            public int? Pages { get; set; }
            public int? Images { get; set; }
            public int? Bookmarks { get; set; }
            public bool? Encrypted { get; set; }
        }

        private static HashSet<string>? BuildCacheFilter(string dbPath,
            List<string> bookmarkTerms, int? bookmarksMin, int? bookmarksMax, int? imagesMin, int? imagesMax,
            List<string> metaTitle, List<string> metaAuthor, List<string> metaSubject, List<string> metaKeywords, List<string> metaCreator, List<string> metaProducer,
            DateTime? createdAfter, DateTime? createdBefore,
            int? pagesMin, int? pagesMax, int? fontsMin, int? fontsMax,
            bool? attachments, bool? embedded, bool? javascript, bool? multimedia,
            bool? encrypted, bool? canCopy, bool? canPrint, bool? canAnnotate, bool? canFillForms, bool? canExtract, bool? canAssemble, bool? canPrintHq,
            out Dictionary<string, CacheMetaSummary> metaLookup)
        {
            metaLookup = new Dictionary<string, CacheMetaSummary>(StringComparer.OrdinalIgnoreCase);
            bool needFilter = (bookmarkTerms?.Count > 0) || bookmarksMin.HasValue || bookmarksMax.HasValue || imagesMin.HasValue || imagesMax.HasValue
                || metaTitle.Any() || metaAuthor.Any() || metaSubject.Any() || metaKeywords.Any() || metaCreator.Any() || metaProducer.Any()
                || createdAfter.HasValue || createdBefore.HasValue
                || pagesMin.HasValue || pagesMax.HasValue || fontsMin.HasValue || fontsMax.HasValue
                || attachments.HasValue || embedded.HasValue || javascript.HasValue || multimedia.HasValue
                || encrypted.HasValue || canCopy.HasValue || canPrint.HasValue || canAnnotate.HasValue || canFillForms.HasValue || canExtract.HasValue || canAssemble.HasValue || canPrintHq.HasValue;
            if (!needFilter) return null;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};");
            conn.Open();
            using var cmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT name, json, stat_bookmarks, stat_total_images, meta_title, meta_author, meta_subject, meta_keywords, meta_creator, meta_producer, meta_creation_date, doc_total_pages, stat_total_fonts, res_attachments, res_embedded_files, res_javascript, res_multimedia, sec_is_encrypted, sec_can_copy, sec_can_print, sec_can_annotate, sec_can_fill_forms, sec_can_extract, sec_can_assemble, sec_can_print_hq FROM caches", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string name = r.IsDBNull(0) ? "" : r.GetString(0);
                string json = r.IsDBNull(1) ? "" : r.GetString(1);
                int? bcount = r.IsDBNull(2) ? null : r.GetInt32(2);
                int? icount = r.IsDBNull(3) ? null : r.GetInt32(3);
                string mtit = r.IsDBNull(4) ? "" : r.GetString(4);
                string maut = r.IsDBNull(5) ? "" : r.GetString(5);
                string msub = r.IsDBNull(6) ? "" : r.GetString(6);
                string mkey = r.IsDBNull(7) ? "" : r.GetString(7);
                string mcre = r.IsDBNull(8) ? "" : r.GetString(8);
                string mprod = r.IsDBNull(9) ? "" : r.GetString(9);
                string mcd = r.IsDBNull(10) ? "" : r.GetString(10);
                int? docPages = r.IsDBNull(11) ? null : (int?)r.GetInt32(11);
                int? stFonts = r.IsDBNull(12) ? null : (int?)r.GetInt32(12);
                int? resAtt = r.IsDBNull(13) ? null : (int?)r.GetInt32(13);
                int? resEmb = r.IsDBNull(14) ? null : (int?)r.GetInt32(14);
                int? resJs = r.IsDBNull(15) ? null : (int?)r.GetInt32(15);
                int? resMm = r.IsDBNull(16) ? null : (int?)r.GetInt32(16);
                int? secEnc = r.IsDBNull(17) ? null : (int?)r.GetInt32(17);
                int? secCopy = r.IsDBNull(18) ? null : (int?)r.GetInt32(18);
                int? secPrint = r.IsDBNull(19) ? null : (int?)r.GetInt32(19);
                int? secAnnot = r.IsDBNull(20) ? null : (int?)r.GetInt32(20);
                int? secFill = r.IsDBNull(21) ? null : (int?)r.GetInt32(21);
                int? secExtract = r.IsDBNull(22) ? null : (int?)r.GetInt32(22);
                int? secAssemble = r.IsDBNull(23) ? null : (int?)r.GetInt32(23);
                int? secPrintHq = r.IsDBNull(24) ? null : (int?)r.GetInt32(24);

                if (bookmarksMin.HasValue && (!bcount.HasValue || bcount.Value < bookmarksMin.Value)) continue;
                if (bookmarksMax.HasValue && (!bcount.HasValue || bcount.Value > bookmarksMax.Value)) continue;
                if (imagesMin.HasValue && (!icount.HasValue || icount.Value < imagesMin.Value)) continue;
                if (imagesMax.HasValue && (!icount.HasValue || icount.Value > imagesMax.Value)) continue;
                if (pagesMin.HasValue && (!docPages.HasValue || docPages.Value < pagesMin.Value)) continue;
                if (pagesMax.HasValue && (!docPages.HasValue || docPages.Value > pagesMax.Value)) continue;
                if (fontsMin.HasValue && (!stFonts.HasValue || stFonts.Value < fontsMin.Value)) continue;
                if (fontsMax.HasValue && (!stFonts.HasValue || stFonts.Value > fontsMax.Value)) continue;
                if (attachments == true && (!resAtt.HasValue || resAtt.Value <= 0)) continue;
                if (embedded == true && (!resEmb.HasValue || resEmb.Value <= 0)) continue;
                if (javascript == true && (!resJs.HasValue || resJs.Value <= 0)) continue;
                if (multimedia == true && (!resMm.HasValue || resMm.Value <= 0)) continue;
                if (encrypted == true && secEnc != 1) continue;
                if (encrypted == false && secEnc == 1) continue;
                if (canCopy == true && secCopy != 1) continue;
                if (canPrint == true && secPrint != 1) continue;
                if (canAnnotate == true && secAnnot != 1) continue;
                if (canFillForms == true && secFill != 1) continue;
                if (canExtract == true && secExtract != 1) continue;
                if (canAssemble == true && secAssemble != 1) continue;
                if (canPrintHq == true && secPrintHq != 1) continue;

                if (createdAfter.HasValue || createdBefore.HasValue)
                {
                    if (DateTime.TryParse(mcd, out var dt))
                    {
                        if (createdAfter.HasValue && dt < createdAfter.Value) continue;
                        if (createdBefore.HasValue && dt > createdBefore.Value) continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                bool metaOk(string text, List<string> patterns)
                {
                    if (patterns == null || patterns.Count == 0) return true;
                    foreach (var t in patterns)
                        if (!WordOption.Matches(text ?? "", t)) return false;
                    return true;
                }

                if (!metaOk(mtit, metaTitle)) continue;
                if (!metaOk(maut, metaAuthor)) continue;
                if (!metaOk(msub, metaSubject)) continue;
                if (!metaOk(mkey, metaKeywords)) continue;
                if (!metaOk(mcre, metaCreator)) continue;
                if (!metaOk(mprod, metaProducer)) continue;

                if (bookmarkTerms != null && bookmarkTerms.Count > 0)
                {
                    var jsonLower = json?.ToLowerInvariant() ?? "";
                    bool allMatch = true;
                    foreach (var t in bookmarkTerms)
                    {
                        if (string.IsNullOrWhiteSpace(t)) continue;
                        if (!jsonLower.Contains(t.ToLowerInvariant()))
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (!allMatch) continue;
                }

                metaLookup[name] = new CacheMetaSummary
                {
                    Title = mtit,
                    Author = maut,
                    Subject = msub,
                    Keywords = mkey,
                    Creator = mcre,
                    Producer = mprod,
                    Pages = docPages,
                    Images = icount,
                    Bookmarks = bcount,
                    Encrypted = secEnc == 1 ? true : secEnc == 0 ? false : (bool?)null
                };
                set.Add(name);
            }
            return set;
        }

        private void SearchInSqlite(string dbPath,
            List<string> terms, List<string> headerTerms, List<string> footerTerms,
            List<string> docTerms, List<string> metaTerms, List<string> fontTerms, List<string> objectTerms,
            string pageRange, int? minWords, int? maxWords, string typeFilter, int? limit, string format, bool wantBBox,
            bool? hasMoney, bool? hasCpf,
            List<string> bookmarkTerms, int? bookmarksMin, int? bookmarksMax, int? imagesMin, int? imagesMax,
            List<string> metaTitle, List<string> metaAuthor, List<string> metaSubject, List<string> metaKeywords, List<string> metaCreator, List<string> metaProducer,
            DateTime? createdAfter, DateTime? createdBefore,
            int? pagesMin, int? pagesMax, int? fontsMin, int? fontsMax,
            bool? attachments, bool? embedded, bool? javascript, bool? multimedia,
            bool? encrypted, bool? canCopy, bool? canPrint, bool? canAnnotate, bool? canFillForms, bool? canExtract, bool? canAssemble, bool? canPrintHq)
        {
            Utils.SqliteCacheStore.EnsureDatabase(dbPath);
            var hits = new List<Hit>();
            var allowedCaches = BuildCacheFilter(dbPath, bookmarkTerms, bookmarksMin, bookmarksMax, imagesMin, imagesMax,
                metaTitle, metaAuthor, metaSubject, metaKeywords, metaCreator, metaProducer,
                createdAfter, createdBefore,
                pagesMin, pagesMax, fontsMin, fontsMax,
                attachments, embedded, javascript, multimedia,
                encrypted, canCopy, canPrint, canAnnotate, canFillForms, canExtract, canAssemble, canPrintHq,
                out var metaLookup);
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};");
            conn.Open();

            var pageSet = BuildPageSet(pageRange, int.MaxValue); // sem total conhecido

            // Texto/header/footer
            if (terms.Count > 0 || headerTerms.Count > 0 || footerTerms.Count > 0)
            {
                bool ftsTried = false;

                if (headerTerms.Count == 0 && footerTerms.Count == 0 && terms.Count > 0)
                {
                    // Prefer FTS5 for plain term search
                    try
                    {
                        ftsTried = true;
                        var ftsQuery = string.Join(" AND ", terms.Select(t => t.Replace('"', ' ').Replace("'", " ")));
                        using var cmdFts = new Microsoft.Data.Sqlite.SqliteCommand("SELECT f.page_number, f.text, c.name FROM page_fts f JOIN caches c ON c.id = f.cache_id WHERE f MATCH @q", conn);
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
                            bool textOk = MatchesTerms(text, terms);
                            if (textOk)
                                CollectTextHits(cacheName, pageNum, "text", text, terms, hits, limit);
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
                    using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(sql, conn);
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
                        if (allowedCaches != null && !allowedCaches.Contains(cacheName)) continue;

                        if (hasMoney == true && pageHasMoney == 0) continue;
                        if (hasCpf == true && pageHasCpf == 0) continue;

                        int wordCount = text.Split(new[]{' ','\n','\t'}, StringSplitOptions.RemoveEmptyEntries).Length;
                        if (minWords.HasValue && wordCount < minWords.Value) continue;
                        if (maxWords.HasValue && wordCount > maxWords.Value) continue;

                        var lines = text.Split('\n');
                        var header = string.Join("\n", lines.Take(5));
                        var footer = string.Join("\n", lines.Reverse().Take(5).Reverse());

                        bool headerOk = MatchesTerms(header, headerTerms);
                        bool footerOk = MatchesTerms(footer, footerTerms);
                        bool textOk = MatchesTerms(text, terms);

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
                            if (headerTerms.Count > 0) CollectTextHits(cacheName, pageNum, "header", header, headerTerms, hits, limit);
                            if (footerTerms.Count > 0) CollectTextHits(cacheName, pageNum, "footer", footer, footerTerms, hits, limit);
                            if (terms.Count > 0 || (headerTerms.Count == 0 && footerTerms.Count == 0))
                                CollectTextHits(cacheName, pageNum, "text", text, terms, hits, limit);
                        }
                        if (limit.HasValue && hits.Count >= limit.Value) break;
                    }
                }
            }

            // Docs
            if (docTerms.Count > 0)
            {
                using var cmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT name FROM caches", conn);
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

        private static bool MatchesTerms(string text, List<string> terms)
        {
            if (terms == null || terms.Count == 0) return true;

            foreach (var term in terms)
            {
                if (!WordOption.Matches(text ?? string.Empty, term))
                    return false;
            }

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

        private static void CollectTextHits(string cache, int pageNumber, string scope, string text, List<string> terms, List<Hit> hits, int? limit)
        {
            if (limit.HasValue && hits.Count >= limit.Value) return;
            if (string.IsNullOrEmpty(text)) return;

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

        // Busca simplificada direto no Postgres (pages table) usando to_tsvector
        private void SearchInPostgres(string pgUri, List<string> terms, int? limit)
        {
            if (terms == null || terms.Count == 0)
            {
                Console.WriteLine("Forneça termos para busca (--pg)");
                return;
            }
            var q = BuildTsQuery(terms);
            var lim = limit ?? 200;
            using var conn = new NpgsqlConnection(pgUri);
            conn.Open();
            using var cmd = new NpgsqlCommand(@"
            SELECT p.process_number, d.doc_key, d.doc_label_raw, pg.page_number,
                   left(pg.text, 300) AS snippet
            FROM pages pg
            JOIN documents d ON d.id = pg.document_id
            JOIN processes p ON p.id = d.process_id
            WHERE to_tsvector('portuguese', coalesce(pg.text,'')) @@ to_tsquery('portuguese', @q)
            ORDER BY pg.document_id, pg.page_number
            LIMIT @lim;
        ", conn);
        cmd.Parameters.AddWithValue("@q", q);
        cmd.Parameters.AddWithValue("@lim", lim);
        using var r = cmd.ExecuteReader();
        int count = 0;
        while (r.Read())
        {
            count++;
            var proc = r.IsDBNull(0) ? "" : r.GetString(0);
            var doc = r.IsDBNull(1) ? "" : r.GetString(1);
            var label = r.IsDBNull(2) ? "" : r.GetString(2);
            var page = r.IsDBNull(3) ? 0 : r.GetInt32(3);
            var snip = r.IsDBNull(4) ? "" : r.GetString(4).Replace('\n', ' ');
            Console.WriteLine($"{proc} | {doc} | {label} | p.{page}: {snip}");
        }
        if (count == 0)
        {
            Console.WriteLine("Nenhum resultado no Postgres.");
        }
    }

        private string BuildTsQuery(List<string> terms)
        {
            // Converte a gramática simples do FPDF (AND por espaço, OR com '|', ! para exato, * para prefixo) em to_tsquery
            var pieces = new List<string>();
            foreach (var raw in terms)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var s = raw.Trim();
                // remove aspas simples/duplas de borda
                if ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'")) && s.Length >= 2)
                    s = s.Substring(1, s.Length - 2);
                bool exact = false;
                if (s.StartsWith("!")) { exact = true; s = s.Substring(1); }
                // tratamos OR/AND explícito dentro do termo
                if (s.Contains("|"))
                {
                    var parts = s.Split('|', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => ToLexeme(p, exact))
                                 .ToArray();
                    pieces.Add("(" + string.Join(" | ", parts) + ")");
                }
                else if (s.Contains("&"))
                {
                    var parts = s.Split('&', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => ToLexeme(p, exact))
                                 .ToArray();
                    pieces.Add("(" + string.Join(" & ", parts) + ")");
                }
                else
                {
                    pieces.Add(ToLexeme(s, exact));
                }
            }
            if (!pieces.Any()) return "";
            return string.Join(" & ", pieces);
        }

        private string ToLexeme(string term, bool exact)
        {
            term = term.Trim();
            if (string.IsNullOrEmpty(term)) return "";
            term = term.Replace("~", "");
            // wildcard -> prefixo
            if (term.Contains("*"))
                term = term.Replace("*", "") + ":*";
            // tsquery exige escape de caracteres especiais
            term = term.Replace("'", " ").Replace("\\", " ");
            if (!exact && term.Contains(" "))
            {
                // frase -> & entre palavras
                var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Select(w => w + ":*");
                return "(" + string.Join(" & ", words) + ")";
            }
            return term;
        }
    }
}
