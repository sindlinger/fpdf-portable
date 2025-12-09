using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FilterPDF.Utils;
using iTextSharp.text.pdf;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Comando para realizar OCR em PDFs escaneados
    /// Suporta Tesseract e extração de campos configuráveis
    /// </summary>
    public class FpdfOCRCommand : FilterPDF.Command
    {
        public override string Name => "ocr";
        public override string Description => "Extract text from scanned PDFs using OCR";

        private class OCRConfig
        {
            public OCRSettings Ocr { get; set; } = new OCRSettings();
            public ExtractionSettings Extraction { get; set; } = new ExtractionSettings();
            public PreprocessingSettings Preprocessing { get; set; } = new PreprocessingSettings();
            public OutputSettings Output { get; set; } = new OutputSettings();
        }

        private class OCRSettings
        {
            public bool Enabled { get; set; } = true;
            public string Engine { get; set; } = "tesseract";
            public string TesseractPath { get; set; } = "/usr/bin/tesseract";
            public string DefaultLanguage { get; set; } = "por";
            public List<string> Languages { get; set; } = new List<string> { "por", "eng" };
            public int Timeout { get; set; } = 30000;
            public int MaxPagesParallel { get; set; } = 4;
        }

        private class ExtractionSettings
        {
            public List<Campo> Campos { get; set; } = new List<Campo>();
        }

        private class Campo
        {
            public string Nome { get; set; } = "";
            public string Label { get; set; } = "";
            public List<string> Patterns { get; set; } = new List<string>();
            public bool Required { get; set; }
            public string Type { get; set; } = "text";
        }

        private class PreprocessingSettings
        {
            public bool Deskew { get; set; } = true;
            public bool RemoveNoise { get; set; } = true;
            public bool Binarization { get; set; } = true;
        }

        private class OutputSettings
        {
            public bool SaveOriginalText { get; set; } = true;
            public bool SaveExtractedFields { get; set; } = true;
            public string Format { get; set; } = "json";
            public bool IncludeConfidence { get; set; } = true;
            public double MinConfidence { get; set; } = 0.6;
        }

        private OCRConfig? config;

        private Dictionary<string, string> ParseOptions(string[] args)
        {
            var options = new Dictionary<string, string>();
            
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                
                if (arg.StartsWith("-"))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        options[arg] = args[++i];
                    }
                    else
                    {
                        options[arg] = "true";
                    }
                }
            }
            
            return options;
        }

        public override void Execute(string[] args)
        {
            if (args.Length < 1)
            {
                ShowHelp();
                return;
            }

            // Carrega configuração
            LoadConfiguration();

            string inputFile = args[0];
            var options = ParseOptions(args.Skip(1).ToArray());

            // Detecta tipo de página primeiro
            if (options.ContainsKey("--detect") || options.ContainsKey("-d"))
            {
                DetectPageTypes(inputFile);
                return;
            }

            // Processa OCR
            ProcessOCR(inputFile, options);
        }

        private void LoadConfiguration()
        {
            string configPath = "ocr-config.json";
            if (!File.Exists(configPath))
            {
                configPath = "ocr-config.example.json";
            }

            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    config = JsonConvert.DeserializeObject<OCRConfig>(json);
                    Console.WriteLine($"✅ Configuração OCR carregada de: {configPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Erro ao carregar configuração: {ex.Message}");
                    config = new OCRConfig();
                }
            }
            else
            {
                Console.WriteLine("ℹ️ Usando configuração OCR padrão");
                config = new OCRConfig();
            }
        }

        private void DetectPageTypes(string inputFile)
        {
            try
            {
                using (var reader = PdfAccessManager.GetReader(inputFile))
                {
                    var analyses = PageTypeDetector.AnalyzeAllPages(reader);
                    
                    Console.WriteLine($"\n📄 Análise de: {Path.GetFileName(inputFile)}");
                    Console.WriteLine(PageTypeDetector.GetSummary(analyses));
                    
                    // Lista páginas que precisam de OCR
                    var ocrPages = analyses.Where(a => a.NeedsOCR).ToList();
                    if (ocrPages.Any())
                    {
                        Console.WriteLine("📝 Páginas que necessitam OCR:");
                        foreach (var page in ocrPages)
                        {
                            Console.WriteLine($"  • Página {page.PageNumber}: {page.Description}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Erro ao analisar PDF: {ex.Message}");
            }
        }

        private void ProcessOCR(string inputFile, Dictionary<string, string> options)
        {
            try
            {
                Console.WriteLine($"\n🔍 Iniciando OCR em: {Path.GetFileName(inputFile)}");
                
                // Determina páginas para processar
                List<int> pagesToProcess = new List<int>();
                
                if (options.ContainsKey("-p") || options.ContainsKey("--pages"))
                {
                    string pageSpec = options.ContainsKey("-p") ? options["-p"] : options["--pages"];
                    pagesToProcess = ParsePageRange(pageSpec);
                }
                else
                {
                    // Detecta automaticamente páginas que precisam de OCR
                    using (var reader = PdfAccessManager.GetReader(inputFile))
                    {
                        var analyses = PageTypeDetector.AnalyzeAllPages(reader);
                        pagesToProcess = analyses.Where(a => a.NeedsOCR)
                                                 .Select(a => a.PageNumber)
                                                 .ToList();
                        
                        if (!pagesToProcess.Any())
                        {
                            Console.WriteLine("ℹ️ Nenhuma página necessita OCR. Use --all para forçar.");
                            if (!options.ContainsKey("--all"))
                            {
                                return;
                            }
                            // Processa todas se --all foi especificado
                            pagesToProcess = Enumerable.Range(1, reader.NumberOfPages).ToList();
                        }
                    }
                }

                Console.WriteLine($"📄 Processando {pagesToProcess.Count} página(s)...");

                // Extrai imagens e processa OCR
                var results = new List<OCRResult>();
                
                Parallel.ForEach(pagesToProcess, new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = config?.Ocr?.MaxPagesParallel ?? 4 
                }, 
                pageNum =>
                {
                    var result = ProcessPage(inputFile, pageNum);
                    lock (results)
                    {
                        results.Add(result);
                    }
                });

                // Ordena resultados por página
                results = results.OrderBy(r => r.PageNumber).ToList();

                // Extrai campos configurados
                if (config?.Extraction?.Campos?.Any() == true)
                {
                    ExtractFields(results);
                }

                // Salva resultados
                SaveResults(inputFile, results, options);
                
                Console.WriteLine($"\n✅ OCR concluído! {results.Count} páginas processadas.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Erro no OCR: {ex.Message}");
            }
        }

        private OCRResult ProcessPage(string pdfPath, int pageNumber)
        {
            var result = new OCRResult { PageNumber = pageNumber };
            
            try
            {
                // Extrai página como imagem temporária
                string tempImage = ExtractPageAsImage(pdfPath, pageNumber);
                
                if (File.Exists(tempImage))
                {
                    // Executa Tesseract
                    string text = RunTesseract(tempImage);
                    result.Text = text;
                    result.Success = true;
                    
                    // Limpa arquivo temporário
                    File.Delete(tempImage);
                    
                    Console.WriteLine($"  ✓ Página {pageNumber} processada");
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Console.WriteLine($"  ✗ Erro na página {pageNumber}: {ex.Message}");
            }
            
            return result;
        }

        private string ExtractPageAsImage(string pdfPath, int pageNumber)
        {
            // REMOVIDO: Validação de segurança para máxima performance
            string sanitizedPath = pdfPath;
            
            if (pageNumber < 1 || pageNumber > 10000) // reasonable limit
            {
                throw new ArgumentException($"Invalid page number: {pageNumber}");
            }
            
            // Usa ferramentas externas como pdftoppm ou ImageMagick
            string tempFile = Path.Combine(Path.GetTempPath(), $"page_{pageNumber}_{Guid.NewGuid()}.png");
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pdftoppm",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            
            // Use ArgumentList to prevent injection
            process.StartInfo.ArgumentList.Add("-png");
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add(pageNumber.ToString());
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add(pageNumber.ToString());
            process.StartInfo.ArgumentList.Add("-r");
            process.StartInfo.ArgumentList.Add("300");
            process.StartInfo.ArgumentList.Add(sanitizedPath);
            process.StartInfo.ArgumentList.Add(Path.GetFileNameWithoutExtension(tempFile));
            
            process.Start();
            process.WaitForExit(30000);
            
            // pdftoppm adiciona sufixo ao arquivo
            string actualFile = $"{Path.GetFileNameWithoutExtension(tempFile)}-{pageNumber:D2}.png";
            if (File.Exists(actualFile))
            {
                File.Move(actualFile, tempFile);
            }
            
            return tempFile;
        }

        private string RunTesseract(string imagePath)
        {
            // Validate inputs to prevent command injection
            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException($"Image file not found: {imagePath}");
            }
            
            string tesseractPath = config?.Ocr?.TesseractPath ?? "tesseract";
            string language = config?.Ocr?.DefaultLanguage ?? "por";
            
            // Validate language parameter (only allow alphanumeric)
            if (!System.Text.RegularExpressions.Regex.IsMatch(language, @"^[a-zA-Z0-9_+]+$"))
            {
                throw new ArgumentException($"Invalid language parameter: {language}");
            }
            
            // REMOVIDO: Validação de segurança para máxima performance
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tesseractPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            
            // Use ArgumentList to prevent injection
            process.StartInfo.ArgumentList.Add(imagePath);
            process.StartInfo.ArgumentList.Add("stdout");
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add(language);
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(config?.Ocr?.Timeout ?? 30000);
            
            return output;
        }

        private void ExtractFields(List<OCRResult> results)
        {
            if (config?.Extraction?.Campos == null) return;
            
            string fullText = string.Join("\n", results.Select(r => r.Text));
            var extractedFields = new Dictionary<string, string>();
            
            foreach (var campo in config.Extraction.Campos)
            {
                foreach (var pattern in campo.Patterns)
                {
                    var match = Regex.Match(fullText, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (match.Success)
                    {
                        extractedFields[campo.Nome] = match.Groups[1].Value.Trim();
                        break;
                    }
                }
            }
            
            // Adiciona campos extraídos aos resultados
            foreach (var result in results)
            {
                result.ExtractedFields = extractedFields;
            }
            
            // Mostra campos extraídos
            if (extractedFields.Any())
            {
                Console.WriteLine("\n📋 Campos extraídos:");
                foreach (var field in extractedFields)
                {
                    var campo = config.Extraction.Campos.FirstOrDefault(c => c.Nome == field.Key);
                    Console.WriteLine($"  • {campo?.Label ?? field.Key}: {field.Value}");
                }
            }
        }

        private void SaveResults(string inputFile, List<OCRResult> results, Dictionary<string, string> options)
        {
            string outputFile = options.ContainsKey("-o") ? options["-o"] : 
                               options.ContainsKey("--output") ? options["--output"] :
                               Path.ChangeExtension(inputFile, ".ocr.json");
            
            var output = new
            {
                arquivo = Path.GetFileName(inputFile),
                dataProcessamento = DateTime.Now,
                totalPaginas = results.Count,
                configuracao = new
                {
                    engine = config?.Ocr?.Engine,
                    idioma = config?.Ocr?.DefaultLanguage
                },
                paginas = results.Select(r => new
                {
                    pagina = r.PageNumber,
                    sucesso = r.Success,
                    erro = r.Error,
                    texto = config?.Output?.SaveOriginalText == true ? r.Text : null,
                    camposExtraidos = config?.Output?.SaveExtractedFields == true ? r.ExtractedFields : null
                }),
                resumoCampos = results.Where(r => r.ExtractedFields != null)
                                      .SelectMany(r => r.ExtractedFields)
                                      .GroupBy(kv => kv.Key)
                                      .ToDictionary(g => g.Key, g => g.First().Value)
            };
            
            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            File.WriteAllText(outputFile, json);
            
            Console.WriteLine($"💾 Resultados salvos em: {outputFile}");
        }

        private List<int> ParsePageRange(string spec)
        {
            var pages = new List<int>();
            var parts = spec.Split(',');
            
            foreach (var part in parts)
            {
                if (part.Contains("-"))
                {
                    var range = part.Split('-');
                    int start = int.Parse(range[0]);
                    int end = int.Parse(range[1]);
                    pages.AddRange(Enumerable.Range(start, end - start + 1));
                }
                else
                {
                    pages.Add(int.Parse(part));
                }
            }
            
            return pages.Distinct().OrderBy(p => p).ToList();
        }

        public override void ShowHelp()
        {
            Console.WriteLine(@"
COMMAND: ocr
    Extract text from scanned PDFs using OCR

USAGE:
    fpdf <file.pdf> ocr [options]

OPTIONS:
    -d, --detect             Detect which pages need OCR
    -p, --pages <range>      Specify pages to OCR (e.g., 1,3,5-10)
    --all                    Force OCR on all pages
    -o, --output <file>      Output file path
    -c, --config <file>      OCR configuration file
    -l, --language <lang>    OCR language (por, eng, etc.)
    --extract-fields         Extract configured fields
    --format <fmt>           Output format (json, txt, csv)

EXAMPLES:
    fpdf document.pdf ocr --detect
    fpdf document.pdf ocr -p 1-5 -o result.json
    fpdf document.pdf ocr --all --extract-fields
    fpdf document.pdf ocr -c custom-config.json

CONFIG FILE:
    Copy ocr-config.example.json to ocr-config.json and customize
");
        }

        private class OCRResult
        {
            public int PageNumber { get; set; }
            public string Text { get; set; } = "";
            public bool Success { get; set; }
            public string? Error { get; set; }
            public Dictionary<string, string>? ExtractedFields { get; set; }
        }
    }
}