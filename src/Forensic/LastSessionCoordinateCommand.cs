using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Detecta diferenças de sessão (texto e imagens) entre um PDF base e um PDF modificado,
    /// reportando coordenadas das adições. Uso: fpdf last-session --a base.pdf --b novo.pdf [--format json|txt]
    /// </summary>
    public class LastSessionCoordinateCommand : Command
    {
        public override string Name => "last-session";
        public override string Description => "Diff com coordenadas entre versões (texto/imagem) para identificar última sessão";

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

            var baseRes = new PDFAnalyzer(a).AnalyzeFull();
            var newRes = new PDFAnalyzer(b).AnalyzeFull();

            var diff = ComputeDiff(baseRes, newRes);

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
                        Console.WriteLine($" +W \"{w.Text}\" ({w.X0:0.##},{w.Y0:0.##})-({w.X1:0.##},{w.Y1:0.##})");
                    foreach (var l in p.NewLines)
                        Console.WriteLine($" +L \"{l.Text}\" ({l.X0:0.##},{l.Y0:0.##})-({l.X1:0.##},{l.Y1:0.##})");
                    foreach (var im in p.NewImages)
                        Console.WriteLine($" +IMG {im.Name} {im.Width}x{im.Height}");
                    Console.WriteLine();
                }
            }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf last-session --a base.pdf --b novo.pdf [--format json|txt]");
            Console.WriteLine("Lista texto (palavras/linhas) e imagens presentes em B mas não em A, com bbox.");
        }

        private LastSessionResult ComputeDiff(PDFAnalysisResult a, PDFAnalysisResult b)
        {
            var result = new LastSessionResult();
            int maxPage = Math.Max(a.Pages.Count, b.Pages.Count);

            for (int i = 0; i < maxPage; i++)
            {
                var pa = a.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                var pb = b.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                if (pb == null) continue;

                var page = new LastSessionPage { PageNumber = i + 1 };

                var wordsA = new HashSet<string>(pa?.TextInfo.Words.Select(w => w.Text) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var linesA = new HashSet<string>(pa?.TextInfo.Lines.Select(l => l.Text) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var imgNamesA = new HashSet<string>(pa?.Resources.Images.Select(img => img.Name) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

                foreach (var w in pb.TextInfo.Words)
                    if (!wordsA.Contains(w.Text)) page.NewWords.Add(w);
                foreach (var l in pb.TextInfo.Lines)
                    if (!linesA.Contains(l.Text)) page.NewLines.Add(l);
                foreach (var img in pb.Resources.Images)
                    if (!imgNamesA.Contains(img.Name)) page.NewImages.Add(img);

                if (page.NewWords.Count > 0 || page.NewLines.Count > 0 || page.NewImages.Count > 0)
                    result.Pages.Add(page);
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

        private class LastSessionResult
        {
            public List<LastSessionPage> Pages { get; set; } = new List<LastSessionPage>();
        }

        private class LastSessionPage
        {
            public int PageNumber { get; set; }
            public List<WordInfo> NewWords { get; set; } = new List<WordInfo>();
            public List<LineInfo> NewLines { get; set; } = new List<LineInfo>();
            public List<ImageInfo> NewImages { get; set; } = new List<ImageInfo>();
        }
    }
}
