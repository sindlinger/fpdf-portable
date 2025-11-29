using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FilterPDF
{
    /// <summary>
    /// Filter Scanned Command - Find pages that are scanned images
    /// </summary>
    public class FpdfScannedCommand
    {
        public static void ShowHelp()
        {
            Console.WriteLine("FILTER SCANNED - Find pages that are scanned images (photos/scans of documents)");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("    fpdf <file.pdf|cache-index> scanned [options]");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("    --threshold <n>          Confidence threshold (0-100, default: 90)");
            Console.WriteLine("    -F, --format <format>    Output format: txt, json (default: txt)");
            Console.WriteLine("    -o, --output <file>      Save output to file");
            Console.WriteLine();
            Console.WriteLine("DETECTION CRITERIA:");
            Console.WriteLine("    - Pages with images but very few words (<50 words = 95% confidence)");
            Console.WriteLine("    - Pages with 1-5 images and <200 words (85% confidence)");
            Console.WriteLine("    - Pages with many images and <150 words (80% confidence)");
            Console.WriteLine("    - Any page with images and <100 words (70% confidence)");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("    fpdf document.pdf scanned");
            Console.WriteLine("    fpdf 1-100 scanned --threshold 80");
            Console.WriteLine("    fpdf 1 scanned --format json -o scanned.json");
            Console.WriteLine();
            Console.WriteLine("OUTPUT:");
            Console.WriteLine("    Shows pages that appear to be scanned documents,");
            Console.WriteLine("    including confidence score, image count, and word count.");
        }

        public static void Execute(string inputFile, dynamic analysisResult, Dictionary<string, string> filterOptions, Dictionary<string, string> outputOptions)
        {
            try
            {
                // Get threshold from options (default 90%)
                int threshold = 90;
                if (filterOptions.ContainsKey("threshold"))
                {
                    int.TryParse(filterOptions["threshold"].ToString(), out threshold);
                }

                // Get format from output options
                string format = "txt";
                if (outputOptions.ContainsKey("format"))
                {
                    format = outputOptions["format"].ToString().ToLower();
                }

                // Read cache file
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine($"Error: Cache file not found: {inputFile}");
                    return;
                }

                string jsonContent = File.ReadAllText(inputFile);
                dynamic cache = JsonConvert.DeserializeObject(jsonContent);

                if (cache?.Pages == null)
                {
                    Console.WriteLine("No pages found in cache file.");
                    return;
                }

                var scannedPages = new List<dynamic>();
                
                foreach (var page in cache.Pages)
                {
                    var pageInfo = AnalyzePage(page);
                    if (pageInfo.confidence >= threshold)
                    {
                        scannedPages.Add(pageInfo);
                    }
                }

                // Output results
                if (format == "json")
                {
                    OutputJson(cache, scannedPages, threshold, outputOptions);
                }
                else
                {
                    OutputText(cache, scannedPages, threshold, outputOptions, inputFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing scanned pages: {ex.Message}");
                if (Environment.GetEnvironmentVariable("FPDF_DEBUG") == "true")
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        private static dynamic AnalyzePage(dynamic page)
        {
            int pageNumber = page.PageNumber ?? page.page_number ?? 0;
            
            // Get word count from TextInfo.WordCount
            int wordCount = 0;
            if (page.TextInfo != null && page.TextInfo.WordCount != null)
            {
                wordCount = (int)page.TextInfo.WordCount;
            }
            else if (page.Words != null)
            {
                wordCount = (int)page.Words;
            }
            else if (page.words != null)
            {
                wordCount = (int)page.words;
            }
            
            int imageCount = 0;
            bool hasLargeImages = false;

            // Count images - primary source is Resources.Images
            if (page.Resources != null)
            {
                var resources = page.Resources as JObject;
                if (resources != null && resources["Images"] != null)
                {
                    var images = resources["Images"] as JArray;
                    if (images != null)
                    {
                        imageCount = images.Count;
                        
                        // Check if any image is large (simplified check based on presence)
                        if (images.Count > 0)
                        {
                            hasLargeImages = true;
                        }
                    }
                }
            }
            
            // Fallback to other image count fields
            if (imageCount == 0)
            {
                if (page.ImagesInfo != null)
                {
                    imageCount = (page.ImagesInfo as JArray)?.Count ?? 0;
                }
                else if (page.images_count != null)
                {
                    imageCount = (int)page.images_count;
                }
            }

            // Calculate confidence
            double confidence = CalculateConfidence(imageCount, wordCount, hasLargeImages);

            return new
            {
                pageNumber = pageNumber,
                imageCount = imageCount,
                wordCount = wordCount,
                confidence = confidence,
                hasLargeImages = hasLargeImages
            };
        }

        private static double CalculateConfidence(int imageCount, int wordCount, bool hasLargeImages)
        {
            // High confidence for pages with images but very few words
            if (imageCount > 0 && wordCount < 50)
                return 95;
            
            // Good confidence for 1-5 images with moderate text
            if (imageCount >= 1 && imageCount <= 5 && wordCount < 200)
                return 85;
            
            // Multiple images with low text
            if (imageCount > 5 && wordCount < 150)
                return 80;
            
            // Has large images with moderate text
            if (hasLargeImages && wordCount < 300)
                return 75;
            
            // Any page with images and low text
            if (imageCount > 0 && wordCount < 100)
                return 70;
            
            // Lower confidence cases
            if (imageCount > 0 && wordCount < 500)
                return 50;
            
            return 0;
        }

        private static void OutputText(dynamic cache, List<dynamic> scannedPages, int threshold, Dictionary<string, string> outputOptions, string inputFile)
        {
            var output = new StringBuilder();
            string fileName = Path.GetFileNameWithoutExtension(cache.OriginalFileName?.ToString() ?? "unknown");
            
            output.AppendLine($"Finding SCANNED PAGES in: {Path.GetFileName(inputFile)}");
            output.AppendLine($"Threshold: {threshold}% confidence");
            output.AppendLine();
            output.AppendLine($"Found {scannedPages.Count} scanned page(s):");
            output.AppendLine();

            if (scannedPages.Count > 0)
            {
                foreach (var page in scannedPages)
                {
                    output.AppendLine($"PAGE {page.pageNumber}:");
                    output.AppendLine($"  Confidence: {page.confidence:F0}%");
                    output.AppendLine($"  Images: {page.imageCount}");
                    output.AppendLine($"  Words: {page.wordCount}");
                    if (page.hasLargeImages)
                    {
                        output.AppendLine($"  Type: Full-page scan (large images detected)");
                    }
                    output.AppendLine();
                }

                // Summary with page ranges
                if (scannedPages.Count > 5)
                {
                    var pageNumbers = scannedPages.Select(p => (int)p.pageNumber).OrderBy(n => n).ToList();
                    var ranges = GetPageRanges(pageNumbers);
                    output.AppendLine($"📑 Page ranges: {string.Join(", ", ranges)}");
                    output.AppendLine($"📊 Total scanned pages: {scannedPages.Count}");
                }
            }
            else
            {
                output.AppendLine($"ℹ️  No scanned pages found with confidence >= {threshold}%");
            }

            // Output to file or console
            if (outputOptions.ContainsKey("output"))
            {
                string outputFile = outputOptions["output"].ToString();
                File.WriteAllText(outputFile, output.ToString());
                Console.WriteLine($"Output saved to: {outputFile}");
            }
            else
            {
                Console.Write(output.ToString());
            }
        }

        private static void OutputJson(dynamic cache, List<dynamic> scannedPages, int threshold, Dictionary<string, string> outputOptions)
        {
            var pageNumbers = scannedPages.Select(p => (int)p.pageNumber).OrderBy(n => n).ToList();
            
            var result = new
            {
                document = Path.GetFileNameWithoutExtension(cache.OriginalFileName?.ToString() ?? "unknown"),
                cache_file = Path.GetFileName(cache.CacheFile?.ToString() ?? "unknown"),
                threshold = threshold,
                total_pages = (cache.Pages as JArray)?.Count ?? 0,
                scanned_pages_count = scannedPages.Count,
                scanned_pages = scannedPages.Select(p => new
                {
                    page = p.pageNumber,
                    confidence = Math.Round((double)p.confidence, 1),
                    images = p.imageCount,
                    words = p.wordCount,
                    large_images = p.hasLargeImages
                }),
                page_ranges = GetPageRanges(pageNumbers)
            };

            string json = JsonConvert.SerializeObject(result, Formatting.Indented);

            // Output to file or console
            if (outputOptions.ContainsKey("output"))
            {
                string outputFile = outputOptions["output"].ToString();
                File.WriteAllText(outputFile, json);
                Console.WriteLine($"Output saved to: {outputFile}");
            }
            else
            {
                Console.WriteLine(json);
            }
        }

        private static List<string> GetPageRanges(List<int> pageNumbers)
        {
            if (pageNumbers.Count == 0) return new List<string>();

            var ranges = new List<string>();
            int start = pageNumbers[0];
            int end = pageNumbers[0];

            for (int i = 1; i < pageNumbers.Count; i++)
            {
                if (pageNumbers[i] == end + 1)
                {
                    end = pageNumbers[i];
                }
                else
                {
                    ranges.Add(start == end ? start.ToString() : $"{start}-{end}");
                    start = end = pageNumbers[i];
                }
            }

            ranges.Add(start == end ? start.ToString() : $"{start}-{end}");
            return ranges;
        }

    }
}