using System;
using System.IO;
using System.Threading.Tasks;
using FilterPDF.Interfaces;

namespace FilterPDF.Services
{
    /// <summary>
    /// Implementation of file system service
    /// Provides abstraction for file system operations to enable testing and security
    /// </summary>
    public class FileSystemService : IFileSystemService
    {
        /// <summary>
        /// Check if a file exists
        /// </summary>
        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        /// <summary>
        /// Check if a directory exists
        /// </summary>
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        /// <summary>
        /// Read all text from a file
        /// </summary>
        public async Task<string> ReadAllTextAsync(string path)
        {
            return await File.ReadAllTextAsync(path);
        }

        /// <summary>
        /// Write all text to a file
        /// </summary>
        public async Task WriteAllTextAsync(string path, string content)
        {
            await File.WriteAllTextAsync(path, content);
        }

        /// <summary>
        /// Get files in a directory matching a pattern
        /// </summary>
        public string[] GetFiles(string directory, string pattern)
        {
            return Directory.GetFiles(directory, pattern);
        }

        /// <summary>
        /// Get the absolute path for a given path
        /// </summary>
        public string GetFullPath(string path)
        {
            return Path.GetFullPath(path);
        }

        /// <summary>
        /// Get the directory name from a path
        /// </summary>
        public string? GetDirectoryName(string path)
        {
            return Path.GetDirectoryName(path);
        }

        /// <summary>
        /// Get the file name without extension
        /// </summary>
        public string GetFileNameWithoutExtension(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// Combine path segments
        /// </summary>
        public string Combine(params string[] paths)
        {
            return Path.Combine(paths);
        }

        /// <summary>
        /// Create a directory if it doesn't exist
        /// </summary>
        public void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }
}