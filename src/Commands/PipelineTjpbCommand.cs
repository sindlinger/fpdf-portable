using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Utils;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using FilterPDF.Strategies;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Exporta JSON único para o pipeline (TJ-PB): documentos + texto + metadados.
    /// Uso: fpdf pipeline-tjpb <arquivo.pdf> [--input-dir <dir>] [--output <path>]
    /// </summary>
    public class PipelineTjpbCommand : Command
    {
        public override string Name => "pipeline-tjpb";
        public override string Description => "Exporta documentos (bookmarks), ranges, headers/footers, tipos, texto completo para o pipeline TJ-PB.";

        public override void Execute(string[] args)
        {
            var opts = ParseArgs(args);
            if (string.IsNullOrWhiteSpace(opts.InputFile) && string.IsNullOrWhiteSpace(opts.InputDir))
            {
                ShowHelp();
                return;
            }

            var outputs = new List<object>();
            var files = new List<string>();
            if (!string.IsNullOrWhiteSpace(opts.InputFile))
                files.Add(opts.InputFile);
            if (!string.IsNullOrWhiteSpace(opts.InputDir))
                files.AddRange(Directory.GetFiles(opts.InputDir, "*.pdf", SearchOption.AllDirectories));

            foreach (var pdf in files.Distinct())
            {
                try
                {
                    var analysis = new PDFAnalyzer(pdf).AnalyzeFull();
                    var segmenter = new DocumentSegmenter(new DocumentSegmentationConfig());
                    var docs = segmenter.FindDocuments(analysis);
                    if (docs == null || docs.Count == 0)
                    {
                        // Fallback: pelo menos 1 documento cobrindo todo o PDF
                        docs = new List<DocumentBoundary>
                        {
                            new DocumentBoundary
                            {
                                Number = 1,
                                StartPage = 1,
                                EndPage = analysis.DocumentInfo.TotalPages,
                                Confidence = 1.0,
                                DetectedType = ""
                            }
                        };
                    }
                    var bookmarks = new List<(int page, string title)>();
                    FlattenBookmarks(analysis.Bookmarks.RootItems, bookmarks);

                    foreach (var d in docs)
                    {
                        var header = MostCommon(analysis.Pages, d.StartPage, d.EndPage, p => p.Headers);
                        var footer = MostCommon(analysis.Pages, d.StartPage, d.EndPage, p => p.Footers);
                        var label = ResolveLabel(bookmarks, d.StartPage, d.EndPage, d.Number);
                        var fullText = ExtractLayoutTextRange(pdf, d.StartPage, d.EndPage);
                        if (string.IsNullOrWhiteSpace(fullText))
                        {
                            // fallback para texto simples do AnalyzeFull
                            fullText = ExtractTextRange(analysis, d.StartPage, d.EndPage);
                        }
                        var imagesCount = CountImages(analysis, d.StartPage, d.EndPage);
                        var fonts = CollectFonts(analysis, d.StartPage, d.EndPage);
                        var pageSize = PageSizeInfo(analysis, d.StartPage, d.EndPage);
                        var date = ExtractDate(footer);
                        var origin = DeriveOrigin(analysis, d.StartPage, header);
                        var footerLines = LastLines(analysis, d.EndPage, 6);
                        var signer = ExtractSigner(footerLines);
                        var role = ExtractRole(footerLines);

                        outputs.Add(new
                        {
                            process = Path.GetFileNameWithoutExtension(pdf),
                            pdf_path = pdf,
                            total_pages = analysis.DocumentInfo.TotalPages,
                            doc_pages = d.PageCount,
                            doc_label = label,
                            start_page = d.StartPage,
                            end_page = d.EndPage,
                            header,
                            origin,
                            subheader = d.DetectedType ?? "",
                            footer,
                            is_attachment = (label ?? "").ToLower().Contains("anexo"),
                            doc_type = d.DetectedType ?? "",
                            text = fullText,
                            images = imagesCount,
                            fonts,
                            page_size = pageSize,
                            has_signature_image = d.HasSignatureImage,
                            doc_date = date,
                            signer,
                            signer_role = role,
                            footer_lines = footerLines
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[pipeline-tjpb] erro em {pdf}: {ex.Message}");
                }
            }

            var payload = new { documents = outputs };
            var outPath = string.IsNullOrWhiteSpace(opts.OutputPath) ? "tmp/pipeline/step2/fpdf.json" : opts.OutputPath;
            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, JsonConvert.SerializeObject(payload, Formatting.Indented));
            Console.WriteLine($"[pipeline-tjpb] gerado {outputs.Count} docs em {outPath}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf pipeline-tjpb <arquivo.pdf> [--input-dir DIR] [--output out.json]");
            Console.WriteLine("Exporta JSON com documentos (bookmarks), ranges, headers/footers, tipos, texto completo, imagens, fontes, tamanho de página.");
        }

        private class Opts
        {
            public string InputFile { get; set; } = "";
            public string InputDir { get; set; } = "";
            public string OutputPath { get; set; } = "";
        }

        private Opts ParseArgs(string[] args)
        {
            var o = new Opts();
            foreach (var a in args)
            {
                // positional: first non-flag is input file
            }
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("-") && string.IsNullOrEmpty(o.InputFile))
                {
                    o.InputFile = a;
                    continue;
                }
                if (a == "--input-dir" && i + 1 < args.Length) { o.InputDir = args[++i]; continue; }
                if (a == "--output" && i + 1 < args.Length) { o.OutputPath = args[++i]; continue; }
            }
            return o;
        }

        private static void FlattenBookmarks(IEnumerable<BookmarkItem> items, List<(int page, string title)> acc)
        {
            foreach (var it in items)
            {
                acc.Add((it.Destination.PageNumber, it.Title ?? ""));
                if (it.Children != null && it.Children.Count > 0)
                    FlattenBookmarks(it.Children, acc);
            }
        }

        private static string ResolveLabel(List<(int page, string title)> bookmarks, int start, int end, int numberFallback)
        {
            var hit = bookmarks
                .Where(b => b.page >= start && b.page <= end)
                .OrderBy(b => b.page)
                .Select(b => b.title)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(hit))
                return hit.Trim();
            return $"doc_{numberFallback:00}";
        }

        private static string MostCommon(IEnumerable<PageAnalysis> pages, int start, int end, Func<PageAnalysis, IEnumerable<string>> selector)
        {
            var slice = pages.Where(p => p.PageNumber >= start && p.PageNumber <= end)
                             .SelectMany(selector)
                             .Where(s => !string.IsNullOrWhiteSpace(s))
                             .Select(s => s.Trim());
            return slice
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "";
        }

        private static string ExtractTextRange(PDFAnalysisResult analysis, int start, int end)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in analysis.Pages.Where(p => p.PageNumber >= start && p.PageNumber <= end)
                                            .OrderBy(p => p.PageNumber))
            {
                var text = p.TextInfo?.PageText ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Extrai texto preservando layout (mesma estratégia do comando `extract`, estratégia 2).
        /// </summary>
        private static string ExtractLayoutTextRange(string pdfPath, int start, int end, int strategy = 2)
        {
            try
            {
                using var reader = PdfAccessManager.CreateTemporaryReader(pdfPath);
                var sb = new System.Text.StringBuilder();
                int total = reader.NumberOfPages;
                int a = Math.Max(1, start);
                int b = Math.Min(end, total);
                for (int page = a; page <= b; page++)
                {
                    ITextExtractionStrategy strat = strategy switch
                    {
                        1 => new LayoutPreservingStrategy(),
                        3 => new ColumnDetectionStrategy(),
                        _ => new AdvancedLayoutStrategy(),
                    };
                    string pageText = PdfTextExtractor.GetTextFromPage(reader, page, strat);
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        sb.AppendLine(pageText);
                    }
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static List<string> FirstLines(PDFAnalysisResult analysis, int pageNumber, int maxLines = 4)
        {
            var p = analysis.Pages.FirstOrDefault(pg => pg.PageNumber == pageNumber);
            if (p == null) return new List<string>();
            var text = p.TextInfo?.PageText ?? "";
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.Trim())
                       .Where(l => l.Length > 0)
                       .Take(maxLines)
                       .ToList();
        }

        private static List<string> LastLines(PDFAnalysisResult analysis, int pageNumber, int maxLines = 6)
        {
            var p = analysis.Pages.FirstOrDefault(pg => pg.PageNumber == pageNumber);
            if (p == null) return new List<string>();
            var text = p.TextInfo?.PageText ?? "";
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .ToList();
            return lines.TakeLast(Math.Min(maxLines, lines.Count)).ToList();
        }

        private static int CountImages(PDFAnalysisResult analysis, int start, int end)
        {
            return analysis.Pages
                .Where(p => p.PageNumber >= start && p.PageNumber <= end)
                .Sum(p => p.Resources?.Images?.Count ?? 0);
        }

        private static List<string> CollectFonts(PDFAnalysisResult analysis, int start, int end)
        {
            return analysis.Pages
                .Where(p => p.PageNumber >= start && p.PageNumber <= end)
                .SelectMany(p => p.FontInfo ?? new List<FontInfo>())
                .Select(f => string.IsNullOrWhiteSpace(f.BaseFont) ? f.Name : f.BaseFont)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static object PageSizeInfo(PDFAnalysisResult analysis, int start, int end)
        {
            var p = analysis.Pages.FirstOrDefault(pg => pg.PageNumber == start) ?? analysis.Pages.FirstOrDefault();
            if (p == null || p.Size == null) return null;
            return new
            {
                width_pts = p.Size.WidthPoints,
                height_pts = p.Size.HeightPoints,
                paper = p.Size.GetPaperSize()
            };
        }

        private static string ExtractDate(string footer)
        {
            if (string.IsNullOrWhiteSpace(footer)) return "";
            // procura dd/mm/aaaa ou dd de mes de aaaa
            var m1 = Regex.Match(footer, @"\b([0-3]?\d/[01]?\d/\d{4})\b");
            if (m1.Success) return m1.Value;
            var m2 = Regex.Match(footer, @"\b([0-3]?\d)\s+de\s+([A-Za-zçãéíóúâêô]+)\s+de\s+(\d{4})", RegexOptions.IgnoreCase);
            if (m2.Success) return m2.Value;
            return "";
        }

        private static string DeriveOrigin(PDFAnalysisResult analysis, int startPage, string header)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(header)) candidates.Add(header);
            candidates.AddRange(FirstLines(analysis, startPage, 4));
            var origin = candidates.FirstOrDefault(l =>
                l.Contains("tribunal", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("juízo", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("comarca", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("diretoria", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("vara", StringComparison.OrdinalIgnoreCase)
            );
            return origin ?? candidates.FirstOrDefault() ?? "";
        }

        private static string ExtractSigner(List<string> footerLines)
        {
            // heurística: linha com 2-6 palavras e >=50% maiúsculas
            foreach (var line in footerLines.AsEnumerable().Reverse())
            {
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length is < 2 or > 6) continue;
                var upperFrac = words.Count(w => w.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) / (double)words.Length;
                if (upperFrac >= 0.5)
                    return line;
            }
            return "";
        }

        private static string ExtractRole(List<string> footerLines)
        {
            var roles = new[] { "juiz", "juíza", "diretor", "diretora", "chefe", "secretário", "secretária", "perito", "engenheiro", "médico", "contador", "coordenador", "coordenadora" };
            foreach (var line in footerLines.AsEnumerable().Reverse())
            {
                var low = line.ToLower();
                var hit = roles.FirstOrDefault(r => low.Contains(r));
                if (hit != null) return hit;
            }
            return "";
        }
    }
}
