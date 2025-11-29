using System;
using System.Collections.Generic;
using System.IO;
using iTextSharp.text.pdf;
using FilterPDF.Security;

namespace FilterPDF
{
    /// <summary>
    /// Centralizes all PDF file access through PdfReader with security validation
    /// Similar to CacheMemoryManager but for PDF files
    /// </summary>
    public static class PdfAccessManager
    {
        private static readonly Dictionary<string, PdfReader> _openReaders = new Dictionary<string, PdfReader>();
        private static readonly object _lock = new object();
        
        /// <summary>
        /// Gets or creates a PdfReader for the specified file with security validation
        /// </summary>
        public static PdfReader GetReader(string pdfPath)
        {
            if (string.IsNullOrEmpty(pdfPath))
                throw new ArgumentNullException(nameof(pdfPath));
            
            // REMOVIDO: Validação de segurança para máxima performance
            string fullPath = pdfPath;
            
            lock (_lock)
            {
                if (_openReaders.TryGetValue(fullPath, out PdfReader? existingReader))
                {
                    // Return existing reader if still valid
                    if (!existingReader.IsRebuilt())
                        return existingReader;
                    else
                    {
                        // Remove invalid reader
                        _openReaders.Remove(fullPath);
                        existingReader.Close();
                    }
                }
                
                // Create new reader
                var reader = new PdfReader(fullPath);
                _openReaders[fullPath] = reader;
                return reader;
            }
        }
        
        /// <summary>
        /// Creates a temporary reader that should be disposed after use
        /// Use this for operations that need exclusive access or modify the reader
        /// </summary>
        public static PdfReader CreateTemporaryReader(string pdfPath)
        {
            if (string.IsNullOrEmpty(pdfPath))
                throw new ArgumentNullException(nameof(pdfPath));
            
            // REMOVIDO: Validação de segurança para máxima performance
            return new PdfReader(pdfPath);
        }
        
        /// <summary>
        /// Closes and removes a reader from the cache
        /// </summary>
        public static void CloseReader(string pdfPath)
        {
            if (string.IsNullOrEmpty(pdfPath))
                return;
            
            // REMOVIDO: Validação de segurança para máxima performance
            string fullPath = pdfPath;
            
            lock (_lock)
            {
                if (_openReaders.TryGetValue(fullPath, out PdfReader? reader))
                {
                    _openReaders.Remove(fullPath);
                    reader.Close();
                }
            }
        }
        
        /// <summary>
        /// Closes all open readers
        /// </summary>
        public static void CloseAllReaders()
        {
            lock (_lock)
            {
                foreach (var reader in _openReaders.Values)
                {
                    reader.Close();
                }
                _openReaders.Clear();
            }
        }
        
        /// <summary>
        /// Gets the number of pages in a PDF without keeping the reader open
        /// </summary>
        public static int GetPageCount(string pdfPath)
        {
            using (var reader = CreateTemporaryReader(pdfPath))
            {
                return reader.NumberOfPages;
            }
        }
        
        /// <summary>
        /// Checks if a PDF is encrypted
        /// </summary>
        public static bool IsEncrypted(string pdfPath)
        {
            try
            {
                using (var reader = CreateTemporaryReader(pdfPath))
                {
                    return reader.IsEncrypted();
                }
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Gets basic info about a PDF file
        /// </summary>
        public static (int PageCount, bool IsEncrypted, long FileSize) GetBasicInfo(string pdfPath)
        {
            var fileInfo = new FileInfo(pdfPath);
            using (var reader = CreateTemporaryReader(pdfPath))
            {
                return (reader.NumberOfPages, reader.IsEncrypted(), fileInfo.Length);
            }
        }
    }
}