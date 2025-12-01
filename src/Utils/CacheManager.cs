using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using FilterPDF.Security;
using FilterPDF.Utils;
using System.Data.SQLite;

namespace FilterPDF
{
    /// <summary>
    /// Gerenciador de cache para PDFs carregados
    /// Permite carregar, listar e buscar PDFs por nome
    /// </summary>
    public static class CacheManager
    {
        private static readonly string DbPath = SqliteCacheStore.DefaultDbPath;
        
        // Lock para prevenir race conditions durante operações concorrentes
        private static readonly object indexLock = new object();
        
        /// <summary>
        /// Informações sobre um PDF em cache
        /// </summary>
        public class CacheEntry
        {
            public string OriginalFileName { get; set; } = "";
            public string OriginalPath { get; set; } = "";
            public string CacheFileName { get; set; } = "";
            public string CachePath { get; set; } = "";
            public DateTime CachedDate { get; set; }
            public long OriginalSize { get; set; }
            public long CacheSize { get; set; }
            public string ExtractionMode { get; set; } = "";
            public string Version { get; set; } = "";
        }
        
        /// <summary>
        /// Índice de todos os arquivos em cache
        /// </summary>
        public class CacheIndex
        {
            public Dictionary<string, CacheEntry> Entries { get; set; } = new Dictionary<string, CacheEntry>();
            public DateTime LastUpdated { get; set; } = DateTime.Now;
            public string Version { get; set; } = "2.12.0";
        }
        
        static CacheManager() { }
        
        /// <summary>
        /// Garante que o diretório de cache existe
        /// </summary>
        private static void EnsureDb()
        {
            SqliteCacheStore.EnsureDatabase(DbPath);
        }

        public class MetaStats
        {
            public int MetaTitle { get; set; }
            public int MetaAuthor { get; set; }
            public int MetaSubject { get; set; }
            public int MetaKeywords { get; set; }
            public int MetaCreationDate { get; set; }
            public int StatTotalImages { get; set; }
            public int StatTotalFonts { get; set; }
            public int StatBookmarks { get; set; }
            public int ResAttachments { get; set; }
            public int ResEmbeddedFiles { get; set; }
            public int ResJavascript { get; set; }
            public int ResMultimedia { get; set; }
            public int SecIsEncrypted { get; set; }
            public long SumImages { get; set; }
            public long SumBookmarks { get; set; }
            public long SumFonts { get; set; }
            public long SumPages { get; set; }
        }

        public class BookmarkSummaryItem
        {
            public string Title { get; set; } = "";
            public int Count { get; set; }
            public List<string> Samples { get; set; } = new List<string>();
        }

        public class TopValueItem
        {
            public string Value { get; set; } = "";
            public int Count { get; set; }
            public List<string> Samples { get; set; } = new List<string>();
        }

        public static MetaStats GetMetaStats()
        {
            EnsureDb();
            var m = new MetaStats();
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                  SUM(meta_title IS NOT NULL AND meta_title <> '') as meta_title,
                  SUM(meta_author IS NOT NULL AND meta_author <> '') as meta_author,
                  SUM(meta_subject IS NOT NULL AND meta_subject <> '') as meta_subject,
                  SUM(meta_keywords IS NOT NULL AND meta_keywords <> '') as meta_keywords,
                  SUM(meta_creation_date IS NOT NULL AND meta_creation_date <> '') as meta_creation_date,
                  SUM(stat_total_images IS NOT NULL) as stat_total_images,
                  SUM(stat_total_fonts IS NOT NULL) as stat_total_fonts,
                  SUM(stat_bookmarks IS NOT NULL) as stat_bookmarks,
                  SUM(res_attachments IS NOT NULL) as res_attachments,
                  SUM(res_embedded_files IS NOT NULL) as res_embedded_files,
                  SUM(res_javascript IS NOT NULL) as res_javascript,
                  SUM(res_multimedia IS NOT NULL) as res_multimedia,
                  SUM(sec_is_encrypted IS NOT NULL) as sec_is_encrypted,
                  COALESCE(SUM(stat_total_images),0) as sum_images,
                  COALESCE(SUM(stat_bookmarks),0) as sum_bookmarks,
                  COALESCE(SUM(stat_total_fonts),0) as sum_fonts,
                  COALESCE(SUM(doc_total_pages),0) as sum_pages
                FROM caches";
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                m.MetaTitle = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                m.MetaAuthor = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                m.MetaSubject = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                m.MetaKeywords = r.IsDBNull(3) ? 0 : r.GetInt32(3);
                m.MetaCreationDate = r.IsDBNull(4) ? 0 : r.GetInt32(4);
                m.StatTotalImages = r.IsDBNull(5) ? 0 : r.GetInt32(5);
                m.StatTotalFonts = r.IsDBNull(6) ? 0 : r.GetInt32(6);
                m.StatBookmarks = r.IsDBNull(7) ? 0 : r.GetInt32(7);
                m.ResAttachments = r.IsDBNull(8) ? 0 : r.GetInt32(8);
                m.ResEmbeddedFiles = r.IsDBNull(9) ? 0 : r.GetInt32(9);
                m.ResJavascript = r.IsDBNull(10) ? 0 : r.GetInt32(10);
                m.ResMultimedia = r.IsDBNull(11) ? 0 : r.GetInt32(11);
                m.SecIsEncrypted = r.IsDBNull(12) ? 0 : r.GetInt32(12);
                m.SumImages = r.IsDBNull(13) ? 0 : r.GetInt64(13);
                m.SumBookmarks = r.IsDBNull(14) ? 0 : r.GetInt64(14);
                m.SumFonts = r.IsDBNull(15) ? 0 : r.GetInt64(15);
                m.SumPages = r.IsDBNull(16) ? 0 : r.GetInt64(16);
            }
            return m;
        }

        public static List<BookmarkSummaryItem> GetBookmarkSummary(int top, int sampleSize)
        {
            var topItems = GetTopValues("bookmark", top, sampleSize);
            var list = new List<BookmarkSummaryItem>();
            foreach (var item in topItems)
            {
                list.Add(new BookmarkSummaryItem
                {
                    Title = item.Value,
                    Count = item.Count,
                    Samples = item.Samples
                });
            }
            return list;
        }

        public static List<TopValueItem> GetTopValues(string field, int top, int sampleSize)
        {
            EnsureDb();
            var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var samples = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            bool HasColumn(SQLiteConnection connection, string table, string column)
            {
                using var c = connection.CreateCommand();
                c.CommandText = $"PRAGMA table_info({table})";
                using var r = c.ExecuteReader();
                while (r.Read())
                {
                    if (!r.IsDBNull(1) && string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            void AddValue(string? rawValue, string sampleName)
            {
                if (string.IsNullOrWhiteSpace(rawValue)) return;
                var value = rawValue.Trim();
                if (value.Length == 0) return;
                if (!freq.TryGetValue(value, out var count))
                {
                    freq[value] = 1;
                    samples[value] = new List<string> { sampleName };
                }
                else
                {
                    freq[value] = count + 1;
                    if (samples[value].Count < sampleSize && !samples[value].Contains(sampleName))
                        samples[value].Add(sampleName);
                }
            }

            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();

            switch (field?.ToLowerInvariant())
            {
                case "bookmark":
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT name, json FROM caches WHERE stat_bookmarks IS NOT NULL";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            var name = r.IsDBNull(0) ? "" : r.GetString(0);
                            var jsonStr = r.IsDBNull(1) ? "" : r.GetString(1);
                            if (string.IsNullOrEmpty(jsonStr)) continue;
                            try
                            {
                                var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(jsonStr);
                                var bookmarks = data?["Bookmarks"];
                                if (bookmarks == null) continue;

                                void Walk(System.Text.Json.Nodes.JsonNode node)
                                {
                                    if (node == null) return;
                                    if (node is System.Text.Json.Nodes.JsonObject obj)
                                    {
                                        var title = obj["Title"]?.ToString();
                                        AddValue(title, name);
                                        var children = obj["Children"];
                                        if (children is System.Text.Json.Nodes.JsonArray arr)
                                        {
                                            foreach (var child in arr) Walk(child);
                                        }
                                    }
                                    else if (node is System.Text.Json.Nodes.JsonArray arr)
                                    {
                                        foreach (var child in arr) Walk(child);
                                    }
                                }

                                Walk(bookmarks);
                            }
                            catch
                            {
                                // ignora json inválido
                            }
                        }
                    }
                    break;

                case "meta_title":
                case "meta_author":
                case "meta_subject":
                case "meta_creator":
                case "meta_producer":
                case "doc_type":
                case "mode":
                    if (field.Equals("doc_type", StringComparison.OrdinalIgnoreCase) && !HasColumn(conn, "caches", "doc_type"))
                    {
                        break; // coluna inexistente, retorna vazio
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = $"SELECT name, {field} FROM caches WHERE {field} IS NOT NULL AND {field} <> ''";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            var name = r.IsDBNull(0) ? "" : r.GetString(0);
                            var value = r.IsDBNull(1) ? null : r.GetString(1);
                            AddValue(value, name);
                        }
                    }
                    break;

                case "meta_keywords":
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT name, meta_keywords FROM caches WHERE meta_keywords IS NOT NULL AND meta_keywords <> ''";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            var name = r.IsDBNull(0) ? "" : r.GetString(0);
                            var value = r.IsDBNull(1) ? "" : r.GetString(1);
                            var parts = value.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 0) AddValue(value, name);
                            foreach (var part in parts) AddValue(part, name);
                        }
                    }
                    break;

                default:
                    throw new ArgumentException($"Campo não suportado: {field}");
            }

            var result = freq
                .Select(kv => new TopValueItem
                {
                    Value = kv.Key,
                    Count = kv.Value,
                    Samples = samples.TryGetValue(kv.Key, out var s) ? s : new List<string>()
                })
                .OrderByDescending(i => i.Count)
                .ThenBy(i => i.Value, StringComparer.OrdinalIgnoreCase)
                .Take(top)
                .ToList();

            return result;
        }
        
        /// <summary>
        /// Adiciona um PDF ao cache com validação de segurança
        /// </summary>
        public static void AddToCache(string originalPdfPath, string cacheFilePath, string extractionMode = "both")
        {
            // Compat: escrita real é feita pelo SqliteCacheStore.UpsertCache; aqui só garantimos schema
            EnsureDb();
        }
        
        /// <summary>
        /// Busca um PDF no cache por nome (sem extensão) ou índice
        /// </summary>
        public static string? FindCacheFile(string pdfNameOrIndex)
        {
            EnsureDb();
            string cacheName = Path.GetFileNameWithoutExtension(pdfNameOrIndex);

            if (int.TryParse(pdfNameOrIndex, out int cacheIndex) && cacheIndex > 0)
            {
                var entries = ListCachedPDFs();
                if (cacheIndex >= 1 && cacheIndex <= entries.Count)
                {
                    cacheName = entries[cacheIndex - 1].CachePath.Replace("db://", "").Split('#')[1];
                }
            }

            if (SqliteCacheStore.CacheExists(DbPath, cacheName))
            {
                return $"db://{DbPath}#{cacheName}";
            }
            return null;
        }
        
        /// <summary>
        /// Lista todos os PDFs em cache
        /// </summary>
        public static List<CacheEntry> ListCachedPDFs()
        {
            EnsureDb();
            var list = new List<CacheEntry>();
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, source, created_at, size_bytes, mode FROM caches ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.IsDBNull(0) ? "" : r.GetString(0);
                var source = r.IsDBNull(1) ? "" : r.GetString(1);
                var created = r.IsDBNull(2) ? "" : r.GetString(2);
                var size = r.IsDBNull(3) ? 0 : r.GetInt64(3);
                var mode = r.IsDBNull(4) ? "sqlite" : r.GetString(4);
                if (!DateTime.TryParse(created, out var cachedAt))
                    cachedAt = DateTime.UtcNow;

                list.Add(new CacheEntry
                {
                    OriginalFileName = string.IsNullOrEmpty(source) ? name + ".pdf" : Path.GetFileName(source),
                    OriginalPath = source,
                    CacheFileName = name + ".sqlite",
                    CachePath = $"db://{DbPath}#{name}",
                    CachedDate = cachedAt,
                    OriginalSize = size,
                    CacheSize = size,
                    ExtractionMode = mode ?? "sqlite",
                    Version = "sqlite"
                });
            }
            return list;
        }
        
        /// <summary>
        /// Remove um PDF do cache
        /// </summary>
        public static bool RemoveFromCache(string pdfName)
        {
            EnsureDb();
            pdfName = Path.GetFileNameWithoutExtension(pdfName);
            // Simple delete from caches table
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM caches WHERE name=@n";
            cmd.Parameters.AddWithValue("@n", pdfName);
            var rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }
        
        /// <summary>
        /// Limpa todo o cache
        /// </summary>
        public static void ClearCache()
        {
            EnsureDb();
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM caches";
            cmd.ExecuteNonQuery();
        }
        
        /// <summary>
        /// Verifica se um PDF está em cache
        /// </summary>
        public static bool IsInCache(string pdfName)
        {
            return FindCacheFile(pdfName) != null;
        }
        
        /// <summary>
        /// Obtém informações sobre um PDF por índice ou nome
        /// </summary>
        public static CacheEntry? GetCacheEntry(string pdfNameOrIndex)
        {
            if (int.TryParse(pdfNameOrIndex, out int cacheIndex) && cacheIndex > 0)
            {
                var entries = ListCachedPDFs();
                if (cacheIndex <= entries.Count)
                {
                    return entries[cacheIndex - 1];
                }
                return null;
            }

            string pdfName = Path.GetFileNameWithoutExtension(pdfNameOrIndex);
            if (SqliteCacheStore.CacheExists(DbPath, pdfName))
            {
                return new CacheEntry
                {
                    OriginalFileName = pdfName + ".pdf",
                    OriginalPath = pdfName + ".pdf",
                    CacheFileName = pdfName + "._cache.json",
                    CachePath = $"db://{DbPath}#{pdfName}",
                    CachedDate = DateTime.Now,
                    ExtractionMode = "sqlite",
                    Version = "sqlite"
                };
            }
            return null;
        }
        
        /// <summary>
        /// Obtém estatísticas do cache
        /// </summary>
        public static CacheStats GetCacheStats()
        {
            EnsureDb();
            int total = 0;
            long totalSize = 0;
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(size_bytes),0) FROM caches";
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    total = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    totalSize = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                }
            }

            return new CacheStats
            {
                TotalEntries = total,
                TotalCacheSize = totalSize,
                TotalOriginalSize = totalSize,
                CacheDirectory = DbPath,
                LastUpdated = DateTime.UtcNow
            };
        }
        
        /// <summary>
        /// Estatísticas do cache
        /// </summary>
        public class CacheStats
        {
            public int TotalEntries { get; set; }
            public long TotalCacheSize { get; set; }
            public long TotalOriginalSize { get; set; }
            public string CacheDirectory { get; set; } = "";
            public DateTime LastUpdated { get; set; }
        }
        
        /// <summary>
        /// Reconstrói o índice a partir dos arquivos de cache existentes
        /// 
        /// Este método é útil quando:
        /// - O processamento paralelo com múltiplos workers causou race conditions
        /// - O arquivo index.json foi corrompido ou deletado
        /// - Existem arquivos de cache órfãos não listados no índice
        /// 
        /// Limitações dos dados reconstruídos:
        /// - ExtractionMode será marcado como "unknown"
        /// - OriginalSize será 0 (tamanho original não é armazenado no cache)
        /// - OriginalPath será apenas o nome do arquivo
        /// - CachedDate será a data de criação do arquivo de cache
        /// </summary>
        /// <returns>Número de entradas reconstruídas no índice</returns>
        public static int RebuildIndexFromFiles()
        {
            Console.WriteLine("Rebuild não é necessário: cache agora é armazenado em SQLite.");
            return 0;
        }
        
        /// <summary>
        /// Normaliza caminhos para o formato correto do sistema operacional atual
        /// Se Windows: formato Windows (C:\path)
        /// Se Linux/WSL: formato Unix (/path)
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
                
            // Se é caminho absoluto, normalizar para o sistema atual
            if (Path.IsPathRooted(path))
            {
                // Em Linux/WSL, converter para formato Unix
                if (Environment.OSVersion.Platform == PlatformID.Unix || 
                    Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    return path.Replace('\\', '/');
                }
                // No Windows, manter formato Windows
                return path;
            }
            
            // Para caminhos relativos, usar Path.GetFullPath mas normalizar resultado
            string fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
            
            // Em Linux/WSL, normalizar para formato Unix
            if (Environment.OSVersion.Platform == PlatformID.Unix || 
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                return fullPath.Replace('\\', '/');
            }
            
            // No Windows, retornar como está
            return fullPath;
        }
    }
}
