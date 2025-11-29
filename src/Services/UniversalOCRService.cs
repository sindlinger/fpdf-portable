using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FilterPDF.Utils;
using iTextSharp.text.pdf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FilterPDF.Services
{
    public static class UniversalOCRService
    {
        private static OCRConfig _config;
        private static readonly object _configLock = new object();

        public static OCRConfig Config
        {
            get
            {
                if (_config == null)
                {
                    lock (_configLock)
                    {
                        if (_config == null)
                        {
                            LoadConfig();
                        }
                    }
                }
                return _config;
            }
        }

        private static void LoadConfig()
        {
            try
            {
                var configPaths = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "ocr.config.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr.config.json"),
                    "/mnt/b/dev-2/fpdf/ocr.config.json",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fpdf", "ocr.config.json")
                };

                string configPath = null;
                foreach (var path in configPaths)
                {
                    if (File.Exists(path))
                    {
                        configPath = path;
                        break;
                    }
                }

                if (configPath != null)
                {
                    var json = File.ReadAllText(configPath);
                    var configData = JsonConvert.DeserializeObject<JObject>(json);
                    _config = new OCRConfig(configData);
                }
                else
                {
                    _config = new OCRConfig(); // Default config
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao carregar configuração OCR: {ex.Message}");
                _config = new OCRConfig(); // Fallback to default
            }
        }

        public static OCRResult ProcessPageWithOCR(string inputPath, PDFAnalysisResult analysisResult, int pageNumber)
        {
            try
            {
                // Find original PDF path
                var pdfPath = FindOriginalPDFPath(inputPath, analysisResult);
                if (string.IsNullOrEmpty(pdfPath))
                {
                    return new OCRResult { Success = false, Error = "PDF original não encontrado" };
                }

                // Extract page as base64
                var base64Content = ExtractPageAsBase64(pdfPath, pageNumber);
                if (string.IsNullOrEmpty(base64Content))
                {
                    return new OCRResult { Success = false, Error = "Falha ao extrair página como base64" };
                }

                // Process with EasyOCR
                return ProcessBase64WithOCR(base64Content, pageNumber);
            }
            catch (Exception ex)
            {
                return new OCRResult { Success = false, Error = ex.Message };
            }
        }

        public static OCRResult ProcessPagesWithOCR(string inputPath, PDFAnalysisResult analysisResult, List<int> pageNumbers)
        {
            var results = new List<OCRResult>();
            
            foreach (var pageNumber in pageNumbers.Take(Config.Performance.MaxPagesParallel))
            {
                var result = ProcessPageWithOCR(inputPath, analysisResult, pageNumber);
                if (result.Success)
                {
                    results.Add(result);
                }
            }

            return CombineOCRResults(results);
        }

        private static string FindOriginalPDFPath(string inputPath, PDFAnalysisResult analysisResult)
        {
            // Use the original PDF file path from analysisResult
            var pdfPath = analysisResult?.FilePath;
            
            if (string.IsNullOrEmpty(pdfPath))
            {
                // Try to find based on cache filename
                var cacheFileName = Path.GetFileNameWithoutExtension(inputPath);
                var possiblePdfName = cacheFileName.Split('_')[0] + ".pdf";
                
                var searchPaths = new[] {
                    Path.Combine(Directory.GetCurrentDirectory(), possiblePdfName),
                    Path.Combine(Directory.GetCurrentDirectory(), "pdfs", possiblePdfName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "pdfs", possiblePdfName),
                    Path.Combine("/home/chanfle/pdfs", possiblePdfName)
                };
                
                foreach (var path in searchPaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            
            return pdfPath;
        }

        private static string ExtractPageAsBase64(string pdfPath, int pageNumber)
        {
            try
            {
                using (var reader = new PdfReader(pdfPath))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        using (var document = new iTextSharp.text.Document())
                        {
                            using (var writer = PdfWriter.GetInstance(document, memoryStream))
                            {
                                document.Open();
                                var cb = writer.DirectContent;
                                var page = writer.GetImportedPage(reader, pageNumber);
                                cb.AddTemplate(page, 0, 0);
                                document.Close();
                            }
                        }
                        
                        byte[] pageBytes = memoryStream.ToArray();
                        return Convert.ToBase64String(pageBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao extrair página {pageNumber}: {ex.Message}");
                return null;
            }
        }

        private static OCRResult ProcessBase64WithOCR(string base64Content, int pageNumber)
        {
            try
            {
                // Create temporary file
                var tempFile = Path.GetTempFileName() + ".b64";
                File.WriteAllText(tempFile, base64Content);
                
                // Get script path
                var scriptPath = GetEasyOCRScriptPath();
                if (string.IsNullOrEmpty(scriptPath))
                {
                    return new OCRResult { Success = false, Error = "Script EasyOCR não encontrado" };
                }
                
                // Run EasyOCR
                var psi = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{scriptPath}\" \"{tempFile}\" json",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(psi))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    // Clean up
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return ParseOCROutput(output, pageNumber);
                    }
                    else
                    {
                        return new OCRResult { Success = false, Error = error };
                    }
                }
            }
            catch (Exception ex)
            {
                return new OCRResult { Success = false, Error = ex.Message };
            }
        }

        private static string GetEasyOCRScriptPath()
        {
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "Scripts", "easyocr_processor.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Scripts", "easyocr_processor.py"),
                "/mnt/b/dev-2/fpdf/src/Scripts/easyocr_processor.py"
            };
            
            return possiblePaths.FirstOrDefault(File.Exists);
        }

        private static OCRResult ParseOCROutput(string output, int pageNumber)
        {
            try
            {
                var ocrData = JsonConvert.DeserializeObject<dynamic>(output);
                var rawText = (string)ocrData.text;
                
                var result = new OCRResult
                {
                    Success = true,
                    PageNumber = pageNumber,
                    RawText = rawText,
                    Confidence = (double)ocrData.total_confidence,
                    WordsFound = (int)ocrData.words_found,
                    ExtractedPatterns = ExtractBrazilianPatterns(rawText)
                };
                
                return result;
            }
            catch (JsonException)
            {
                // If JSON parsing fails, treat as plain text
                return new OCRResult
                {
                    Success = true,
                    PageNumber = pageNumber,
                    RawText = output,
                    Confidence = 0.0,
                    WordsFound = output.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                    ExtractedPatterns = ExtractBrazilianPatterns(output)
                };
            }
        }

        private static Dictionary<string, List<string>> ExtractBrazilianPatterns(string text)
        {
            var patterns = new Dictionary<string, List<string>>();
            
            foreach (var pattern in Config.BrazilianPatterns)
            {
                if (pattern.Value.Extract)
                {
                    var regex = new Regex(pattern.Value.Pattern, RegexOptions.IgnoreCase);
                    var matches = regex.Matches(text);
                    
                    if (matches.Count > 0)
                    {
                        patterns[pattern.Key] = matches.Cast<Match>()
                            .Select(m => m.Value.Trim())
                            .Distinct()
                            .ToList();
                    }
                }
            }
            
            return patterns;
        }

        private static OCRResult CombineOCRResults(List<OCRResult> results)
        {
            if (!results.Any())
            {
                return new OCRResult { Success = false, Error = "Nenhum resultado OCR válido" };
            }

            var combined = new OCRResult
            {
                Success = true,
                RawText = string.Join("\n\n", results.Select(r => r.RawText)),
                Confidence = results.Average(r => r.Confidence),
                WordsFound = results.Sum(r => r.WordsFound),
                ExtractedPatterns = new Dictionary<string, List<string>>()
            };

            // Combine patterns from all results
            foreach (var result in results)
            {
                foreach (var pattern in result.ExtractedPatterns)
                {
                    if (!combined.ExtractedPatterns.ContainsKey(pattern.Key))
                    {
                        combined.ExtractedPatterns[pattern.Key] = new List<string>();
                    }
                    combined.ExtractedPatterns[pattern.Key].AddRange(pattern.Value);
                }
            }

            // Remove duplicates
            foreach (var key in combined.ExtractedPatterns.Keys.ToList())
            {
                combined.ExtractedPatterns[key] = combined.ExtractedPatterns[key].Distinct().ToList();
            }

            return combined;
        }

        public static string FormatOCROutput(OCRResult result, bool includePatterns = true)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"   ✅ OCR realizado com sucesso (EasyOCR)");
            sb.AppendLine($"   📊 Palavras encontradas: {result.WordsFound}");
            sb.AppendLine($"   🎯 Confiança média: {result.Confidence:F2}");
            
            if (includePatterns && result.ExtractedPatterns.Any())
            {
                sb.AppendLine($"   🇧🇷 Padrões brasileiros encontrados:");
                
                foreach (var pattern in result.ExtractedPatterns)
                {
                    var patternInfo = Config.BrazilianPatterns.ContainsKey(pattern.Key) 
                        ? Config.BrazilianPatterns[pattern.Key] 
                        : null;
                    
                    var description = patternInfo?.Description ?? pattern.Key.ToUpper();
                    sb.AppendLine($"      {GetPatternIcon(pattern.Key)} {description}:");
                    
                    foreach (var value in pattern.Value)
                    {
                        sb.AppendLine($"         {value}");
                    }
                }
            }
            
            sb.AppendLine();
            sb.AppendLine("📄 TEXTO EXTRAÍDO:");
            sb.AppendLine("==================");
            sb.AppendLine(result.RawText);
            sb.AppendLine("==================");
            
            return sb.ToString();
        }

        private static string GetPatternIcon(string patternKey)
        {
            return patternKey.ToLower() switch
            {
                "cpf" => "🆔",
                "cnpj" => "🏢", 
                "currency" => "💰",
                "dates" => "📅",
                "process_number" => "📋",
                "cep" => "📍",
                _ => "📄"
            };
        }
    }

    public class OCRConfig
    {
        public OCRSettings Settings { get; set; }
        public Dictionary<string, PatternConfig> BrazilianPatterns { get; set; }
        public Dictionary<string, object> GovernmentPatterns { get; set; }
        public OutputFormatting OutputFormatting { get; set; }
        public PerformanceConfig Performance { get; set; }

        public OCRConfig()
        {
            // Default configuration
            Settings = new OCRSettings
            {
                Language = "pt",
                FallbackLanguage = "en",
                ResolutionDpi = 300,
                ConfidenceThreshold = 0.6,
                GpuEnabled = true
            };

            BrazilianPatterns = GetDefaultBrazilianPatterns();
            Performance = new PerformanceConfig
            {
                MaxPagesParallel = 3,
                TimeoutSeconds = 60,
                CacheResults = true,
                RetryAttempts = 2
            };
        }

        public OCRConfig(JObject configData)
        {
            try
            {
                Settings = configData["ocr_settings"]?.ToObject<OCRSettings>() ?? new OCRSettings();
                
                var patternsData = configData["brazilian_patterns"]?.ToObject<JObject>();
                BrazilianPatterns = new Dictionary<string, PatternConfig>();
                
                if (patternsData != null)
                {
                    foreach (var kvp in patternsData)
                    {
                        BrazilianPatterns[kvp.Key] = kvp.Value.ToObject<PatternConfig>();
                    }
                }
                
                Performance = configData["performance"]?.ToObject<PerformanceConfig>() ?? new PerformanceConfig();
            }
            catch
            {
                // Fallback to defaults
                Settings = new OCRSettings();
                BrazilianPatterns = GetDefaultBrazilianPatterns();
                Performance = new PerformanceConfig();
            }
        }

        private Dictionary<string, PatternConfig> GetDefaultBrazilianPatterns()
        {
            return new Dictionary<string, PatternConfig>
            {
                ["cpf"] = new PatternConfig
                {
                    Pattern = @"\d{3}\.?\d{3}\.?\d{3}-?\d{2}",
                    Description = "CPF brasileiro",
                    Extract = true
                },
                ["cnpj"] = new PatternConfig
                {
                    Pattern = @"\d{2}\.?\d{3}\.?\d{3}/?\\d{4}-?\d{2}",
                    Description = "CNPJ brasileiro",
                    Extract = true
                },
                ["currency"] = new PatternConfig
                {
                    Pattern = @"R\$\s*\d{1,3}(?:[.,]\d{3})*[.,]\d{2}",
                    Description = "Valores monetários brasileiros",
                    Extract = true
                }
            };
        }
    }

    public class OCRSettings
    {
        public string Language { get; set; } = "pt";
        public string FallbackLanguage { get; set; } = "en";
        public int ResolutionDpi { get; set; } = 300;
        public double ConfidenceThreshold { get; set; } = 0.6;
        public bool GpuEnabled { get; set; } = true;
    }

    public class PatternConfig
    {
        public string Pattern { get; set; }
        public string Description { get; set; }
        public string Format { get; set; }
        public bool Extract { get; set; }
        public bool Validate { get; set; }
    }

    public class OutputFormatting
    {
        public bool StructuredOutput { get; set; } = true;
        public bool IncludeConfidence { get; set; } = true;
        public bool IncludeCoordinates { get; set; } = false;
        public bool GroupByPattern { get; set; } = true;
        public bool SortByConfidence { get; set; } = true;
    }

    public class PerformanceConfig
    {
        public int MaxPagesParallel { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 60;
        public bool CacheResults { get; set; } = true;
        public int RetryAttempts { get; set; } = 2;
    }

    public class OCRResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int PageNumber { get; set; }
        public string RawText { get; set; }
        public double Confidence { get; set; }
        public int WordsFound { get; set; }
        public Dictionary<string, List<string>> ExtractedPatterns { get; set; } = new Dictionary<string, List<string>>();
    }
}