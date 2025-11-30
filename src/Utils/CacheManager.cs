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
