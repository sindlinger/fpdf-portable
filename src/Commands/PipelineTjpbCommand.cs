using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Executa a etapa FPDF do pipeline-tjpb: processa todos os PDFs de um diretório
    /// e gera um JSON consolidado compatível com tmp/pipeline/step2/fpdf.json.
    ///
    /// Uso:
    ///   fpdf pipeline-tjpb --input-dir tmp/preprocessor_stage --output tmp/pipeline/step2/fpdf.json
    ///
    /// Campos gerados por documento:
    /// - process, pdf_path
    /// - doc_label, start_page, end_page, doc_pages, total_pages
    /// - text (concatenação das páginas do documento)
    /// - fonts (nomes), images (quantidade), page_size, has_signature_image (heurística)
    /// </summary>
    public class PipelineTjpbCommand : Command
    {
        public override string Name => "pipeline-tjpb";
        public override string Description => "Etapa FPDF do pipeline-tjpb: consolida documentos em JSON";

        public override void Execute(string[] args)
        {
            string inputDir = ".";
            string output = "fpdf.json";
            bool splitAnexos = false; // backward compat (anexos now covered by bookmark docs)
            int maxBookmarkPages = 30;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--input-dir" && i + 1 < args.Length) inputDir = args[i + 1];
                if (args[i] == "--output" && i + 1 < args.Length) output = args[i + 1];
                if (args[i] == "--split-anexos") splitAnexos = true;
                if (args[i] == "--max-bookmark-pages" && i + 1 < args.Length && int.TryParse(args[i + 1], out var m)) maxBookmarkPages = m;
            }

            var dir = new DirectoryInfo(inputDir);
            if (!dir.Exists)
            {
                Console.WriteLine($"Diretório não encontrado: {inputDir}");
                return;
            }

            var pdfs = dir.GetFiles("*.pdf").OrderBy(f => f.Name).ToList();
            var allDocs = new List<Dictionary<string, object>>();

            foreach (var pdf in pdfs)
            {
                try
                {
                    var analysis = new PDFAnalyzer(pdf.FullName).AnalyzeFull();
                    var bookmarkDocs = BuildBookmarkBoundaries(analysis, maxBookmarkPages);
                    var segmenter = new DocumentSegmenter(new DocumentSegmentationConfig());
                    var docs = bookmarkDocs.Count > 0 ? bookmarkDocs : segmenter.FindDocuments(analysis);

                    foreach (var d in docs)
                    {
                        var obj = BuildDocObject(d, analysis, pdf.FullName);
                        allDocs.Add(obj);

                        // Legacy split-anexos now redundant; kept for compatibility
                        if (splitAnexos && d.DetectedType == "anexo")
                        {
                            var anexosChildren = SplitAnexos(d, analysis, pdf.FullName);
                            allDocs.AddRange(anexosChildren);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[pipeline-tjpb] WARN {pdf.Name}: {ex.Message}");
                }
            }

            var result = new { documents = allDocs };
            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
            File.WriteAllText(output, json);
            Console.WriteLine($"[pipeline-tjpb] {allDocs.Count} documentos -> {output}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf pipeline-tjpb --input-dir <dir> --output fpdf.json [--split-anexos] [--max-bookmark-pages N]");
            Console.WriteLine("Gera JSON compatível com tmp/pipeline/step2/fpdf.json para o CI Dashboard.");
            Console.WriteLine("--split-anexos: cria subdocumentos a partir de bookmarks 'Anexo/Anexos' dentro de cada documento.");
            Console.WriteLine("--max-bookmark-pages: se um bookmark tiver mais páginas que N, ele é re-segmentado internamente (default 30).");
        }

        private Dictionary<string, object> BuildDocObject(DocumentBoundary d, PDFAnalysisResult analysis, string pdfPath)
        {
            var docText = string.Join("\n", Enumerable.Range(d.StartPage, d.PageCount)
                                                      .Select(p => analysis.Pages[p - 1].TextInfo.PageText ?? ""));

            var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int p = d.StartPage; p <= d.EndPage; p++)
                foreach (var f in analysis.Pages[p - 1].TextInfo.Fonts)
                    fonts.Add(f.Name);

            int images = 0;
            bool hasSignature = false;
            int wordCount = 0;
            int charCount = 0;
            double wordsArea = 0;
            double pageAreaAcc = 0;
            var wordsWithCoords = new List<Dictionary<string, object>>();
            var docBookmarks = ExtractBookmarksForRange(analysis, d.StartPage, d.EndPage);
            var unpackedAttachments = new List<Dictionary<string, object>>();
            string header = string.Empty;
            string footer = string.Empty;

            for (int p = d.StartPage; p <= d.EndPage; p++)
            {
                images += analysis.Pages[p - 1].Resources.Images?.Count ?? 0;
                // Heurística simples: imagem com largura/altura > 100 pode ser assinatura
                hasSignature |= (analysis.Pages[p - 1].Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false);

                var page = analysis.Pages[p - 1];
                wordCount += page.TextInfo.WordCount;
                charCount += page.TextInfo.CharacterCount;

                var size = page.Size;
                var pageArea = Math.Max(1, size.Width * size.Height);
                pageAreaAcc += pageArea;

                foreach (var w in page.TextInfo.Words)
                {
                    double wArea = Math.Max(0, (w.X1 - w.X0) * (w.Y1 - w.Y0));
                    wordsArea += wArea;
                    wordsWithCoords.Add(new Dictionary<string, object>
                    {
                        ["text"] = w.Text,
                        ["page"] = p,
                        ["font"] = w.Font,
                        ["size"] = w.Size,
                        ["bold"] = w.Bold,
                        ["italic"] = w.Italic,
                        ["underline"] = w.Underline,
                        ["render_mode"] = w.RenderMode,
                        ["char_spacing"] = w.CharSpacing,
                        ["word_spacing"] = w.WordSpacing,
                        ["horiz_scaling"] = w.HorizontalScaling,
                        ["rise"] = w.Rise,
                        ["x0"] = w.X0,
                        ["y0"] = w.Y0,
                        ["x1"] = w.X1,
                        ["y1"] = w.Y1,
                        ["nx0"] = w.NormX0,
                        ["ny0"] = w.NormY0,
                        ["nx1"] = w.NormX1,
                        ["ny1"] = w.NormY1
                    });
                }

                if (string.IsNullOrEmpty(header) && page.TextInfo.Headers.Any()) header = page.TextInfo.Headers.First();
                if (string.IsNullOrEmpty(footer) && page.TextInfo.Footers.Any()) footer = page.TextInfo.Footers.First();
            }

            var pageSize = analysis.Pages.First().Size.GetPaperSize();

            // Heurística: bookmark intitulado "Anexo" ou "Anexos" pode conter vários documentos fundidos.
            // Vamos registrar todos os bookmarks de nível 1/2 com esse título dentro do range,
            // para permitir pós-processamento (split fino) sem quebrar a segmentação principal.
            var anexos = docBookmarks
                .Where(b => Regex.IsMatch(b["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase))
                .ToList();
            double textDensity = pageAreaAcc > 0 ? wordsArea / pageAreaAcc : 0;
            double blankRatio = 1 - textDensity;

            var docType = string.IsNullOrEmpty(d.DetectedType) ? "heuristic" : d.DetectedType;

            return new Dictionary<string, object>
            {
                ["process"] = Path.GetFileNameWithoutExtension(pdfPath),
                ["pdf_path"] = pdfPath,
                ["doc_label"] = ExtractDocumentName(d),
                ["doc_type"] = docType,
                ["start_page"] = d.StartPage,
                ["end_page"] = d.EndPage,
                ["doc_pages"] = d.PageCount,
                ["total_pages"] = analysis.DocumentInfo.TotalPages,
                ["text"] = docText,
                ["fonts"] = fonts.ToArray(),
                ["images"] = images,
                ["page_size"] = pageSize,
                ["has_signature_image"] = hasSignature,
                ["is_attachment"] = false,
                ["word_count"] = wordCount,
                ["char_count"] = charCount,
                ["text_density"] = textDensity,
                ["blank_ratio"] = blankRatio,
                ["words"] = wordsWithCoords,
                ["header"] = header,
                ["footer"] = footer,
                ["bookmarks"] = docBookmarks,
                ["anexos_bookmarks"] = anexos
            };
        }

        private List<Dictionary<string, object>> ExtractBookmarksForRange(PDFAnalysisResult analysis, int startPage, int endPage)
        {
            var list = new List<Dictionary<string, object>>();
            if (analysis.Bookmarks?.RootItems == null) return list;

            void Walk(IEnumerable<BookmarkItem> items)
            {
                foreach (var b in items)
                {
                    int page = b.Destination?.PageNumber ?? 0;
                    if (page >= startPage && page <= endPage && page > 0)
                    {
                        list.Add(new Dictionary<string, object>
                        {
                            ["title"] = b.Title,
                            ["page"] = page,
                            ["level"] = b.Level
                        });
                    }

                    if (b.Children != null && b.Children.Count > 0)
                        Walk(b.Children);
                }
            }

            Walk(analysis.Bookmarks.RootItems);
            return list.OrderBy(b => (int)b["page"]).ToList();
        }

        /// <summary>
        /// Converte bookmarks em limites de documentos. Bookmarks passam a ser "documentos".
        /// Se não houver bookmarks, retorna lista vazia (segmenter padrão será usado).
        /// </summary>
        private List<DocumentBoundary> BuildBookmarkBoundaries(PDFAnalysisResult analysis, int maxBookmarkPages)
        {
            var result = new List<DocumentBoundary>();
            var bms = ExtractBookmarksForRange(analysis, 1, analysis.DocumentInfo.TotalPages);
            if (bms.Count == 0) return result;

            for (int i = 0; i < bms.Count; i++)
            {
                int start = (int)bms[i]["page"];
                int end = (i + 1 < bms.Count) ? ((int)bms[i + 1]["page"]) - 1 : analysis.DocumentInfo.TotalPages;
                if (end < start) end = start;

                bool isAnexo = Regex.IsMatch(bms[i]["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase);
                bool needsSegment = isAnexo || ((end - start + 1) > maxBookmarkPages);

                // Se o bookmark for Anexo (sempre) ou muito grande, resegmenta internamente com o segmenter heurístico
                if (needsSegment)
                {
                    var segmenter = new DocumentSegmenter(new DocumentSegmentationConfig());
                    var subAnalysis = CloneRange(analysis, start, end);
                    var subDocs = segmenter.FindDocuments(subAnalysis);

                    foreach (var sd in subDocs)
                    {
                        sd.StartPage += (start - 1); // ajustar para páginas originais
                        sd.EndPage += (start - 1);
                        sd.DetectedType = isAnexo ? "anexo+segment" : "bookmark+segment";
                        result.Add(sd);
                    }

                    // Se não encontrou subdocs (caso extremo), registra o próprio bookmark
                    if (subDocs.Count == 0)
                    {
                        var fallback = new DocumentBoundary
                        {
                            StartPage = start,
                            EndPage = end,
                            DetectedType = isAnexo ? "anexo" : "bookmark",
                            FirstPageText = analysis.Pages[start - 1].TextInfo.PageText,
                            LastPageText = analysis.Pages[end - 1].TextInfo.PageText,
                            FullText = string.Join("\n", Enumerable.Range(start, end - start + 1).Select(p => analysis.Pages[p - 1].TextInfo.PageText)),
                            Fonts = new HashSet<string>(analysis.Pages.Skip(start - 1).Take(end - start + 1).SelectMany(p => p.TextInfo.Fonts.Select(f => f.Name)), StringComparer.OrdinalIgnoreCase),
                            PageSize = analysis.Pages.First().Size.GetPaperSize(),
                            HasSignatureImage = analysis.Pages.Skip(start - 1).Take(end - start + 1).Any(p => p.Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false),
                            TotalWords = analysis.Pages.Skip(start - 1).Take(end - start + 1).Sum(p => p.TextInfo.WordCount)
                        };
                        result.Add(fallback);
                    }
                }
                else
                {
                    var boundary = new DocumentBoundary
                    {
                        StartPage = start,
                        EndPage = end,
                        DetectedType = isAnexo ? "anexo" : "bookmark",
                        FirstPageText = analysis.Pages[start - 1].TextInfo.PageText,
                        LastPageText = analysis.Pages[end - 1].TextInfo.PageText,
                        FullText = string.Join("\n", Enumerable.Range(start, end - start + 1).Select(p => analysis.Pages[p - 1].TextInfo.PageText)),
                        Fonts = new HashSet<string>(analysis.Pages.Skip(start - 1).Take(end - start + 1).SelectMany(p => p.TextInfo.Fonts.Select(f => f.Name)), StringComparer.OrdinalIgnoreCase),
                        PageSize = analysis.Pages.First().Size.GetPaperSize(),
                        HasSignatureImage = analysis.Pages.Skip(start - 1).Take(end - start + 1).Any(p => p.Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false),
                        TotalWords = analysis.Pages.Skip(start - 1).Take(end - start + 1).Sum(p => p.TextInfo.WordCount)
                    };
                    result.Add(boundary);
                }
            }

            return result;
        }

        private PDFAnalysisResult CloneRange(PDFAnalysisResult analysis, int startPage, int endPage)
        {
            var clone = new PDFAnalysisResult
            {
                FilePath = analysis.FilePath,
                FileSize = analysis.FileSize,
                AnalysisDate = analysis.AnalysisDate,
                Metadata = analysis.Metadata,
                XMPMetadata = analysis.XMPMetadata,
                DocumentInfo = new DocumentInfo { TotalPages = endPage - startPage + 1 },
                Pages = analysis.Pages.Skip(startPage - 1).Take(endPage - startPage + 1)
                    .Select(p => p) // shallow copy is enough for segmentation heuristics
                    .ToList(),
                Security = analysis.Security,
                Resources = analysis.Resources,
                Statistics = analysis.Statistics,
                Accessibility = analysis.Accessibility,
                Layers = analysis.Layers,
                Signatures = analysis.Signatures,
                ColorProfiles = analysis.ColorProfiles,
                Bookmarks = new BookmarkStructure { RootItems = new List<BookmarkItem>(), MaxDepth = 0, TotalCount = 0 },
                PDFACompliance = analysis.PDFACompliance,
                Multimedia = analysis.Multimedia,
                PDFAValidation = analysis.PDFAValidation,
                SecurityInfo = analysis.SecurityInfo,
                AccessibilityInfo = analysis.AccessibilityInfo
            };
            return clone;
        }

        private string ExtractDocumentName(DocumentBoundary d)
        {
            // mesmo critério do FpdfDocumentsCommand: primeira linha do FullText/FirstPageText
            var text = d.FullText ?? d.FirstPageText ?? "";
            var firstLine = text.Split('\n').FirstOrDefault() ?? "";
            return firstLine.Length > 80 ? firstLine.Substring(0, 80) + "..." : firstLine;
        }

        private List<Dictionary<string, object>> SplitAnexos(DocumentBoundary parent, PDFAnalysisResult analysis, string pdfPath)
        {
            var list = new List<Dictionary<string, object>>();

            // Pega bookmarks "Anexo/Anexos" no range do documento
            var anexos = ExtractBookmarksForRange(analysis, parent.StartPage, parent.EndPage)
                .Where(b => Regex.IsMatch(b["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase))
                .OrderBy(b => (int)b["page"])
                .ToList();

            if (anexos.Count == 0) return list;

            // Cria subfaixas a partir dos bookmarks: cada anexo vai da página do bookmark até a página anterior ao próximo bookmark (ou fim do doc)
            for (int i = 0; i < anexos.Count; i++)
            {
                int start = (int)anexos[i]["page"];
                int end = (i + 1 < anexos.Count) ? ((int)anexos[i + 1]["page"]) - 1 : parent.EndPage;
                if (start < parent.StartPage || start > parent.EndPage) continue;
                if (end < start) end = start;

                var boundary = new DocumentBoundary
                {
                    StartPage = start,
                    EndPage = end,
                    DetectedType = "anexo",
                    FirstPageText = analysis.Pages[start - 1].TextInfo.PageText,
                    LastPageText = analysis.Pages[end - 1].TextInfo.PageText,
                    FullText = string.Join("\n", Enumerable.Range(start, end - start + 1).Select(p => analysis.Pages[p - 1].TextInfo.PageText)),
                    Fonts = parent.Fonts,
                    PageSize = parent.PageSize,
                    HasSignatureImage = parent.HasSignatureImage,
                    TotalWords = parent.TotalWords
                };

                var obj = BuildDocObject(boundary, analysis, pdfPath);
                obj["parent_doc_label"] = ExtractDocumentName(parent);
                obj["doc_label"] = anexos[i]["title"]?.ToString() ?? "Anexo";
                obj["doc_type"] = "anexo_split";
                list.Add(obj);
            }

            return list;
        }
    }
}
