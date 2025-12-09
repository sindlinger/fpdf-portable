using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FilterPDF;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Stats Command - Statistical analysis of PDFs
    /// Works for single PDF (detailed) or range (aggregated)
    /// </summary>
    public class FpdfStatsCommand : Command
    {
        public override string Name => "stats";
        public override string Description => "Statistical analysis of PDFs";

        public override void ShowHelp()
        {
            Console.WriteLine("STATS - Statistical analysis of PDFs");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("    fpdf <index|range> stats [options]");
            Console.WriteLine("    fpdf stats --input-dir <directory> [options]");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("    --scanned            Focus on scanned documents analysis");
            Console.WriteLine("    --type               Analyze document types (scanned/text/mixed)");
            Console.WriteLine("    --composition        Show composition statistics");
            Console.WriteLine("    --page-sizes         Show page dimension statistics and distribution");
            Console.WriteLine("    --images             Show image count, sizes and distribution statistics");
            Console.WriteLine("    --keywords <list>    Search for specific keywords (comma-separated)");
            Console.WriteLine("    --detailed           Show detailed analysis (for single PDF)");
            Console.WriteLine("    --format <fmt>       Output format: txt, json (default: txt)");
            Console.WriteLine("    -o, --output <file>  Save output to file");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("    fpdf 1 stats                    # Detailed stats for PDF #1");
            Console.WriteLine("    fpdf 1-100 stats                # Aggregated stats for range");
            Console.WriteLine("    fpdf 1-100 stats --scanned      # Focus on scanned documents");
            Console.WriteLine("    fpdf 1-100 stats --type         # Document type analysis");
            Console.WriteLine("    fpdf 1-100 stats --page-sizes   # Analyze page size distribution");
            Console.WriteLine("    fpdf 1-100 stats --images       # Analyze image distribution and sizes");
            Console.WriteLine("    fpdf 1 stats --detailed         # Very detailed single PDF analysis");
            Console.WriteLine();
            Console.WriteLine("OUTPUT:");
            Console.WriteLine("    Single PDF: Detailed composition, content, and metadata");
            Console.WriteLine("    Range: Aggregated statistics and patterns");
        }

        public override void Execute(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }


            var options = ParseOptions(args);
            var input = args[0];

            // Check if it's a directory scan
            if (options.ContainsKey("input-dir"))
            {
                ExecuteDirectoryStats(options["input-dir"], options);
                return;
            }

            // Parse range or single index
            var indices = ParseIndices(input);
            if (indices.Count == 0)
            {
                Console.WriteLine($"Error: Invalid input '{input}'");
                return;
            }

            if (indices.Count == 1)
            {
                // Single PDF - detailed analysis
                ExecuteSinglePdfStats(indices[0], options);
            }
            else
            {
                // Range - aggregated analysis
                ExecuteRangeStats(indices, options);
            }
        }

        private void ExecuteSinglePdfStats(int index, Dictionary<string, string> options)
        {
            var rows = PgAnalysisLoader.ListProcesses();
            if (index < 1 || index > rows.Count)
            {
                Console.WriteLine($"Error: cache {index} not found in Postgres");
                return;
            }
            var row = rows[index - 1];
            var summary = PgAnalysisLoader.GetProcessSummaryById(row.Id);
            if (summary == null)
            {
                Console.WriteLine($"Error: cache {index} not found in Postgres");
                return;
            }

            if (options.ContainsKey("format") && options["format"] == "json")
            {
                var obj = new
                {
                    summary.ProcessNumber,
                    summary.TotalPages,
                    summary.TotalWords,
                    summary.TotalImages,
                    summary.TotalFonts,
                    summary.ScanRatio,
                    summary.IsScanned,
                    summary.IsEncrypted,
                    Permissions = new
                    {
                        summary.PermCopy, summary.PermPrint, summary.PermAnnotate, summary.PermFillForms, summary.PermExtract, summary.PermAssemble, summary.PermPrintHq
                    },
                    Resources = new { summary.HasJs, summary.HasEmbedded, summary.HasAttachments, summary.HasMultimedia, summary.HasForms },
                    Metadata = new { summary.MetaTitle, summary.MetaAuthor, summary.MetaSubject, summary.MetaKeywords }
                };
                Console.WriteLine(JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            else
            {
                OutputSinglePdfText(summary, index);
            }
        }

        private void OutputSinglePdfText(PgAnalysisLoader.ProcessSummary summary, int index)
        {
            var output = new StringBuilder();
            var fileName = summary.ProcessNumber;

            output.AppendLine($"ANÁLISE ESTATÍSTICA - PDF #{index}");
            output.AppendLine("================================");
            output.AppendLine($"📄 Arquivo: {fileName}");
            output.AppendLine($"📦 Tamanho cache: n/d (Postgres)");
            output.AppendLine();
            output.AppendLine("COMPOSIÇÃO:");
            output.AppendLine($"├─ Total de páginas: {summary.TotalPages}");
            var scannedPages = (int)Math.Round(summary.ScanRatio * summary.TotalPages / 100m);
            var textPages = summary.TotalPages - scannedPages;
            output.AppendLine($"├─ Páginas escaneadas: {scannedPages} ({summary.ScanRatio:F1}%)");
            output.AppendLine($"├─ Páginas texto: {textPages} ({(summary.TotalPages>0 ? 100 - summary.ScanRatio : 0):F1}%)");
            output.AppendLine($"└─ Páginas em branco: n/d");
            output.AppendLine();

            output.AppendLine("CONTEÚDO:");
            output.AppendLine($"├─ Total de palavras: {summary.TotalWords:N0}");
            output.AppendLine($"├─ Total de imagens: {summary.TotalImages}");
            output.AppendLine($"├─ Total de fontes: {summary.TotalFonts}");
            output.AppendLine($"└─ Média palavras/página: {(summary.TotalPages>0 ? summary.TotalWords/summary.TotalPages : 0)}");
            output.AppendLine();

            output.AppendLine("CLASSIFICAÇÃO:");
            if (summary.ScanRatio >= 90)
            {
                output.AppendLine($"→ Tipo: Documento Escaneado ({summary.ScanRatio:F1}%)");
                output.AppendLine("→ Recomendação: Aplicar OCR");
            }
            else if (summary.ScanRatio >= 50)
            {
                output.AppendLine($"→ Tipo: Documento Misto ({summary.ScanRatio:F1}% escaneado)");
                output.AppendLine("→ Recomendação: OCR parcial pode ser necessário");
            }
            else
            {
                output.AppendLine("→ Tipo: Documento de Texto Nativo");
                output.AppendLine("→ Texto já extraível");
            }

            output.AppendLine();
            output.AppendLine("PERMISSÕES/SEGURANÇA:");
            output.AppendLine($"- Criptografado: {summary.IsEncrypted}");
            output.AppendLine($"- Pode copiar: {summary.PermCopy} | Imprimir: {summary.PermPrint} | Anotar: {summary.PermAnnotate} | Formulários: {summary.PermFillForms}");

            output.AppendLine();
            output.AppendLine("RECURSOS:");
            output.AppendLine($"- JS: {summary.HasJs}, Embedded: {summary.HasEmbedded}, Anexos: {summary.HasAttachments}, Multimedia: {summary.HasMultimedia}, Forms: {summary.HasForms}");

            output.AppendLine();
            output.AppendLine("METADADOS:");
            output.AppendLine($"- Título: {summary.MetaTitle}");
            output.AppendLine($"- Autor: {summary.MetaAuthor}");
            output.AppendLine($"- Assunto: {summary.MetaSubject}");

            Console.Write(output.ToString());
        }

        private void ExecuteRangeStats(List<int> indices, Dictionary<string, string> options)
        {
            var output = new StringBuilder();
            var stats = new RangeStatistics();

            // Analyze each PDF in range
            foreach (var index in indices)
            {
                var pg = PgAnalysisLoader.GetByIndex(index.ToString());
                if (!pg.HasValue)
                {
                    stats.InvalidCount++;
                    continue;
                }
                var (analysis, row) = pg.Value;
                var json = string.IsNullOrWhiteSpace(row.Json) ? JsonConvert.SerializeObject(analysis) : row.Json;
                var cache = JsonConvert.DeserializeObject<dynamic>(json);
                AnalyzePdf(cache, row.ProcessNumber, stats, index);
            }

            // Output results
            if (options.ContainsKey("scanned"))
            {
                OutputScannedAnalysis(stats, indices, output);
            }
            else if (options.ContainsKey("type"))
            {
                OutputTypeAnalysis(stats, indices, output);
            }
            else if (options.ContainsKey("page-sizes"))
            {
                OutputPageSizeAnalysis(stats, indices, output);
            }
            else if (options.ContainsKey("images"))
            {
                OutputImageAnalysis(stats, indices, output);
            }
            else
            {
                OutputQuickStats(stats, indices, output);
            }

            // Save or display
            if (options.ContainsKey("output"))
            {
                File.WriteAllText(options["output"], output.ToString());
                Console.WriteLine($"Output saved to: {options["output"]}");
            }
            else
            {
                Console.Write(output.ToString());
            }
        }

        private void OutputQuickStats(RangeStatistics stats, List<int> indices, StringBuilder output)
        {
            var rangeStr = GetRangeString(indices);
            output.AppendLine($"ESTATÍSTICAS RÁPIDAS (PDFs {rangeStr})");
            output.AppendLine("==================================");
            output.AppendLine($"✓ PDFs válidos: {stats.ValidCount}/{indices.Count} ({stats.ValidCount * 100.0 / indices.Count:F0}%)");
            output.AppendLine($"✓ Total páginas: {stats.TotalPages:N0}");
            output.AppendLine($"✓ Escaneados: {stats.ScannedPdfCount} ({stats.ScannedPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine($"✓ Texto nativo: {stats.TextPdfCount} ({stats.TextPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine($"✓ Tamanho cache: {stats.TotalCacheSize / (1024 * 1024)} MB");
        }

        private void OutputScannedAnalysis(RangeStatistics stats, List<int> indices, StringBuilder output)
        {
            var rangeStr = GetRangeString(indices);
            output.AppendLine("ANÁLISE DE DOCUMENTOS ESCANEADOS");
            output.AppendLine("=================================");
            output.AppendLine($"Range: {rangeStr}");
            output.AppendLine($"Threshold: 90%");
            output.AppendLine();
            output.AppendLine("RESUMO:");
            output.AppendLine($"├─ Total analisado: {stats.ValidCount} PDFs");
            output.AppendLine($"├─ Escaneados (>90%): {stats.ScannedPdfCount} PDFs ({stats.ScannedPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine($"├─ Parcialmente escaneados: {stats.MixedPdfCount} PDFs ({stats.MixedPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine($"└─ Texto nativo: {stats.TextPdfCount} PDFs ({stats.TextPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine();
            
            if (stats.ScannedPdfCount > 0)
            {
                output.AppendLine("DETALHES DOS ESCANEADOS:");
                output.AppendLine($"├─ Total de páginas escaneadas: {stats.TotalScannedPages:N0}");
                output.AppendLine($"├─ Média de páginas por PDF: {stats.TotalScannedPages / stats.ScannedPdfCount}");
                output.AppendLine($"└─ Confiança média: {stats.AverageScannedConfidence:F1}%");
                output.AppendLine();
            }

            output.AppendLine("RECOMENDAÇÃO:");
            if (stats.ScannedPdfCount > 0)
                output.AppendLine($"→ {stats.ScannedPdfCount} PDFs precisam de OCR");
            if (stats.MixedPdfCount > 0)
                output.AppendLine($"→ {stats.MixedPdfCount} PDFs podem precisar de OCR parcial");
        }

        private void OutputTypeAnalysis(RangeStatistics stats, List<int> indices, StringBuilder output)
        {
            var rangeStr = GetRangeString(indices);
            output.AppendLine($"ANÁLISE DE TIPOS DE DOCUMENTO");
            output.AppendLine("==============================");
            output.AppendLine($"Range: {rangeStr}");
            output.AppendLine();
            output.AppendLine("DISTRIBUIÇÃO:");
            output.AppendLine($"├─ Documentos escaneados: {stats.ScannedPdfCount} ({stats.ScannedPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine($"├─ Documentos de texto: {stats.TextPdfCount} ({stats.TextPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine($"└─ Documentos mistos: {stats.MixedPdfCount} ({stats.MixedPdfCount * 100.0 / stats.ValidCount:F0}%)");
            output.AppendLine();
            output.AppendLine("PÁGINAS POR TIPO:");
            output.AppendLine($"├─ Páginas escaneadas: {stats.TotalScannedPages:N0}");
            output.AppendLine($"├─ Páginas de texto: {stats.TotalTextPages:N0}");
            output.AppendLine($"└─ Total de páginas: {stats.TotalPages:N0}");
        }

        private void OutputImageAnalysis(RangeStatistics stats, List<int> indices, StringBuilder output)
        {
            var rangeStr = GetRangeString(indices);
            output.AppendLine("ANÁLISE DE IMAGENS");
            output.AppendLine("==================");
            output.AppendLine($"Range: {rangeStr}");
            output.AppendLine();

            // Estatísticas gerais
            output.AppendLine("ESTATÍSTICAS GERAIS:");
            output.AppendLine($"├─ Total de imagens: {stats.TotalImages:N0}");
            output.AppendLine($"├─ Páginas com imagens: {stats.PagesWithImages:N0}");
            output.AppendLine($"├─ Média de imagens/página: {(stats.PagesWithImages > 0 ? (double)stats.TotalImages / stats.PagesWithImages : 0):F1}");
            output.AppendLine($"└─ PDFs analisados: {stats.ValidCount}");
            output.AppendLine();

            // Distribuição de imagens por página
            if (stats.ImagesPerPageDistribution.Count > 0)
            {
                output.AppendLine("DISTRIBUIÇÃO DE IMAGENS POR PÁGINA:");
                var sortedDist = stats.ImagesPerPageDistribution.OrderByDescending(kvp => kvp.Value);
                foreach (var dist in sortedDist.Take(10))
                {
                    output.AppendLine($"├─ {dist.Key} imagem(ns): {dist.Value} páginas");
                }
                output.AppendLine();
            }

            // Top 20 maiores imagens
            if (stats.LargestImages.Count > 0)
            {
                output.AppendLine("TOP 20 MAIORES IMAGENS:");
                output.AppendLine("PDF#  Pág#  Dimensões (px)    Área (px²)     Tamanho    Cor");
                output.AppendLine("--------------------------------------------------------------------");
                
                var topImages = stats.LargestImages.OrderByDescending(img => img.Area).Take(20);
                foreach (var img in topImages)
                {
                    var sizeKB = img.EstimatedSize > 0 ? $"{img.EstimatedSize / 1024}KB" : "N/A";
                    var colorType = img.ColorSpace.Contains("RGB") ? "Colorido" :
                                   img.ColorSpace.Contains("Gray") ? "P&B" : "Outro";
                    output.AppendLine($"{img.PdfIndex,-5} {img.PageNumber,-5} {img.Width}x{img.Height,-10} {img.Area,12:N0}   {sizeKB,-10} {colorType}");
                }
                output.AppendLine();

                // Estatísticas das imagens grandes
                var veryLargeImages = stats.LargestImages.Count(img => img.Area > 1000000); // Maior que 1000x1000
                var fullPageImages = stats.LargestImages.Count(img => img.Width > 500 && img.Height > 700); // Possível página completa
                
                output.AppendLine("ESTATÍSTICAS DE IMAGENS GRANDES:");
                output.AppendLine($"├─ Imagens muito grandes (>1M px²): {veryLargeImages}");
                output.AppendLine($"├─ Imagens tamanho página (>500x700): {fullPageImages}");
                output.AppendLine($"└─ Total de imagens grandes (>250K px²): {stats.LargestImages.Count}");
            }
        }

        private void OutputPageSizeAnalysis(RangeStatistics stats, List<int> indices, StringBuilder output)
        {
            var rangeStr = GetRangeString(indices);
            output.AppendLine("ANÁLISE DE TAMANHOS DE PÁGINA");
            output.AppendLine("==============================");
            output.AppendLine($"Range: {rangeStr}");
            output.AppendLine();

            // Distribuição por tamanho de papel
            output.AppendLine("DISTRIBUIÇÃO POR TAMANHO:");
            var sortedSizes = stats.PageSizeDistribution.OrderByDescending(kvp => kvp.Value);
            foreach (var size in sortedSizes.Take(10))
            {
                var percentage = stats.TotalPages > 0 ? (size.Value * 100.0 / stats.TotalPages) : 0;
                output.AppendLine($"├─ {size.Key}: {size.Value} páginas ({percentage:F1}%)");
            }
            output.AppendLine();

            // Top 20 maiores páginas
            output.AppendLine("TOP 20 MAIORES PÁGINAS:");
            output.AppendLine("PDF#  Pág#  Tamanho (pontos)    Área (mm²)      Tipo");
            output.AppendLine("------------------------------------------------------------");
            
            var topPages = stats.LargestPages.OrderByDescending(p => p.Area).Take(20);
            foreach (var page in topPages)
            {
                var areaInMm = page.Area * 0.1247; // Convert points² to mm²
                output.AppendLine($"{page.PdfIndex,-5} {page.PageNumber,-5} {page.Width:F0}x{page.Height:F0} {areaInMm,12:N0} {page.SizeCategory}");
            }
            output.AppendLine();

            // Estatísticas de páginas grandes
            var largePagesCount = stats.LargestPages.Count(p => p.Area > 600000); // Páginas significativamente maiores que A4
            var averageArea = stats.LargestPages.Count > 0 ? stats.LargestPages.Average(p => p.Area) : 0;
            
            output.AppendLine("ESTATÍSTICAS DE PÁGINAS GRANDES:");
            output.AppendLine($"├─ Páginas maiores que A4: {largePagesCount}");
            output.AppendLine($"├─ Área média das páginas: {averageArea * 0.1247:N0} mm²");
            output.AppendLine($"└─ Total de páginas grandes: {stats.LargestPages.Count}");
        }

        private void AnalyzePdf(dynamic cache, string cacheFile, RangeStatistics stats, int index)
        {
            stats.ValidCount++;
            
            stats.TotalCacheSize += 0;

            var pages = cache.Pages as JArray;
            if (pages != null)
            {
                var totalPages = pages.Count;
                stats.TotalPages += totalPages;

                var scannedPages = CountScannedPages(pages);
                var scannedPercent = totalPages > 0 ? (scannedPages * 100.0 / totalPages) : 0;

                stats.TotalScannedPages += scannedPages;
                stats.TotalTextPages += (totalPages - scannedPages);

                // Use the PDF index passed to the method
                var pdfIndexInt = index;

                // Analyze page sizes and images
                for (int i = 0; i < pages.Count; i++)
                {
                    var page = pages[i];
                    
                    // Analyze page size
                    var pageSize = page["Size"];
                    if (pageSize != null)
                    {
                        var width = pageSize["WidthPoints"];
                        var height = pageSize["HeightPoints"];

                        if (width != null && height != null)
                        {
                            var w = (double)width;
                            var h = (double)height;
                            var area = w * h;
                            
                            var sizeCategory = GetSizeCategory(w, h);
                            var sizeKey = $"{w:F0}x{h:F0} ({sizeCategory})";
                            
                            // Update distribution
                            if (!stats.PageSizeDistribution.ContainsKey(sizeKey))
                                stats.PageSizeDistribution[sizeKey] = 0;
                            stats.PageSizeDistribution[sizeKey]++;

                            // Track largest pages (potential attachments)
                            if (area > 600000) // Only track pages significantly larger than A4 (501,090)
                            {
                                stats.LargestPages.Add(new PageSizeInfo
                                {
                                    PdfIndex = pdfIndexInt,
                                    PageNumber = i + 1,
                                    Width = w,
                                    Height = h,
                                    SizeCategory = sizeCategory,
                                    PaperSize = "Custom"
                                });
                            }
                        }
                    }
                    
                    // Analyze images in the page
                    var resources = page["Resources"];
                    if (resources != null)
                    {
                        var images = resources["Images"] as JArray;
                        if (images != null && images.Count > 0)
                        {
                            stats.PagesWithImages++;
                            stats.TotalImages += images.Count;
                            
                            // Track distribution of images per page
                            if (!stats.ImagesPerPageDistribution.ContainsKey(images.Count))
                                stats.ImagesPerPageDistribution[images.Count] = 0;
                            stats.ImagesPerPageDistribution[images.Count]++;
                            
                            // Track largest images
                            foreach (var img in images)
                            {
                                var imgWidth = img["Width"];
                                var imgHeight = img["Height"];
                                var imgSize = img["EstimatedSize"];
                                var imgName = img["Name"]?.ToString() ?? "unknown";
                                var colorSpace = img["ColorSpace"]?.ToString() ?? "";
                                
                                if (imgWidth != null && imgHeight != null)
                                {
                                    var w = (int)imgWidth;
                                    var h = (int)imgHeight;
                                    var size = imgSize != null ? (int)imgSize : 0;
                                    var area = w * h;
                                    
                                    // Track large images (larger than 500x500 or 250,000 pixels)
                                    if (area > 250000)
                                    {
                                        stats.LargestImages.Add(new ImageInfo
                                        {
                                            PdfIndex = pdfIndexInt,
                                            PageNumber = i + 1,
                                            ImageName = imgName,
                                            Width = w,
                                            Height = h,
                                            EstimatedSize = size,
                                            ColorSpace = colorSpace
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                if (scannedPercent >= 90)
                {
                    stats.ScannedPdfCount++;
                    stats.TotalScannedConfidence += scannedPercent;
                }
                else if (scannedPercent >= 50)
                {
                    stats.MixedPdfCount++;
                }
                else
                {
                    stats.TextPdfCount++;
                }
            }
        }

        private int CountScannedPages(JArray pages)
        {
            int count = 0;
            foreach (var page in pages)
            {
                if (IsScannedPage(page))
                    count++;
            }
            return count;
        }

        private bool IsScannedPage(dynamic page)
        {
            // For images-only cache, pages with images and no/low text are scanned
            int imageCount = 0;
            int wordCount = 0;

            var resources = page["Resources"];
            if (resources != null)
            {
                var images = resources["Images"] as JArray;
                if (images != null)
                    imageCount = images.Count;
            }

            var textInfo = page["TextInfo"];
            if (textInfo != null)
            {
                var wc = textInfo["WordCount"];
                if (wc != null)
                    wordCount = (int)wc;
            }

            // Consider scanned if has images and very few words
            // This works with images-only cache where WordCount might be present but low
            return imageCount > 0 && wordCount < 50;
        }

        private void OutputSinglePdfJson(int index, dynamic cache, string cacheFile, Dictionary<string, string> options)
        {
            var fileInfo = new FileInfo(cacheFile);
            var pages = cache.Pages as JArray;
            var totalPages = pages?.Count ?? 0;
            var scannedPages = pages != null ? CountScannedPages(pages) : 0;
            
            var result = new
            {
                pdf_index = index,
                file_name = Path.GetFileNameWithoutExtension(cache.OriginalFileName?.ToString() ?? "unknown"),
                cache_size_kb = fileInfo.Length / 1024,
                composition = new
                {
                    total_pages = totalPages,
                    scanned_pages = scannedPages,
                    scanned_percent = totalPages > 0 ? (scannedPages * 100.0 / totalPages) : 0,
                    text_pages = totalPages - scannedPages
                },
                classification = GetClassification(scannedPages, totalPages)
            };

            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            
            if (options.ContainsKey("output"))
            {
                File.WriteAllText(options["output"], json);
                Console.WriteLine($"Output saved to: {options["output"]}");
            }
            else
            {
                Console.WriteLine(json);
            }
        }

        private string GetClassification(int scannedPages, int totalPages)
        {
            if (totalPages == 0) return "empty";
            var percent = scannedPages * 100.0 / totalPages;
            if (percent >= 90) return "scanned";
            if (percent >= 50) return "mixed";
            return "text";
        }

        private string GetSizeCategory(double width, double height)
        {
            var area = width * height;
            
            // A4: 595 x 842 points (área ≈ 501,090)
            // A3: 842 x 1191 points (área ≈ 1,002,822)
            // Letter: 612 x 792 points (área ≈ 484,704)
            
            if (Math.Abs(width - 595) < 10 && Math.Abs(height - 842) < 10)
                return "A4";
            if (Math.Abs(width - 842) < 10 && Math.Abs(height - 1191) < 10)
                return "A3";
            if (Math.Abs(width - 612) < 10 && Math.Abs(height - 792) < 10)
                return "Letter";
            if (Math.Abs(width - 744) < 10 && Math.Abs(height - 1052) < 10)
                return "B5";
            
            if (area > 1000000)
                return "Muito-Grande";
            if (area > 700000)
                return "Grande";
            if (area > 400000)
                return "Médio";
                
            return "Pequeno";
        }

        private void AnalyzeImagesForSinglePdf(JArray pages, int pdfIndex, StringBuilder output)
        {
            output.AppendLine("ANÁLISE DE IMAGENS POR PÁGINA:");
            output.AppendLine("Pág#  Imgs  Maior Imagem (px)    Área (px²)     Tamanho");
            output.AppendLine("----------------------------------------------------------");

            var pageImages = new List<(int pageNum, int imageCount, ImageInfo largestImage)>();
            var totalImages = 0;
            var pagesWithImages = 0;

            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var resources = page["Resources"];
                
                if (resources != null)
                {
                    var images = resources["Images"] as JArray;
                    if (images != null && images.Count > 0)
                    {
                        totalImages += images.Count;
                        pagesWithImages++;
                        
                        ImageInfo largestImage = null;
                        int maxArea = 0;
                        
                        foreach (var img in images)
                        {
                            var imgWidth = img["Width"];
                            var imgHeight = img["Height"];
                            
                            if (imgWidth != null && imgHeight != null)
                            {
                                var w = (int)imgWidth;
                                var h = (int)imgHeight;
                                var area = w * h;
                                
                                if (area > maxArea)
                                {
                                    maxArea = area;
                                    largestImage = new ImageInfo
                                    {
                                        Width = w,
                                        Height = h,
                                        EstimatedSize = img["EstimatedSize"] != null ? (int)img["EstimatedSize"] : 0,
                                        ColorSpace = img["ColorSpace"]?.ToString() ?? ""
                                    };
                                }
                            }
                        }
                        
                        if (largestImage != null)
                        {
                            var sizeKB = largestImage.EstimatedSize > 0 ? 
                                $"{largestImage.EstimatedSize / 1024}KB" : "N/A";
                            output.AppendLine($"{i + 1,-4} {images.Count,-5} {largestImage.Width}x{largestImage.Height,-10} {largestImage.Area,12:N0}   {sizeKB}");
                        }
                        else
                        {
                            output.AppendLine($"{i + 1,-4} {images.Count,-5} -");
                        }
                    }
                }
            }

            output.AppendLine();
            output.AppendLine("RESUMO DE IMAGENS:");
            output.AppendLine($"├─ Total de imagens: {totalImages}");
            output.AppendLine($"├─ Páginas com imagens: {pagesWithImages}/{pages.Count} ({(pages.Count > 0 ? pagesWithImages * 100.0 / pages.Count : 0):F1}%)");
            output.AppendLine($"├─ Média de imagens/página: {(pagesWithImages > 0 ? (double)totalImages / pagesWithImages : 0):F1}");
            
            // Identificar páginas suspeitas
            var suspiciousPages = new List<int>();
            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var resources = page["Resources"];
                
                if (resources != null)
                {
                    var images = resources["Images"] as JArray;
                    if (images != null && images.Count == 1) // Uma única imagem grande
                    {
                        foreach (var img in images)
                        {
                            var w = img["Width"] != null ? (int)img["Width"] : 0;
                            var h = img["Height"] != null ? (int)img["Height"] : 0;
                            
                            // Página suspeita: única imagem grande (possível scan de página completa)
                            if (w > 500 && h > 700)
                            {
                                suspiciousPages.Add(i + 1);
                            }
                        }
                    }
                }
            }
            
            if (suspiciousPages.Count > 0)
            {
                output.AppendLine($"└─ Páginas com imagem única grande: {string.Join(", ", suspiciousPages)}");
            }
        }

        private void AnalyzePageSizesForSinglePdf(JArray pages, int pdfIndex, StringBuilder output)
        {
            output.AppendLine("ANÁLISE DE TAMANHOS DE PÁGINA:");
            output.AppendLine("Pág#  Tamanho (pontos)    Área (mm²)      Tipo");
            output.AppendLine("-----------------------------------------------");

            var pageSizes = new List<PageSizeInfo>();
            var sizeDistribution = new Dictionary<string, int>();

            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var pageSize = page["Size"];
                
                if (pageSize != null)
                {
                    var width = pageSize["WidthPoints"];
                    var height = pageSize["HeightPoints"];

                    if (width != null && height != null)
                    {
                        var w = (double)width;
                        var h = (double)height;
                        var area = w * h;
                        var sizeCategory = GetSizeCategory(w, h);
                        
                        var pageSizeInfo = new PageSizeInfo
                        {
                            PdfIndex = pdfIndex,
                            PageNumber = i + 1,
                            Width = w,
                            Height = h,
                            SizeCategory = sizeCategory,
                            PaperSize = "Custom"
                        };
                        
                        pageSizes.Add(pageSizeInfo);
                        
                        var sizeKey = $"{w:F0}x{h:F0} ({sizeCategory})";
                        if (!sizeDistribution.ContainsKey(sizeKey))
                            sizeDistribution[sizeKey] = 0;
                        sizeDistribution[sizeKey]++;

                        var areaInMm = area * 0.1247; // Convert points² to mm²
                        output.AppendLine($"{i + 1,-4} {w:F0}x{h:F0} {areaInMm,10:N0} {sizeCategory}");
                    }
                }
            }

            output.AppendLine();
            output.AppendLine("DISTRIBUIÇÃO POR TAMANHO:");
            var sortedSizes = sizeDistribution.OrderByDescending(kvp => kvp.Value);
            foreach (var size in sortedSizes)
            {
                var percentage = pages.Count > 0 ? (size.Value * 100.0 / pages.Count) : 0;
                output.AppendLine($"├─ {size.Key}: {size.Value} páginas ({percentage:F1}%)");
            }

            // Show statistics about page sizes
            var largePagesCount = pageSizes.Count(p => p.Area > 600000); // Significantly larger than A4
            
            if (largePagesCount > 0)
            {
                output.AppendLine();
                output.AppendLine("ESTATÍSTICAS DE TAMANHOS:");
                output.AppendLine($"└─ Páginas maiores que A4: {largePagesCount} páginas");
            }
        }

        private string GetRangeString(List<int> indices)
        {
            if (indices.Count == 1)
                return indices[0].ToString();
            
            if (indices.Count == 2)
                return $"{indices[0]},{indices[1]}";
            
            // Check if it's a continuous range
            bool isRange = true;
            for (int i = 1; i < indices.Count; i++)
            {
                if (indices[i] != indices[i - 1] + 1)
                {
                    isRange = false;
                    break;
                }
            }

            if (isRange)
                return $"{indices.First()}-{indices.Last()}";
            
            return $"{indices.First()}-{indices.Last()} ({indices.Count} PDFs)";
        }

        private List<int> ParseIndices(string input)
        {
            var indices = new List<int>();

            // Handle range (e.g., "1-100")
            if (input.Contains("-"))
            {
                var parts = input.Split('-');
                if (parts.Length == 2 && 
                    int.TryParse(parts[0], out int start) && 
                    int.TryParse(parts[1], out int end))
                {
                    for (int i = start; i <= end; i++)
                    {
                        indices.Add(i);
                    }
                }
            }
            // Handle comma-separated (e.g., "1,5,10")
            else if (input.Contains(","))
            {
                foreach (var part in input.Split(','))
                {
                    if (int.TryParse(part.Trim(), out int index))
                    {
                        indices.Add(index);
                    }
                }
            }
            // Handle single index
            else if (int.TryParse(input, out int single))
            {
                indices.Add(single);
            }

            return indices;
        }

        private Dictionary<string, string> ParseOptions(string[] args)
        {
            var options = new Dictionary<string, string>();
            
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                
                if (arg == "--scanned")
                {
                    options["scanned"] = "true";
                }
                else if (arg == "--type")
                {
                    options["type"] = "true";
                }
                else if (arg == "--composition")
                {
                    options["composition"] = "true";
                }
                else if (arg == "--page-sizes")
                {
                    options["page-sizes"] = "true";
                }
                else if (arg == "--images")
                {
                    options["images"] = "true";
                }
                else if (arg == "--detailed")
                {
                    options["detailed"] = "true";
                }
                else if ((arg == "--format" || arg == "-F") && i + 1 < args.Length)
                {
                    options["format"] = args[++i];
                }
                else if ((arg == "--output" || arg == "-o") && i + 1 < args.Length)
                {
                    options["output"] = args[++i];
                }
                else if (arg == "--keywords" && i + 1 < args.Length)
                {
                    options["keywords"] = args[++i];
                }
                else if (arg == "--input-dir" && i + 1 < args.Length)
                {
                    options["input-dir"] = args[++i];
                }
            }

            return options;
        }

        private void ExecuteDirectoryStats(string directory, Dictionary<string, string> options)
        {
            // Future implementation for directory scanning
            Console.WriteLine($"Directory stats not yet implemented for: {directory}");
        }

        private class RangeStatistics
        {
            public int ValidCount { get; set; }
            public int InvalidCount { get; set; }
            public int TotalPages { get; set; }
            public int TotalScannedPages { get; set; }
            public int TotalTextPages { get; set; }
            public int ScannedPdfCount { get; set; }
            public int TextPdfCount { get; set; }
            public int MixedPdfCount { get; set; }
            public long TotalCacheSize { get; set; }
            public double TotalScannedConfidence { get; set; }
            public List<PageSizeInfo> LargestPages { get; set; } = new List<PageSizeInfo>();
            public Dictionary<string, int> PageSizeDistribution { get; set; } = new Dictionary<string, int>();
            public List<ImageInfo> LargestImages { get; set; } = new List<ImageInfo>();
            public Dictionary<int, int> ImagesPerPageDistribution { get; set; } = new Dictionary<int, int>();
            public int TotalImages { get; set; }
            public int PagesWithImages { get; set; }
            
            public double AverageScannedConfidence => 
                ScannedPdfCount > 0 ? TotalScannedConfidence / ScannedPdfCount : 0;
        }

        private class PageSizeInfo
        {
            public int PdfIndex { get; set; }
            public int PageNumber { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Area => Width * Height;
            public string SizeCategory { get; set; }
            public string PaperSize { get; set; }
        }

        private class ImageInfo
        {
            public int PdfIndex { get; set; }
            public int PageNumber { get; set; }
            public string ImageName { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int EstimatedSize { get; set; }
            public int Area => Width * Height;
            public string ColorSpace { get; set; }
        }
    }
}
