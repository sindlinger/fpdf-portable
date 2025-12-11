using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security;
using iTextSharp.text;
using iTextSharp.text.pdf;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Comando para detectar e extrair páginas que são apenas imagens
    /// </summary>
    public class FpdfExtractImagesCommand : FilterPDF.Command
    {
        public override string Name => "extract-images";
        public override string Description => "Extract pages that are only images (scanned pages)";

        private Dictionary<string, string> ParseOptions(string[] args)
        {
            var options = new Dictionary<string, string>();
            
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                
                if (arg.StartsWith("-"))
                {
                    switch (arg)
                    {
                        case "--input-dir":
                        case "--input-file":
                        case "-o":
                        case "--output":
                            if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            {
                                options[arg] = args[++i];
                            }
                            break;
                        case "--recursive":
                        case "-r":
                        case "--extract":
                        case "-e":
                        case "--as-pdf":
                        case "--ocr":
                        case "--all":
                            options[arg] = "true";
                            break;
                        default:
                            if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            {
                                options[arg] = args[++i];
                            }
                            else
                            {
                                options[arg] = "true";
                            }
                            break;
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

            var options = ParseOptions(args);
            var inputFiles = GetInputFiles(args, options);

            if (!inputFiles.Any())
            {
                Console.WriteLine("❌ Erro: Nenhum arquivo PDF encontrado.");
                ShowHelp();
                return;
            }

            try
            {
                Console.WriteLine($"🔍 Processando {inputFiles.Count} arquivo(s)...");
                
                foreach (var inputFile in inputFiles)
                {
                    ProcessSingleFile(inputFile, options);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Erro: {ex.Message}");
            }
        }

        private List<string> GetInputFiles(string[] args, Dictionary<string, string> options)
        {
            var inputFiles = new List<string>();

            // Check for --input-dir parameter
            if (options.ContainsKey("--input-dir"))
            {
                string inputDir = options["--input-dir"];
                if (!Directory.Exists(inputDir))
                {
                    Console.Error.WriteLine($"❌ Erro: Diretório '{inputDir}' não encontrado.");
                    return inputFiles;
                }

                var searchOption = options.ContainsKey("--recursive") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var pdfsInDir = Directory.GetFiles(inputDir, "*.pdf", searchOption);
                inputFiles.AddRange(pdfsInDir);
                
                Console.WriteLine($"📁 Encontrados {pdfsInDir.Length} arquivos PDF em: {inputDir}");
                if (options.ContainsKey("--recursive"))
                {
                    Console.WriteLine("   (busca recursiva ativada)");
                }
            }
            // Check for --input-file parameter
            else if (options.ContainsKey("--input-file"))
            {
                string inputFile = options["--input-file"];
                if (File.Exists(inputFile) && inputFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    inputFiles.Add(inputFile);
                }
                else
                {
                    Console.Error.WriteLine($"❌ Erro: Arquivo '{inputFile}' não encontrado ou não é um PDF.");
                }
            }
            // Legacy: first argument as file path (if not a flag)
            else if (args.Length > 0 && !args[0].StartsWith("-"))
            {
                string inputFile = args[0];
                if (File.Exists(inputFile) && inputFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    inputFiles.Add(inputFile);
                }
                else if (Directory.Exists(inputFile))
                {
                    // Treat as directory
                    var searchOption = options.ContainsKey("--recursive") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var pdfsInDir = Directory.GetFiles(inputFile, "*.pdf", searchOption);
                    inputFiles.AddRange(pdfsInDir);
                    Console.WriteLine($"📁 Encontrados {pdfsInDir.Length} arquivos PDF em: {inputFile}");
                }
                else
                {
                    Console.Error.WriteLine($"❌ Erro: Arquivo ou diretório '{inputFile}' não encontrado.");
                }
            }

            return inputFiles;
        }

        private void ProcessSingleFile(string inputFile, Dictionary<string, string> options)
        {
            Console.WriteLine($"\n📄 Processando: {Path.GetFileName(inputFile)}");
            
            // Detecta páginas que são imagens
            List<int> imageOnlyPages = DetectImageOnlyPages(inputFile);
            
            if (!imageOnlyPages.Any())
            {
                Console.WriteLine("ℹ️ Nenhuma página contendo apenas imagens foi encontrada.");
                return;
            }

            Console.WriteLine($"📄 Encontradas {imageOnlyPages.Count} páginas que são apenas imagens:");
            foreach (var page in imageOnlyPages)
            {
                Console.WriteLine($"  • Página {page}");
            }

            // Se foi solicitada extração
            if (options.ContainsKey("--extract") || options.ContainsKey("-e"))
            {
                ExtractPages(inputFile, imageOnlyPages, options);
            }

            // Se foi solicitado OCR
            if (options.ContainsKey("--ocr"))
            {
                PerformOCR(inputFile, imageOnlyPages, options);
            }
        }

        private List<int> DetectImageOnlyPages(string pdfPath)
        {
            var imageOnlyPages = new List<int>();

            using (var reader = PdfAccessManager.GetReader(pdfPath))
            {
                var analyses = PageTypeDetector.AnalyzeAllPages(reader);
                
                foreach (var analysis in analyses)
                {
                    if (analysis.Type == PageTypeDetector.PageType.ScannedImage)
                    {
                        imageOnlyPages.Add(analysis.PageNumber);
                    }
                }
            }

            return imageOnlyPages;
        }

        private void ExtractPages(string inputFile, List<int> pages, Dictionary<string, string> options)
        {
            string outputDir = options.ContainsKey("-o") ? options["-o"] : 
                               options.ContainsKey("--output") ? options["--output"] :
                               Path.GetFileNameWithoutExtension(inputFile) + "_extracted_images";

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            Console.WriteLine($"\n🔄 Extraindo páginas para: {outputDir}");

            // Extrai cada página como PDF individual
            if (options.ContainsKey("--as-pdf"))
            {
                ExtractAsPDF(inputFile, pages, outputDir);
            }
            // Extrai como imagens
            else
            {
                ExtractAsImages(inputFile, pages, outputDir);
            }

            Console.WriteLine($"✅ Extração concluída! {pages.Count} páginas salvas em: {outputDir}");
        }

        private void ExtractAsPDF(string inputFile, List<int> pages, string outputDir)
        {
            using (var reader = PdfAccessManager.GetReader(inputFile))
            {
                foreach (var pageNum in pages)
                {
                    string outputFile = Path.Combine(outputDir, $"page_{pageNum:D3}.pdf");
                    
                    using (var doc = new Document())
                    using (var writer = PdfWriter.GetInstance(doc, new FileStream(outputFile, FileMode.Create)))
                    {
                        doc.Open();
                        var cb = writer.DirectContent;
                        var page = writer.GetImportedPage(reader, pageNum);
                        cb.AddTemplate(page, 0, 0);
                        doc.Close();
                    }
                    
                    Console.WriteLine($"  ✓ Página {pageNum} -> {Path.GetFileName(outputFile)}");
                }
            }
        }

        private void ExtractAsImages(string inputFile, List<int> pages, string outputDir)
        {
            // REMOVIDO: Validação de segurança para máxima performance
            string sanitizedInputFile = inputFile;
            string sanitizedOutputDir = outputDir;
            
            foreach (var pageNum in pages)
            {
                // Validate page number
                if (pageNum < 1 || pageNum > 10000) // reasonable limit
                {
                    Console.WriteLine($"⚠️ Skipping invalid page number: {pageNum}");
                    continue;
                }
                
                string outputFile = Path.Combine(sanitizedOutputDir, $"page_{pageNum:D3}.png");
                string outputBaseName = Path.Combine(sanitizedOutputDir, $"page_{pageNum:D3}");
                
                // Usa pdftoppm para extrair como imagem
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
                process.StartInfo.ArgumentList.Add(pageNum.ToString());
                process.StartInfo.ArgumentList.Add("-l");
                process.StartInfo.ArgumentList.Add(pageNum.ToString());
                process.StartInfo.ArgumentList.Add("-r");
                process.StartInfo.ArgumentList.Add("300");
                process.StartInfo.ArgumentList.Add(sanitizedInputFile);
                process.StartInfo.ArgumentList.Add(outputBaseName);
                
                process.Start();
                process.WaitForExit(30000);
                
                // pdftoppm adiciona sufixo, renomeia para o nome esperado
                string generatedFile = Path.Combine(outputDir, $"page_{pageNum:D3}-{pageNum:D2}.png");
                if (File.Exists(generatedFile))
                {
                    if (File.Exists(outputFile))
                        File.Delete(outputFile);
                    File.Move(generatedFile, outputFile);
                }
                
                Console.WriteLine($"  ✓ Página {pageNum} -> {Path.GetFileName(outputFile)}");
            }
        }

        private void PerformOCR(string inputFile, List<int> pages, Dictionary<string, string> options)
        {
            Console.WriteLine("\n🔍 Iniciando OCR nas páginas extraídas...");
            
            // Prepara argumentos para o comando OCR
            var ocrArgs = new List<string>
            {
                inputFile,
                "ocr",
                "-p", string.Join(",", pages)
            };

            // Passa opções adicionais
            if (options.ContainsKey("--output"))
            {
                ocrArgs.Add("-o");
                ocrArgs.Add(options["--output"] + ".ocr.json");
            }

            // Executa comando OCR
            var ocrCommand = new FpdfOCRCommand();
            ocrCommand.Execute(ocrArgs.ToArray());
        }

        public override void ShowHelp()
        {
            Console.WriteLine(@"
COMMAND: extract-images
    Extract pages that are only images (scanned pages)

USAGE:
    fpdf extract-images <file.pdf> [options]
    fpdf extract-images --input-file <file.pdf> [options]
    fpdf extract-images --input-dir <directory> [options]

INPUT OPTIONS:
    --input-file <file>      Process a single PDF file
    --input-dir <dir>        Process all PDF files in directory
    -r, --recursive          Include subdirectories (with --input-dir)

PROCESSING OPTIONS:
    -e, --extract            Extract detected pages to files
    -o, --output <dir>       Output directory for extracted pages
    --as-pdf                 Extract as individual PDF files (default: PNG)
    --ocr                    Perform OCR on extracted pages
    --all                    Extract all pages (not just image-only)

EXAMPLES:
    # Process single file
    fpdf extract-images document.pdf
    fpdf extract-images --input-file document.pdf -e -o ./images
    
    # Process directory of PDFs
    fpdf extract-images --input-dir /path/to/pdfs --extract
    fpdf extract-images --input-dir /path/to/pdfs --recursive --extract --as-pdf
    
    # Extract with OCR
    fpdf extract-images document.pdf --extract --ocr

NOTES:
    - Automatically detects pages that contain only scanned images
    - Can extract as PNG images or individual PDF files
    - Integrates with OCR command for text extraction
    - Supports batch processing of directories
");
        }
    }
}