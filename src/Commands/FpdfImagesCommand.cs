using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Utils;
using FilterPDF.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FilterPDF
{
    /// <summary>
    /// Fpdf Images Command - Lista imagens contidas no PDF
    /// </summary>
    public class FpdfImagesCommand
    {
        public static void ShowHelp()
        {
            Console.WriteLine("fpdf images - List images in PDF files");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("    fpdf <cache_index> images [options]");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("    --nota-empenho           Filter for nota de empenho documents only");
            Console.WriteLine("    --image-size <size>      Filter by image size (e.g. \"744x1052\" or \"744-750 x 1055\")");
            Console.WriteLine("    --min-width <width>      Minimum image width");
            Console.WriteLine("    --max-width <width>      Maximum image width");
            Console.WriteLine("    --min-height <height>    Minimum image height");
            Console.WriteLine("    --max-height <height>    Maximum image height");
            Console.WriteLine("    --output-dir <dir>       Output directory for extracted images (default: current dir)");
            Console.WriteLine("    -o, --output <file>      Save results to file");
            Console.WriteLine("    -F, --format <fmt>       Output format: txt, json, csv, count, png");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("    fpdf 1 images                                           # List all images");
            Console.WriteLine("    fpdf 1 images --nota-empenho -F png --output-dir ~/DE_cache  # Extract only notas de empenho");
            Console.WriteLine("    fpdf 1 images --image-size \"744x1052\"                  # Find specific size");
            Console.WriteLine("    fpdf 1-10 images --image-size \"744-750 x 1055\"         # Size range");
            Console.WriteLine("    fpdf 700 images -F png --output-dir ~/extracted         # Extract as PNG");
            Console.WriteLine("    fpdf 1 images --min-width 700 --max-width 800 -F json  # Filter and export");
        }

        public static void Execute(string cacheFile, PDFAnalysisResult analysisResult, 
            Dictionary<string, string> filterOptions, Dictionary<string, string> outputOptions)
        {
            Console.WriteLine($"🔧 DEBUG: ===== FpdfImagesCommand.Execute STARTED =====");
            Console.WriteLine($"🔧 DEBUG: FpdfImagesCommand.Execute called with {outputOptions.Count} output options");
            if (analysisResult?.Pages == null)
            {
                Console.WriteLine("No page data found in cache.");
                return;
            }

            // Parse filters
            var imageSizeFilter = ParseImageSizeFilter(filterOptions);
            
            // Get format
            string format = outputOptions.ContainsKey("format") ? outputOptions["format"] : 
                           outputOptions.ContainsKey("-F") ? outputOptions["-F"] : 
                           outputOptions.ContainsKey("--format") ? outputOptions["--format"] : "txt";
            bool extractAsPng = format.ToLower() == "png";
            
            Console.WriteLine($"🔧 DEBUG: format='{format}', extractAsPng={extractAsPng}");
            Console.WriteLine($"🔧 DEBUG: outputOptions keys: {string.Join(", ", outputOptions.Keys)}");
            
            // Get output directory for PNG extraction
            string outputDir = "";
            if (extractAsPng)
            {
                outputDir = filterOptions.ContainsKey("--output-dir") 
                    ? ExpandPath(filterOptions["--output-dir"]) 
                    : Directory.GetCurrentDirectory();
                    
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
            }
            
            // Get cache index
            string cacheIndex = Path.GetFileNameWithoutExtension(cacheFile);
            
            // Collect results
            var results = new List<PageImageInfo>();
            int totalImages = 0;
            int matchingImages = 0;
            var extractedFiles = new List<string>();

            // Process each page
            for (int pageNum = 0; pageNum < analysisResult.Pages.Count; pageNum++)
            {
                var page = analysisResult.Pages[pageNum];
                
                // Skip pages without images
                if (page.Resources?.Images == null || page.Resources.Images.Count == 0)
                    continue;
                
                var pageInfo = new PageImageInfo
                {
                    PageNumber = pageNum + 1,
                    Images = new List<ImageDetails>()
                };
                
                // Process each image in the page
                int imageIndex = 0;
                foreach (var img in page.Resources.Images)
                {
                    totalImages++;
                    imageIndex++;
                    
                    // Check filters
                    if (!MatchesImageSize(img.Width, img.Height, imageSizeFilter, filterOptions))
                        continue;
                    
                    matchingImages++;
                    
                    var imageDetails = new ImageDetails
                    {
                        Index = imageIndex,
                        Name = !string.IsNullOrEmpty(img.Name) ? img.Name : $"Image{imageIndex}",
                        Width = img.Width,
                        Height = img.Height,
                        ColorSpace = img.ColorSpace,
                        CompressionType = img.CompressionType,
                        BitsPerComponent = img.BitsPerComponent,
                        EstimatedSize = img.EstimatedSize,
                        Area = img.Width * img.Height
                    };
                    
                    pageInfo.Images.Add(imageDetails);
                    
                    // Extract as PNG if requested
                    if (extractAsPng)
                    {
                        try
                        {
                            var extractionService = new ImageExtractionService();
                            var options = new ImageExtractionOptions
                            {
                                OutputDirectory = outputDir,
                                OutputFormat = "png",
                                MinWidth = filterOptions.ContainsKey("--min-width") ? 
                                    int.Parse(filterOptions["--min-width"]) : null,
                                MinHeight = filterOptions.ContainsKey("--min-height") ? 
                                    int.Parse(filterOptions["--min-height"]) : null,
                                MaxWidth = filterOptions.ContainsKey("--max-width") ? 
                                    int.Parse(filterOptions["--max-width"]) : null,
                                MaxHeight = filterOptions.ContainsKey("--max-height") ? 
                                    int.Parse(filterOptions["--max-height"]) : null,
                                FileNamePattern = $"pdf_{cacheIndex}_p{pageNum + 1}_img{imageIndex}_{{width}}x{{height}}.{{ext}}",
                                CreatePlaceholderOnFailure = true
                            };
                            
                            var tempResult = new PDFAnalysisResult
                            {
                                FilePath = analysisResult.FilePath,
                                Pages = new List<PageAnalysis> { page }
                            };
                            
                            var extractionResult = extractionService.ExtractImagesFromCache(tempResult, options);
                            
                            foreach (var extracted in extractionResult.ExtractedFiles)
                            {
                                if (extracted.OriginalImageInfo.Width == img.Width && 
                                    extracted.OriginalImageInfo.Height == img.Height)
                                {
                                    extractedFiles.Add(extracted.OutputPath);
                                    imageDetails.ExtractedFile = extracted.OutputPath;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"      ⚠️  Extraction failed: {ex.Message}");
                        }
                    }
                }
                
                if (pageInfo.Images.Count > 0)
                {
                    results.Add(pageInfo);
                }
            }

            // Output results
            Console.WriteLine($"🔧 DEBUG: About to check format '{format}' (ToLower='{format.ToLower()}')");
            if (format.ToLower() == "count")
            {
                Console.WriteLine($"TOTAL_IMAGES: {totalImages}");
                Console.WriteLine($"MATCHING_IMAGES: {matchingImages}");
                Console.WriteLine($"PAGES_WITH_IMAGES: {results.Count}");
            }
            else if (format.ToLower() == "json")
            {
                var jsonResult = new
                {
                    source = cacheFile,
                    totalPages = analysisResult.Pages.Count,
                    totalImages = totalImages,
                    matchingImages = matchingImages,
                    pagesWithImages = results.Count,
                    pages = results.Select(p => new
                    {
                        pageNumber = p.PageNumber,
                        imageCount = p.Images.Count,
                        images = p.Images.Select(i => new
                        {
                            index = i.Index,
                            name = i.Name,
                            width = i.Width,
                            height = i.Height,
                            area = i.Area,
                            colorSpace = i.ColorSpace,
                            compressionType = i.CompressionType,
                            bitsPerComponent = i.BitsPerComponent,
                            estimatedSize = i.EstimatedSize,
                            extractedFile = i.ExtractedFile
                        })
                    }),
                    extractedFiles = extractedFiles
                };
                
                string outputFile = outputOptions.ContainsKey("-o") ? outputOptions["-o"] : "";
                string jsonOutput = JsonConvert.SerializeObject(jsonResult, Formatting.Indented);
                
                if (!string.IsNullOrEmpty(outputFile))
                {
                    File.WriteAllText(outputFile, jsonOutput);
                    Console.WriteLine($"✅ Results saved to: {outputFile}");
                }
                else
                {
                    Console.WriteLine(jsonOutput);
                }
            }
            else if (format.ToLower() == "csv")
            {
                var csv = new StringBuilder();
                csv.AppendLine("Page,Index,Name,Width,Height,Area,ColorSpace,Compression,BitsPerComponent,EstimatedSize");
                
                foreach (var page in results)
                {
                    foreach (var img in page.Images)
                    {
                        csv.AppendLine($"{page.PageNumber},{img.Index},\"{img.Name}\",{img.Width},{img.Height}," +
                                      $"{img.Area},\"{img.ColorSpace}\",\"{img.CompressionType}\"," +
                                      $"{img.BitsPerComponent},{img.EstimatedSize}");
                    }
                }
                
                string outputFile = outputOptions.ContainsKey("-o") ? outputOptions["-o"] : "";
                if (!string.IsNullOrEmpty(outputFile))
                {
                    File.WriteAllText(outputFile, csv.ToString());
                    Console.WriteLine($"✅ CSV saved to: {outputFile}");
                }
                else
                {
                    Console.WriteLine(csv.ToString());
                }
            }
            else if (format.ToLower() == "png")
            {
                // PNG extraction mode - extract and show summary
                var actualExtractedFiles = new List<string>();
                
                Console.WriteLine($"\n🎯 PNG Extraction Summary");
                Console.WriteLine($"📁 Output directory: {outputDir}");
                Console.WriteLine();
                
                // Extract each image using our corrected Base64 logic
                foreach (var page in results)
                {
                    foreach (var img in page.Images)
                    {
                        // Extract process number from cache for simple naming
                        string processNumber = ExtractProcessNumber(cacheIndex);
                        string outputPath = Path.Combine(outputDir, $"{processNumber}_p{page.PageNumber}_img{img.Index}.png");
                        Console.WriteLine($"🖼️  Extracting: {Path.GetFileName(outputPath)}");
                        
                        if (TryExtractImageAsPng(img, analysisResult, outputPath))
                        {
                            actualExtractedFiles.Add(outputPath);
                            Console.WriteLine($"   ✅ Success: {outputPath}");
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ Failed to extract");
                        }
                    }
                }
                
                Console.WriteLine();
                Console.WriteLine($"📊 Total images extracted: {actualExtractedFiles.Count}");
                
                if (actualExtractedFiles.Count > 0)
                {
                    Console.WriteLine("✅ Extracted files:");
                    foreach (var file in actualExtractedFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        Console.WriteLine($"  • {Path.GetFileName(file)} ({fileInfo.Length:N0} bytes)");
                    }
                }
                else
                {
                    Console.WriteLine("❌ No images were extracted.");
                    Console.WriteLine("💡 Possible reasons:");
                    Console.WriteLine("   • No Base64 image data available in cache");
                    Console.WriteLine("   • Images may be compressed or in unsupported format");
                    Console.WriteLine("   • No images match the specified filters");
                    Console.WriteLine();
                    Console.WriteLine("🔧 Try:");
                    Console.WriteLine("   • Reloading PDF cache with image data enabled");
                    Console.WriteLine("   • Using different filter criteria");
                    Console.WriteLine("   • Checking if original PDF contains extractable images");
                }
            }
            else
            {
                // Default text output - organized by pages
                
                // Get PDF info from cache
                string pdfName = analysisResult.FilePath != null ? Path.GetFileName(analysisResult.FilePath) : "Unknown PDF";
                string cacheInfo = $"[Cache #{cacheIndex}] {pdfName}";
                
                Console.WriteLine($"\n📄 {cacheInfo}");
                Console.WriteLine($"   Total: {totalImages} images in {analysisResult.Pages.Count} pages");
                
                if (imageSizeFilter != null || filterOptions.Count > 0)
                {
                    Console.WriteLine($"   Filtered: {matchingImages} images matching criteria");
                }
                Console.WriteLine();
                
                foreach (var page in results)
                {
                    Console.WriteLine($"Page {page.PageNumber}: {page.Images.Count} image(s)");
                    
                    foreach (var img in page.Images)
                    {
                        Console.WriteLine($"  [{img.Index}] {img.Name}:");
                        Console.WriteLine($"      Size: {img.Width}x{img.Height} pixels (area: {img.Area:N0})");
                        Console.WriteLine($"      Type: {img.ColorSpace}, {img.CompressionType}");
                        Console.WriteLine($"      Quality: {img.BitsPerComponent} bits/component");
                        Console.WriteLine($"      Est. size: {img.EstimatedSize:N0} bytes");
                        
                        // PNG extraction if requested
                        if (extractAsPng)
                        {
                            string outputPath = Path.Combine(outputDir, $"image_{img.Index}_{img.Name}.png");
                            Console.WriteLine($"      🖼️  Extracting PNG: {Path.GetFileName(outputPath)}");
                            
                            if (TryExtractImageAsPng(img, analysisResult, outputPath))
                            {
                                Console.WriteLine($"      ✅ PNG saved: {outputPath}");
                            }
                            else
                            {
                                Console.WriteLine($"      ❌ PNG extraction failed");
                            }
                        }
                    }
                    Console.WriteLine();
                }
                
                if (results.Count == 0)
                {
                    if (totalImages == 0)
                    {
                        Console.WriteLine($"❌ No images found in {pdfName}");
                        Console.WriteLine("   This PDF may contain only text or the cache needs to be reloaded with image extraction.");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️  {totalImages} images found but none match the filters");
                        Console.WriteLine("   Try adjusting your filter criteria.");
                    }
                }
            }
        }
        
        private class PageImageInfo
        {
            public int PageNumber { get; set; }
            public List<ImageDetails> Images { get; set; }
        }
        
        private class ImageDetails
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string ColorSpace { get; set; }
            public string CompressionType { get; set; }
            public int BitsPerComponent { get; set; }
            public long EstimatedSize { get; set; }
            public int Area { get; set; }
            public string ExtractedFile { get; set; }
        }
        
        private static bool TryExtractImageAsPng(ImageDetails imageDetails, PDFAnalysisResult analysisResult, string outputPath)
        {
            try
            {
                // Look for the image in the cached pages and extract using our corrected Base64 logic
                foreach (var page in analysisResult.Pages)
                {
                    foreach (var imageInfo in page.Resources.Images)
                    {
                        if (imageInfo.Name == imageDetails.Name)
                        {
                            Console.WriteLine($"      🔍 Found image in cache: {imageInfo.Base64Data?.Length ?? 0} bytes of Base64Data");
                            
                            if (string.IsNullOrEmpty(imageInfo.Base64Data))
                            {
                                Console.WriteLine($"      ❌ No Base64Data available");
                                return false;
                            }
                            
                            // Pass the dimensions from imageDetails, not from imageInfo
                            // because imageDetails has the display dimensions from PDF
                            return CreatePngFromBase64(imageInfo, outputPath, imageDetails);
                        }
                    }
                }
                
                Console.WriteLine($"      ⚠️  Image not found in cache");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      ❌ Exception during PNG extraction: {ex.Message}");
                return false;
            }
        }
        
        private static bool CreatePngFromBase64(ImageInfo imageInfo, string outputPath, ImageDetails imageDetails = null)
        {
            try
            {
                // Decode base64 to byte array
                byte[] imageBytes = Convert.FromBase64String(imageInfo.Base64Data);
                Console.WriteLine($"      ✅ Base64 decoded: {imageBytes.Length} bytes");
                
                // Pass the imageInfo which has Width, Height, CompressionType
                string base64ForConversion = Convert.ToBase64String(imageBytes);
                
                // Create an ImageWithData object with the info we have
                // Use dimensions from imageDetails if provided (display dimensions)
                // Otherwise fall back to imageInfo dimensions (actual dimensions)
                var imgData = new ImageDataExtractor.ImageWithData
                {
                    Width = imageDetails?.Width ?? imageInfo.Width,
                    Height = imageDetails?.Height ?? imageInfo.Height,
                    CompressionType = imageInfo.CompressionType,
                    Base64Data = base64ForConversion
                };
                
                Console.WriteLine($"      📐 Using dimensions: {imgData.Width}x{imgData.Height} (from {(imageDetails != null ? "imageDetails" : "imageInfo")})");
                
                return ImageDataExtractor.CreatePngFromBase64(base64ForConversion, outputPath, imgData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      ❌ PNG creation failed: {ex.Message}");
                return false;
            }
        }
        
        private static ImageSizeFilter ParseImageSizeFilter(Dictionary<string, string> filterOptions)
        {
            if (!filterOptions.ContainsKey("--image-size"))
                return null;
                
            string sizeStr = filterOptions["--image-size"];
            
            var parts = sizeStr.Split(new[] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return null;
                
            var filter = new ImageSizeFilter();
            
            // Parse width (can be range like "744-750")
            string widthPart = parts[0].Trim();
            if (widthPart.Contains("-"))
            {
                var range = widthPart.Split('-');
                if (int.TryParse(range[0], out int min) && int.TryParse(range[1], out int max))
                {
                    filter.MinWidth = min;
                    filter.MaxWidth = max;
                }
            }
            else if (int.TryParse(widthPart, out int width))
            {
                filter.MinWidth = width;
                filter.MaxWidth = width;
            }
            
            // Parse height (can be range)
            string heightPart = parts[1].Trim();
            if (heightPart.Contains("-"))
            {
                var range = heightPart.Split('-');
                if (int.TryParse(range[0], out int min) && int.TryParse(range[1], out int max))
                {
                    filter.MinHeight = min;
                    filter.MaxHeight = max;
                }
            }
            else if (int.TryParse(heightPart, out int height))
            {
                filter.MinHeight = height;
                filter.MaxHeight = height;
            }
            
            return filter;
        }
        
        private static bool MatchesImageSize(int width, int height, ImageSizeFilter filter, 
            Dictionary<string, string> filterOptions)
        {
            // Check nota de empenho filter first
            if (filterOptions.ContainsKey("--nota-empenho"))
            {
                return IsNotaDeEmpenhoImage(width, height);
            }
            
            // Check custom size filter
            if (filter != null)
            {
                bool widthMatch = width >= filter.MinWidth && width <= filter.MaxWidth;
                bool heightMatch = height >= filter.MinHeight && height <= filter.MaxHeight;
                if (!widthMatch || !heightMatch)
                    return false;
            }
            
            // Check individual min/max filters
            if (filterOptions.ContainsKey("--min-width"))
            {
                if (int.TryParse(filterOptions["--min-width"], out int minWidth) && width < minWidth)
                    return false;
            }
            
            if (filterOptions.ContainsKey("--max-width"))
            {
                if (int.TryParse(filterOptions["--max-width"], out int maxWidth) && width > maxWidth)
                    return false;
            }
            
            if (filterOptions.ContainsKey("--min-height"))
            {
                if (int.TryParse(filterOptions["--min-height"], out int minHeight) && height < minHeight)
                    return false;
            }
            
            if (filterOptions.ContainsKey("--max-height"))
            {
                if (int.TryParse(filterOptions["--max-height"], out int maxHeight) && height > maxHeight)
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Detecta se uma imagem é uma nota de empenho baseado nas dimensões típicas
        /// </summary>
        private static bool IsNotaDeEmpenhoImage(int width, int height)
        {
            // Dimensões típicas de notas de empenho observadas:
            // 744x1052, 744x1055, 744x1050, 745x1052, etc.
            
            // Tolerância para variações de escaneamento/conversão
            int tolerance = 10;
            
            // Dimensões base comuns para notas de empenho
            var notaEmpenhoSizes = new[]
            {
                new { Width = 744, Height = 1052 },  // Mais comum
                new { Width = 744, Height = 1055 },  // Variação
                new { Width = 744, Height = 1050 },  // Variação
                new { Width = 745, Height = 1052 },  // Pequena variação
                new { Width = 743, Height = 1052 },  // Pequena variação
                new { Width = 792, Height = 1224 },  // A4 em pontos (72 DPI)
                new { Width = 595, Height = 842 }    // A4 padrão
            };
            
            foreach (var size in notaEmpenhoSizes)
            {
                bool widthMatch = Math.Abs(width - size.Width) <= tolerance;
                bool heightMatch = Math.Abs(height - size.Height) <= tolerance;
                
                if (widthMatch && heightMatch)
                {
                    Console.WriteLine($"🎯 Nota de empenho detectada: {width}x{height} (padrão {size.Width}x{size.Height})");
                    return true;
                }
            }
            
            // Verificar proporção típica (aproximadamente 0.7 = 744/1052)
            double aspectRatio = (double)width / height;
            double notaEmpenhoRatio = 744.0 / 1052.0; // ≈ 0.707
            
            if (Math.Abs(aspectRatio - notaEmpenhoRatio) <= 0.05 && 
                width >= 700 && width <= 800 && 
                height >= 1000 && height <= 1100)
            {
                Console.WriteLine($"🎯 Nota de empenho detectada por proporção: {width}x{height} (ratio: {aspectRatio:F3})");
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Legacy method - replaced by ImageExtractionService
        /// </summary>
        [Obsolete("Use ImageExtractionService for improved extraction")]
        private static string ExtractImageAsPng(string cacheFile, PDFAnalysisResult analysisResult, 
            int pageNumber, ImageInfo imageInfo, string cacheIndex, string outputDir, int imageIndex)
        {
            // This method has been replaced by the new ImageExtractionService
            // Kept for compatibility but functionality moved to the service
            Console.WriteLine("⚠️  Legacy extraction method - using new service instead");
            return null;
        }
        
        private static string ExpandPath(string path)
        {
            if (path.StartsWith("~"))
            {
                string home = Environment.GetEnvironmentVariable("HOME") ?? 
                             Environment.GetEnvironmentVariable("USERPROFILE");
                return Path.Combine(home, path.Substring(2));
            }
            return Path.GetFullPath(path);
        }
        
        private class ImageSizeFilter
        {
            public int MinWidth { get; set; }
            public int MaxWidth { get; set; }
            public int MinHeight { get; set; }
            public int MaxHeight { get; set; }
        }
        
        /// <summary>
        /// Extract process number from cache index for simple naming
        /// </summary>
        private static string ExtractProcessNumber(string cacheIndex)
        {
            try
            {
                // Try to extract numbers from cache index
                var numbers = System.Text.RegularExpressions.Regex.Replace(cacheIndex, @"[^\d\-]", "");
                if (!string.IsNullOrEmpty(numbers))
                {
                    return numbers;
                }
                
                // Fallback to cache index itself
                return cacheIndex.Replace("_cache", "").Replace(".", "");
            }
            catch
            {
                return cacheIndex;
            }
        }
    }
}