using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FilterPDF.Interfaces
{
    /// <summary>
    /// Service for console output abstraction
    /// </summary>
    public interface IOutputService
    {
        /// <summary>
        /// Write a line to the console
        /// </summary>
        void WriteLine(string message = "");
        
        /// <summary>
        /// Write text to the console without a newline
        /// </summary>
        void Write(string message);
        
        /// <summary>
        /// Write an error message to stderr
        /// </summary>
        void WriteError(string message);
        
        /// <summary>
        /// Write output to a file
        /// </summary>
        Task WriteToFileAsync(string filePath, string content);
        
        /// <summary>
        /// Flush output streams
        /// </summary>
        void Flush();
        
        /// <summary>
        /// Redirect console output to capture for processing
        /// </summary>
        IDisposable RedirectOutput(out StringWriter outputCapture);
    }

    /// <summary>
    /// Service for file system operations
    /// </summary>
    public interface IFileSystemService
    {
        /// <summary>
        /// Check if a file exists
        /// </summary>
        bool FileExists(string path);
        
        /// <summary>
        /// Check if a directory exists
        /// </summary>
        bool DirectoryExists(string path);
        
        /// <summary>
        /// Read all text from a file
        /// </summary>
        Task<string> ReadAllTextAsync(string path);
        
        /// <summary>
        /// Write all text to a file
        /// </summary>
        Task WriteAllTextAsync(string path, string content);
        
        /// <summary>
        /// Get files in a directory matching a pattern
        /// </summary>
        string[] GetFiles(string directory, string pattern);
        
        /// <summary>
        /// Get the absolute path for a given path
        /// </summary>
        string GetFullPath(string path);
        
        /// <summary>
        /// Get the directory name from a path
        /// </summary>
        string? GetDirectoryName(string path);
        
        /// <summary>
        /// Get the file name without extension
        /// </summary>
        string GetFileNameWithoutExtension(string path);
        
        /// <summary>
        /// Combine path segments
        /// </summary>
        string Combine(params string[] paths);
        
        /// <summary>
        /// Create a directory if it doesn't exist
        /// </summary>
        void EnsureDirectoryExists(string directoryPath);
    }

    /// <summary>
    /// Service for command management
    /// </summary>
    public interface ICommandRegistry
    {
        /// <summary>
        /// Register a command with the registry
        /// </summary>
        void RegisterCommand(string name, Command command);
        
        /// <summary>
        /// Get a command by name
        /// </summary>
        Command? GetCommand(string name);
        
        /// <summary>
        /// Check if a command exists
        /// </summary>
        bool HasCommand(string name);
        
        /// <summary>
        /// Get all registered command names
        /// </summary>
        IEnumerable<string> GetCommandNames();
        
        /// <summary>
        /// Initialize all commands
        /// </summary>
        void InitializeCommands();
    }

    /// <summary>
    /// Service for cache operations
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// Find a cache file by identifier
        /// </summary>
        string? FindCacheFile(string identifier);
        
        /// <summary>
        /// List all cached PDFs
        /// </summary>
        List<Interfaces.CacheEntry> ListCachedPDFs();
        
        /// <summary>
        /// Load cache file contents
        /// </summary>
        Task<PDFAnalysisResult?> LoadCacheFileAsync(string cachePath);
        
        /// <summary>
        /// Save analysis result to cache
        /// </summary>
        Task SaveToCacheAsync(string originalFilePath, PDFAnalysisResult analysisResult);
        
        /// <summary>
        /// Clear cache
        /// </summary>
        void ClearCache();
        
        /// <summary>
        /// Get cache directory path
        /// </summary>
        string GetCacheDirectory();
    }

    /// <summary>
    /// Cache entry model
    /// </summary>
    public class CacheEntry
    {
        public string CachePath { get; set; } = "";
        public string OriginalFileName { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public long FileSize { get; set; }
    }
}