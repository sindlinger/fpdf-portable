using System;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;

namespace FilterPDF
{
    /// <summary>
    /// Gerencia cache em memória de arquivos JSON deserializados para evitar recarregamento
    /// </summary>
    public static class CacheMemoryManager
    {
        private static readonly ConcurrentDictionary<string, CachedAnalysis> _memoryCache = new();
        private static readonly object _lockObj = new object();
        
        private class CachedAnalysis
        {
            public PDFAnalysisResult Analysis { get; set; } = new PDFAnalysisResult();
            public DateTime LoadTime { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public long FileSize { get; set; }
        }
        
        /// <summary>
        /// Carrega um arquivo de cache, usando memória se já foi carregado
        /// </summary>
        public static PDFAnalysisResult? LoadCacheFile(string cacheFilePath)
        {
            var fileInfo = new FileInfo(cacheFilePath);
            var cacheKey = fileInfo.FullName;
            
            // Verificar se já está em memória
            if (_memoryCache.TryGetValue(cacheKey, out var cached))
            {
                Console.Error.WriteLine($"[DEBUG] Using MEMORY CACHE for {Path.GetFileName(cacheFilePath)} (loaded at {cached.LoadTime:HH:mm:ss})");
                return cached.Analysis;
            }
            
            // Carregar do disco
            lock (_lockObj)
            {
                // Double-check após lock
                if (_memoryCache.TryGetValue(cacheKey, out cached))
                {
                    return cached.Analysis;
                }
                
                Console.Error.WriteLine($"[DEBUG] Loading from DISK: {Path.GetFileName(cacheFilePath)}");
                var startTime = DateTime.Now;
                
                var json = File.ReadAllText(cacheFilePath);
                var loadTime = DateTime.Now - startTime;
                Console.Error.WriteLine($"[DEBUG] File.ReadAllText took {loadTime.TotalMilliseconds:F1}ms");
                
                startTime = DateTime.Now;
                var analysis = JsonConvert.DeserializeObject<PDFAnalysisResult>(json);
                var deserializeTime = DateTime.Now - startTime;
                Console.Error.WriteLine($"[DEBUG] JSON deserialization took {deserializeTime.TotalMilliseconds:F1}ms");
                
                if (analysis != null)
                {
                    // Adicionar ao cache em memória
                    _memoryCache[cacheKey] = new CachedAnalysis
                    {
                        Analysis = analysis,
                        LoadTime = DateTime.Now,
                        FilePath = cacheFilePath,
                        FileSize = fileInfo.Length
                    };
                    
                    Console.Error.WriteLine($"[DEBUG] Added to memory cache. Total cached files: {_memoryCache.Count}");
                }
                
                return analysis;
            }
        }
        
        /// <summary>
        /// Limpa o cache em memória
        /// </summary>
        public static void ClearCache()
        {
            _memoryCache.Clear();
            Console.Error.WriteLine("[DEBUG] Memory cache cleared");
        }
        
        /// <summary>
        /// Obtém estatísticas do cache
        /// </summary>
        public static (int FileCount, long TotalSize) GetCacheStats()
        {
            int count = _memoryCache.Count;
            long totalSize = 0;
            
            foreach (var item in _memoryCache.Values)
            {
                totalSize += item.FileSize;
            }
            
            return (count, totalSize);
        }
    }
}