using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DiffMatchPatch;
using FilterPDF.Models;
using FilterPDF.TjpbDespachoExtractor.Config;
using FilterPDF.TjpbDespachoExtractor.Models;
using FilterPDF.TjpbDespachoExtractor.Utils;

namespace FilterPDF.TjpbDespachoExtractor.Extraction
{
    public class ExtractionOptions
    {
        public bool Dump { get; set; }
        public string? DumpDir { get; set; }
        public bool Verbose { get; set; }
        public string? BookmarkContains { get; set; }
        public string? ProcessNumber { get; set; }
        public List<string> FooterSigners { get; set; } = new List<string>();
        public string? FooterSignatureRaw { get; set; }
    }

    public class DespachoExtractor
    {
        private readonly TjpbDespachoConfig _cfg;
        private readonly diff_match_patch _dmp;

        public DespachoExtractor(TjpbDespachoConfig cfg)
        {
            _cfg = cfg;
            _dmp = new diff_match_patch();
            _dmp.Match_Threshold = 0.6f;
            _dmp.Match_Distance = 5000;
        }

        public ExtractionResult Extract(PDFAnalysisResult analysis, string sourcePath, ExtractionOptions options, Action<LogEntry>? logFn = null)
        {
            var startedAt = DateTime.UtcNow;
            var result = new ExtractionResult();
            var fileName = Path.GetFileName(sourcePath);
            var totalPages = analysis.DocumentInfo?.TotalPages ?? analysis.Pages.Count;

            result.Pdf = new PdfInfo
            {
                FileName = fileName,
                FilePath = sourcePath,
                Pages = totalPages,
                Sha256 = ComputeFileSha256(sourcePath)
            };

            result.Run = new RunInfo
            {
                StartedAt = startedAt.ToString("o"),
                ConfigVersion = _cfg.Version,
                ToolVersions = new Dictionary<string, string>
                {
                    { "fpdf", FilterPDF.Version.Current },
                    { "diff_match_patch", "1" },
                    { "diffplex", "1" }
                }
            };

            var bookmarks = FlattenBookmarks(analysis.Bookmarks);
            result.Bookmarks.AddRange(bookmarks);
            Log(result, logFn, "info", "bookmarks_loaded", new Dictionary<string, object> { { "count", bookmarks.Count } });

            var density = ComputeDensities(analysis);
            var bookmarkRanges = BuildBookmarkRanges(analysis.Bookmarks, totalPages);
            var bookmarkCandidates = bookmarkRanges
                .Where(b => IsDespachoBookmark(b.Title))
                .Where(b => string.IsNullOrWhiteSpace(options.BookmarkContains) ||
                            (!string.IsNullOrWhiteSpace(b.Title) && b.Title.Contains(options.BookmarkContains, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var candidates = bookmarkCandidates.Count > 0
                ? bookmarkCandidates.Select(b => (b.StartPage1, b.EndPage1, b.Title, b.Level, true)).ToList()
                : BuildCandidateWindows(bookmarks, density, totalPages).Select(c => (c.startPage1, c.endPage1, "", 0, false)).ToList();
            var scored = new List<(CandidateWindowInfo info, double score)>();

            foreach (var c in candidates)
            {
                var info = ScoreCandidate(analysis, c.Item1, c.Item2, density,
                    bookmarkTitle: c.Item5 ? c.Item3 : null,
                    bookmarkLevel: c.Item5 ? c.Item4 : (int?)null,
                    source: c.Item5 ? "bookmark" : "heuristic");
                result.Candidates.Add(info);
                var matchScore = Math.Max(info.ScoreDmp, info.ScoreDiffPlex);
                scored.Add((info, matchScore));
            }

            if (scored.Count == 0)
            {
                result.Errors.Add("no_candidates_found");
                result.Run.FinishedAt = DateTime.UtcNow.ToString("o");
                return result;
            }

            var best = scored
                .OrderByDescending(s => s.score)
                .ThenByDescending(s => s.info.AnchorsHit.Count)
                .First();

            if (best.score < _cfg.Thresholds.Match.DocScoreMin)
            {
                result.Errors.Add("best_score_below_threshold");
            }

            var range = best.info.Signals.TryGetValue("source", out var src) && string.Equals(src?.ToString(), "bookmark", StringComparison.OrdinalIgnoreCase)
                ? (startPage1: best.info.StartPage1, endPage1: best.info.EndPage1)
                : AdjustRange(analysis, best.info.StartPage1, best.info.EndPage1, density);
            Log(result, logFn, "info", "range_final", new Dictionary<string, object>
            {
                { "startPage1", range.startPage1 },
                { "endPage1", range.endPage1 }
            });

            var pageCount = range.endPage1 - range.startPage1 + 1;
            if (pageCount < _cfg.Thresholds.MinPages)
            {
                result.Errors.Add("range_below_min_pages");
                Log(result, logFn, "warn", "range_below_min_pages", new Dictionary<string, object>
                {
                    { "startPage1", range.startPage1 },
                    { "endPage1", range.endPage1 },
                    { "minPages", _cfg.Thresholds.MinPages }
                });
                result.Run.FinishedAt = DateTime.UtcNow.ToString("o");
                return result;
            }

            var rangeText = BuildRangeText(analysis, range.startPage1, range.endPage1);
            var tipoRange = DetectDespachoTipoByText(rangeText);
            var effectiveEnd = tipoRange == "encaminhamento_cm"
                ? range.startPage1
                : FindSignaturePageInRange(analysis, range.startPage1, range.endPage1);
            if (effectiveEnd < range.startPage1)
                effectiveEnd = range.startPage1;

            Log(result, logFn, "info", "range_selection", new Dictionary<string, object>
            {
                { "startPage1", range.startPage1 },
                { "endPage1", range.endPage1 },
                { "effectiveEndPage1", effectiveEnd },
                { "tipoRange", tipoRange }
            });

            var regions = BuildTemplateRegions(analysis, range.startPage1, effectiveEnd);
            if (tipoRange == "encaminhamento_cm")
                regions = regions.Where(r => r.Name.Equals("first_top", StringComparison.OrdinalIgnoreCase)).ToList();
            if (options.Verbose || options.Dump)
            {
                foreach (var r in regions)
                {
                    Log(result, logFn, "info", "region_text", new Dictionary<string, object>
                    {
                        { "name", r.Name },
                        { "page1", r.Page1 },
                        { "bboxN", r.BBox },
                        { "text", Truncate(r.Text, 2000) }
                    });
                }
            }

            var doc = BuildDocument(analysis, range.startPage1, effectiveEnd, best.score, tipoRange, regions,
                options.ProcessNumber ?? "", options.FooterSigners, options.FooterSignatureRaw);
            result.Documents.Add(doc);

            LogVariationSnippets(result, logFn, regions);

            var certidaoDoc = BuildCertidaoDocument(analysis, options.ProcessNumber ?? "", options.FooterSigners, options.FooterSignatureRaw);
            if (certidaoDoc != null)
            {
                result.Documents.Add(certidaoDoc);
                Log(result, logFn, "info", "certidao_cm_found", new Dictionary<string, object>
                {
                    { "page1", certidaoDoc.StartPage1 },
                    { "docType", certidaoDoc.DocType }
                });
            }

            result.Run.FinishedAt = DateTime.UtcNow.ToString("o");
            return result;
        }

        public List<DespachoDocumentInfo> ExtractAllByBookmarks(PDFAnalysisResult analysis, string sourcePath, ExtractionOptions options, Action<LogEntry>? logFn = null)
        {
            var docs = new List<DespachoDocumentInfo>();
            if (analysis == null || analysis.Pages == null || analysis.Pages.Count == 0) return docs;

            var totalPages = analysis.DocumentInfo?.TotalPages ?? analysis.Pages.Count;
            var bookmarkRanges = BuildBookmarkRanges(analysis.Bookmarks, totalPages)
                .Where(b => IsDespachoBookmark(b.Title))
                .ToList();
            if (bookmarkRanges.Count == 0) return docs;

            var density = ComputeDensities(analysis);

            foreach (var br in bookmarkRanges)
            {
                var pageCount = br.EndPage1 - br.StartPage1 + 1;
                if (pageCount < _cfg.Thresholds.MinPages) continue;

                var firstText = analysis.Pages[br.StartPage1 - 1].TextInfo?.PageText ?? "";
                var firstNorm = TextUtils.NormalizeForMatch(firstText);
                if (!ContainsAny(firstNorm, _cfg.Anchors.Subheader) || !ContainsAny(firstNorm, _cfg.Anchors.Title))
                    continue;

                var rangeText = BuildRangeText(analysis, br.StartPage1, br.EndPage1);
                var tipoRange = DetectDespachoTipoByText(rangeText);
                var effectiveEnd = tipoRange == "encaminhamento_cm"
                    ? br.StartPage1
                    : FindSignaturePageInRange(analysis, br.StartPage1, br.EndPage1);
                if (effectiveEnd < br.StartPage1) effectiveEnd = br.StartPage1;

                var regions = BuildTemplateRegions(analysis, br.StartPage1, effectiveEnd);
                if (tipoRange == "encaminhamento_cm")
                    regions = regions.Where(r => r.Name.Equals("first_top", StringComparison.OrdinalIgnoreCase)).ToList();

                var lastRegionText = string.Join(" ", regions.Where(r => r.Name.Equals("last_bottom", StringComparison.OrdinalIgnoreCase)).Select(r => r.Text));
                var lastPageText = analysis.Pages[effectiveEnd - 1].TextInfo?.PageText ?? "";
                var rangeTailText = BuildRangeTailText(analysis, br.StartPage1, br.EndPage1);

                if (!HasRobsonSignature(analysis, sourcePath, options, lastRegionText, lastPageText, rangeTailText))
                    continue;

                var info = ScoreCandidate(analysis, br.StartPage1, br.EndPage1, density,
                    bookmarkTitle: br.Title, bookmarkLevel: br.Level, source: "bookmark");
                var matchScore = Math.Max(info.ScoreDmp, info.ScoreDiffPlex);

                var doc = BuildDocument(analysis, br.StartPage1, effectiveEnd, matchScore, tipoRange, regions,
                    options.ProcessNumber ?? "", options.FooterSigners, options.FooterSignatureRaw);
                docs.Add(doc);
            }

            return docs;
        }

        public DespachoDocumentInfo? ExtractCertidao(PDFAnalysisResult analysis, ExtractionOptions options)
        {
            if (analysis == null) return null;
            return BuildCertidaoDocument(analysis, options?.ProcessNumber ?? "", options?.FooterSigners ?? new List<string>(), options?.FooterSignatureRaw);
        }

        private void LogVariationSnippets(ExtractionResult result, Action<LogEntry>? logFn, List<RegionSegment> regions)
        {
            if (regions == null || regions.Count == 0) return;
            var autorHints = _cfg.DespachoType.AutorizacaoHints.Concat(_cfg.DespachoType.GeorcHints).ToList();
            var consHints = _cfg.DespachoType.ConselhoHints;

            foreach (var r in regions)
            {
                if (string.IsNullOrWhiteSpace(r.Text)) continue;
                if (r.Name.Equals("last_bottom", StringComparison.OrdinalIgnoreCase))
                {
                    var hits = CollectHintSnippets(r.Text, autorHints, 220);
                    if (hits.Count > 0)
                    {
                        Log(result, logFn, "info", "autorizacao_variations", new Dictionary<string, object>
                        {
                            { "page1", r.Page1 },
                            { "region", r.Name },
                            { "snippets", hits }
                        });
                    }
                }
                if (r.Name.StartsWith("last_bottom", StringComparison.OrdinalIgnoreCase))
                {
                    var hits = CollectHintSnippets(r.Text, consHints, 220);
                    if (hits.Count > 0)
                    {
                        Log(result, logFn, "info", "conselho_variations", new Dictionary<string, object>
                        {
                            { "page1", r.Page1 },
                            { "region", r.Name },
                            { "snippets", hits }
                        });
                    }
                }
            }
        }

        private List<string> CollectHintSnippets(string text, List<string> hints, int window)
        {
            var snippets = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || hints == null || hints.Count == 0) return snippets;
            var normText = TextUtils.NormalizeForMatch(text);
            foreach (var h in hints)
            {
                if (string.IsNullOrWhiteSpace(h)) continue;
                var nh = TextUtils.NormalizeForMatch(h);
                if (string.IsNullOrWhiteSpace(nh)) continue;
                var idx = normText.IndexOf(nh, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var start = Math.Max(0, idx - window / 2);
                var len = Math.Min(normText.Length - start, window);
                var snip = normText.Substring(start, len);
                if (!snippets.Contains(snip))
                    snippets.Add(snip);
            }
            return snippets;
        }

        private string BuildRangeText(PDFAnalysisResult analysis, int startPage1, int endPage1)
        {
            if (analysis.Pages == null || analysis.Pages.Count == 0) return "";
            var start = Math.Max(1, startPage1);
            var end = Math.Min(analysis.Pages.Count, endPage1);
            var parts = new List<string>();
            for (int p = start; p <= end; p++)
            {
                var text = analysis.Pages[p - 1].TextInfo?.PageText ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }
            return string.Join("\n", parts);
        }

        private string BuildRangeTailText(PDFAnalysisResult analysis, int startPage1, int endPage1)
        {
            if (analysis.Pages == null || analysis.Pages.Count == 0) return "";
            var start = Math.Max(1, startPage1);
            var end = Math.Min(analysis.Pages.Count, endPage1);
            var parts = new List<string>();
            for (int p = start; p <= end; p++)
            {
                var text = analysis.Pages[p - 1].TextInfo?.PageText ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                parts.Add(GetTailText(text));
            }
            return string.Join("\n", parts);
        }

        private int FindSignaturePageInRange(PDFAnalysisResult analysis, int startPage1, int endPage1)
        {
            if (analysis.Pages == null || analysis.Pages.Count == 0) return endPage1;
            var start = Math.Max(1, startPage1);
            var end = Math.Min(analysis.Pages.Count, endPage1);
            // Prefer last page with Robson in tail
            for (int p = end; p >= start; p--)
            {
                var text = analysis.Pages[p - 1].TextInfo?.PageText ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                var tail = GetTailText(text);
                if (TextUtils.NormalizeForMatch(tail).Contains("robson"))
                    return p;
            }
            // Fallback: last page with signature anchor in tail
            for (int p = end; p >= start; p--)
            {
                var text = analysis.Pages[p - 1].TextInfo?.PageText ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                var tail = GetTailText(text);
                var norm = TextUtils.NormalizeForMatch(tail);
                if (norm.Contains("documento assinado") || norm.Contains("assinado eletronicamente"))
                    return p;
            }
            return end;
        }

        private bool HasRobsonSignature(PDFAnalysisResult analysis, string sourcePath, ExtractionOptions options, string lastBottomText, string lastPageText, string rangeTailText)
        {
            bool HasRobson(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return false;
                return TextUtils.NormalizeForMatch(value).Contains("robson");
            }

            if (analysis?.Signatures != null)
            {
                foreach (var sig in analysis.Signatures)
                {
                    if (HasRobson(sig.SignerName) || HasRobson(sig.Name) || HasRobson(sig.Certificate?.Subject))
                        return true;
                }
            }

            if (options?.FooterSigners != null && options.FooterSigners.Any(HasRobson))
                return true;
            if (HasRobson(options?.FooterSignatureRaw))
                return true;

            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                var sigs = SignatureExtractor.ExtractSignatures(sourcePath);
                foreach (var s in sigs)
                {
                    if (HasRobson(s.SignerName))
                        return true;
                }
            }

            if (HasRobson(lastBottomText) || HasRobson(lastPageText) || HasRobson(rangeTailText))
                return true;

            return false;
        }

        private string DetectDespachoTipoByText(string text)
        {
            var norm = TextUtils.NormalizeForMatch(text);
            if (ContainsAny(norm, _cfg.DespachoType.GeorcHints) || ContainsAny(norm, _cfg.DespachoType.AutorizacaoHints))
                return "autorizacao";
            var conselhoStrong = new List<string> { "encaminh", "submet", "remet", "remetam", "remessa" };
            if (ContainsAny(norm, _cfg.DespachoType.ConselhoHints) && ContainsAny(norm, conselhoStrong))
                return "encaminhamento_cm";
            return "indefinido";
        }

        private static string GetTailText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var start = (int)(text.Length * 0.7);
            if (start < 0) start = 0;
            if (start >= text.Length) return text;
            return text.Substring(start);
        }

        private DespachoDocumentInfo BuildDocument(PDFAnalysisResult analysis, int startPage1, int endPage1, double matchScore,
            string despachoTipo, List<RegionSegment> regions, string processNumber, List<string> footerSigners, string? footerSignatureRaw)
        {
            var doc = new DespachoDocumentInfo
            {
                DocType = "despacho",
                DespachoTipo = despachoTipo ?? "",
                StartPage1 = startPage1,
                EndPage1 = endPage1,
                MatchScore = matchScore
            };

            var paragraphs = new List<ParagraphSegment>();
            var bands = new List<BandInfo>();
            var bandSegments = new List<BandSegment>();
            var pages = new List<PageTextInfo>();

            var pagesForExtraction = new HashSet<int> { startPage1, endPage1 };

            ParagraphSegment? firstPara = null;
            ParagraphSegment? lastPara = null;
            var paragraphKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddParagraph(ParagraphSegment? p)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Text)) return;
                var key = $"{p.Page1}:{TextUtils.NormalizeWhitespace(p.Text)}";
                if (paragraphKeys.Add(key))
                    paragraphs.Add(p);
            }

            for (int p = startPage1; p <= endPage1; p++)
            {
                var page = analysis.Pages[p - 1];
                if (!pagesForExtraction.Contains(p))
                    continue;

                pages.Add(new PageTextInfo { Page1 = p, Text = page.TextInfo.PageText ?? "" });

                var words = TextUtils.DeduplicateWords(page.TextInfo.Words ?? new List<WordInfo>());
                var bandSeg = BandSegmenter.SegmentPage(words, p, _cfg.Thresholds.Bands, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);
                bands.AddRange(bandSeg.Bands.Where(b => !string.Equals(b.Band, "body", StringComparison.OrdinalIgnoreCase)));
                bandSegments.AddRange(bandSeg.BandSegments.Where(b => !string.Equals(b.Band, "body", StringComparison.OrdinalIgnoreCase)));

                // Top region (header/subheader/titulo + primeiro paragrafo da primeira pagina)
                if (p == startPage1)
                {
                    var topWords = regions
                        .Where(r => r.Page1 == p && r.Name.Equals("first_top", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(r => r.Words ?? new List<WordInfo>())
                        .ToList();
                    if (topWords.Count > 0)
                    {
                        AddBodyBand(bands, bandSegments, p, topWords, "first_top");
                        var lines = LineBuilder.BuildLines(topWords, p, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);
                        var paras = ParagraphBuilder.BuildParagraphs(lines, _cfg.Thresholds.Paragraph.ParagraphGapY);
                        var hints = new List<string>();
                        hints.AddRange(_cfg.Priorities.ProcessoAdminLabels);
                        hints.AddRange(_cfg.Priorities.PeritoLabels);
                        hints.AddRange(_cfg.Priorities.VaraLabels);
                        hints.AddRange(_cfg.Priorities.ComarcaLabels);
                        var cnjRx = new Regex(_cfg.Regex.ProcessoCnj, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                        firstPara ??= SelectParagraphByHints(paras, hints, cnjRx) ?? paras.FirstOrDefault();
                    }
                }

                // Bottom region (ultimo paragrafo + rodape da ultima pagina)
                if (p == endPage1)
                {
                    var bottomWords = regions
                        .Where(r => r.Page1 == p && r.Name.Equals("last_bottom", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(r => r.Words ?? new List<WordInfo>())
                        .ToList();
                    if (bottomWords.Count > 0)
                    {
                        AddBodyBand(bands, bandSegments, p, bottomWords, "last_bottom");
                        var lines = LineBuilder.BuildLines(bottomWords, p, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);
                        var paras = ParagraphBuilder.BuildParagraphs(lines, _cfg.Thresholds.Paragraph.ParagraphGapY);
                        var candidate = SelectParagraphByHints(paras, _cfg.Anchors.Footer) ?? paras.LastOrDefault();
                        if (candidate != null)
                            lastPara = candidate;
                    }
                }
            }

            AddParagraph(firstPara);
            AddParagraph(lastPara);
            // reindex
            for (int i = 0; i < paragraphs.Count; i++)
                paragraphs[i].Index = i;

            doc.Bands = bands;
            doc.Paragraphs = paragraphs.Select(p => new ParagraphInfo
            {
                Page1 = p.Page1,
                Index = p.Index,
                Text = p.Text,
                HashSha256 = TextUtils.Sha256Hex(TextUtils.NormalizeForHash(p.Text)),
                BBoxN = p.BBox
            }).ToList();

            var fullText = string.Join("\n", pages.Select(p => p.Text));
            var ctx = new DespachoContext
            {
                FullText = fullText,
                Paragraphs = paragraphs,
                Bands = bands,
                BandSegments = bandSegments,
                Regions = regions ?? new List<RegionSegment>(),
                Pages = pages,
                FileName = Path.GetFileName(analysis.FilePath ?? ""),
                FilePath = analysis.FilePath ?? "",
                Config = _cfg,
                StartPage1 = startPage1,
                EndPage1 = endPage1,
                ProcessNumber = processNumber ?? "",
                FooterSigners = footerSigners ?? new List<string>(),
                FooterSignatureRaw = footerSignatureRaw
            };

            var fieldExtractor = new FieldExtractor(_cfg);
            doc.Fields = fieldExtractor.ExtractAll(ctx);

            var warnings = new List<string>();
            if (string.Equals(despachoTipo, "encaminhamento_cm", StringComparison.OrdinalIgnoreCase) &&
                regions.Any(r => r.Name.StartsWith("last_bottom", StringComparison.OrdinalIgnoreCase)))
                warnings.Add("unexpected_last_bottom_for_conselho");

            if ((string.Equals(despachoTipo, "autorizacao", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(despachoTipo, "georc", StringComparison.OrdinalIgnoreCase)) &&
                !regions.Any(r => r.Name.Equals("last_bottom", StringComparison.OrdinalIgnoreCase)))
                warnings.Add("missing_last_bottom_for_georc");
            if (doc.Fields["PROCESSO_ADMINISTRATIVO"].Method == "not_found" && doc.Fields["PROCESSO_JUDICIAL"].Method == "not_found")
                warnings.Add("missing_process_numbers");
            doc.Warnings = warnings;

            return doc;
        }

        private DespachoDocumentInfo? BuildCertidaoDocument(PDFAnalysisResult analysis, string processNumber, List<string> footerSigners, string? footerSignatureRaw)
        {
            var page1 = CertidaoExtraction.FindCertidaoPage(analysis, _cfg);
            if (page1 <= 0) return null;

            var regions = CertidaoExtraction.BuildCertidaoRegions(analysis, page1, _cfg);
            var fullRegion = regions.FirstOrDefault(r => r.Name.Equals("certidao_full", StringComparison.OrdinalIgnoreCase));
            if (fullRegion == null || fullRegion.Words == null || fullRegion.Words.Count == 0)
                return null;

            var scoreFull = ComputeTemplateScore(fullRegion.Text ?? "", _cfg.TemplateRegions.CertidaoFull.Templates);
            var scoreValue = 0.0;
            var valueRegion = regions.FirstOrDefault(r => r.Name.Equals("certidao_value_date", StringComparison.OrdinalIgnoreCase));
            if (valueRegion != null)
                scoreValue = ComputeTemplateScore(valueRegion.Text ?? "", _cfg.TemplateRegions.CertidaoValueDate.Templates);
            var matchScore = Math.Max(scoreFull, scoreValue);

            var doc = new DespachoDocumentInfo
            {
                DocType = "certidao_cm",
                DespachoTipo = "certidao_cm",
                StartPage1 = page1,
                EndPage1 = page1,
                MatchScore = matchScore
            };

            var bands = new List<BandInfo>();
            var bandSegments = new List<BandSegment>();
            foreach (var r in regions)
            {
                if (r.Words == null || r.Words.Count == 0) continue;
                AddBodyBand(bands, bandSegments, page1, r.Words, r.Name);
            }

            var lines = LineBuilder.BuildLines(fullRegion.Words, page1, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);
            var paras = ParagraphBuilder.BuildParagraphs(lines, _cfg.Thresholds.Paragraph.ParagraphGapY);
            for (int i = 0; i < paras.Count; i++) paras[i].Index = i;

            doc.Bands = bands;
            doc.Paragraphs = paras.Select(p => new ParagraphInfo
            {
                Page1 = p.Page1,
                Index = p.Index,
                Text = p.Text,
                HashSha256 = TextUtils.Sha256Hex(TextUtils.NormalizeForHash(p.Text)),
                BBoxN = p.BBox
            }).ToList();

            var pages = new List<PageTextInfo>
            {
                new PageTextInfo { Page1 = page1, Text = analysis.Pages[page1 - 1].TextInfo.PageText ?? "" }
            };

            var ctx = new DespachoContext
            {
                FullText = fullRegion.Text ?? pages[0].Text,
                Paragraphs = paras,
                Bands = bands,
                BandSegments = bandSegments,
                Regions = regions,
                Pages = pages,
                FileName = Path.GetFileName(analysis.FilePath ?? ""),
                FilePath = analysis.FilePath ?? "",
                Config = _cfg,
                StartPage1 = page1,
                EndPage1 = page1,
                ProcessNumber = processNumber ?? "",
                FooterSigners = footerSigners ?? new List<string>(),
                FooterSignatureRaw = footerSignatureRaw
            };

            var fieldExtractor = new FieldExtractor(_cfg);
            doc.Fields = fieldExtractor.ExtractAll(ctx);
            doc.Warnings = new List<string>();

            return doc;
        }

        private ParagraphSegment? SelectParagraphByHints(List<ParagraphSegment> paras, List<string> hints, Regex? primaryRegex = null)
        {
            if (paras == null || paras.Count == 0) return null;
            if (hints == null || hints.Count == 0) return null;
            if (primaryRegex != null)
            {
                foreach (var p in paras)
                {
                    var raw = p.Text ?? "";
                    var collapsed = TextUtils.CollapseSpacedLettersText(raw);
                    if (primaryRegex.IsMatch(collapsed) || primaryRegex.IsMatch(raw))
                        return p;
                }
            }
            var normHints = hints.Where(h => !string.IsNullOrWhiteSpace(h))
                                 .Select(h => TextUtils.NormalizeForMatch(h))
                                 .ToList();
            foreach (var p in paras)
            {
                var norm = TextUtils.NormalizeForMatch(TextUtils.CollapseSpacedLettersText(p.Text ?? ""));
                foreach (var h in normHints)
                {
                    if (norm.Contains(h))
                        return p;
                }
            }
            return null;
        }

        private void AddBodyBand(List<BandInfo> bands, List<BandSegment> bandSegments, int page1, List<WordInfo> words, string region)
        {
            if (words == null || words.Count == 0) return;
            var text = TextUtils.BuildTextFromWords(words, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.TemplateRegions.WordGapX);
            var bbox = TextUtils.UnionBBox(words);
            var hash = TextUtils.Sha256Hex(TextUtils.NormalizeForHash(text));

            bands.Add(new BandInfo
            {
                Page1 = page1,
                Band = "body",
                Region = region ?? "",
                Text = text,
                HashSha256 = hash,
                BBoxN = bbox
            });

            bandSegments.Add(new BandSegment
            {
                Page1 = page1,
                Band = "body",
                Text = text,
                Words = words,
                BBox = bbox
            });
        }

        private List<RegionSegment> BuildTemplateRegions(PDFAnalysisResult analysis, int startPage1, int endPage1)
        {
            var regions = new List<RegionSegment>();
            if (analysis.Pages == null || analysis.Pages.Count == 0) return regions;

            // ---------- FIRST PAGE: header/subheader/title + first paragraph ----------
            var firstPage = analysis.Pages[Math.Max(0, startPage1 - 1)];
            var firstWords = TextUtils.DeduplicateWords(firstPage.TextInfo?.Words ?? new List<WordInfo>());
            var firstSeg = BandSegmenter.SegmentPage(firstWords, startPage1, _cfg.Thresholds.Bands, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);

            var bodyLines = LineBuilder.BuildLines(firstSeg.BodyWords ?? new List<WordInfo>(), startPage1, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);
            var bodyParas = ParagraphBuilder.BuildParagraphs(bodyLines, _cfg.Thresholds.Paragraph.ParagraphGapY);
            var hints = new List<string>();
            hints.AddRange(_cfg.Priorities.ProcessoAdminLabels);
            hints.AddRange(_cfg.Priorities.PeritoLabels);
            hints.AddRange(_cfg.Priorities.VaraLabels);
            hints.AddRange(_cfg.Priorities.ComarcaLabels);
            var cnjRx = new Regex(_cfg.Regex.ProcessoCnj, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var firstPara = SelectParagraphByHints(bodyParas, hints, cnjRx) ?? bodyParas.FirstOrDefault();

            var headerWords = firstSeg.BandSegments
                .Where(b => b.Band.Equals("header", StringComparison.OrdinalIgnoreCase) ||
                            b.Band.Equals("subheader", StringComparison.OrdinalIgnoreCase) ||
                            b.Band.Equals("title", StringComparison.OrdinalIgnoreCase))
                .SelectMany(b => b.Words ?? new List<WordInfo>())
                .ToList();

            var firstRegionWords = new List<WordInfo>();
            firstRegionWords.AddRange(headerWords);
            if (firstPara?.Words != null) firstRegionWords.AddRange(firstPara.Words);
            var rFirst = BuildRegionFromWords("first_top", startPage1, firstRegionWords);
            if (rFirst != null) regions.Add(rFirst);

            // ---------- LAST PAGE: last paragraph + footer ----------
            var lastPage = analysis.Pages[Math.Max(0, endPage1 - 1)];
            var lastWords = TextUtils.DeduplicateWords(lastPage.TextInfo?.Words ?? new List<WordInfo>());
            var lastSeg = BandSegmenter.SegmentPage(lastWords, endPage1, _cfg.Thresholds.Bands, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);
            var lastLines = LineBuilder.BuildLines(lastSeg.BodyWords ?? new List<WordInfo>(), endPage1, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.Thresholds.Paragraph.WordGapX);
            var lastParas = ParagraphBuilder.BuildParagraphs(lastLines, _cfg.Thresholds.Paragraph.ParagraphGapY);
            var lastPara = SelectParagraphByHints(lastParas, _cfg.Anchors.Footer) ?? lastParas.LastOrDefault();

            var footerWords = lastSeg.BandSegments
                .Where(b => b.Band.Equals("footer", StringComparison.OrdinalIgnoreCase))
                .SelectMany(b => b.Words ?? new List<WordInfo>())
                .ToList();

            var lastRegionWords = new List<WordInfo>();
            if (lastPara?.Words != null) lastRegionWords.AddRange(lastPara.Words);
            lastRegionWords.AddRange(footerWords);
            var rLast = BuildRegionFromWords("last_bottom", endPage1, lastRegionWords);
            if (rLast != null) regions.Add(rLast);

            return regions;
        }

        private RegionSegment? BuildRegionFromWords(string name, int page1, List<WordInfo> words)
        {
            if (words == null || words.Count == 0) return null;
            var filtered = TextUtils.DeduplicateWords(words)
                .Where(w => w != null)
                .OrderByDescending(w => (w.NormY0 + w.NormY1) / 2.0)
                .ThenBy(w => w.NormX0)
                .ToList();

            if (filtered.Count == 0) return null;
            var text = TextUtils.BuildTextFromWords(filtered, _cfg.Thresholds.Paragraph.LineMergeY, _cfg.TemplateRegions.WordGapX);
            var bbox = TextUtils.UnionBBox(filtered);

            return new RegionSegment
            {
                Name = name,
                Page1 = page1,
                Words = filtered,
                Text = text,
                BBox = bbox
            };
        }

        private List<BookmarkInfo> FlattenBookmarks(BookmarkStructure? bookmarks)
        {
            var list = new List<BookmarkInfo>();
            if (bookmarks?.RootItems == null) return list;

            void Walk(IEnumerable<BookmarkItem> items)
            {
                foreach (var item in items)
                {
                    var page1 = item.Destination?.PageNumber ?? 0;
                    if (page1 > 0)
                    {
                        list.Add(new BookmarkInfo
                        {
                            Title = item.Title ?? "",
                            Page1 = page1,
                            Page0 = page1 - 1
                        });
                    }
                    if (item.Children != null && item.Children.Count > 0)
                        Walk(item.Children);
                }
            }

            Walk(bookmarks.RootItems);
            return list;
        }

        private Dictionary<int, double> ComputeDensities(PDFAnalysisResult analysis)
        {
            var dict = new Dictionary<int, double>();
            for (int i = 0; i < analysis.Pages.Count; i++)
            {
                var page = analysis.Pages[i];
                var area = Math.Max(1.0, page.Size.Width * page.Size.Height);
                double wordsArea = 0;
                foreach (var w in page.TextInfo.Words)
                {
                    var wArea = Math.Max(0, (w.X1 - w.X0) * (w.Y1 - w.Y0));
                    wordsArea += wArea;
                }
                dict[i + 1] = wordsArea / area;
            }
            return dict;
        }

        private List<(int startPage1, int endPage1)> BuildCandidateWindows(List<BookmarkInfo> bookmarks, Dictionary<int, double> density, int totalPages)
        {
            var candidates = new HashSet<(int, int)>();

            foreach (var bm in bookmarks)
            {
                var t = TextUtils.NormalizeForMatch(bm.Title);
                if (t.Contains("despacho") || t.Contains("diesp") || t.Contains("diretoria especial"))
                {
                    AddWindows(candidates, bm.Page1, totalPages);
                }
            }

            var topDensity = density
                .Where(kv => kv.Value >= _cfg.Thresholds.DensityMin)
                .OrderByDescending(kv => kv.Value)
                .Take(6)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var p in topDensity)
                AddWindows(candidates, p, totalPages);

            return candidates.Select(c => (c.Item1, c.Item2)).ToList();
        }

        private void AddWindows(HashSet<(int, int)> set, int page1, int totalPages)
        {
            int[] sizes = { 2, 3, 4 };
            foreach (var s in sizes)
            {
                var end = Math.Min(totalPages, page1 + s - 1);
                if (end >= page1)
                    set.Add((page1, end));
            }
        }

        private CandidateWindowInfo ScoreCandidate(PDFAnalysisResult analysis, int startPage1, int endPage1, Dictionary<int, double> density, string? bookmarkTitle = null, int? bookmarkLevel = null, string? source = null)
        {
            var text = string.Join("\n", Enumerable.Range(startPage1, endPage1 - startPage1 + 1)
                .Select(p => analysis.Pages[p - 1].TextInfo.PageText ?? ""));
            var norm = TextUtils.NormalizeForMatch(text);

            var anchorsHit = new List<string>();
            if (ContainsAny(norm, _cfg.Anchors.Header)) anchorsHit.Add("HEADER_TJPB");
            if (ContainsAny(norm, _cfg.Anchors.Subheader)) anchorsHit.Add("DIRETORIA_ESPECIAL");
            if (ContainsAny(norm, _cfg.Anchors.Title)) anchorsHit.Add("DESPACHO_TITULO");
            if (ContainsAny(norm, _cfg.Anchors.Footer)) anchorsHit.Add("ASSINATURA_ELETRONICA");

            var hasRobson = ContainsAny(norm, _cfg.Anchors.SignerHints);
            var hasCrc = norm.Contains("crc");

            var template = BuildTemplateText();
            var anchorText = BuildAnchorText(text);
            var scoreDmp = ComputeDmpScore(template, anchorText);
            var scoreDiffPlex = scoreDmp < _cfg.Thresholds.Match.DocScoreMin
                ? DiffPlexMatcher.Similarity(TextUtils.NormalizeForMatch(template), TextUtils.NormalizeForMatch(anchorText))
                : scoreDmp;

            var densityMap = new Dictionary<string, double>();
            for (int p = startPage1; p <= endPage1; p++)
                densityMap[$"p{p}"] = density.TryGetValue(p, out var d) ? d : 0;

            var signals = new Dictionary<string, object>
            {
                { "hasRobson", hasRobson },
                { "hasCRC", hasCrc },
                { "hasDiretoriaEspecial", anchorsHit.Contains("DIRETORIA_ESPECIAL") }
            };
            if (!string.IsNullOrWhiteSpace(source))
                signals["source"] = source;
            if (!string.IsNullOrWhiteSpace(bookmarkTitle))
                signals["bookmarkTitle"] = bookmarkTitle;
            if (bookmarkLevel.HasValue)
                signals["bookmarkLevel"] = bookmarkLevel.Value;

            return new CandidateWindowInfo
            {
                StartPage1 = startPage1,
                EndPage1 = endPage1,
                ScoreDmp = scoreDmp,
                ScoreDiffPlex = scoreDiffPlex,
                AnchorsHit = anchorsHit,
                Density = densityMap,
                Signals = signals
            };
        }

        private class BookmarkRange
        {
            public string Title { get; set; } = "";
            public int Level { get; set; }
            public int StartPage1 { get; set; }
            public int EndPage1 { get; set; }
        }

        private List<BookmarkRange> BuildBookmarkRanges(BookmarkStructure? bookmarks, int totalPages)
        {
            var list = new List<BookmarkRange>();
            if (bookmarks?.RootItems == null || bookmarks.RootItems.Count == 0) return list;

            var flat = new List<BookmarkItem>();
            void Walk(IEnumerable<BookmarkItem> items)
            {
                foreach (var item in items)
                {
                    flat.Add(item);
                    if (item.Children != null && item.Children.Count > 0)
                        Walk(item.Children);
                }
            }

            Walk(bookmarks.RootItems);

            var ordered = flat
                .Where(b => b.Destination?.PageNumber > 0)
                .Select(b => new { b.Title, b.Level, Page1 = b.Destination.PageNumber })
                .OrderBy(b => b.Page1)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var start = ordered[i].Page1;
                var end = (i + 1 < ordered.Count) ? Math.Max(start, ordered[i + 1].Page1 - 1) : totalPages;
                list.Add(new BookmarkRange
                {
                    Title = ordered[i].Title ?? "",
                    Level = ordered[i].Level,
                    StartPage1 = start,
                    EndPage1 = end
                });
            }

            return list;
        }

        private bool IsDespachoBookmark(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var norm = TextUtils.NormalizeForMatch(title);
            if (norm.Contains("despacho")) return true;
            foreach (var t in _cfg.Anchors.Title)
            {
                if (!string.IsNullOrWhiteSpace(t) && norm.Contains(TextUtils.NormalizeForMatch(t)))
                    return true;
            }
            return false;
        }

        private (int startPage1, int endPage1) AdjustRange(PDFAnalysisResult analysis, int startPage1, int endPage1, Dictionary<int, double> density)
        {
            int total = analysis.DocumentInfo?.TotalPages ?? analysis.Pages.Count;
            int start = startPage1;
            int end = endPage1;

            if (start > 1)
            {
                var prevText = TextUtils.NormalizeForMatch(analysis.Pages[start - 2].TextInfo.PageText ?? "");
                if (ContainsAny(prevText, _cfg.Anchors.Header))
                    start -= 1;
            }

            bool footerFound = WindowHasFooter(analysis, start, end);

            while (end - start + 1 < _cfg.Thresholds.MinPages && end < total)
            {
                end++;
                footerFound = footerFound || PageHasFooter(analysis, end);
            }

            while (!footerFound && end < total && end - start + 1 < _cfg.Thresholds.MaxPages)
            {
                end++;
                footerFound = PageHasFooter(analysis, end) || footerFound;
                var d = density.TryGetValue(end, out var v) ? v : 0;
                if (footerFound && d < _cfg.Thresholds.DensityMin * 0.5)
                    break;
            }

            if (end - start + 1 > _cfg.Thresholds.MaxPages)
                end = start + _cfg.Thresholds.MaxPages - 1;

            return (start, end);
        }

        private bool WindowHasFooter(PDFAnalysisResult analysis, int startPage1, int endPage1)
        {
            for (int p = startPage1; p <= endPage1; p++)
                if (PageHasFooter(analysis, p))
                    return true;
            return false;
        }

        private bool PageHasFooter(PDFAnalysisResult analysis, int page1)
        {
            var text = TextUtils.NormalizeForMatch(analysis.Pages[page1 - 1].TextInfo.PageText ?? "");
            return ContainsAny(text, _cfg.Anchors.Footer);
        }

        private bool ContainsAny(string textNorm, List<string> anchors)
        {
            if (anchors == null || anchors.Count == 0) return false;
            foreach (var a in anchors)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                if (textNorm.Contains(TextUtils.NormalizeForMatch(a)))
                    return true;
            }
            return false;
        }

        private string BuildTemplateText()
        {
            var parts = new List<string>();
            parts.AddRange(_cfg.Anchors.Header);
            parts.AddRange(_cfg.Anchors.Subheader);
            parts.AddRange(_cfg.Anchors.Title);
            parts.AddRange(_cfg.Anchors.Footer);
            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private string BuildAnchorText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var normAnchors = _cfg.Anchors.Header
                .Concat(_cfg.Anchors.Subheader)
                .Concat(_cfg.Anchors.Title)
                .Concat(_cfg.Anchors.Footer)
                .Select(TextUtils.NormalizeForMatch)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            var lines = text.Split('\n');
            var chosen = new List<string>();
            foreach (var line in lines)
            {
                var ln = TextUtils.NormalizeForMatch(line);
                if (normAnchors.Any(a => ln.Contains(a)))
                    chosen.Add(line);
            }
            if (chosen.Count == 0)
            {
                var clean = TextUtils.NormalizeWhitespace(text);
                return clean.Length > 2000 ? clean.Substring(0, 2000) : clean;
            }
            return string.Join("\n", chosen);
        }

        private double ComputeDmpScore(string template, string text)
        {
            var tNorm = TextUtils.NormalizeForMatch(template);
            var xNorm = TextUtils.NormalizeForMatch(text);
            if (string.IsNullOrWhiteSpace(tNorm) || string.IsNullOrWhiteSpace(xNorm)) return 0;
            var diffs = _dmp.diff_main(tNorm, xNorm, false);
            _dmp.diff_cleanupSemantic(diffs);
            var dist = _dmp.diff_levenshtein(diffs);
            var maxLen = Math.Max(tNorm.Length, xNorm.Length);
            if (maxLen == 0) return 0;
            var score = 1.0 - (double)dist / maxLen;
            if (score < 0) score = 0;
            return score;
        }

        private double ComputeTemplateScore(string regionText, List<string> templates)
        {
            if (string.IsNullOrWhiteSpace(regionText)) return 0.0;
            if (templates == null || templates.Count == 0) return 0.0;
            var placeholder = new Regex(@"\{\{\s*([A-Z0-9_]+)\s*\}\}", RegexOptions.IgnoreCase);
            var regionNorm = TextUtils.NormalizeForMatch(regionText);
            double best = 0.0;
            foreach (var t in templates)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                var core = placeholder.Replace(t, "");
                var coreNorm = TextUtils.NormalizeForMatch(core);
                if (string.IsNullOrWhiteSpace(coreNorm)) continue;
                var score = DiffPlexMatcher.Similarity(coreNorm, regionNorm);
                if (score > best) best = score;
            }
            return best;
        }

        private string ComputeFileSha256(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(path);
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        private string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen);
        }

        private void Log(ExtractionResult result, Action<LogEntry>? logFn, string level, string message, Dictionary<string, object> data)
        {
            var entry = new LogEntry
            {
                Level = level,
                Message = message,
                Data = data,
                At = DateTime.UtcNow.ToString("o")
            };
            result.Logs.Add(entry);
            logFn?.Invoke(entry);
        }

        private List<SignatureInfo> MergeSignatures(List<SignatureInfo> digital, List<SignatureInfo> text)
        {
            var all = new List<SignatureInfo>();
            if (digital != null) all.AddRange(digital);
            if (text != null) all.AddRange(text);
            return all
                .GroupBy(s => $"{s.Method}|{s.FieldName}|{s.Page1}|{s.SignerName}")
                .Select(g => g.First())
                .ToList();
        }

        private List<SignatureInfo> BuildFieldSignatures(DespachoDocumentInfo doc)
        {
            var list = new List<SignatureInfo>();
            if (doc == null || doc.Fields == null) return list;
            if (!doc.Fields.TryGetValue("ASSINANTE", out var ass) || ass.Method == "not_found")
                return list;

            var sig = new SignatureInfo
            {
                Method = "field_assinante",
                FieldName = "",
                SignerName = ass.Value ?? "",
                Page1 = ass.Evidence?.Page1 ?? 0,
                BBoxN = ass.Evidence?.BBoxN,
                Snippet = ass.Evidence?.Snippet ?? ""
            };
            if (doc.Fields.TryGetValue("DATA", out var data) && data.Method != "not_found")
                sig.SignDate = data.Value;
            list.Add(sig);
            return list;
        }
    }
}
