using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

#pragma warning disable CA1416 // System.Drawing on non-Windows; acceptable for legacy validation path

namespace FilterPDF.Services
{
    /// <summary>
    /// Validation service for image extraction operations
    /// </summary>
    public class ImageValidationService
    {
        /// <summary>
        /// Validate extraction options before processing
        /// </summary>
        public ValidationResult ValidateExtractionOptions(ImageExtractionOptions options)
        {
            var result = new ValidationResult();
            
            try
            {
                // Validate output directory
                if (string.IsNullOrWhiteSpace(options.OutputDirectory))
                {
                    result.AddError("Output directory cannot be empty");
                }
                else
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(options.OutputDirectory);
                        var parentDir = Path.GetDirectoryName(fullPath);
                        
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            result.AddError($"Parent directory does not exist: {parentDir}");
                        }
                        
                        // Test write permissions
                        if (Directory.Exists(options.OutputDirectory))
                        {
                            var testFile = Path.Combine(options.OutputDirectory, $"test_write_{Guid.NewGuid()}.tmp");
                            try
                            {
                                File.WriteAllText(testFile, "test");
                                File.Delete(testFile);
                            }
                            catch (Exception ex)
                            {
                                result.AddError($"No write permission to output directory: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddError($"Invalid output directory path: {ex.Message}");
                    }
                }
                
                // Validate output format
                var supportedFormats = new[] { "png", "jpg", "jpeg", "bmp", "tiff" };
                if (!supportedFormats.Contains(options.OutputFormat.ToLower()))
                {
                    result.AddError($"Unsupported output format: {options.OutputFormat}. " +
                        $"Supported: {string.Join(", ", supportedFormats)}");
                }
                
                // Validate filename pattern
                if (string.IsNullOrWhiteSpace(options.FileNamePattern))
                {
                    result.AddError("Filename pattern cannot be empty");
                }
                else
                {
                    if (!options.FileNamePattern.Contains("{ext}"))
                    {
                        result.AddWarning("Filename pattern should include {ext} placeholder for file extension");
                    }
                    
                    // Check for invalid filename characters
                    var invalidChars = Path.GetInvalidFileNameChars();
                    var patternWithoutPlaceholders = options.FileNamePattern
                        .Replace("{page}", "1")
                        .Replace("{index}", "1")
                        .Replace("{width}", "100")
                        .Replace("{height}", "100")
                        .Replace("{ext}", "png");
                    
                    if (patternWithoutPlaceholders.IndexOfAny(invalidChars) >= 0)
                    {
                        result.AddError("Filename pattern contains invalid characters");
                    }
                }
                
                // Validate dimension filters
                if (options.MinWidth.HasValue && options.MinWidth.Value <= 0)
                {
                    result.AddError("Minimum width must be greater than 0");
                }
                
                if (options.MaxWidth.HasValue && options.MaxWidth.Value <= 0)
                {
                    result.AddError("Maximum width must be greater than 0");
                }
                
                if (options.MinHeight.HasValue && options.MinHeight.Value <= 0)
                {
                    result.AddError("Minimum height must be greater than 0");
                }
                
                if (options.MaxHeight.HasValue && options.MaxHeight.Value <= 0)
                {
                    result.AddError("Maximum height must be greater than 0");
                }
                
                if (options.MinWidth.HasValue && options.MaxWidth.HasValue && 
                    options.MinWidth.Value > options.MaxWidth.Value)
                {
                    result.AddError("Minimum width cannot be greater than maximum width");
                }
                
                if (options.MinHeight.HasValue && options.MaxHeight.HasValue && 
                    options.MinHeight.Value > options.MaxHeight.Value)
                {
                    result.AddError("Minimum height cannot be greater than maximum height");
                }
                
                // Validate quality level
                if (options.QualityLevel < 1 || options.QualityLevel > 100)
                {
                    result.AddError("Quality level must be between 1 and 100");
                }
                
                // Performance warnings
                if (options.MinWidth.HasValue && options.MinWidth.Value > 5000)
                {
                    result.AddWarning("Very large minimum width may impact performance");
                }
                
                if (options.MinHeight.HasValue && options.MinHeight.Value > 5000)
                {
                    result.AddWarning("Very large minimum height may impact performance");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Validation error: {ex.Message}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Validate extracted image file
        /// </summary>
        public ValidationResult ValidateExtractedImage(string imagePath, ImageInfo originalInfo)
        {
            var result = new ValidationResult();
            
            try
            {
                // Check file exists
                if (!File.Exists(imagePath))
                {
                    result.AddError("Extracted image file does not exist");
                    return result;
                }
                
                // Check file size
                var fileInfo = new FileInfo(imagePath);
                if (fileInfo.Length == 0)
                {
                    result.AddError("Extracted image file is empty");
                    return result;
                }
                
                if (fileInfo.Length < 100) // Very small file, likely corrupted
                {
                    result.AddWarning("Extracted image file is very small, may be corrupted");
                }
                
                // Validate image format and dimensions
                try
                {
                    using (var image = Image.FromFile(imagePath))
                    {
                        // Check dimensions match original
                        if (image.Width != originalInfo.Width)
                        {
                            result.AddWarning($"Width mismatch: expected {originalInfo.Width}, got {image.Width}");
                        }
                        
                        if (image.Height != originalInfo.Height)
                        {
                            result.AddWarning($"Height mismatch: expected {originalInfo.Height}, got {image.Height}");
                        }
                        
                        // Check if image is valid
                        if (image.Width <= 0 || image.Height <= 0)
                        {
                            result.AddError("Invalid image dimensions");
                        }
                        
                        // Performance check for very large images
                        var totalPixels = (long)image.Width * image.Height;
                        if (totalPixels > 100_000_000) // 100 megapixels
                        {
                            result.AddWarning("Very large image extracted, may impact system performance");
                        }
                    }
                }
                catch (OutOfMemoryException)
                {
                    result.AddError("Image file is corrupted or not a valid image format");
                }
                catch (ArgumentException)
                {
                    result.AddError("Image file is not a valid image format");
                }
                catch (Exception ex)
                {
                    result.AddError($"Error validating image: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Validation error: {ex.Message}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Validate PDF analysis result for image extraction
        /// </summary>
        public ValidationResult ValidatePdfAnalysisResult(PDFAnalysisResult analysisResult)
        {
            var result = new ValidationResult();
            
            try
            {
                if (analysisResult == null)
                {
                    result.AddError("PDF analysis result is null");
                    return result;
                }
                
                if (analysisResult.Pages == null || analysisResult.Pages.Count == 0)
                {
                    result.AddError("No pages found in PDF analysis result");
                    return result;
                }
                
                int totalImages = 0;
                int pagesWithImages = 0;
                
                foreach (var page in analysisResult.Pages)
                {
                    if (page.Resources?.Images != null && page.Resources.Images.Count > 0)
                    {
                        pagesWithImages++;
                        totalImages += page.Resources.Images.Count;
                        
                        // Validate image info completeness
                        foreach (var img in page.Resources.Images)
                        {
                            if (img.Width <= 0 || img.Height <= 0)
                            {
                                result.AddWarning($"Page {page.PageNumber}: Image has invalid dimensions ({img.Width}x{img.Height})");
                            }
                            
                            if (string.IsNullOrEmpty(img.CompressionType))
                            {
                                result.AddWarning($"Page {page.PageNumber}: Image missing compression type information");
                            }
                            
                            if (string.IsNullOrEmpty(img.ColorSpace))
                            {
                                result.AddWarning($"Page {page.PageNumber}: Image missing color space information");
                            }
                        }
                    }
                }
                
                if (totalImages == 0)
                {
                    result.AddWarning("No images found in any page of the PDF");
                }
                else
                {
                    result.AddInfo($"Found {totalImages} images across {pagesWithImages} pages");
                }
                
                // Check for original PDF file accessibility
                if (!string.IsNullOrEmpty(analysisResult.FilePath))
                {
                    if (!File.Exists(analysisResult.FilePath))
                    {
                        result.AddWarning("Original PDF file not accessible - extraction may use cached data only");
                    }
                }
                else
                {
                    result.AddWarning("Original PDF file path not available - extraction limited to cached data");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Validation error: {ex.Message}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Validate system requirements for image extraction
        /// </summary>
        public ValidationResult ValidateSystemRequirements()
        {
            var result = new ValidationResult();
            
            try
            {
                // Check available disk space
                var drives = DriveInfo.GetDrives();
                bool hasLowDiskSpace = false;
                
                foreach (var drive in drives.Where(d => d.IsReady))
                {
                    var freeSpaceMB = drive.AvailableFreeSpace / (1024 * 1024);
                    if (freeSpaceMB < 100) // Less than 100MB
                    {
                        result.AddWarning($"Low disk space on {drive.Name}: {freeSpaceMB}MB available");
                        hasLowDiskSpace = true;
                    }
                }
                
                if (!hasLowDiskSpace)
                {
                    result.AddInfo("Sufficient disk space available");
                }
                
                // Check System.Drawing availability
                try
                {
                    using (var testBitmap = new Bitmap(1, 1))
                    {
                        // System.Drawing is available
                    }
                    result.AddInfo("System.Drawing library available for image processing");
                }
                catch (Exception ex)
                {
                    result.AddError($"System.Drawing not available: {ex.Message}");
                }
                
                // Check memory availability (basic check)
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    var workingSet = Environment.WorkingSet / (1024 * 1024);
                    if (workingSet > 1000) // More than 1GB
                    {
                        result.AddWarning($"High memory usage: {workingSet}MB");
                    }
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Could not check memory usage: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"System validation error: {ex.Message}");
            }
            
            return result;
        }
    }
    
    /// <summary>
    /// Validation result container
    /// </summary>
    public class ValidationResult
    {
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> InfoMessages { get; set; } = new List<string>();
        
        public bool IsValid => Errors.Count == 0;
        public bool HasWarnings => Warnings.Count > 0;
        public bool HasMessages => InfoMessages.Count > 0;
        
        public void AddError(string message)
        {
            Errors.Add(message);
        }
        
        public void AddWarning(string message)
        {
            Warnings.Add(message);
        }
        
        public void AddInfo(string message)
        {
            InfoMessages.Add(message);
        }
        
        public void PrintToConsole()
        {
            foreach (var error in Errors)
            {
                Console.WriteLine($"❌ Error: {error}");
            }
            
            foreach (var warning in Warnings)
            {
                Console.WriteLine($"⚠️  Warning: {warning}");
            }
            
            foreach (var info in InfoMessages)
            {
                Console.WriteLine($"ℹ️  Info: {info}");
            }
        }
        
        public string GetSummary()
        {
            if (IsValid && !HasWarnings)
            {
                return "✅ All validations passed";
            }
            else if (IsValid && HasWarnings)
            {
                return $"⚠️  Valid with {Warnings.Count} warning(s)";
            }
            else
            {
                return $"❌ Invalid: {Errors.Count} error(s), {Warnings.Count} warning(s)";
            }
        }
    }
}
#pragma warning restore CA1416
// Suppress platform warnings (System.Drawing on non-Windows) for this legacy validator.
#pragma warning disable CA1416
