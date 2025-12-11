using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using FilterPDF.Models;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Extrai campos definidos em um template (bboxes normalizadas) usando as palavras já extraídas (iText7).
    /// Uso: fpdf arquivo.pdf templates --template template.json [--format json]
    /// Template JSON: [{ "name":"processo", "page":1, "x0":0.1,"y0":0.8,"x1":0.6,"y1":0.9, "tolerance":0.01 }]
    /// </summary>
    public class FpdfTemplatesCommand
    {
        public void Execute(string inputFile, PDFAnalysisResult analysis, Dictionary<string, string> options)
        {
            if (!options.ContainsKey("--template"))
            {
                Console.Error.WriteLine("Error: --template <arquivo.json> é obrigatório.");
                return;
            }

            var mode = options.ContainsKey("--mode") ? options["--mode"] : "overlap"; // overlap | center

            var templatePath = options["--template"];
            if (!File.Exists(templatePath))
            {
                Console.Error.WriteLine($"Error: template file '{templatePath}' not found.");
                return;
            }

            var regions = JsonConvert.DeserializeObject<List<TemplateRegion>>(File.ReadAllText(templatePath));
            if (regions == null || regions.Count == 0)
            {
                Console.Error.WriteLine("Error: template vazio ou inválido.");
                return;
            }

            // Se não veio análise em cache, gerar
            if (analysis == null)
            {
                var analyzer = new PDFAnalyzer(inputFile);
                analysis = analyzer.AnalyzeFull();
            }

            var results = new List<TemplateFieldValue>();

            foreach (var region in regions)
            {
                var pages = analysis.Pages
                    .Where(p => !region.Page.HasValue || p.PageNumber == region.Page.Value)
                    .ToList();

                foreach (var page in pages)
                {
                    var words = page.TextInfo.Words ?? new List<WordInfo>();
                    var tol = region.Tolerance;

                    float rx0 = region.X0 - tol;
                    float rx1 = region.X1 + tol;
                    float ry0 = region.Y0 - tol;
                    float ry1 = region.Y1 + tol;

                    bool IsHit(WordInfo w)
                    {
                        if (mode == "center")
                        {
                            float cx = (w.NormX0 + w.NormX1) / 2f;
                            float cy = (w.NormY0 + w.NormY1) / 2f;
                            return cx >= rx0 && cx <= rx1 && cy >= ry0 && cy <= ry1;
                        }
                        // overlap mode (default)
                        return w.NormX1 >= rx0 && w.NormX0 <= rx1 &&
                               w.NormY1 >= ry0 && w.NormY0 <= ry1;
                    }

                    var hits = words.Where(IsHit)
                    .OrderByDescending(w => w.NormY0)
                    .ThenBy(w => w.NormX0)
                    .ToList();

                    var value = string.Join(" ", hits.Select(h => h.Text));

                    results.Add(new TemplateFieldValue
                    {
                        Name = region.Name,
                        Page = page.PageNumber,
                        Value = value,
                        WordCount = hits.Count
                    });
                }
            }

            var output = new TemplateExtractionResult
            {
                File = Path.GetFileName(inputFile),
                Fields = results
            };

            Console.WriteLine(JsonConvert.SerializeObject(output, Formatting.Indented));
        }
    }
}
