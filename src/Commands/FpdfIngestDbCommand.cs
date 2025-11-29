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
    /// Uso: fpdf ingest-db <cache|range|dir|pdf> --db-path data/sqlite/sqlite-mcp.db
    /// </summary>
    public class FpdfIngestDbCommand : Command
    {
        public override string Name => "ingest-db";
        public override string Description => "Ingere caches em SQLite";

        public override void Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Uso: fpdf ingest-db <cache|range|dir|pdf> [--db-path path]");
                return;
            }

            string target = args[0];
            string dbPath = "data/sqlite/sqlite-mcp.db";
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--db-path" && i + 1 < args.Length)
                {
                    dbPath = args[++i];
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".");

            var caches = ResolveCaches(target);
            if (caches.Count == 0)
            {
                Console.WriteLine($"Nenhum cache encontrado para '{target}'.");
                return;
            }

            try
            {
                using var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();
                using var tx = conn.BeginTransaction();

                foreach (var cacheFile in caches)
                {
                    var analysis = LoadAnalysis(cacheFile);
                    if (analysis == null)
                    {
                        Console.WriteLine($"Cache inválido: {cacheFile}");
                        continue;
                    }
                    UpsertProcess(conn, analysis, cacheFile);
                    UpsertDocuments(conn, analysis);
                    UpsertPages(conn, analysis);
                    // field_hits não implementado (não estava presente no cache); placeholder
                }

                tx.Commit();
                Console.WriteLine($"Ingestão concluída: {caches.Count} cache(s) -> {dbPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro na ingestão: {ex.Message}");
            }
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

        private PDFAnalysisResult LoadAnalysis(string cacheFile)
        {
            try
            {
                var json = File.ReadAllText(cacheFile);
                return JsonConvert.DeserializeObject<PDFAnalysisResult>(json);
            }
            catch
            {
                return null;
            }
        }

        private void UpsertProcess(System.Data.SQLite.SQLiteConnection conn, PDFAnalysisResult analysis, string cacheFile)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO processes(id, sei, origem, created_at) VALUES (@id,@sei,@origem,@created)";
            cmd.Parameters.AddWithValue("@id", analysis.DocumentInfo?.OriginalFileName ?? Path.GetFileNameWithoutExtension(cacheFile));
            cmd.Parameters.AddWithValue("@sei", analysis.DocumentInfo?.OriginalFileName ?? "");
            cmd.Parameters.AddWithValue("@origem", cacheFile);
            cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("s"));
            cmd.ExecuteNonQuery();
        }

        private void UpsertDocuments(System.Data.SQLite.SQLiteConnection conn, PDFAnalysisResult analysis)
        {
            if (analysis.Documents == null) return;
            foreach (var d in analysis.Documents)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO documents(process_id, name, doc_type, start_page, end_page) VALUES (@p,@n,@t,@s,@e)";
                cmd.Parameters.AddWithValue("@p", analysis.DocumentInfo?.OriginalFileName ?? "");
                cmd.Parameters.AddWithValue("@n", d.Name ?? "");
                cmd.Parameters.AddWithValue("@t", d.Type ?? "");
                cmd.Parameters.AddWithValue("@s", d.StartPage);
                cmd.Parameters.AddWithValue("@e", d.EndPage);
                cmd.ExecuteNonQuery();
            }
        }

        private void UpsertPages(System.Data.SQLite.SQLiteConnection conn, PDFAnalysisResult analysis)
        {
            if (analysis.Pages == null) return;
            foreach (var p in analysis.Pages)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO pages(document_id, page_number, text) VALUES ((SELECT id FROM documents WHERE process_id=@p LIMIT 1), @num, @text)";
                cmd.Parameters.AddWithValue("@p", analysis.DocumentInfo?.OriginalFileName ?? "");
                cmd.Parameters.AddWithValue("@num", p.PageNumber);
                cmd.Parameters.AddWithValue("@text", p.TextInfo?.PageText ?? "");
                cmd.ExecuteNonQuery();
            }
        }
    }
}
