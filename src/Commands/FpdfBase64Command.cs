using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FilterPDF.Utils;
using iText.Kernel.Pdf;
using Newtonsoft.Json;

namespace FilterPDF
{
    public static class FpdfBase64Command
    {
        public static void Execute(string inputPath, PDFAnalysisResult analysisResult, Dictionary<string, string> filterOptions, Dictionary<string, string> outputOptions)
        {
            // Check if user wants to extract a page as base64
            if (filterOptions.ContainsKey("--extract-page"))
            {
                ExtractPageAsBase64(inputPath, analysisResult, filterOptions, outputOptions);
                return;
            }

            // Check if raw output is requested
            bool isRawOutput = (outputOptions.ContainsKey("-F") && outputOptions["-F"] == "raw") || 
                              (outputOptions.ContainsKey("--format") && outputOptions["--format"] == "raw");

            if (!isRawOutput)
            {
                Console.WriteLine($"🔍 Loading from CACHE: {System.IO.Path.GetFileName(inputPath)}");
                Console.WriteLine("   Fonte: Cache (PERFORMANCE MÁXIMA - sem reprocessamento)");
                Console.WriteLine($"   DEBUG: Loading cache file at {DateTime.Now:HH:mm:ss.fff}");
                Console.WriteLine();
            }

            if (!isRawOutput)
            {
                Console.WriteLine($"Finding BASE64 content in: {System.IO.Path.GetFileName(inputPath)}");
                if (filterOptions.ContainsKey("--word") || filterOptions.ContainsKey("-w"))
                {
                    var word = filterOptions.ContainsKey("--word") ? filterOptions["--word"] : filterOptions["-w"];
                    Console.WriteLine($"Active filters:");
                    Console.WriteLine($"  --word: {word}");
                }
                Console.WriteLine();
            }

            var base64Results = FindBase64Content(analysisResult, filterOptions);
            var totalFound = base64Results.Sum(r => r.Base64Strings.Count);

            if (outputOptions.ContainsKey("-F") && outputOptions["-F"] == "count" || outputOptions.ContainsKey("--format") && outputOptions["--format"] == "count")
            {
                Console.WriteLine($"BASE64_STRINGS_COUNT: {totalFound}");
                Console.WriteLine($"BASE64_PAGES_COUNT: {base64Results.Count}");
                return;
            }

            if (outputOptions.ContainsKey("-F") && outputOptions["-F"] == "ocr" || outputOptions.ContainsKey("--format") && outputOptions["--format"] == "ocr")
            {
                ProcessOCRFormat(base64Results, inputPath);
                return;
            }

            if (outputOptions.ContainsKey("-F") && outputOptions["-F"] == "json" || outputOptions.ContainsKey("--format") && outputOptions["--format"] == "json")
            {
                var jsonResult = new
                {
                    source = inputPath,
                    totalPages = analysisResult.Pages.Count,
                    base64Results = base64Results.Select(r => new
                    {
                        pageNumber = r.PageNumber,
                        base64Count = r.Base64Strings.Count,
                        base64Strings = r.Base64Strings.Select(b => new
                        {
                            content = b.Content.Length > 100 ? b.Content.Substring(0, 100) + "..." : b.Content,
                            fullLength = b.Content.Length,
                            position = b.Position,
                            decodedSize = b.DecodedData?.Length ?? 0,
                            decodedPreview = b.DecodedPreview,
                            isPossibleImage = b.IsPossibleImage,
                            isPossibleDocument = b.IsPossibleDocument
                        }).ToList()
                    }).ToList(),
                    totalBase64Strings = totalFound
                };

                Console.WriteLine(JsonConvert.SerializeObject(jsonResult, Formatting.Indented));
                return;
            }

            Console.WriteLine($"Found {totalFound} base64 string(s) across {base64Results.Count} page(s):");
            Console.WriteLine();

            foreach (var result in base64Results)
            {
                Console.WriteLine($"📄 Página {result.PageNumber}: {result.Base64Strings.Count} base64 string(s) encontrada(s)");
                
                foreach (var b64 in result.Base64Strings)
                {
                    Console.WriteLine($"   🔍 Base64 encontrado (posição {b64.Position}):");
                    Console.WriteLine($"      Tamanho: {b64.Content.Length} caracteres");
                    Console.WriteLine($"      Preview: {(b64.Content.Length > 80 ? b64.Content.Substring(0, 80) + "..." : b64.Content)}");
                    
                    if (b64.DecodedData != null)
                    {
                        Console.WriteLine($"      Decodificado: {b64.DecodedData.Length} bytes");
                        Console.WriteLine($"      Preview decodificado: {b64.DecodedPreview}");
                        
                        if (b64.IsPossibleImage)
                        {
                            Console.WriteLine($"      🖼️  POSSÍVEL IMAGEM detectada!");
                        }
                        if (b64.IsPossibleDocument)
                        {
                            Console.WriteLine($"      📄 POSSÍVEL DOCUMENTO detectada!");
                        }
                    }
                    Console.WriteLine();
                }
            }

            if (totalFound == 0)
            {
                Console.WriteLine("❌ Nenhum conteúdo base64 encontrado.");
            }
            else
            {
                Console.WriteLine($"📊 Total: {totalFound} base64 string(s) em {base64Results.Count} página(s)");
                Console.WriteLine("💡 Use -F json para ver detalhes completos ou -F ocr para extrair texto das imagens");
            }
        }

        private static List<Base64Result> FindBase64Content(PDFAnalysisResult analysisResult, Dictionary<string, string> filterOptions)
        {
            var results = new List<Base64Result>();
            
            // Regex para detectar base64: mínimo 20 caracteres, múltiplo de 4, caracteres válidos
            var base64Regex = new Regex(@"[A-Za-z0-9+/]{20,}={0,2}", RegexOptions.Compiled);
            
            foreach (var page in analysisResult.Pages)
            {
                var base64Strings = new List<Base64String>();
                
                // Procurar base64 no texto da página
                if (!string.IsNullOrEmpty(page.TextInfo.PageText))
                {
                    var matches = base64Regex.Matches(page.TextInfo.PageText);
                    
                    foreach (Match match in matches)
                    {
                        var content = match.Value;
                        
                        // Validar se é base64 válido
                        if (IsValidBase64(content))
                        {
                            // Aplicar filtro de palavra se especificado
                            bool includeResult = true;
                            if (filterOptions.ContainsKey("--word") || filterOptions.ContainsKey("-w"))
                            {
                                var word = filterOptions.ContainsKey("--word") ? filterOptions["--word"] : filterOptions["-w"];
                                
                                // Verificar se a palavra está no contexto ao redor do base64
                                var contextStart = Math.Max(0, match.Index - 200);
                                var contextEnd = Math.Min(page.TextInfo.PageText.Length, match.Index + match.Length + 200);
                                var context = page.TextInfo.PageText.Substring(contextStart, contextEnd - contextStart);
                                
                                includeResult = context.Contains(word, StringComparison.OrdinalIgnoreCase);
                            }
                            
                            if (includeResult)
                            {
                                var base64String = new Base64String
                                {
                                    Content = content,
                                    Position = match.Index
                                };
                                
                                // Tentar decodificar
                                try
                                {
                                    base64String.DecodedData = Convert.FromBase64String(content);
                                    base64String.DecodedPreview = GetDecodedPreview(base64String.DecodedData);
                                    base64String.IsPossibleImage = IsPossibleImageData(base64String.DecodedData);
                                    base64String.IsPossibleDocument = IsPossibleDocumentData(base64String.DecodedData);
                                }
                                catch
                                {
                                    // Falha na decodificação - pode não ser base64 real
                                    continue;
                                }
                                
                                base64Strings.Add(base64String);
                            }
                        }
                    }
                }
                
                if (base64Strings.Any())
                {
                    results.Add(new Base64Result
                    {
                        PageNumber = page.PageNumber,
                        Base64Strings = base64Strings
                    });
                }
            }
            
            return results;
        }
        
        private static bool IsValidBase64(string content)
        {
            // Base64 deve ter comprimento múltiplo de 4
            if (content.Length % 4 != 0) return false;
            
            // Deve ter pelo menos 20 caracteres para ser interessante
            if (content.Length < 20) return false;
            
            // Tentar decodificar para validar
            try
            {
                Convert.FromBase64String(content);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private static string GetDecodedPreview(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            
            // Tentar interpretar como texto UTF-8
            try
            {
                var text = Encoding.UTF8.GetString(data);
                // Se contém muitos caracteres de controle, provavelmente não é texto
                var controlChars = text.Count(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t');
                if (controlChars > text.Length * 0.1) // Mais de 10% caracteres de controle
                {
                    return $"[DADOS BINÁRIOS - {data.Length} bytes]";
                }
                
                return text.Length > 100 ? text.Substring(0, 100) + "..." : text;
            }
            catch
            {
                return $"[DADOS BINÁRIOS - {data.Length} bytes]";
            }
        }
        
        private static bool IsPossibleImageData(byte[] data)
        {
            if (data == null || data.Length < 8) return false;
            
            // Verificar assinaturas de formatos de imagem comuns
            // JPEG
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return true;
            // PNG
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
            // GIF
            if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return true;
            // BMP
            if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D) return true;
            // TIFF
            if (data.Length >= 4 && ((data[0] == 0x49 && data[1] == 0x49) || (data[0] == 0x4D && data[1] == 0x4D))) return true;
            
            return false;
        }
        
        private static bool IsPossibleDocumentData(byte[] data)
        {
            if (data == null || data.Length < 8) return false;
            
            // PDF
            if (data.Length >= 4 && data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46) return true;
            // ZIP (inclui DOCX, XLSX, etc)
            if (data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B) return true;
            
            return false;
        }
        
        private static void ExtractPageAsBase64(string inputPath, PDFAnalysisResult analysisResult, Dictionary<string, string> filterOptions, Dictionary<string, string> outputOptions)
        {
            // Get page number
            if (!int.TryParse(filterOptions["--extract-page"], out int pageNumber))
            {
                Console.WriteLine($"❌ Erro: Número de página inválido: {filterOptions["--extract-page"]}");
                return;
            }
            
            // Validate page number
            if (pageNumber < 1 || pageNumber > analysisResult.Pages.Count)
            {
                Console.WriteLine($"❌ Erro: Página {pageNumber} não existe. O PDF tem {analysisResult.Pages.Count} páginas.");
                return;
            }
            
            try
            {
                // Use the original PDF file path from analysisResult
                var pdfPath = analysisResult.FilePath;
                
                // If FilePath is empty, try to find the original PDF file
                if (string.IsNullOrEmpty(pdfPath))
                {
                    // inputPath is the cache file, extract PDF filename from it
                    var cacheFileName = System.IO.Path.GetFileNameWithoutExtension(inputPath);
                    // Try to find the PDF file based on cache name pattern
                    var possiblePdfName = cacheFileName.Split('_')[0] + ".pdf";
                    
                    // Search in common locations
                    var searchPaths = new[] {
                        System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), possiblePdfName),
                        System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "pdfs", possiblePdfName),
                        System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "pdfs", possiblePdfName),
                        System.IO.Path.Combine("/home/chanfle/pdfs", possiblePdfName)
                    };
                    
                    foreach (var path in searchPaths)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            pdfPath = path;
                            break;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(pdfPath))
                    {
                        Console.WriteLine($"❌ Erro: Não foi possível encontrar o arquivo PDF original.");
                        Console.WriteLine($"   O comando --extract-page requer acesso ao PDF original.");
                        Console.WriteLine($"   Certifique-se de que o PDF está disponível no sistema.");
                        return;
                    }
                }
                
                // Open the PDF file with iText7 and copy only the requested page
                using var src = new PdfDocument(new PdfReader(pdfPath));
                using var ms = new MemoryStream();
                using (var dest = new PdfDocument(new PdfWriter(ms)))
                {
                    src.CopyPagesTo(pageNumber, pageNumber, dest);
                }

                byte[] pageBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(pageBytes);

                if (outputOptions.ContainsKey("-F") && outputOptions["-F"] == "ocr" ||
                    outputOptions.ContainsKey("--format") && outputOptions["--format"] == "ocr")
                {
                    ProcessOCRForBase64(base64String, inputPath, pageNumber);
                }
                else if (outputOptions.ContainsKey("-F") && outputOptions["-F"] == "json" ||
                         outputOptions.ContainsKey("--format") && outputOptions["--format"] == "json")
                {
                    var jsonResult = new
                    {
                        source = inputPath,
                        pageNumber,
                        base64Length = base64String.Length,
                        base64Content = base64String,
                        originalSizeBytes = pageBytes.Length
                    };
                    Console.WriteLine(JsonConvert.SerializeObject(jsonResult, Formatting.Indented));
                }
                else if (outputOptions.ContainsKey("-F") && outputOptions["-F"] == "raw" ||
                         outputOptions.ContainsKey("--format") && outputOptions["--format"] == "raw")
                {
                    Console.WriteLine(base64String);
                }
                else
                {
                    Console.WriteLine($"📄 Página {pageNumber} extraída como Base64:");
                    Console.WriteLine($"   Tamanho original: {pageBytes.Length:N0} bytes");
                    Console.WriteLine($"   Tamanho base64: {base64String.Length:N0} caracteres");
                    Console.WriteLine();
                    Console.WriteLine("BASE64 CONTENT:");
                    Console.WriteLine("================");
                    Console.WriteLine(base64String);
                    Console.WriteLine("================");
                    Console.WriteLine();
                    Console.WriteLine("💡 Dica: Use -F raw para obter apenas o conteúdo base64 puro");
                    Console.WriteLine("💡 Dica: Use -F json para obter resultado estruturado");
                    Console.WriteLine("💡 Dica: Use -F ocr para extrair texto da página usando EasyOCR");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao extrair página como base64: {ex.Message}");
            }
        }
        
        private static void ProcessOCRFormat(List<Base64Result> base64Results, string inputPath)
        {
            Console.WriteLine("🔍 Processando OCR para conteúdo base64 encontrado...");
            Console.WriteLine();
            
            foreach (var result in base64Results)
            {
                foreach (var b64String in result.Base64Strings)
                {
                    // Processar apenas se for possível imagem ou documento
                    if (b64String.IsPossibleImage || b64String.IsPossibleDocument)
                    {
                        Console.WriteLine($"📄 Página {result.PageNumber} - OCR do base64 (posição {b64String.Position}):");
                        ProcessOCRForBase64(b64String.Content, inputPath, result.PageNumber);
                        Console.WriteLine();
                    }
                }
            }
        }
        
        private static void ProcessOCRForBase64(string base64Content, string inputPath, int pageNumber)
        {
            try
            {
                // Create temporary file with base64 content
                var tempFile = Path.GetTempFileName() + ".b64";
                File.WriteAllText(tempFile, base64Content);
                
                // Get the path to the EasyOCR processor script
                var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "Scripts", "easyocr_processor.py");
                
                // If script is not found in expected location, try alternative paths
                if (!File.Exists(scriptPath))
                {
                    var alternativePaths = new[]
                    {
                        Path.Combine(Directory.GetCurrentDirectory(), "src", "Scripts", "easyocr_processor.py"),
                        Path.Combine(Directory.GetCurrentDirectory(), "easyocr_processor.py"),
                        "/mnt/b/dev-2/fpdf/src/Scripts/easyocr_processor.py"
                    };
                    
                    foreach (var altPath in alternativePaths)
                    {
                        if (File.Exists(altPath))
                        {
                            scriptPath = altPath;
                            break;
                        }
                    }
                }
                
                if (!File.Exists(scriptPath))
                {
                    Console.WriteLine("❌ Erro: Script EasyOCR não encontrado.");
                    Console.WriteLine("   Certifique-se de que easyocr_processor.py está disponível.");
                    return;
                }
                
                // Run EasyOCR processor
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
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        try
                        {
                            // Parse OCR result
                            var ocrResult = JsonConvert.DeserializeObject<dynamic>(output);
                            
                            Console.WriteLine($"   ✅ OCR realizado com sucesso (EasyOCR)");
                            Console.WriteLine($"   📊 Palavras encontradas: {ocrResult.words_found}");
                            Console.WriteLine($"   🎯 Confiança média: {ocrResult.total_confidence:F2}");
                            Console.WriteLine();
                            Console.WriteLine("📄 TEXTO EXTRAÍDO:");
                            Console.WriteLine("==================");
                            Console.WriteLine(ocrResult.text);
                            Console.WriteLine("==================");
                        }
                        catch (JsonException)
                        {
                            // If JSON parsing fails, show raw output
                            Console.WriteLine("   ✅ OCR realizado (EasyOCR)");
                            Console.WriteLine();
                            Console.WriteLine("📄 TEXTO EXTRAÍDO:");
                            Console.WriteLine("==================");
                            Console.WriteLine(output);
                            Console.WriteLine("==================");
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ Erro no processamento OCR:");
                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine($"   Erro: {error}");
                        }
                        else
                        {
                            Console.WriteLine("   Não foi possível extrair texto do conteúdo base64");
                        }
                    }
                }
                
                // Clean up
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro no processamento OCR: {ex.Message}");
            }
        }
    }
    
    public class Base64Result
    {
        public int PageNumber { get; set; }
        public List<Base64String> Base64Strings { get; set; } = new List<Base64String>();
    }
    
    public class Base64String
    {
        public string Content { get; set; }
        public int Position { get; set; }
        public byte[] DecodedData { get; set; }
        public string DecodedPreview { get; set; }
        public bool IsPossibleImage { get; set; }
        public bool IsPossibleDocument { get; set; }
    }
}
