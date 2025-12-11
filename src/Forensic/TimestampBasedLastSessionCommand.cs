using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Usa a diferença de ModDate/CreationDate para inferir a última sessão entre dois PDFs.
    /// Uso: fpdf ts-last-session --a base.pdf --b novo.pdf [--format json|txt]
    /// </summary>
    public class TimestampBasedLastSessionCommand : Command
    {
        public override string Name => "ts-last-session";
        public override string Description => "Compara timestamps de metadados para sugerir última sessão";

        public override void Execute(string[] args)
        {
            var opts = ParseArgs(args);
            if (!opts.ContainsKey("--a") || !opts.ContainsKey("--b")) { ShowHelp(); return; }
            string a = opts["--a"]; string b = opts["--b"]; string format = opts.ContainsKey("--format") ? opts["--format"].ToLowerInvariant() : "txt";
            if (!File.Exists(a) || !File.Exists(b)) { Console.WriteLine("Arquivo --a ou --b não encontrado."); return; }

            var resA = new PDFAnalyzer(a).AnalyzeFull();
            var resB = new PDFAnalyzer(b).AnalyzeFull();

            var modA = resA.Metadata.ModificationDate;
            var modB = resB.Metadata.ModificationDate;
            var createdA = resA.Metadata.CreationDate;
            var createdB = resB.Metadata.CreationDate;

            var report = new TimestampReport
            {
                FileA = a,
                FileB = b,
                CreationA = createdA,
                CreationB = createdB,
                ModA = modA,
                ModB = modB,
                Suggestion = Suggest(modA, modB)
            };

            if (format == "json")
                Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            else
            {
                Console.WriteLine($"A: created={createdA} mod={modA}");
                Console.WriteLine($"B: created={createdB} mod={modB}");
                Console.WriteLine($"Sugestão de última sessão: {report.Suggestion}");
            }
        }

        private string Suggest(DateTime? modA, DateTime? modB)
        {
            if (modA == null || modB == null) return "Indefinido";
            return modB > modA ? "B é mais recente" : (modA > modB ? "A é mais recente" : "Iguais");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf ts-last-session --a base.pdf --b novo.pdf [--format json|txt]");
        }

        private Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--") && i + 1 < args.Length)
                {
                    dict[args[i]] = args[i + 1];
                    i++;
                }
            }
            return dict;
        }

        private class TimestampReport
        {
            public string FileA { get; set; } = "";
            public string FileB { get; set; } = "";
            public DateTime? CreationA { get; set; }
            public DateTime? CreationB { get; set; }
            public DateTime? ModA { get; set; }
            public DateTime? ModB { get; set; }
            public string Suggestion { get; set; } = "";
        }
    }
}
