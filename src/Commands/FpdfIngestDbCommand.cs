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
        public override string Description => "Ingere caches em SQLite";

        public override void ShowHelp()
        {
            Console.WriteLine("Uso: fpdf ingest-db <cache|range|dir|pdf> [--db-path path]");
            Console.WriteLine("Exemplo: fpdf ingest-db .cache (db padrão: data/sqlite/sqlite-mcp.db)");
        }

        public override void Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Uso: fpdf ingest-db <cache|range|dir|pdf> [--db-path path]");
                return;
            }

            string target = args[0];
            string dbPath = SqliteCacheStore.DefaultDbPath;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--db-path" && i + 1 < args.Length)
                {
                    dbPath = args[++i];
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".");

            Console.WriteLine("ingest-db desativado: pipeline agora é somente SQLite gerado via 'fpdf load'.");
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
