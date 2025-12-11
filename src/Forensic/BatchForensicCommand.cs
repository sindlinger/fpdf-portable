using System;
using System.Collections.Generic;
using System.IO;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Executa comandos forenses (true-diff, last-session, enhanced-last-session, ts-last-session) em lote.
    /// Uso: fpdf forensic-batch --pairs list.txt --out-dir out/
    /// Onde list.txt contém linhas: base.pdf;novo.pdf
    /// </summary>
    public class BatchForensicCommand : Command
    {
        public override string Name => "forensic-batch";
        public override string Description => "Roda diffs forenses em lote";

        public override void Execute(string[] args)
        {
            string pairsFile = null;
            string outDir = "forensic-out";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--pairs" && i + 1 < args.Length) pairsFile = args[i + 1];
                if (args[i] == "--out-dir" && i + 1 < args.Length) outDir = args[i + 1];
            }
            if (string.IsNullOrEmpty(pairsFile) || !File.Exists(pairsFile))
            {
                Console.WriteLine("Informe --pairs <arquivo> com linhas base;novo");
                return;
            }
            Directory.CreateDirectory(outDir);

            var lines = File.ReadAllLines(pairsFile);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains(";")) continue;
                var parts = line.Split(';');
                if (parts.Length < 2) continue;
                var a = parts[0].Trim();
                var b = parts[1].Trim();
                if (!File.Exists(a) || !File.Exists(b)) continue;

                RunAndSave("true-diff", a, b, outDir);
                RunAndSave("last-session", a, b, outDir);
                RunAndSave("enhanced-last-session", a, b, outDir);
                RunAndSave("ts-last-session", a, b, outDir);
            }
            Console.WriteLine("Forensic batch concluído.");
        }

        private void RunAndSave(string cmd, string a, string b, string outDir)
        {
            // simples: cria instância e executa com args
            Command c = cmd switch
            {
                "true-diff" => new TrueDiffCommand(),
                "last-session" => new LastSessionCoordinateCommand(),
                "enhanced-last-session" => new EnhancedLastSessionCommand(),
                "ts-last-session" => new TimestampBasedLastSessionCommand(),
                _ => null
            };
            if (c == null) return;
            var outputFile = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(a)}__{Path.GetFileNameWithoutExtension(b)}__{cmd}.txt");
            var prevOut = Console.Out;
            using var sw = new StreamWriter(outputFile);
            Console.SetOut(sw);
            c.Execute(new[] { "--a", a, "--b", b, "--format", "txt" });
            Console.SetOut(prevOut);
            Console.WriteLine($"Salvo {outputFile}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf forensic-batch --pairs list.txt --out-dir out/");
            Console.WriteLine("Roda true-diff, last-session, enhanced-last-session e ts-last-session para cada par base;novo.");
        }
    }
}
