using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using FilterPDF.Utils;

namespace FilterPDF
{
    /// <summary>
    /// Ingesta caches/analysis em um SQLite local (ou MCP, futuro) preenchendo
    /// as tabelas definidas em scripts/init_db.sql: processes, documents, pages, field_hits.
    /// Uso: fpdf ingest-db <cache|range|dir|pdf> (db padrão: data/sqlite/sqlite-mcp.db)
    /// </summary>
    public class FpdfIngestDbCommand : Command
    {
        public override string Name => "ingest-db";
        public override string Description => "Ingere caches .json em Postgres";

        public override void ShowHelp()
        {
            Console.WriteLine("Uso: fpdf ingest-db <cache|dir> [--pg-uri uri]");
            Console.WriteLine("Exemplo: fpdf ingest-db .cache --pg-uri postgres://fpdf:fpdf@localhost:5432/fpdf");
        }

        public override void Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Uso: fpdf ingest-db <cache|range|dir|pdf> [--db-path path]");
                return;
            }

            string target = args[0];
            string pgUri = Utils.PgDocStore.DefaultPgUri;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--pg-uri" && i + 1 < args.Length)
                {
                    pgUri = args[++i];
                }
            }

            var files = ResolveCaches(target);
            if (!files.Any())
            {
                Console.WriteLine($"Nenhum cache json encontrado em {target}");
                return;
            }

            var classifier = new Utils.BookmarkClassifier();
            int ok = 0, fail = 0;
            foreach (var f in files)
            {
                try
                {
                    var json = File.ReadAllText(f);
                    var analysis = JsonConvert.DeserializeObject<PDFAnalysisResult>(json);
                    if (analysis == null)
                        throw new Exception("JSON vazio");
                    Utils.PgDocStore.UpsertProcess(pgUri, analysis.FilePath ?? f, analysis, classifier, storeJson:false);
                    ok++;
                    Console.WriteLine($"✔ {Path.GetFileName(f)} → PG");
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine($"✗ {Path.GetFileName(f)}: {ex.Message}");
                }
            }

            Console.WriteLine($"Concluído. OK={ok} FAIL={fail}");
        }

        private List<string> ResolveCaches(string target)
        {
            var list = new List<string>();
            if (File.Exists(target) && target.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(target);
            }
            else if (Directory.Exists(target))
            {
                list.AddRange(Directory.GetFiles(target, "*.json", SearchOption.AllDirectories));
            }
            else
            {
                // range de caches: não implementado aqui; ficaríamos no input direto
            }
            return list;
        }

    }
}
