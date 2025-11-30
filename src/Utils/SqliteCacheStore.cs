using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Newtonsoft.Json.Linq;

namespace FilterPDF.Utils
{
    /// <summary>
    /// Helper util to persist and read cache data in SQLite.
    /// Single source of truth for cache (replaces JSON files/index).
    /// </summary>
    public static class SqliteCacheStore
    {
        public static readonly string DefaultDbPath = Path.Combine("data", "sqlite", "sqlite-mcp.db");

        public static void EnsureDatabase(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".");
            using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA foreign_keys=ON;

                CREATE TABLE IF NOT EXISTS caches (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT UNIQUE,
                    source TEXT,
                    created_at TEXT,
                    mode TEXT,
                    size_bytes INTEGER
                );

                CREATE TABLE IF NOT EXISTS processes (
                    id TEXT PRIMARY KEY,
                    sei TEXT,
                    origem TEXT,
                    created_at TEXT
                );

                CREATE TABLE IF NOT EXISTS documents (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    process_id TEXT REFERENCES processes(id) ON DELETE CASCADE,
                    name TEXT,
                    doc_type TEXT,
                    start_page INT,
                    end_page INT
                );

                CREATE TABLE IF NOT EXISTS pages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    cache_id INTEGER NOT NULL REFERENCES caches(id) ON DELETE CASCADE,
                    document_id INTEGER REFERENCES documents(id) ON DELETE CASCADE,
                    page_number INTEGER,
                    text TEXT,
                    word_count INTEGER GENERATED ALWAYS AS (
                        (length(text) - length(replace(text, ' ', '')) + 1)
                    ) VIRTUAL,
                    header TEXT,
                    footer TEXT,
                    type TEXT,
                    has_money INTEGER DEFAULT 0,
                    has_cpf INTEGER DEFAULT 0,
                    fonts TEXT,
                    UNIQUE(cache_id, page_number)
                );

                CREATE VIRTUAL TABLE IF NOT EXISTS page_fts USING fts5(
                    cache_id UNINDEXED,
                    page_number UNINDEXED,
                    text,
                    tokenize='unicode61'
                );

                CREATE TRIGGER IF NOT EXISTS pages_ai AFTER INSERT ON pages
                BEGIN
                    INSERT INTO page_fts(rowid, cache_id, page_number, text)
                    VALUES (new.id, new.cache_id, new.page_number, new.text);
                END;

                CREATE TRIGGER IF NOT EXISTS pages_ad AFTER DELETE ON pages
                BEGIN
                    DELETE FROM page_fts WHERE rowid = old.id;
                END;

                CREATE TRIGGER IF NOT EXISTS pages_au AFTER UPDATE OF text ON pages
                BEGIN
                    UPDATE page_fts SET text = new.text WHERE rowid = new.id;
                END;

                CREATE INDEX IF NOT EXISTS idx_pages_cache_page ON pages(cache_id, page_number);
                CREATE INDEX IF NOT EXISTS idx_documents_process ON documents(process_id);
            ";
            cmd.ExecuteNonQuery();

            // Migração leve: garantir colunas novas sem quebrar bases existentes
            void TryAlter(string sql)
            {
                try
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = sql;
                    c.ExecuteNonQuery();
                }
                catch
                {
                    // ignora se já existe
                }
            }
            TryAlter("ALTER TABLE pages ADD COLUMN has_money INTEGER DEFAULT 0;");
            TryAlter("ALTER TABLE pages ADD COLUMN has_cpf INTEGER DEFAULT 0;");
            TryAlter("ALTER TABLE pages ADD COLUMN fonts TEXT;");
        }

        public static bool CacheExists(string dbPath, string cacheName)
        {
            EnsureDatabase(dbPath);
            using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM caches WHERE name=@n LIMIT 1";
            cmd.Parameters.AddWithValue("@n", cacheName);
            using var reader = cmd.ExecuteReader();
            return reader.Read();
        }

        public static void UpsertCache(string dbPath, string cacheName, string sourcePath, PDFAnalysisResult analysis, string mode)
        {
            EnsureDatabase(dbPath);
            using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
            using var tx = conn.BeginTransaction();

            long cacheId = InsertOrUpdateCache(conn, cacheName, sourcePath, mode, 0);

            // Clear old pages for idempotency
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM pages WHERE cache_id=@cid";
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
                    var hasMoney = HasMoney(text) ? 1 : 0;
                    var hasCpf = HasCpf(text) ? 1 : 0;
                    using var ins = conn.CreateCommand();
                    ins.CommandText = @"INSERT INTO pages(cache_id,page_number,text,header,footer,has_money,has_cpf,fonts)
                                        VALUES (@c,@p,@t,@h,@f,@m,@cpf,@fonts)";
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
        }

        public static string? GetCacheJson(string dbPath, string cacheName)
        {
            return null; // JSON legacy removido
        }

        public static List<string> ListCacheNames(string dbPath)
        {
            var list = new List<string>();
            if (!File.Exists(dbPath)) return list;
            using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM caches ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }
            return list;
        }

        private static long InsertOrUpdateCache(SQLiteConnection conn, string cacheName, string sourcePath, string mode, long sizeBytes)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO caches(name, source, created_at, mode, size_bytes, json)
                                VALUES (@n,@s,@c,@m,@sz,NULL)
                                ON CONFLICT(name) DO UPDATE SET
                                  source=excluded.source,
                                  created_at=excluded.created_at,
                                  mode=excluded.mode,
                                  size_bytes=excluded.size_bytes
                                ;
                                SELECT id FROM caches WHERE name=@n LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", cacheName);
            cmd.Parameters.AddWithValue("@s", sourcePath);
            cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("s"));
            cmd.Parameters.AddWithValue("@m", mode);
            cmd.Parameters.AddWithValue("@sz", sizeBytes);
            var result = cmd.ExecuteScalar();
            return (result is long l) ? l : Convert.ToInt64(result);
        }

        private static int ComputeWordCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(new[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static IEnumerable<(int pageNumber, string text)> ExtractPages(string jsonString)
        {
            var list = new List<(int, string)>();
            if (string.IsNullOrWhiteSpace(jsonString)) return list;
            try
            {
                var root = JToken.Parse(jsonString);
                var pages = root["Pages"] ?? root["pages"];
                if (pages == null || pages.Type != JTokenType.Array) return list;
                int idx = 0;
                foreach (var p in pages)
                {
                    idx++;
                    int pageNumber = p["PageNumber"]?.Value<int?>() ?? idx;
                    string text =
                        p["TextInfo"]?["PageText"]?.Value<string>() ??
                        p["pageText"]?.Value<string>() ??
                        p["text"]?.Value<string>() ?? "";
                    list.Add((pageNumber, text));
                }
            }
            catch
            {
                // swallow; return what we have
            }
            return list;
        }

        private static bool HasMoney(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"R\$ ?[0-9]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static bool HasCpf(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\\d{3}\\.\\d{3}\\.\\d{3}-\\d{2}\\b|\b\\d{11}\\b");
        }
    }
}
