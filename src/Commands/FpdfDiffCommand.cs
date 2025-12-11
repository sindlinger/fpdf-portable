using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Compara um PDF "target" com um PDF "template" e lista palavras/linhas que existem no target e não no template.
    /// Útil para identificar campos preenchidos.
    /// Uso: fpdf diff --template base.pdf --target preenchido.pdf [--format json|txt]
    /// </summary>
    public class FpdfDiffCommand : Command
    {
        public override string Name => "diff";
        public override string Description => "Diff de preenchimentos entre um template e um PDF preenchido (iText7)";

        public override void Execute(string[] args)
        {
            var options = ParseArgs(args);
            if (!options.ContainsKey("--template") || !options.ContainsKey("--target"))
            {
                Console.WriteLine("Uso: fpdf diff --template base.pdf --target preenchido.pdf [--format json|txt]");
                return;
            }

            string templatePath = options["--template"];
            string targetPath = options["--target"];
            string format = options.ContainsKey("--format") ? options["--format"].ToLowerInvariant() : "txt";

            if (!File.Exists(templatePath) || !File.Exists(targetPath))
            {
                Console.WriteLine("Template ou target não encontrado.");
                return;
            }

            var templateAnalysis = new PDFAnalyzer(templatePath).AnalyzeFull();
            var targetAnalysis = new PDFAnalyzer(targetPath).AnalyzeFull();

            var diff = ComputeDiff(templateAnalysis, targetAnalysis);

            if (format == "json")
            {
                Console.WriteLine(JsonConvert.SerializeObject(diff, Formatting.Indented));
            }
            else
            {
                foreach (var p in diff.Pages)
                {
                    Console.WriteLine($"Página {p.PageNumber}:");
                    foreach (var w in p.Words)
                    {
                        Console.WriteLine($"  \"{w.Text}\" @ ({w.X0:0.##},{w.Y0:0.##})-({w.X1:0.##},{w.Y1:0.##})");
                    }
                    foreach (var l in p.Lines)
                    {
                        Console.WriteLine($"  [linha] \"{l.Text}\" @ ({l.X0:0.##},{l.Y0:0.##})-({l.X1:0.##},{l.Y1:0.##})");
                    }
                    Console.WriteLine();
                }
            }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf diff --template base.pdf --target preenchido.pdf [--format json|txt]");
            Console.WriteLine("Mostra palavras/linhas presentes no alvo e ausentes no template (campos preenchidos).");
        }

        private DiffResult ComputeDiff(PDFAnalysisResult template, PDFAnalysisResult target)
        {
            var result = new DiffResult();
            int maxPage = Math.Max(template.Pages.Count, target.Pages.Count);

            for (int i = 0; i < maxPage; i++)
            {
                var tPage = template.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                var gPage = target.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                if (gPage == null) continue;

                var pageResult = new DiffPage { PageNumber = i + 1 };

                var tmplWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (tPage != null)
                {
                    foreach (var w in tPage.TextInfo.Words)
                        tmplWords.Add(w.Text);
                    foreach (var l in tPage.TextInfo.Lines)
                        tmplWords.Add(l.Text);
                }

                // Palavras novas
                foreach (var w in gPage.TextInfo.Words)
                {
                    if (!tmplWords.Contains(w.Text))
                    {
                        pageResult.Words.Add(w);
                    }
                }

                // Linhas novas
                foreach (var l in gPage.TextInfo.Lines)
                {
                    if (!tmplWords.Contains(l.Text))
                    {
                        pageResult.Lines.Add(l);
                    }
                }

                if (pageResult.Words.Count > 0 || pageResult.Lines.Count > 0)
                    result.Pages.Add(pageResult);
            }

            return result;
        }

        private Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith("--") && i + 1 < args.Length)
                {
                    dict[a] = args[i + 1];
                    i++;
                }
            }
            return dict;
        }

        private class DiffResult
        {
            public List<DiffPage> Pages { get; set; } = new List<DiffPage>();
        }

        private class DiffPage
        {
            public int PageNumber { get; set; }
            public List<WordInfo> Words { get; set; } = new List<WordInfo>();
            public List<LineInfo> Lines { get; set; } = new List<LineInfo>();
        }
    }
}
