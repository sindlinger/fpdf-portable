using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using iTextSharp.text.pdf;
using FilterPDF.Utils;

namespace FilterPDF.Services
{
    /// <summary>
    /// Comprehensive image extraction service for PDF images with format conversion pipeline
    /// </summary>
    public class ImageExtractionService
    {
        private readonly IProgressReporter _progressReporter;
        private readonly ImageValidationService _validationService;
        
        public ImageExtractionService(IProgressReporter progressReporter = null)
        {
            _progressReporter = progressReporter ?? new ConsoleProgressReporter();
            _validationService = new ImageValidationService();
        }
        
        /// <summary>
        /// Extract images from cached PDF data and convert to PNG format
        /// </summary>
        public ImageExtractionResult ExtractImagesFromCache(PDFAnalysisResult analysisResult, 
            ImageExtractionOptions options)
        {
            var result = new ImageExtractionResult
            {
                ExtractedFiles = new List<ExtractedImageInfo>(),
                Errors = new List<string>(),
                StartTime = DateTime.Now
            };
            
            try
            {
                // Pre-validation
                var optionsValidation = _validationService.ValidateExtractionOptions(options);
                if (!optionsValidation.IsValid)
                {
                    result.Errors.AddRange(optionsValidation.Errors);
                    return result;
                }
                
                var analysisValidation = _validationService.ValidatePdfAnalysisResult(analysisResult);
                if (!analysisValidation.IsValid)
                {
                    result.Errors.AddRange(analysisValidation.Errors);
                    return result;
                }
                
                // Log warnings but continue
                if (optionsValidation.HasWarnings || analysisValidation.HasWarnings)
                {
                    Console.WriteLine("⚠️  Pre-extraction warnings:");
                    optionsValidation.PrintToConsole();
                    analysisValidation.PrintToConsole();
                }
                
                // System requirements check
                var systemValidation = _validationService.ValidateSystemRequirements();
                if (systemValidation.HasWarnings)
                {
                    Console.WriteLine("⚠️  System warnings:");
                    systemValidation.PrintToConsole();
                }
                
                // Create output directory
                if (!Directory.Exists(options.OutputDirectory))
                {
                    Directory.CreateDirectory(options.OutputDirectory);
                }
                
                // Process pages
                var totalImages = CountMatchingImages(analysisResult, options);
                int processedImages = 0;
                
                _progressReporter.Start(totalImages, "Extracting images");
                
                for (int pageNum = 0; pageNum < analysisResult.Pages.Count; pageNum++)
                {
                    var page = analysisResult.Pages[pageNum];
                    
                    if (page.Resources?.Images == null || page.Resources.Images.Count == 0)
                        continue;
                    
                    int imageIndex = 0;
                    foreach (var img in page.Resources.Images)
                    {
                        imageIndex++;
                        processedImages++;
                        
                        _progressReporter.Update(processedImages, 
                            $"Page {pageNum + 1}, Image {imageIndex}");
                        
                        if (!MatchesFilters(img, options))
                            continue;
                        
                        try
                        {
                            var extractedImage = ExtractSingleImage(img, pageNum + 1, 
                                imageIndex, options, analysisResult);
                            
                            if (extractedImage != null)
                            {
                                result.ExtractedFiles.Add(extractedImage);
                                result.SuccessCount++;
                            }
                            else
                            {
                                result.FailureCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Page {pageNum + 1}, Image {imageIndex}: {ex.Message}");
                            result.FailureCount++;
                        }
                    }
                }
                
                _progressReporter.Complete($"Extracted {result.SuccessCount} images");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Global extraction error: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            
            return result;
        }
        
        /// <summary>
        /// Extract images directly from PDF file with original quality
        /// </summary>
        public ImageExtractionResult ExtractImagesFromPdf(string pdfPath, 
            ImageExtractionOptions options)
        {
            var result = new ImageExtractionResult
            {
                ExtractedFiles = new List<ExtractedImageInfo>(),
                Errors = new List<string>(),
                StartTime = DateTime.Now
            };
            
            try
            {
                if (!File.Exists(pdfPath))
                {
                    result.Errors.Add($"PDF file not found: {pdfPath}");
                    return result;
                }
                
                using (var reader = new PdfReader(pdfPath))
                {
                    var totalImages = CountPdfImages(reader, options);
                    int processedImages = 0;
                    
                    _progressReporter.Start(totalImages, "Extracting from PDF");
                    
                    for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                    {
                        var images = ExtractPageImages(reader, pageNum, options);
                        
                        foreach (var img in images)
                        {
                            processedImages++;
                            _progressReporter.Update(processedImages, 
                                $"Page {pageNum}, Image {img.ImageIndex}");
                            
                            result.ExtractedFiles.Add(img);
                            result.SuccessCount++;
                        }
                    }
                    
                    _progressReporter.Complete($"Extracted {result.SuccessCount} images");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"PDF extraction error: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            
            return result;
        }
        
        private ExtractedImageInfo ExtractSingleImage(ImageInfo imageInfo, int pageNumber,
            int imageIndex, ImageExtractionOptions options, PDFAnalysisResult analysisResult)
        {
            // Generate output filename
            string fileName = options.FileNamePattern
                .Replace("{page}", pageNumber.ToString())
                .Replace("{index}", imageIndex.ToString())
                .Replace("{width}", imageInfo.Width.ToString())
                .Replace("{height}", imageInfo.Height.ToString())
                .Replace("{ext}", options.OutputFormat.ToLower());
            
            string outputPath = Path.Combine(options.OutputDirectory, fileName);
            
            // Try different extraction methods
            bool success = false;
            
            // Method 1: Use cached image data if available (PRIORITY)
            if (!string.IsNullOrEmpty(imageInfo.Base64Data))
            {
                success = TryExtractFromCachedData(imageInfo, outputPath, options);
            }
            
            // Method 2: Try to find and extract from original PDF
            if (!success && !string.IsNullOrEmpty(analysisResult.FilePath))
            {
                success = TryExtractFromOriginalPdf(analysisResult.FilePath, pageNumber,
                    imageInfo, outputPath, options);
            }
            
            // Method 3: Generate placeholder/error image
            if (!success && options.CreatePlaceholderOnFailure)
            {
                success = CreatePlaceholderImage(imageInfo, outputPath, options);
            }
            
            if (success)
            {
                return new ExtractedImageInfo
                {
                    OriginalImageInfo = imageInfo,
                    PageNumber = pageNumber,
                    ImageIndex = imageIndex,
                    OutputPath = outputPath,
                    OutputFormat = options.OutputFormat,
                    FileSize = new FileInfo(outputPath).Length,
                    ExtractionMethod = GetLastUsedMethod()
                };
            }
            
            return null;
        }
        
        private bool TryExtractFromOriginalPdf(string pdfPath, int pageNumber,
            ImageInfo targetImage, string outputPath, ImageExtractionOptions options)
        {
            try
            {
                if (!File.Exists(pdfPath))
                    return false;
                
                using (var reader = new PdfReader(pdfPath))
                {
                    var pageDict = reader.GetPageN(pageNumber);
                    var resources = pageDict?.GetAsDict(PdfName.RESOURCES);
                    var xObjects = resources?.GetAsDict(PdfName.XOBJECT);
                    
                    if (xObjects != null)
                    {
                        foreach (var key in xObjects.Keys)
                        {
                            var obj = xObjects.GetAsIndirectObject(key);
                            if (obj != null)
                            {
                                var xObj = PdfReader.GetPdfObject(obj);
                                if (xObj is PRStream stream)
                                {
                                    var subType = stream.GetAsName(PdfName.SUBTYPE);
                                    if (PdfName.IMAGE.Equals(subType))
                                    {
                                        // Check if this matches our target image
                                        var width = stream.GetAsNumber(PdfName.WIDTH)?.IntValue ?? 0;
                                        var height = stream.GetAsNumber(PdfName.HEIGHT)?.IntValue ?? 0;
                                        
                                        if (width == targetImage.Width && height == targetImage.Height)
                                        {
                                            return ExtractStreamToFile(stream, outputPath, options);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not extract from original PDF: {ex.Message}");
            }
            
            return false;
        }
        
        private bool TryExtractFromCachedData(ImageInfo imageInfo, string outputPath, 
            ImageExtractionOptions options)
        {
            try
            {
                // Check if we have base64 data available
                if (string.IsNullOrEmpty(imageInfo.Base64Data))
                {
                    Console.WriteLine("      ❌ No Base64Data in cache");
                    return false;
                }

                Console.WriteLine($"      🔍 Processing Base64Data ({imageInfo.Base64Data.Length} chars)");

                // Decode base64 to byte array
                byte[] imageBytes;
                try
                {
                    imageBytes = Convert.FromBase64String(imageInfo.Base64Data);
                    Console.WriteLine($"      ✅ Base64 decoded: {imageBytes.Length} bytes");
                }
                catch (FormatException ex)
                {
                    Console.WriteLine($"      ❌ Base64 decode failed: {ex.Message}");
                    return false;
                }
                
                // For PNG output, convert to Base64 and then to PNG
                if (options.OutputFormat.ToLower() == "png")
                {
                    string base64 = Convert.ToBase64String(imageBytes);
                    return ImageDataExtractor.CreatePngFromBase64(base64, outputPath);
                }
                else
                {
                    // Save raw data for other formats
                    File.WriteAllBytes(outputPath, imageBytes);
                    _lastUsedMethod = "Cached Raw Data";
                    Console.WriteLine($"      ✅ Raw data saved: {outputPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      ❌ Cache extraction error: {ex.Message}");
                return false;
            }
        }
        
        private bool CreatePlaceholderImage(ImageInfo imageInfo, string outputPath, 
            ImageExtractionOptions options)
        {
            try
            {
                using (var bitmap = new Bitmap(imageInfo.Width, imageInfo.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // Create a placeholder image
                    graphics.Clear(Color.LightGray);
                    
                    var font = new Font("Arial", 12);
                    var brush = new SolidBrush(Color.Black);
                    var text = $"Image Placeholder\n{imageInfo.Width}x{imageInfo.Height}\n{imageInfo.CompressionType}";
                    
                    var textSize = graphics.MeasureString(text, font);
                    var x = (imageInfo.Width - textSize.Width) / 2;
                    var y = (imageInfo.Height - textSize.Height) / 2;
                    
                    graphics.DrawString(text, font, brush, x, y);
                    
                    // Save as PNG
                    bitmap.Save(outputPath, ImageFormat.Png);
                    
                    font.Dispose();
                    brush.Dispose();
                }
                
                _lastUsedMethod = "Placeholder";
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private bool ExtractStreamToFile(PRStream stream, string outputPath, 
            ImageExtractionOptions options)
        {
            try
            {
                var filter = stream.Get(PdfName.FILTER);
                
                // Handle JPEG images
                if (IsJpegFilter(filter))
                {
                    var bytes = PdfReader.GetStreamBytesRaw(stream);
                    
                    if (options.OutputFormat.ToLower() == "png")
                    {
                        // Convert JPEG to PNG
                        return ConvertJpegToPng(bytes, outputPath);
                    }
                    else
                    {
                        // Save as JPEG
                        File.WriteAllBytes(outputPath, bytes);
                        _lastUsedMethod = "Direct JPEG";
                        return true;
                    }
                }
                else
                {
                    // Handle other formats
                    var decodedBytes = PdfReader.GetStreamBytes(stream);
                    return ConvertRawToPng(decodedBytes, outputPath, stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Stream extraction error: {ex.Message}");
                return false;
            }
        }
        
        private bool ConvertJpegToPng(byte[] jpegBytes, string outputPath)
        {
            try
            {
                using (var memoryStream = new MemoryStream(jpegBytes))
                using (var image = Image.FromStream(memoryStream))
                {
                    image.Save(outputPath, ImageFormat.Png);
                    _lastUsedMethod = "JPEG to PNG";
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        
        private bool ConvertRawToPng(byte[] rawBytes, string outputPath, PRStream stream)
        {
            try
            {
                var width = stream.GetAsNumber(PdfName.WIDTH)?.IntValue ?? 0;
                var height = stream.GetAsNumber(PdfName.HEIGHT)?.IntValue ?? 0;
                var bitsPerComponent = stream.GetAsNumber(PdfName.BITSPERCOMPONENT)?.IntValue ?? 8;
                
                // Simple RGB conversion (may need enhancement for different color spaces)
                if (bitsPerComponent == 8 && rawBytes.Length >= width * height * 3)
                {
                    using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                    {
                        var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                            ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                        
                        System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, data.Scan0,
                            Math.Min(rawBytes.Length, data.Stride * height));
                        
                        bitmap.UnlockBits(data);
                        bitmap.Save(outputPath, ImageFormat.Png);
                        
                        _lastUsedMethod = "Raw to PNG";
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through to alternative methods
            }
            
            return false;
        }
        
        private bool IsJpegFilter(iTextSharp.text.pdf.PdfObject filter)
        {
            if (filter == null) return false;
            
            var filterStr = filter.ToString();
            return filterStr.Contains("DCTDecode") || filterStr.Contains("JPXDecode");
        }
        
        private bool MatchesFilters(ImageInfo image, ImageExtractionOptions options)
        {
            if (options.MinWidth.HasValue && image.Width < options.MinWidth.Value)
                return false;
                
            if (options.MaxWidth.HasValue && image.Width > options.MaxWidth.Value)
                return false;
                
            if (options.MinHeight.HasValue && image.Height < options.MinHeight.Value)
                return false;
                
            if (options.MaxHeight.HasValue && image.Height > options.MaxHeight.Value)
                return false;
                
            return true;
        }
        
        private int CountMatchingImages(PDFAnalysisResult analysisResult, ImageExtractionOptions options)
        {
            int count = 0;
            
            foreach (var page in analysisResult.Pages)
            {
                if (page.Resources?.Images != null)
                {
                    count += page.Resources.Images.Count(img => MatchesFilters(img, options));
                }
            }
            
            return count;
        }
        
        private int CountPdfImages(PdfReader reader, ImageExtractionOptions options)
        {
            // Simplified count - would need to implement full filtering
            int count = 0;
            
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                var images = DetailedImageExtractor.ExtractCompleteImageDetails(reader, pageNum);
                count += images.Count(img => MatchesFilters(img, options));
            }
            
            return count;
        }
        
        private List<ExtractedImageInfo> ExtractPageImages(PdfReader reader, int pageNum,
            ImageExtractionOptions options)
        {
            var results = new List<ExtractedImageInfo>();
            var images = DetailedImageExtractor.ExtractCompleteImageDetails(reader, pageNum);
            
            int imageIndex = 0;
            foreach (var img in images)
            {
                imageIndex++;
                
                if (!MatchesFilters(img, options))
                    continue;
                
                try
                {
                    var extracted = ExtractSingleImage(img, pageNum, imageIndex, options, null);
                    if (extracted != null)
                    {
                        results.Add(extracted);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error extracting image {imageIndex} from page {pageNum}: {ex.Message}");
                }
            }
            
            return results;
        }
        
        private string _lastUsedMethod = "";
        private string GetLastUsedMethod() => _lastUsedMethod;
        
        /// <summary>
        /// Detect image format from byte array signature
        /// </summary>
        private string DetectImageFormat(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length < 8)
                return "Unknown";
            
            // JPEG signature: FF D8 FF
            if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF)
                return "JPEG";
            
            // PNG signature: 89 50 4E 47 0D 0A 1A 0A
            if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && 
                imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                return "PNG";
            
            // GIF signature: GIF87a or GIF89a
            if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46)
                return "GIF";
            
            // BMP signature: BM
            if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
                return "BMP";
            
            // TIFF signatures: II* or MM*
            if ((imageBytes[0] == 0x49 && imageBytes[1] == 0x49 && imageBytes[2] == 0x2A) ||
                (imageBytes[0] == 0x4D && imageBytes[1] == 0x4D && imageBytes[2] == 0x2A))
                return "TIFF";
            
            return "Unknown";
        }
        
        /// <summary>
        /// Convert image bytes to PNG format using System.Drawing
        /// </summary>
        private bool ConvertToPngFromBytes(byte[] imageBytes, string outputPath, string sourceFormat)
        {
            try
            {
                // If already PNG, save directly
                if (sourceFormat == "PNG")
                {
                    File.WriteAllBytes(outputPath, imageBytes);
                    _lastUsedMethod = "Cached PNG";
                    return true;
                }
                
                // Convert using System.Drawing
                using (var memoryStream = new MemoryStream(imageBytes))
                {
                    try
                    {
                        using (var image = Image.FromStream(memoryStream))
                        {
                            image.Save(outputPath, ImageFormat.Png);
                            _lastUsedMethod = $"Cached {sourceFormat} to PNG";
                            return true;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Try alternative approach for unsupported formats
                        return ConvertRawDataToPng(imageBytes, outputPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to convert {sourceFormat} to PNG: {ex.Message}");
                return ConvertRawDataToPng(imageBytes, outputPath);
            }
        }
        
        /// <summary>
        /// Fallback method to convert raw image data to PNG
        /// </summary>
        private bool ConvertRawDataToPng(byte[] rawBytes, string outputPath)
        {
            try
            {
                // This is a simplified approach - in practice, we'd need to know
                // the exact format and color space of the raw data
                // For now, we'll try to create a basic bitmap if we can determine dimensions
                
                // Calculate potential dimensions based on data size
                int dataSize = rawBytes.Length;
                int[] possibleWidths = { 100, 200, 300, 400, 500, 600, 800, 1000, 1200, 1600, 2000 };
                
                foreach (int width in possibleWidths)
                {
                    // Try RGB (3 bytes per pixel)
                    int height = dataSize / (width * 3);
                    if (height > 0 && width * height * 3 == dataSize)
                    {
                        return CreateBitmapFromRawRgb(rawBytes, width, height, outputPath);
                    }
                    
                    // Try RGBA (4 bytes per pixel)
                    height = dataSize / (width * 4);
                    if (height > 0 && width * height * 4 == dataSize)
                    {
                        return CreateBitmapFromRawRgba(rawBytes, width, height, outputPath);
                    }
                    
                    // Try grayscale (1 byte per pixel)
                    height = dataSize / width;
                    if (height > 0 && width * height == dataSize)
                    {
                        return CreateBitmapFromRawGrayscale(rawBytes, width, height, outputPath);
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Create PNG from raw RGB data
        /// </summary>
        private bool CreateBitmapFromRawRgb(byte[] rgbData, int width, int height, string outputPath)
        {
            try
            {
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                {
                    var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                        ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                    
                    System.Runtime.InteropServices.Marshal.Copy(rgbData, 0, data.Scan0, rgbData.Length);
                    bitmap.UnlockBits(data);
                    bitmap.Save(outputPath, ImageFormat.Png);
                    
                    _lastUsedMethod = "Raw RGB to PNG";
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Create PNG from raw RGBA data
        /// </summary>
        private bool CreateBitmapFromRawRgba(byte[] rgbaData, int width, int height, string outputPath)
        {
            try
            {
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                        ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    
                    System.Runtime.InteropServices.Marshal.Copy(rgbaData, 0, data.Scan0, rgbaData.Length);
                    bitmap.UnlockBits(data);
                    bitmap.Save(outputPath, ImageFormat.Png);
                    
                    _lastUsedMethod = "Raw RGBA to PNG";
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Create PNG from raw grayscale data
        /// </summary>
        private bool CreateBitmapFromRawGrayscale(byte[] grayData, int width, int height, string outputPath)
        {
            try
            {
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int index = y * width + x;
                            if (index < grayData.Length)
                            {
                                byte gray = grayData[index];
                                bitmap.SetPixel(x, y, Color.FromArgb(gray, gray, gray));
                            }
                        }
                    }
                    
                    bitmap.Save(outputPath, ImageFormat.Png);
                    _lastUsedMethod = "Raw Grayscale to PNG";
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        
        private void WriteChunk(BinaryWriter writer, string chunkType, byte[] data)
        {
            // Length (big-endian)
            writer.Write(BitConverter.GetBytes((uint)data.Length).Reverse().ToArray());
            
            // Type
            writer.Write(System.Text.Encoding.ASCII.GetBytes(chunkType));
            
            // Data
            writer.Write(data);
            
            // CRC (simplified - just use type + data)
            var crcData = System.Text.Encoding.ASCII.GetBytes(chunkType).Concat(data).ToArray();
            writer.Write(BitConverter.GetBytes(CalculateCRC32(crcData)).Reverse().ToArray());
        }
        
        private byte[] CreateIHDR(int width, int height)
        {
            var ihdr = new List<byte>();
            ihdr.AddRange(BitConverter.GetBytes((uint)width).Reverse());
            ihdr.AddRange(BitConverter.GetBytes((uint)height).Reverse());
            ihdr.Add(8); // Bit depth
            ihdr.Add(2); // Color type (RGB)
            ihdr.Add(0); // Compression method
            ihdr.Add(0); // Filter method
            ihdr.Add(0); // Interlace method
            return ihdr.ToArray();
        }
        
        private byte[] CreateIDATFromRaw(byte[] rawData, int width, int height)
        {
            // This is a very simplified approach
            // In a real implementation, you'd need to properly process the raw data
            // For now, just compress the raw data
            using (var output = new MemoryStream())
            using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
            {
                // Add filter byte (0 = None) for each scanline
                for (int y = 0; y < height; y++)
                {
                    deflate.WriteByte(0); // Filter type
                    int rowBytes = Math.Min(width * 3, rawData.Length - (y * width * 3));
                    if (rowBytes > 0)
                    {
                        deflate.Write(rawData, y * width * 3, rowBytes);
                    }
                }
                deflate.Flush();
                return output.ToArray();
            }
        }
        
        private uint CalculateCRC32(byte[] data)
        {
            // Simplified CRC32 calculation
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }
            return ~crc;
        }
    }
    
    /// <summary>
    /// Configuration options for image extraction
    /// </summary>
    public class ImageExtractionOptions
    {
        public string OutputDirectory { get; set; } = Directory.GetCurrentDirectory();
        public string OutputFormat { get; set; } = "png";
        public string FileNamePattern { get; set; } = "page_{page}_img_{index}_{width}x{height}.{ext}";
        
        // Filtering options
        public int? MinWidth { get; set; }
        public int? MaxWidth { get; set; }
        public int? MinHeight { get; set; }
        public int? MaxHeight { get; set; }
        
        // Behavior options
        public bool CreatePlaceholderOnFailure { get; set; } = false;
        public bool OverwriteExisting { get; set; } = true;
        public int QualityLevel { get; set; } = 90; // For JPEG output
    }
    
    /// <summary>
    /// Result of image extraction operation
    /// </summary>
    public class ImageExtractionResult
    {
        public List<ExtractedImageInfo> ExtractedFiles { get; set; } = new List<ExtractedImageInfo>();
        public List<string> Errors { get; set; } = new List<string>();
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        
        public bool IsSuccess => SuccessCount > 0 && Errors.Count == 0;
        public int TotalProcessed => SuccessCount + FailureCount;
    }
    
    /// <summary>
    /// Information about an extracted image file
    /// </summary>
    public class ExtractedImageInfo
    {
        public ImageInfo OriginalImageInfo { get; set; }
        public int PageNumber { get; set; }
        public int ImageIndex { get; set; }
        public string OutputPath { get; set; } = "";
        public string OutputFormat { get; set; } = "";
        public long FileSize { get; set; }
        public string ExtractionMethod { get; set; } = "";
        
        public string FileName => Path.GetFileName(OutputPath);
        public double FileSizeMB => FileSize / (1024.0 * 1024.0);
    }
    
    /// <summary>
    /// Progress reporting interface
    /// </summary>
    public interface IProgressReporter
    {
        void Start(int totalItems, string operation);
        void Update(int completedItems, string currentItem);
        void Complete(string summary);
    }
    
    /// <summary>
    /// Console-based progress reporter with enhanced batch reporting
    /// </summary>
    public class ConsoleProgressReporter : IProgressReporter
    {
        private int _totalItems;
        private DateTime _startTime;
        private DateTime _lastUpdate;
        private int _lastReportedItems;
        private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(500);
        
        public void Start(int totalItems, string operation)
        {
            _totalItems = totalItems;
            _startTime = DateTime.Now;
            _lastUpdate = _startTime;
            _lastReportedItems = 0;
            
            Console.WriteLine($"🚀 {operation}");
            Console.WriteLine($"📊 Total items to process: {totalItems:N0}");
            Console.WriteLine($"⏰ Started at: {_startTime:HH:mm:ss}");
            Console.WriteLine();
        }
        
        public void Update(int completedItems, string currentItem)
        {
            var now = DateTime.Now;
            
            // Throttle updates to avoid console spam
            if (now - _lastUpdate < _updateInterval && completedItems < _totalItems)
                return;
            
            var percentage = _totalItems > 0 ? (completedItems * 100) / _totalItems : 0;
            var elapsed = now - _startTime;
            
            // Calculate processing rate
            var itemsSinceLastUpdate = completedItems - _lastReportedItems;
            var timeSinceLastUpdate = now - _lastUpdate;
            var currentRate = timeSinceLastUpdate.TotalSeconds > 0 ? 
                itemsSinceLastUpdate / timeSinceLastUpdate.TotalSeconds : 0;
            
            // Calculate ETA
            var overallRate = completedItems > 0 ? elapsed.TotalSeconds / completedItems : 0;
            var eta = overallRate > 0 ? TimeSpan.FromSeconds(((_totalItems - completedItems) * overallRate)) : TimeSpan.Zero;
            
            // Progress bar
            var barWidth = 30;
            var filledWidth = (int)((double)completedItems / _totalItems * barWidth);
            var progressBar = "[" + new string('█', filledWidth) + new string('·', barWidth - filledWidth) + "]";
            
            Console.Write($"\r{progressBar} {percentage:F1}% ({completedItems:N0}/{_totalItems:N0}) | " +
                         $"Rate: {currentRate:F1}/s | ETA: {eta:mm\\:ss} | {TruncateString(currentItem, 25)}");
            
            _lastUpdate = now;
            _lastReportedItems = completedItems;
        }
        
        public void Complete(string summary)
        {
            var duration = DateTime.Now - _startTime;
            var averageRate = _totalItems > 0 ? _totalItems / duration.TotalSeconds : 0;
            
            Console.WriteLine(); // New line after progress bar
            Console.WriteLine($"✅ {summary}");
            Console.WriteLine($"⏱️  Total time: {duration:mm\\:ss}");
            Console.WriteLine($"📈 Average rate: {averageRate:F1} items/second");
            Console.WriteLine($"🏁 Completed at: {DateTime.Now:HH:mm:ss}");
        }
        
        private string TruncateString(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            
            return text.Substring(0, maxLength - 3) + "...";
        }
    }
    
    /// <summary>
    /// Silent progress reporter for automated/batch operations
    /// </summary>
    public class SilentProgressReporter : IProgressReporter
    {
        public void Start(int totalItems, string operation) { }
        public void Update(int completedItems, string currentItem) { }
        public void Complete(string summary) { }
    }
    
    /// <summary>
    /// File-based progress reporter for logging to file
    /// </summary>
    public class FileProgressReporter : IProgressReporter
    {
        private readonly string _logFile;
        private DateTime _startTime;
        
        public FileProgressReporter(string logFile)
        {
            _logFile = logFile;
        }
        
        public void Start(int totalItems, string operation)
        {
            _startTime = DateTime.Now;
            File.AppendAllText(_logFile, $"[{_startTime:yyyy-MM-dd HH:mm:ss}] Started: {operation} - {totalItems} items\n");
        }
        
        public void Update(int completedItems, string currentItem)
        {
            // Log only significant milestones to avoid large log files
            if (completedItems % 10 == 0 || completedItems == 1)
            {
                var elapsed = DateTime.Now - _startTime;
                File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Progress: {completedItems} items | {elapsed:mm\\:ss} elapsed\n");
            }
        }
        
        public void Complete(string summary)
        {
            var duration = DateTime.Now - _startTime;
            File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Completed: {summary} in {duration:mm\\:ss}\n\n");
        }
    }
}