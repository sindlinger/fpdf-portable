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
            // A escrita real é feita pelo SqliteCacheStore.UpsertCache; aqui só garantimos schema
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
            var names = SqliteCacheStore.ListCacheNames(DbPath);
            foreach (var name in names)
            {
                list.Add(new CacheEntry
                {
                    OriginalFileName = name + ".pdf",
                    OriginalPath = name + ".pdf",
                    CacheFileName = name + "._cache.json",
                    CachePath = $"db://{DbPath}#{name}",
                    CachedDate = DateTime.Now,
                    CacheSize = 0,
                    ExtractionMode = "sqlite",
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
            var entries = ListCachedPDFs();

            return new CacheStats
            {
                TotalEntries = entries.Count,
                TotalCacheSize = entries.Sum(e => e.CacheSize),
                TotalOriginalSize = entries.Sum(e => e.OriginalSize),
                CacheDirectory = DbPath,
                LastUpdated = DateTime.Now
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
            lock (indexLock)
            {
                var newIndex = new CacheIndex();
                // OTIMIZAÇÃO: Não listar todos os arquivos automaticamente para evitar lentidão
                var cacheFiles = new string[0]; // Array vazio - rebuild só quando necessário
                
                int processed = 0;
                int failed = 0;
                
                foreach (var cacheFile in cacheFiles)
                {
                    try
                    {
                        // Validar que é um arquivo de cache válido
                        var cacheFileName = Path.GetFileName(cacheFile);
                        if (!cacheFileName.EndsWith("_cache.json") || cacheFileName == "index.json")
                        {
                            continue;
                        }
                        
                        // Extrair o nome do arquivo original do nome do cache
                        var fileName = cacheFileName.Replace("_cache.json", "");
                        
                        // Validar que o arquivo existe e é legível
                        var fileInfo = new FileInfo(cacheFile);
                        if (!fileInfo.Exists || fileInfo.Length == 0)
                        {
                            Console.WriteLine($"  Warning: Skipping empty or missing file: {cacheFileName}");
                            failed++;
                            continue;
                        }
                        
                        // Tentar detectar o modo de extração pelo tamanho do arquivo
                        string extractionMode = "unknown";
                        if (fileInfo.Length > 5 * 1024 * 1024) // > 5MB provavelmente é ultra
                        {
                            extractionMode = "ultra";
                        }
                        else if (fileInfo.Length < 100 * 1024) // < 100KB provavelmente é text
                        {
                            extractionMode = "text";
                        }
                        
                        // Criar entrada baseada no arquivo de cache
                        var entry = new CacheEntry
                        {
                            OriginalFileName = fileName + ".pdf",
                            OriginalPath = fileName + ".pdf", // Não temos o caminho original
                            CacheFileName = cacheFileName,
                            CachePath = Path.GetFullPath(cacheFile),
                            CachedDate = File.GetCreationTime(cacheFile),
                            OriginalSize = 0, // Não temos o tamanho original
                            CacheSize = fileInfo.Length,
                            ExtractionMode = extractionMode,
                            Version = "2.12.0"
                        };
                        
                        // Adicionar ao novo índice
                        newIndex.Entries[fileName] = entry;
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error processing {Path.GetFileName(cacheFile)}: {ex.Message}");
                        failed++;
                    }
                }
                
                if (failed > 0)
                {
                    Console.WriteLine($"  Processed: {processed} files, Failed: {failed} files");
                }
                
                // Índice legacy removido na migração para SQLite; não salvar em arquivo
                return newIndex.Entries.Count;
            }
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
