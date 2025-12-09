using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
using Newtonsoft.Json;
using FilterPDF.Services;

namespace FilterPDF.Utils
{
    /// <summary>
    /// Persistência direta em Postgres (caches/pages). Substitui o SQLite.
    /// </summary>
    public static class PgCacheStore
    {
        public static string DefaultPgUri = "postgres://fpdf:fpdf@localhost:5432/fpdf";

        public static void EnsureDatabase(string pgUri)
        {
            // Schema é criado via tools/pg_ddl.sql; aqui só testamos conexão.
            using var conn = new NpgsqlConnection(pgUri);
            conn.Open();
        }

        public static bool CacheExists(string pgUri, string cacheName)
        {
            using var conn = new NpgsqlConnection(pgUri);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT 1 FROM caches WHERE name=@n LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@n", cacheName);
            var r = cmd.ExecuteScalar();
            return r != null;
        }

        public static long UpsertCache(string pgUri, string cacheName, string sourcePath, PDFAnalysisResult analysis, string mode)
        {
            using var conn = new NpgsqlConnection(pgUri);
            conn.Open();
            using var tx = conn.BeginTransaction();

            var inferredProcess = InferProcessFromName(cacheName);
            var metaJson = SerializeMetadata(analysis);
            var meta = FlattenMeta(analysis);

            long cacheId;
            using (var cmd = new NpgsqlCommand(@"
                INSERT INTO caches(name, source, created_at, mode, stat_total_images, stat_bookmarks, stat_fonts, stat_total_words, stat_total_chars,
                                   meta_author, process_number, json)
                VALUES (@n,@s, now(), @m, @imgs, @bkm, @fonts, @words, @chars, @meta_author, @proc, @json)
                ON CONFLICT (process_number) DO UPDATE SET
                   name = EXCLUDED.name,
                   source = EXCLUDED.source,
                   mode = EXCLUDED.mode,
                   stat_total_images = EXCLUDED.stat_total_images,
                   stat_bookmarks = EXCLUDED.stat_bookmarks,
                   stat_fonts = EXCLUDED.stat_fonts,
                   stat_total_words = EXCLUDED.stat_total_words,
                   stat_total_chars = EXCLUDED.stat_total_chars,
                   meta_author = EXCLUDED.meta_author,
                   json = EXCLUDED.json
                RETURNING id;
            ", conn, tx))
            {
                cmd.Parameters.AddWithValue("@n", cacheName);
                cmd.Parameters.AddWithValue("@s", sourcePath ?? "");
                cmd.Parameters.AddWithValue("@m", mode ?? "");
                cmd.Parameters.AddWithValue("@imgs", analysis?.Statistics?.TotalImages ?? 0);
                cmd.Parameters.AddWithValue("@bkm", analysis?.Bookmarks?.TotalCount ?? 0);
                cmd.Parameters.AddWithValue("@fonts", analysis?.Resources?.TotalFonts ?? 0);
                cmd.Parameters.AddWithValue("@words", analysis?.Statistics?.TotalWords ?? 0);
                cmd.Parameters.AddWithValue("@chars", analysis?.Statistics?.TotalCharacters ?? 0);
                cmd.Parameters.AddWithValue("@meta_author", meta.MetaAuthor ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@proc", inferredProcess ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@json", (object)metaJson ?? DBNull.Value);
                cacheId = (long)(cmd.ExecuteScalar()!);
            }

            // Clear old pages (idempotente)
            using (var del = new NpgsqlCommand("DELETE FROM pages WHERE cache_id=@cid", conn, tx))
            {
                del.Parameters.AddWithValue("@cid", cacheId);
                del.ExecuteNonQuery();
            }

            if (analysis?.Pages != null)
            {
                foreach (var page in analysis.Pages)
                {
                    var text = page?.TextInfo?.PageText ?? "";
                    var header = string.Join("\n", text.Split('\n').Take(5));
                    var footer = string.Join("\n", text.Split('\n').Reverse().Take(5).Reverse());
                    var fonts = page?.TextInfo?.Fonts != null ? string.Join("|", page.TextInfo.Fonts.Select(f => f.Name)) : "";
                    var imgCount = page?.Resources?.Images?.Count ?? 0;
                    var annCount = page?.Annotations?.Count ?? 0;
                    var hasMoney = HasMoney(text) ? 1 : 0;
                    var hasCpf = HasCpf(text) ? 1 : 0;
                    using var ins = new NpgsqlCommand(@"
                        INSERT INTO pages(cache_id,page_number,text,header_virtual,footer_virtual,has_money,has_cpf,fonts)
                        VALUES (@c,@p,@t,@h,@f,@m,@cpf,@fonts)
                    ", conn, tx);
                    ins.Parameters.AddWithValue("@c", cacheId);
                    ins.Parameters.AddWithValue("@p", page?.PageNumber ?? 0);
                    ins.Parameters.AddWithValue("@t", text);
                    ins.Parameters.AddWithValue("@h", header);
                    ins.Parameters.AddWithValue("@f", footer);
                    ins.Parameters.AddWithValue("@m", hasMoney);
                    ins.Parameters.AddWithValue("@cpf", hasCpf);
                    ins.Parameters.AddWithValue("@fonts", fonts);
                    ins.ExecuteNonQuery();
                }
            }

            tx.Commit();
            return cacheId;
        }

        public static List<string> ListCaches(string pgUri)
        {
            using var conn = new NpgsqlConnection(pgUri);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT name FROM caches ORDER BY created_at DESC", conn);
            var lst = new List<string>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) lst.Add(r.GetString(0));
            return lst;
        }

        private static string? InferProcessFromName(string cacheName)
        {
            // Heurística simples: pega dígitos do nome
            var digits = new string(cacheName.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digits) ? null : digits;
        }

        private static string SerializeMetadata(PDFAnalysisResult analysis)
        {
            return analysis == null ? null : JsonConvert.SerializeObject(analysis, Formatting.None);
        }

        private class MetaDto
        {
            public string MetaAuthor { get; set; }
        }

        private static MetaDto FlattenMeta(PDFAnalysisResult analysis)
        {
            if (analysis == null) return new MetaDto();
            return new MetaDto
            {
                MetaAuthor = analysis.Metadata?.Author
            };
        }

        private static bool HasMoney(string text)
        {
            return text.Contains("R$") || text.IndexOf("honor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasCpf(string text)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{3}[\.-]?\d{3}[\.-]?\d{3}[\.-]?\d{2}\b");
        }
    }
}
