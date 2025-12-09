using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using FilterPDF.Interfaces;
using FilterPDF.Utils;

namespace FilterPDF.Services
{
    /// <summary>
    /// Implementation of cache service
    /// Manages PDF analysis cache operations
    /// </summary>
    public class CacheService : ICacheService
    {
        private readonly IFileSystemService _fileSystem;
        private readonly string _cacheDirectory;

        public CacheService(IFileSystemService fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _cacheDirectory = GetCacheDirectory();
        }

        /// <summary>
        /// Find a cache file by identifier
        /// </summary>
        public string? FindCacheFile(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return null;

            // Delegate to existing CacheManager for backwards compatibility
            return CacheManager.FindCacheFile(identifier);
        }

        /// <summary>
        /// List all cached PDFs
        /// </summary>
        public List<Interfaces.CacheEntry> ListCachedPDFs()
        {
            // Delegate to existing CacheManager and convert format
            var existingEntries = CacheManager.ListCachedPDFs();
            
            return existingEntries.Select(entry => new Interfaces.CacheEntry
            {
                CachePath = entry.CachePath,
                OriginalFileName = entry.OriginalFileName,
                CreatedDate = entry.CachedDate,
                FileSize = entry.OriginalSize
            }).ToList();
        }

        /// <summary>
        /// Load cache file contents
        /// </summary>
        public async Task<PDFAnalysisResult?> LoadCacheFileAsync(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath) || !_fileSystem.FileExists(cachePath))
                return null;

            try
            {
                // Use existing CacheMemoryManager for consistency
                return CacheMemoryManager.LoadCacheFile(cachePath);
            }
            catch (Exception)
            {
                // If synchronous load fails, try async read
                try
                {
                    var content = await _fileSystem.ReadAllTextAsync(cachePath);
                    return JsonConvert.DeserializeObject<PDFAnalysisResult>(content);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Save analysis result to cache
        /// </summary>
        public async Task SaveToCacheAsync(string originalFilePath, PDFAnalysisResult analysisResult)
        {
            if (string.IsNullOrEmpty(originalFilePath) || analysisResult == null)
                throw new ArgumentException("Invalid parameters for cache save operation");

            _fileSystem.EnsureDirectoryExists(_cacheDirectory);

            // Generate cache file name based on original file
            var fileName = _fileSystem.GetFileNameWithoutExtension(originalFilePath);
            var cacheFileName = $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var cachePath = _fileSystem.Combine(_cacheDirectory, cacheFileName);

            // Serialize and save
            var content = JsonConvert.SerializeObject(analysisResult, Formatting.Indented);
            await _fileSystem.WriteAllTextAsync(cachePath, content);
        }

        /// <summary>
        /// Clear cache
        /// </summary>
        public void ClearCache()
        {
            if (!_fileSystem.DirectoryExists(_cacheDirectory))
                return;

            var cacheFiles = _fileSystem.GetFiles(_cacheDirectory, "*.json");
            
            foreach (var file in cacheFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore individual file deletion errors
                }
            }
        }

        /// <summary>
        /// Get cache directory path
        /// </summary>
        public string GetCacheDirectory()
        {
            // Use consistent cache directory with existing system
            return _fileSystem.Combine(Directory.GetCurrentDirectory(), "cache");
        }
    }
}