using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Gera um banco de hashes de laudos a partir de uma pasta de referência.
    /// Usa LaudoDetector para confirmar laudo e extrair perito/CPF/especialidade,
    /// e por padrão deriva a espécie do nome da subpasta imediatamente acima do PDF.
    ///
    /// Uso:
    ///   fpdf laudo-hash-db --dir pasta_ref --out hashes.json [--keep-non-laudo] [--no-spec-from-path] [--jsonl]
    /// </summary>
    public class LaudoHashDbCommand : Command
    {
        public override string Name => "laudo-hash-db";
        public override string Description => "Gera banco de hashes de laudos (hash, espécie, perito, cpf, especialidade).";

        public override void Execute(string[] args)
        {
            string dir = "";
            string outFile = Path.Combine(Directory.GetCurrentDirectory(), "laudo_hashes.json");
            bool keepNonLaudo = false;
            bool specFromPath = true;
            string baseDir = "";
            bool jsonl = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dir":
                    case "-d":
                        if (i + 1 < args.Length) dir = args[++i];
                        break;
                    case "--out":
                    case "-o":
                        if (i + 1 < args.Length) outFile = args[++i];
                        break;
                    case "--keep-non-laudo":
                        keepNonLaudo = true;
                        break;
                    case "--no-spec-from-path":
                        specFromPath = false;
                        break;
                    case "--base-dir":
                        if (i + 1 < args.Length) baseDir = args[++i];
                        break;
                    case "--jsonl":
                        jsonl = true;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                Console.WriteLine("Informe --dir <pasta de referência>"); return;
            }

            if (string.IsNullOrWhiteSpace(baseDir)) baseDir = Path.GetFullPath(dir);

            var files = Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories);
            var entries = new List<object>();
            using var outWriter = jsonl ? new StreamWriter(outFile, false) : null;

            foreach (var file in files)
            {
                try
                {
                    var analyzer = new PDFAnalyzer(file);
                    var analysis = analyzer.AnalyzeFull();
                    var pagesText = analysis.Pages.Select(p => p.TextInfo.PageText ?? "").ToList();
                    string header = analysis.Pages.FirstOrDefault()?.TextInfo.Headers.FirstOrDefault() ?? "";
                    string footer = analysis.Pages.FirstOrDefault()?.TextInfo.Footers.FirstOrDefault() ?? "";
                    string fullText = string.Join("\n", pagesText);
                    string docLabel = Path.GetFileNameWithoutExtension(file);

                    var det = LaudoDetector.Detect(docLabel, pagesText, header, footer, fullText);
                    if (!keepNonLaudo && !det.IsLaudo) continue;

                    string especie = det.Especie ?? "";
                    if (specFromPath)
                    {
                        var rel = Path.GetRelativePath(baseDir, Path.GetDirectoryName(file) ?? "");
                        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                            especie = parts[0]; // usa pasta de nível superior como espécie
                    }

                    var obj = new
                    {
                        file = file,
                        doc_label = docLabel,
                        laudo_hash = det.Hash,
                        laudo_score = det.Score,
                        is_laudo = det.IsLaudo,
                        especie = especie,
                        perito = det.Perito,
                        cpf = det.Cpf,
                        especialidade = det.Especialidade
                    };

                    if (jsonl && outWriter != null)
                        outWriter.WriteLine(JsonConvert.SerializeObject(obj));
                    else
                        entries.Add(obj);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Erro em {file}: {ex.Message}");
                }
            }

            if (!jsonl)
            {
                File.WriteAllText(outFile, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }

            Console.WriteLine($"[laudo-hash-db] Gravado em {outFile}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf laudo-hash-db --dir <pasta_ref> [--out hashes.json] [--keep-non-laudo] [--no-spec-from-path] [--jsonl]");
        }
    }
}
