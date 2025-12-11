using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Compara dois PDFs e lista linhas/palavras diferentes (conteúdo textual) com coordenadas.
    /// Uso: fpdf true-diff --a base.pdf --b novo.pdf [--format json|txt]
    /// </summary>
    public class TrueDiffCommand : Command
    {
        public override string Name => "true-diff";
        public override string Description => "Diff textual com coordenadas entre dois PDFs (iText7)";

        public override void Execute(string[] args)
        {
            var opts = ParseArgs(args);
            if (!opts.ContainsKey("--a") || !opts.ContainsKey("--b"))
            {
                ShowHelp();
                return;
            }
            string a = opts["--a"];
            string b = opts["--b"];
            string format = opts.ContainsKey("--format") ? opts["--format"].ToLowerInvariant() : "txt";

            if (!File.Exists(a) || !File.Exists(b))
            {
                Console.WriteLine("Arquivo --a ou --b não encontrado.");
                return;
            }

            var resA = new PDFAnalyzer(a).AnalyzeFull();
            var resB = new PDFAnalyzer(b).AnalyzeFull();
            var diff = ComputeDiff(resA, resB);

            if (format == "json")
            {
                Console.WriteLine(JsonConvert.SerializeObject(diff, Formatting.Indented));
            }
            else
            {
                foreach (var p in diff.Pages)
                {
                    Console.WriteLine($"Página {p.PageNumber}:");
                    foreach (var w in p.NewWords)
                        Console.WriteLine($" +W \"{w.Text}\" @ ({w.X0:0.##},{w.Y0:0.##})-({w.X1:0.##},{w.Y1:0.##})");
                    foreach (var l in p.NewLines)
                        Console.WriteLine($" +L \"{l.Text}\" @ ({l.X0:0.##},{l.Y0:0.##})-({l.X1:0.##},{l.Y1:0.##})");
                    Console.WriteLine();
                }
            }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf true-diff --a base.pdf --b novo.pdf [--format json|txt]");
            Console.WriteLine("Mostra palavras e linhas presentes em B que não estão em A, com bbox.");
        }

        private TrueDiffResult ComputeDiff(PDFAnalysisResult a, PDFAnalysisResult b)
        {
            var result = new TrueDiffResult();
            int maxPage = Math.Max(a.Pages.Count, b.Pages.Count);

            for (int i = 0; i < maxPage; i++)
            {
                var pa = a.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                var pb = b.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                if (pb == null) continue;

                var pageResult = new TrueDiffPage { PageNumber = i + 1 };

                var wordsA = new HashSet<string>(pa?.TextInfo.Words.Select(w => w.Text) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var linesA = new HashSet<string>(pa?.TextInfo.Lines.Select(l => l.Text) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

                foreach (var w in pb.TextInfo.Words)
                    if (!wordsA.Contains(w.Text)) pageResult.NewWords.Add(w);

                foreach (var l in pb.TextInfo.Lines)
                    if (!linesA.Contains(l.Text)) pageResult.NewLines.Add(l);

                if (pageResult.NewWords.Count > 0 || pageResult.NewLines.Count > 0)
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

        private class TrueDiffResult
        {
            public List<TrueDiffPage> Pages { get; set; } = new List<TrueDiffPage>();
        }

        private class TrueDiffPage
        {
            public int PageNumber { get; set; }
            public List<WordInfo> NewWords { get; set; } = new List<WordInfo>();
            public List<LineInfo> NewLines { get; set; } = new List<LineInfo>();
        }
    }
}
