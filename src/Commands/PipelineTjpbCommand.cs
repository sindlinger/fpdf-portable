using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--input-dir" && i + 1 < args.Length) inputDir = args[i + 1];
                if (args[i] == "--output" && i + 1 < args.Length) output = args[i + 1];
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
                    var segmenter = new DocumentSegmenter(new DocumentSegmentationConfig());
                    var docs = segmenter.FindDocuments(analysis);

                    foreach (var d in docs)
                    {
                        var obj = BuildDocObject(d, analysis, pdf.FullName);
                        allDocs.Add(obj);
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
            Console.WriteLine("fpdf pipeline-tjpb --input-dir <dir> --output fpdf.json");
            Console.WriteLine("Gera JSON compatível com tmp/pipeline/step2/fpdf.json para o CI Dashboard.");
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
            for (int p = d.StartPage; p <= d.EndPage; p++)
            {
                images += analysis.Pages[p - 1].Resources.Images?.Count ?? 0;
                // Heurística simples: imagem com largura/altura > 100 pode ser assinatura
                hasSignature |= (analysis.Pages[p - 1].Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false);
            }

            var pageSize = analysis.Pages.First().Size.GetPaperSize();

            return new Dictionary<string, object>
            {
                ["process"] = Path.GetFileNameWithoutExtension(pdfPath),
                ["pdf_path"] = pdfPath,
                ["doc_label"] = ExtractDocumentName(d),
                ["start_page"] = d.StartPage,
                ["end_page"] = d.EndPage,
                ["doc_pages"] = d.PageCount,
                ["total_pages"] = analysis.DocumentInfo.TotalPages,
                ["text"] = docText,
                ["fonts"] = fonts.ToArray(),
                ["images"] = images,
                ["page_size"] = pageSize,
                ["has_signature_image"] = hasSignature,
                ["is_attachment"] = false // não inferido aqui
            };
        }

        private string ExtractDocumentName(DocumentBoundary d)
        {
            // mesmo critério do FpdfDocumentsCommand: primeira linha do FullText/FirstPageText
            var text = d.FullText ?? d.FirstPageText ?? "";
            var firstLine = text.Split('\n').FirstOrDefault() ?? "";
            return firstLine.Length > 80 ? firstLine.Substring(0, 80) + "..." : firstLine;
        }
    }
}
