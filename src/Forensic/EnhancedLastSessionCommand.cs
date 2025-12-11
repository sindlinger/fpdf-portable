using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Variante enriquecida do last-session: compara texto (palavras/linhas), imagens e campos de formulário
    /// para identificar preenchimentos e diferenças de sessão.
    /// Uso: fpdf enhanced-last-session --a base.pdf --b novo.pdf [--format json|txt]
    /// </summary>
    public class EnhancedLastSessionCommand : Command
    {
        public override string Name => "enhanced-last-session";
        public override string Description => "Diff enriquecido (texto/linhas/imagens/form fields) entre duas versões";

        public override void Execute(string[] args)
        {
            var opts = ParseArgs(args);
            if (!opts.ContainsKey("--a") || !opts.ContainsKey("--b")) { ShowHelp(); return; }
            string a = opts["--a"], b = opts["--b"]; string format = opts.ContainsKey("--format") ? opts["--format"].ToLowerInvariant() : "txt";
            if (!File.Exists(a) || !File.Exists(b)) { Console.WriteLine("Arquivo --a ou --b não encontrado."); return; }

            var baseRes = new PDFAnalyzer(a).AnalyzeFull();
            var newRes = new PDFAnalyzer(b).AnalyzeFull();
            var diff = ComputeDiff(baseRes, newRes);

            if (format == "json")
                Console.WriteLine(JsonConvert.SerializeObject(diff, Formatting.Indented));
            else
            {
                foreach (var p in diff.Pages)
                {
                    Console.WriteLine($"Página {p.PageNumber}:");
                    foreach (var w in p.NewWords) Console.WriteLine($" +W {w.Text}");
                    foreach (var l in p.NewLines) Console.WriteLine($" +L {l.Text}");
                    foreach (var f in p.NewFields) Console.WriteLine($" +FIELD {f.Name}={f.Value}");
                    foreach (var im in p.NewImages) Console.WriteLine($" +IMG {im.Name} {im.Width}x{im.Height}");
                    Console.WriteLine();
                }
            }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf enhanced-last-session --a base.pdf --b novo.pdf [--format json|txt]");
        }

        private EnhancedDiff ComputeDiff(PDFAnalysisResult a, PDFAnalysisResult b)
        {
            var result = new EnhancedDiff();
            int maxPage = Math.Max(a.Pages.Count, b.Pages.Count);
            for (int i = 0; i < maxPage; i++)
            {
                var pa = a.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                var pb = b.Pages.FirstOrDefault(p => p.PageNumber == i + 1);
                if (pb == null) continue;

                var page = new EnhancedDiffPage { PageNumber = i + 1 };

                var wordsA = new HashSet<string>(pa?.TextInfo.Words.Select(w => w.Text) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var linesA = new HashSet<string>(pa?.TextInfo.Lines.Select(l => l.Text) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var imgsA = new HashSet<string>(pa?.Resources.Images.Select(i => i.Name) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var fieldsA = new HashSet<string>(pa?.Resources.FormFields.Select(f => f.Name + "=" + f.Value) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

                foreach (var w in pb.TextInfo.Words)
                    if (!wordsA.Contains(w.Text)) page.NewWords.Add(w);
                foreach (var l in pb.TextInfo.Lines)
                    if (!linesA.Contains(l.Text)) page.NewLines.Add(l);
                foreach (var im in pb.Resources.Images)
                    if (!imgsA.Contains(im.Name)) page.NewImages.Add(im);
                foreach (var ff in pb.Resources.FormFields)
                {
                    var key = ff.Name + "=" + ff.Value;
                    if (!fieldsA.Contains(key)) page.NewFields.Add(ff);
                }

                if (page.HasChanges)
                    result.Pages.Add(page);
            }
            return result;
        }

        private Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith("--") && i + 1 < args.Length)
                { dict[args[i]] = args[i + 1]; i++; }
            return dict;
        }

        private class EnhancedDiff
        {
            public List<EnhancedDiffPage> Pages { get; set; } = new List<EnhancedDiffPage>();
        }

        private class EnhancedDiffPage
        {
            public int PageNumber { get; set; }
            public List<WordInfo> NewWords { get; set; } = new List<WordInfo>();
            public List<LineInfo> NewLines { get; set; } = new List<LineInfo>();
            public List<ImageInfo> NewImages { get; set; } = new List<ImageInfo>();
            public List<FormField> NewFields { get; set; } = new List<FormField>();
            public bool HasChanges => NewWords.Count + NewLines.Count + NewImages.Count + NewFields.Count > 0;
        }
    }
}
