using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using FilterPDF.Strategies;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Compara um conjunto de PDFs de mesmo template e identifica linhas variantes (campos preenchidos).
    /// Saída padrão: JSON com linhas estáveis (template) e linhas variantes por arquivo.
    /// </summary>
    public class TemplateDiffCommand : Command
    {
        public override string Name => "template-diff";
        public override string Description => "Detecta campos preenchidos comparando múltiplos PDFs do mesmo template";

        public override void Execute(string[] args)
        {
            if (args.Length < 1 || args.Any(a => a == "--help" || a == "-h"))
            {
                ShowHelp();
                return;
            }

            string dirOrPattern = args[0];
            string directory = Directory.Exists(dirOrPattern) ? dirOrPattern : Path.GetDirectoryName(dirOrPattern) ?? ".";
            string pattern = Directory.Exists(dirOrPattern) ? "*.pdf" : Path.GetFileName(dirOrPattern);
            double threshold = 0.9; // 90% presença para ser considerado parte do template
            if (args.Length >= 3 && (args[1] == "--threshold" || args[1] == "-t") && double.TryParse(args[2], out var t))
                threshold = Math.Clamp(t, 0.5, 1.0);

            var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                                   .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                   .ToList();

            if (files.Count < 2)
            {
                Console.Error.WriteLine("Need at least 2 PDF files to compare templates.");
                return;
            }

            Console.WriteLine($"📚 Analisando {files.Count} PDFs em {directory} (limiar de template: {threshold:P0})...");

            // 1) Coletar linhas por arquivo (texto normalizado)
            var fileLines = new Dictionary<string, HashSet<string>>();
            var lineFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var pdf in files)
            {
                var lines = ExtractLines(pdf);
                fileLines[pdf] = lines;
                foreach (var line in lines)
                {
                    lineFrequency[line] = lineFrequency.TryGetValue(line, out var c) ? c + 1 : 1;
                }
            }

            int fileCount = files.Count;
            var templateLines = lineFrequency
                .Where(kv => kv.Value >= fileCount * threshold)
                .Select(kv => kv.Key)
                .OrderBy(s => s)
                .ToHashSet(StringComparer.Ordinal);

            // 2) Variantes por arquivo (linhas que não são de template)
            var variants = new Dictionary<string, List<string>>();
            foreach (var kv in fileLines)
            {
                var diff = kv.Value.Where(l => !templateLines.Contains(l)).OrderBy(s => s).ToList();
                variants[kv.Key] = diff;
            }

            // 3) Emitir JSON simples
            var result = new
            {
                template_line_count = templateLines.Count,
                template_lines = templateLines.ToList(),
                files = variants.Select(v => new { file = Path.GetFileName(v.Key), variant_lines = v.Value }).ToList()
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
            Console.WriteLine(json);
        }

        private HashSet<string> ExtractLines(string pdfPath)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            using var doc = new PdfDocument(new PdfReader(pdfPath));
            int pages = doc.GetNumberOfPages();
            var strategyFactory = new Func<FilterPDF.Strategies.LayoutPreservingStrategy7>(() => new FilterPDF.Strategies.LayoutPreservingStrategy7());

            for (int p = 1; p <= pages; p++)
            {
                var strategy = strategyFactory();
                string text = PdfTextExtractor.GetTextFromPage(doc.GetPage(p), strategy);
                foreach (var line in text.Split('\n'))
                {
                    var normalized = line.Trim();
                    if (!string.IsNullOrEmpty(normalized))
                        set.Add(normalized);
                }
            }
            return set;
        }

        public override void ShowHelp()
        {
            Console.WriteLine($"COMMAND: {Name}");
            Console.WriteLine($"    {Description}");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("    fpdf template-diff <dir_or_glob> [-t 0.9]");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("    fpdf template-diff ./lote_pdfs");
            Console.WriteLine("    fpdf template-diff ./lote/*.pdf -t 0.85");
        }
    }
}
