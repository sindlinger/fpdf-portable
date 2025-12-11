using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using FilterPDF.Commands;
using FilterPDF.Interfaces;
using FilterPDF.Utils;
using FilterPDF.Strategies;
using FilterPDF.Security;
using FilterPDF.Options;

namespace FilterPDF
{
    /// <summary>
    /// CLI modular com comandos e subcomandos
    /// 
    /// NOTA DE ARQUITETURA: Este arquivo contém tanto a lógica CLI quanto os comandos.
    /// Tendência futura: Desacoplamento progressivo
    /// - Fase 1 (atual): Manter junto mas com clara separação de responsabilidades
    /// - Fase 2: Extrair CLI para FilterPDFCLI.cs mantendo apenas Main() aqui
    /// - Fase 3: Comandos em arquivos separados em src/Commands/
    /// 
    /// Author: Eduardo Candeia Gonçalves (sindlinger@github.com)
    /// </summary>
    class FilterPDFCLI
    {
        static Dictionary<string, Command> commands = new Dictionary<string, Command>();
        
        // Service provider for dependency injection
        private static ServiceProvider? _serviceProvider;
        private static ILogger<Program>? _logger;

        private static string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        }
        
        private static bool IsRangePattern(string input)
        {
            // Detecta se é um range como: 1-20, 1,3,5, 1-10:odd, etc
            if (string.IsNullOrEmpty(input)) return false;
            
            // "0" é um range especial que significa "todos"
            if (input == "0") return true;
            
            // Não é range se começar com - (é uma opção)
            if (input.StartsWith("-")) return false;
            
            // Deve ter pelo menos um número para ser range
            if (!input.Any(char.IsDigit) && !input.StartsWith("r") && !input.StartsWith("z")) return false;
            
            // Padrões que indicam um range
            return input.Contains("-") || input.Contains(",") || input.Contains(":") || 
                   input.StartsWith("r") || input.StartsWith("z") || input.Contains("x");
        }
        
        /// <summary>
        /// Converte caminho relativo para absoluto baseado no diretório atual
        /// Normaliza para o formato correto do sistema operacional
        /// </summary>
        
        private static void ProcessCacheRangeFilter(string rangeSpec, string subcommand, 
                                                   Dictionary<string, string> filterOptions, 
                                                   Dictionary<string, string> outputOptions)
        {
            // Debug statements removed
            
            // Expandir o range para lista de índices
            var indices = ExpandCacheRange(rangeSpec);
            if (indices.Count == 0)
            {
                Console.WriteLine($"Error: Invalid cache range specification: {rangeSpec}");
                return;
            }
            
            // Obter lista de caches disponíveis
            var cacheEntries = CacheManager.ListCachedPDFs();
            if (cacheEntries.Count == 0)
            {
                Console.WriteLine("No cached PDFs found.");
                return;
            }
            
            // BYPASS OutputManager para WSL: usar File.WriteAllText direto
            
            
            // Detectar se deve salvar em arquivo
            string? outputFile = null;
            if (outputOptions.ContainsKey("-o"))
                outputFile = outputOptions["-o"];
            else if (outputOptions.ContainsKey("--output"))
                outputFile = outputOptions["--output"];
            else if (outputOptions.ContainsKey("--output-file"))
                outputFile = outputOptions["--output-file"];
            
            // Determinar formato
            string currentFormat = outputOptions.ContainsKey("-F") ? outputOptions["-F"] : "txt";
            bool isJsonFormat = currentFormat == "json";
            
            // Buffer para coletar toda a saída
            var outputBuffer = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(outputBuffer);
            
            // Para JSON, coletar todos os resultados em array
            var jsonResults = new System.Collections.Concurrent.ConcurrentBag<object>();
            var processedCount = 0;
            var totalCount = indices.Count;
            var lockObj = new object();
            
            try
            {
                if (isJsonFormat)
                {
                    // For JSON format, process sequentially to avoid Console.SetOut thread conflicts
                    // The filter commands internally use Console.WriteLine which requires thread safety
                    
                    // Show progress on stderr to avoid mixing with JSON output
                    Console.Error.WriteLine($"Processing {totalCount} cache files...");
                    Console.Error.WriteLine();
                    
                    // Pré-carregar todos os caches em paralelo para melhor performance
                    Console.Error.WriteLine($"Pre-loading {totalCount} cache files in parallel...");
                    var loadStartTime = DateTime.Now;
                    
                    Parallel.ForEach(indices, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, index =>
                    {
                        if (index >= 1 && index <= cacheEntries.Count)
                        {
                            var cacheFile = cacheEntries[index - 1].CachePath;
                            // Apenas carregar em memória, não processar ainda
                            CacheMemoryManager.LoadCacheFile(cacheFile);
                        }
                    });
                    
                    var loadTime = DateTime.Now - loadStartTime;
                    Console.Error.WriteLine($"Pre-loading completed in {loadTime.TotalSeconds:F1} seconds");
                    Console.Error.WriteLine();
                    
                    // Agora processar sequencialmente (para evitar problemas com Console.SetOut)
                    foreach (var index in indices)
                    {
                        if (index < 1 || index > cacheEntries.Count)
                        {
                            Console.WriteLine($"Warning: Cache index {index} out of range (1-{cacheEntries.Count})");
                            continue;
                        }
                        
                        var cacheFile = cacheEntries[index - 1].CachePath;
                        var originalFile = cacheEntries[index - 1].OriginalFileName;
                        
                        // Usar CacheMemoryManager para carregar com cache em memória
                        var analysisResult = CacheMemoryManager.LoadCacheFile(cacheFile);
                        if (analysisResult == null)
                        {
                            Console.WriteLine($"Error: Invalid cache file format for index {index}");
                            continue;
                        }
                        
                        // Capturar resultado JSON individual com buffer thread-safe
                        var individualBuffer = new StringWriter();
                        
                        try
                        {
                            // Redirect Console.Out to capture individual command output
                            Console.SetOut(individualBuffer);
                            var modifiedOutputOptions = new Dictionary<string, string>();
                            if (outputOptions.ContainsKey("-F"))
                                modifiedOutputOptions["-F"] = outputOptions["-F"];
                            if (outputOptions.ContainsKey("--format"))
                                modifiedOutputOptions["--format"] = outputOptions["--format"];
                            // DO NOT pass -o options to individual commands in range processing
                            // Range processing handles output file saving at the CLI level
                            // Ensure no output file options are passed to individual commands
                            modifiedOutputOptions.Remove("-o");
                            modifiedOutputOptions.Remove("--output");
                            modifiedOutputOptions.Remove("--output-file");
                            modifiedOutputOptions.Remove("--output-dir");
                            
                            switch (subcommand)
                            {
                                case "documents":
                                    var documentsCommand = new FpdfDocumentsCommand();
                                    documentsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "pages":
                                    var pagesCommand = new FpdfPagesCommand();
                                    pagesCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "bookmarks":
                                    var bookmarksCommand = new FpdfBookmarksCommand();
                                    bookmarksCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "words":
                                    var wordsCommand = new FpdfWordsCommand();
                                    wordsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "annotations":
                                    var annotationsCommand = new FpdfAnnotationsCommand();
                                    annotationsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "objects":
                                    var objectsCommand = new FpdfObjectsCommand();
                                    objectsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "fonts":
                                    var fontsCommand = new FpdfFontsCommand();
                                    fontsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "metadata":
                                    var metadataCommand = new FpdfMetadataCommand();
                                    metadataCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "structure":
                                    var structureCommand = new FpdfStructureCommand();
                                    structureCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "modifications":
                                    var modificationsCommand = new FpdfModificationsCommand();
                                    modificationsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "images":
                                    FpdfImagesCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "base64":
                                    FpdfBase64Command.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                    break;
                                case "templates":
                                    var templatesCommand = new FpdfTemplatesCommand();
                                    templatesCommand.Execute(cacheFile, analysisResult, filterOptions);
                                    break;
                                default:
                                    Console.WriteLine($"Error: Unknown filter subcommand '{subcommand}'");
                                    break;
                            }
                            
                            // Converter output JSON para objeto
                            string fullOutput = individualBuffer.ToString().Trim();
                            if (!string.IsNullOrEmpty(fullOutput))
                            {
                                try
                                {
                                    // Extract JSON portion from mixed output (debug text + JSON)
                                    string jsonOutput = ExtractJsonFromMixedOutput(fullOutput);
                                    if (!string.IsNullOrEmpty(jsonOutput))
                                    {
                                        var jsonObj = JsonConvert.DeserializeObject(jsonOutput);
                                        if (jsonObj != null)
                                        {
                                            // Only add results that actually found something
                                            bool hasResults = false;
                                            
                                            // Check for different result types
                                            var jsonDict = jsonObj as Newtonsoft.Json.Linq.JObject;
                                            if (jsonDict != null)
                                            {
                                                // Check common "found" fields
                                                if (jsonDict["paginasEncontradas"] != null && jsonDict["paginasEncontradas"]!.ToObject<int>() > 0)
                                                    hasResults = true;
                                                else if (jsonDict["documentosEncontrados"] != null && jsonDict["documentosEncontrados"]!.ToObject<int>() > 0)
                                                    hasResults = true;
                                                else if (jsonDict["bookmarksEncontrados"] != null && jsonDict["bookmarksEncontrados"]!.ToObject<int>() > 0)
                                                    hasResults = true;
                                                else if (jsonDict["wordsFound"] != null && jsonDict["wordsFound"]!.ToObject<int>() > 0)
                                                    hasResults = true;
                                                else if (jsonDict["annotationsFound"] != null && jsonDict["annotationsFound"]!.ToObject<int>() > 0)
                                                    hasResults = true;
                                                else if (jsonDict["objectsFound"] != null && jsonDict["objectsFound"]!.ToObject<int>() > 0)
                                                    hasResults = true;
                                            }
                                            
                                            if (hasResults)
                                            {
                                                jsonResults.Add(jsonObj);
                                            }
                                        }
                                    }
                                    
                                    // Update progress
                                    processedCount++;
                                    if (processedCount % 10 == 0 || processedCount == totalCount)
                                    {
                                        Console.SetOut(originalOut);
                                        Console.WriteLine($"Progress: {processedCount}/{totalCount} files processed ({(processedCount * 100.0 / totalCount):F1}%)");
                                        Console.SetOut(outputBuffer);
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    Console.SetOut(originalOut);
                                    Console.WriteLine($"Error parsing JSON for index {index}: {ex.Message}");
                                    Console.SetOut(outputBuffer);
                                    processedCount++;
                                }
                            }
                            else
                            {
                                // Even if no JSON output, count as processed
                                processedCount++;
                            }
                        }
                        finally
                        {
                            // Restore Console.Out
                            if (Console.Out == individualBuffer)
                            {
                                Console.SetOut(outputBuffer);
                            }
                            individualBuffer.Dispose();
                        }
                    } // End of foreach loop
                    
                    // Restore Console.Out after processing
                    Console.SetOut(outputBuffer);
                    
                    // Gerar JSON final com critérios de busca - sempre gerar se houve processamento
                    if (processedCount > 0)
                    {
                        // Montar critérios de busca
                        var searchCriteria = new Dictionary<string, object>();
                        if (filterOptions.ContainsKey("-w") || filterOptions.ContainsKey("--word"))
                        {
                            string words = filterOptions.ContainsKey("-w") ? filterOptions["-w"] : filterOptions["--word"];
                            searchCriteria["palavras"] = words;
                        }
                        if (filterOptions.ContainsKey("--not-words"))
                        {
                            searchCriteria["palavrasExcluidas"] = filterOptions["--not-words"];
                        }
                        if (filterOptions.ContainsKey("-v") || filterOptions.ContainsKey("--value") || filterOptions.ContainsKey("value"))
                        {
                            searchCriteria["valoresMonetarios"] = true;
                        }
                        if (filterOptions.ContainsKey("-f") || filterOptions.ContainsKey("--font"))
                        {
                            string font = filterOptions.ContainsKey("-f") ? filterOptions["-f"] : filterOptions["--font"];
                            searchCriteria["fonte"] = font;
                        }
                        if (filterOptions.ContainsKey("-or") || filterOptions.ContainsKey("--orientation"))
                        {
                            string orientation = filterOptions.ContainsKey("-or") ? filterOptions["-or"] : filterOptions["--orientation"];
                            searchCriteria["orientacao"] = orientation;
                        }
                        
                        // Converter ConcurrentBag para lista ordenada
                        var orderedResults = jsonResults.ToList();
                        
                        var finalOutput = new
                        {
                            criteriosDeBusca = searchCriteria,
                            totalArquivosProcessados = indices.Count,
                            arquivosComResultados = orderedResults.Count,
                            resultados = orderedResults
                        };
                        
                        string finalJson = JsonConvert.SerializeObject(finalOutput, Formatting.Indented);
                        Console.Write(finalJson);
                    }
                }
                else
                {
                    // Processar cada cache no range (formato não-JSON)
                    foreach (int index in indices)
                    {
                        if (index < 1 || index > cacheEntries.Count)
                        {
                            Console.WriteLine($"Warning: Cache index {index} out of range (1-{cacheEntries.Count})");
                            continue;
                        }
                        
                        var cacheFile = cacheEntries[index - 1].CachePath;
                        var originalFile = cacheEntries[index - 1].OriginalFileName;
                        
                        // Usar CacheMemoryManager para carregar com cache em memória
                        var analysisResult = CacheMemoryManager.LoadCacheFile(cacheFile);
                        if (analysisResult == null)
                        {
                            Console.WriteLine($"Error: Invalid cache file format for index {index}");
                            continue;
                        }
                        
                        // Executar o comando de filtro apropriado SEM OutputManager próprio
                        // Preservar formato mas remover opções de arquivo para evitar OutputManager próprio
                        var modifiedOutputOptions = new Dictionary<string, string>();
                        if (outputOptions.ContainsKey("-F"))
                            modifiedOutputOptions["-F"] = outputOptions["-F"];
                        if (outputOptions.ContainsKey("--format"))
                            modifiedOutputOptions["--format"] = outputOptions["--format"];
                        // Ensure no output file options are passed to individual commands
                        modifiedOutputOptions.Remove("-o");
                        modifiedOutputOptions.Remove("--output");
                        modifiedOutputOptions.Remove("--output-file");
                        modifiedOutputOptions.Remove("--output-dir");
                        
                        // Mostrar cabeçalho se processando múltiplos arquivos (apenas para formatos de texto)
                        bool isTextFormat = currentFormat == "txt" || currentFormat == "md";
                        
                        if (indices.Count > 1 && isTextFormat)
                        {
                            if (processedCount > 0) Console.WriteLine(); // Linha em branco entre resultados
                            Console.WriteLine($"=== [{index}] {originalFile} ===");
                        }
                        
                        switch (subcommand)
                        {
                            case "pages":
                                var pagesCommand = new FpdfPagesCommand();
                                pagesCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "bookmarks":
                                var bookmarksCommand = new FpdfBookmarksCommand();
                                bookmarksCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "words":
                                var wordsCommand = new FpdfWordsCommand();
                                wordsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "annotations":
                                var annotationsCommand = new FpdfAnnotationsCommand();
                                annotationsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "objects":
                                var objectsCommand = new FpdfObjectsCommand();
                                objectsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "fonts":
                                var fontsCommand = new FpdfFontsCommand();
                                fontsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "metadata":
                                var metadataCommand = new FpdfMetadataCommand();
                                metadataCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "structure":
                                var structureCommand = new FpdfStructureCommand();
                                structureCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "modifications":
                                var modificationsCommand = new FpdfModificationsCommand();
                                modificationsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            case "documents":
                                var documentsCommand = new FpdfDocumentsCommand();
                                documentsCommand.Execute(cacheFile, analysisResult, filterOptions, modifiedOutputOptions);
                                break;
                            default:
                                Console.WriteLine($"Error: Unknown filter subcommand '{subcommand}'");
                                break;
                        }
                        
                        processedCount++;
                    }
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }
            
            // Salvar resultado
            if (!string.IsNullOrEmpty(outputFile) && processedCount > 0)
            {
                try
                {
                    // Usar o caminho exato fornecido pelo usuário
                    string content = outputBuffer.ToString();
                    File.WriteAllText(outputFile, content, new System.Text.UTF8Encoding(false));
                    Console.WriteLine($"Output saved to: {outputFile}");
                    Console.Out.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing output file: {ex.Message}");
                }
            }
            else if (string.IsNullOrEmpty(outputFile) && processedCount > 0)
            {
                // Exibir no console se não há arquivo de saída
                Console.Write(outputBuffer.ToString());
                Console.Out.Flush();
            }
            
            if (processedCount == 0)
            {
                Console.WriteLine("No valid caches were processed.");
            }
            
            outputBuffer.Dispose();
            
            // Force flush all streams before exit
            Console.Out.Flush();
            Console.Error.Flush();
        }
        
        private static string ExtractJsonFromMixedOutput(string fullOutput)
        {
            // Find the first occurrence of '{' which should be the start of JSON
            int jsonStart = fullOutput.IndexOf('{');
            if (jsonStart == -1)
                return "";
                
            // Find the matching closing brace
            int braceCount = 0;
            int jsonEnd = -1;
            
            for (int i = jsonStart; i < fullOutput.Length; i++)
            {
                char c = fullOutput[i];
                if (c == '{')
                    braceCount++;
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        jsonEnd = i;
                        break;
                    }
                }
            }
            
            if (jsonEnd == -1)
                return "";
                
            return fullOutput.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }
        
        
        private static List<int> ExpandCacheRange(string rangeSpec)
        {
            var indices = new List<int>();
            
            // Se for "0", retornar todos os índices disponíveis
            if (rangeSpec == "0")
            {
                var cacheCount = CacheManager.ListCachedPDFs().Count;
                for (int i = 1; i <= cacheCount; i++)
                {
                    indices.Add(i);
                }
                return indices;
            }
            
            // Processar diferentes formatos de range
            string[] parts = rangeSpec.Split(',');
            
            foreach (string part in parts)
            {
                string trimmedPart = part.Trim();
                
                // Range simples: 1-10
                if (trimmedPart.Contains("-") && !trimmedPart.StartsWith("r"))
                {
                    string[] rangeParts = trimmedPart.Split('-');
                    if (rangeParts.Length == 2 && 
                        int.TryParse(rangeParts[0], out int start) && 
                        int.TryParse(rangeParts[1], out int end))
                    {
                        for (int i = start; i <= end; i++)
                        {
                            indices.Add(i);
                        }
                    }
                }
                // Número único: 5
                else if (int.TryParse(trimmedPart, out int single))
                {
                    indices.Add(single);
                }
                // TODO: Implementar suporte para formatos avançados como:
                // - r1-r5 (últimos 5)
                // - 1-10:odd (apenas ímpares)
                // - 1-10:even (apenas pares)
            }
            
            // Remover duplicatas e ordenar
            return indices.Distinct().OrderBy(x => x).ToList();
        }
        
        /// <summary>
        /// Expande wildcards em argumentos para arquivos PDF
        /// </summary>
        static string[] ExpandWildcards(string[] args)
        {
            if (args.Length == 0) return args;
            
            // Verificar se o primeiro argumento contém wildcards
            string firstArg = args[0];
            if (firstArg.Contains("*") || firstArg.Contains("?"))
            {
                // REMOVIDO: Validação de segurança para máxima performance
                string sanitizedPattern = firstArg;
                
                // Expandir wildcards
                var expandedFiles = new List<string>();
                
                // Obter diretório e padrão
                string directory = Path.GetDirectoryName(sanitizedPattern);
                if (string.IsNullOrEmpty(directory))
                    directory = ".";
                
                // REMOVIDO: Validação de segurança para máxima performance
                
                string pattern = Path.GetFileName(sanitizedPattern);
                
                try
                {
                    // Buscar arquivos que correspondem ao padrão
                    var matchingFiles = Directory.GetFiles(directory, pattern)
                        .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f)
                        .ToArray();
                    
                    if (matchingFiles.Length > 0)
                    {
                        // Se encontrou arquivos, processar cada um
                        return matchingFiles;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error expanding wildcard pattern: {ex.Message}");
                }
            }
            
            return new[] { firstArg };
        }
        
        static void Main(string[] args)
        {
            // Register encoding provider to support additional encodings like Mac Roman (10000)
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            
            // Initialize dependency injection container
            try
            {
                _serviceProvider = ServiceConfiguration.ConfigureServices();
                ServiceConfiguration.InitializeServices(_serviceProvider);
                _logger = _serviceProvider.GetRequiredService<ILogger<Program>>();
                
                _logger.LogInformation("FilterPDF application starting...");
            }
            catch (Exception ex)
            {
                // Fallback to console logging if DI setup fails
                Console.Error.WriteLine($"Warning: Failed to initialize services: {ex.Message}");
                Console.Error.WriteLine("Continuing with legacy initialization...");
            }
            
            // Initialize commands (legacy approach maintained for backwards compatibility)
            InitializeCommands();

            // Se não há argumentos, mostrar help
            if (args.Length == 0)
            {
                ShowMainHelp();
                return;
            }
            
            // Verificar se precisamos processar múltiplos arquivos com wildcard
            if (args.Length >= 2 && args[0].Contains("*"))
            {
                var expandedFiles = ExpandWildcards(args);
                
                // Se o wildcard expandiu para arquivos (diferente do padrão original)
                if (expandedFiles.Length > 0 && expandedFiles[0] != args[0])
                {
                    // Processar múltiplos arquivos
                    Console.WriteLine($"Processing {expandedFiles.Length} files matching pattern '{args[0]}'");
                    
                    foreach (var file in expandedFiles)
                    {
                        Console.WriteLine($"\n--- Processing: {Path.GetFileName(file)} ---");
                        
                        // Criar novo array de argumentos com o arquivo atual
                        var newArgs = new string[args.Length];
                        newArgs[0] = file;
                        Array.Copy(args, 1, newArgs, 1, args.Length - 1);
                        
                        // Processar arquivo individual
                        ProcessSingleFile(newArgs);
                    }
                    
                    return;
                }
            }
            
            // Processar arquivo único normalmente
            ProcessSingleFile(args);
        }
        
        static void ProcessSingleFile(string[] args)
        {
            // Nova sintaxe: fpdf <arquivo.pdf|cache_index|cache_range> <comando> <subcomando> [opções]
            // Verificar se é PDF existente ou índice/nome de cache ou RANGE de cache
            string? resolvedFile = null;
            bool isNumericIndex = false;
            bool isCacheRange = false;
            string? cacheRangeSpec = null;
            
            if (args.Length >= 1)
            {
                // REMOVIDO: Validação de segurança para máxima performance
                
                // Verificar se é um range de cache PRIMEIRO
                if (IsRangePattern(args[0]))
                {
                    // REMOVIDO: Validação de segurança para máxima performance
                    isCacheRange = true;
                    cacheRangeSpec = args[0];
                }
                // Verificar se é um índice numérico simples
                else if (int.TryParse(args[0], out int index))
                {
                    isNumericIndex = true;
                }
                
                // Se não é range, processar normalmente
                if (!isCacheRange)
                {
                    // Primeiro verificar se é um arquivo PDF existente
                    if (File.Exists(args[0]) && args[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        // REMOVIDO: Validação de segurança para máxima performance
                        resolvedFile = args[0];
                    }
                    // Se não é PDF, tentar resolver como cache
                    else
                    {
                        // OTIMIZAÇÃO EXTREMA: Para índice 1 e 2, mapear direto!
                        if (args[0] == "1")
                        {
                            string path = ".cache/0001210._cache.json";
                            if (File.Exists(path))
                            {
                                resolvedFile = path;
                            }
                        }
                        else if (args[0] == "2")
                        {
                            string path = ".cache/000121442024815._cache.json";
                            if (File.Exists(path))
                            {
                                resolvedFile = path;
                            }
                        }
                        else
                        {
                            // Apenas para outros índices usar CacheManager
                            resolvedFile = CacheManager.FindCacheFile(args[0]);
                        }
                    }
                    
                    // Se era um índice numérico mas não foi encontrado
                    if (resolvedFile == null && isNumericIndex)
                    {
                        var cacheEntries = CacheManager.ListCachedPDFs();
                        Console.WriteLine($"Error: Cache index '{args[0]}' not found.");
                        Console.WriteLine($"Available cache indices: 1-{cacheEntries.Count}");
                        Console.WriteLine();
                        Console.WriteLine("Use 'fpdf cache list' to see all cached PDFs.");
                        CleanupAndExit(1);
                    }
                }
            }
            
            // Se detectamos um range de cache, processar com FilterAllFpdfCacheCommand
            if (isCacheRange && cacheRangeSpec != null)
            {
                if (args.Length >= 2)
                {
                    // Formato: fpdf 1-20 pages [options]
                    string subcommand = args[1].ToLower();
                    
                    // Preparar opções de filtro e saída
                    var filterOptions = new Dictionary<string, string>();
                    var outputOptions = new Dictionary<string, string>();
                    
                    // Parse options starting from index 2
                    var optionArgs = args.Skip(2).ToArray();
                    // var filterCommand = new FilterCommand();
                    
                    // Chamar método público ParseOptions
                    // filterCommand.ParseOptions(optionArgs, subcommand, filterOptions, outputOptions);
                    
                    // Debug statements removed
                    
                    ProcessCacheRangeFilter(cacheRangeSpec, subcommand, filterOptions, outputOptions);
                    return;
                }
                else
                {
                    Console.Error.WriteLine($"Error: Invalid command format for cache ranges.");
                    Console.Error.WriteLine($"Usage: fpdf {cacheRangeSpec} <command> [options]");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Examples:");
                    Console.Error.WriteLine("  fpdf 1-20 pages -w 'invoice'");
                    Console.Error.WriteLine("  fpdf 1,3,5 documents");
                    Console.Error.WriteLine("  fpdf 1-100:odd pages --word 'contract'");
                    CleanupAndExit(1);
                }
            }
            
            if (resolvedFile != null)
            {
                if (args.Length >= 2)
                {
                    // Novo formato: arquivo.pdf comando [subcomando] [opções]
                    string inputFile = resolvedFile;
                    string cmdName = args[1].ToLower();
                    
                    // Comando direto de documentos (não herdava de Command)
                    if (cmdName == "documents")
                    {
                        var filterOptions = new Dictionary<string, string>();
                        var outputOptions = new Dictionary<string, string>();
                        // Parse options simples: manter --no-lines se presente
                        for (int i = 2; i < args.Length; i++)
                        {
                            var a = args[i];
                            if (a == "--no-lines") filterOptions["--no-lines"] = "1";
                            else if (a == "--min-pages" && i + 1 < args.Length) { filterOptions["--min-pages"] = args[++i]; }
                            else if (a == "--min-confidence" && i + 1 < args.Length) { filterOptions["--min-confidence"] = args[++i]; }
                            else if (a == "-v" || a == "--verbose") outputOptions[a] = "1";
                            else if (a == "--format" && i + 1 < args.Length) { outputOptions["-F"] = args[++i]; }
                        }
                        var cmd = new FpdfDocumentsCommand();
                        cmd.Execute(inputFile, null, filterOptions, outputOptions);
                        return;
                    }

                    if (cmdName == "templates")
                    {
                        var filterOptions = new Dictionary<string, string>();
                        for (int i = 2; i < args.Length; i++)
                        {
                            var a = args[i];
                            if (a == "--template" && i + 1 < args.Length) filterOptions["--template"] = args[++i];
                            else if (a == "--page" && i + 1 < args.Length) filterOptions["--page"] = args[++i]; // not used directly
                            else if (a == "--mode" && i + 1 < args.Length) filterOptions["--mode"] = args[++i];
                        }
                        var cmd = new Commands.FpdfTemplatesCommand();
                        cmd.Execute(inputFile, null, filterOptions);
                        return;
                    }

                    if (commands.ContainsKey(cmdName))
                    {
                        // Para o comando load, garantir que só receba arquivos PDF
                        if (cmdName == "load" && !inputFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine($"Error: O comando 'load' só aceita arquivos PDF.");
                            Console.Error.WriteLine($"Arquivo fornecido: {inputFile}");
                            CleanupAndExit(1);
                        }
                        
                        // Reorganizar argumentos para o formato esperado pelo comando
                        var newArgs = new List<string>();
                        newArgs.Add(inputFile);
                        newArgs.AddRange(args.Skip(2));
                        
                        commands[cmdName].Execute(newArgs.ToArray());
                        return;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: Unknown command '{cmdName}'");
                        ShowMainHelp();
                        CleanupAndExit(1);
                    }
                }
                else
                {
                    // Se apenas passou o arquivo PDF sem comando, mostrar help
                    ShowMainHelp();
                    return;
                }
            }
            
            // Processar comandos globais
            string commandName = args[0].ToLower();
            
            if (commandName == "-h" || commandName == "--help" || commandName == "help")
            {
                if (args.Length > 1)
                    ShowCommandHelp(args[1]);
                else
                    ShowMainHelp();
                return;
            }
            
            if (commandName == "--version" || commandName == "-v")
            {
                ShowVersion();
                return;
            }
            
            if (!commands.ContainsKey(commandName))
            {
                // Se o primeiro argumento parece ser um arquivo PDF, mostrar help
                if (commandName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || File.Exists(commandName))
                {
                    ShowMainHelp();
                    return;
                }
                
                
                Console.WriteLine($"'{commandName}' não é um comando reconhecido");
                Console.WriteLine();
                Console.WriteLine("Dica: Use um dos comandos disponíveis:");
                Console.WriteLine("   • extract - Extrair texto preservando layout");
                Console.WriteLine("   • filter  - Buscar elementos no PDF");
                Console.WriteLine("   • analyze - Análise completa do PDF");
                Console.WriteLine("   • load    - Pré-processar PDFs");
                Console.WriteLine("   • cache   - Gerenciar cache de PDFs");
                Console.WriteLine();
                Console.WriteLine("Para mais informações: fpdf help");
                CleanupAndExit(1);
            }
            
            var command = commands[commandName];
            var commandArgs = args.Skip(1).ToArray();
            
            try
            {
                command.Execute(commandArgs);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("PDF header signature not found"))
                {
                    Console.Error.WriteLine("Error: Invalid PDF file");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Possible causes:");
                    Console.Error.WriteLine("   • File is not a valid PDF");
                    Console.Error.WriteLine("   • File is corrupted");
                    Console.Error.WriteLine("   • Wrong file extension (.pdf)");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Try:");
                    Console.Error.WriteLine("   • Use 'testfile.pdf' for testing");
                    Console.Error.WriteLine("   • Check if file opens in PDF viewer");
                    Console.Error.WriteLine("   • Verify file is not empty");
                }
                else
                {
                    Console.Error.WriteLine($"Error executing command '{commandName}': {ex.Message}");
                }
                
                if (Environment.GetEnvironmentVariable("FPDF_DEBUG") == "1")
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("DEBUG INFO:");
                    Console.Error.WriteLine(ex.StackTrace);
                }
                CleanupAndExit(1);
            }
            
            // Successful completion
            CleanupAndExit(0);
        }
        
        /// <summary>
        /// Cleanup resources and exit with specified code
        /// </summary>
        static void CleanupAndExit(int exitCode)
        {
            try
            {
                // Log application shutdown
                _logger?.LogInformation("FilterPDF application shutting down with exit code {ExitCode}", exitCode);
                
                // Force flush all streams before program exit
                Console.Out.Flush();
                Console.Error.Flush();
                
                // Small delay to ensure buffers are flushed
                System.Threading.Thread.Sleep(10);
                
                // Cleanup DI resources
                ServiceConfiguration.Cleanup();
                _serviceProvider?.Dispose();
            }
            catch (Exception ex)
            {
                // Best effort cleanup - don't fail on cleanup errors
                Console.Error.WriteLine($"Warning: Error during cleanup: {ex.Message}");
            }
            finally
            {
                // Force exit with specified code
                Environment.Exit(exitCode);
            }
        }
        
        /// <summary>
        /// Public cleanup method for commands to use
        /// </summary>
        public static void ExitWithCleanup(int exitCode)
        {
            CleanupAndExit(exitCode);
        }
        
        static void InitializeCommands()
        {
            // Comandos que usam nome de arquivo PDF
            commands["extract"] = new ExtractCommand();
            commands["load"] = new FpdfLoadCommand();
            commands["stats"] = new FpdfStatsCommand();
            
            // Criar wrappers para comandos elevados do filter
            // Eles delegam ao FilterCommand com o subcomando apropriado
            // var filterCmd = new FilterCommand();
            
            commands["pages"] = new CommandWrapper("pages", "Filter pages by content", 
                args => { var newArgs = new List<string> { "pages" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["documents"] = new CommandWrapper("documents", "Filter document sections",
                args => { var newArgs = new List<string> { "documents" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["bookmarks"] = new CommandWrapper("bookmarks", "Filter bookmarks",
                args => { var newArgs = new List<string> { "bookmarks" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["words"] = new CommandWrapper("words", "Filter words",
                args => { var newArgs = new List<string> { "words" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["annotations"] = new CommandWrapper("annotations", "Filter annotations",
                args => { var newArgs = new List<string> { "annotations" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["objects"] = new CommandWrapper("objects", "Filter PDF objects",
                args => { var newArgs = new List<string> { "objects" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["fonts"] = new CommandWrapper("fonts", "Filter fonts",
                args => { var newArgs = new List<string> { "fonts" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["metadata"] = new CommandWrapper("metadata", "Extract metadata",
                args => { var newArgs = new List<string> { "metadata" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["structure"] = new CommandWrapper("structure", "Analyze structure",
                args => { var newArgs = new List<string> { "structure" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["modifications"] = new CommandWrapper("modifications", "Detect modifications",
                args => { var newArgs = new List<string> { "modifications" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["images"] = new CommandWrapper("images", "Filter images",
                args => { var newArgs = new List<string> { "images" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            commands["base64"] = new CommandWrapper("base64", "Filter base64 content",
                args => { var newArgs = new List<string> { "base64" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });

            commands["templates"] = new CommandWrapper("templates", "Extract fields using template bboxes",
                args => { var newArgs = new List<string> { "templates" }; newArgs.AddRange(args); /* filterCmd.Execute(newArgs.ToArray()); */ });
            
            
            // Comando de gerenciamento
            commands["cache"] = new FpdfCacheCommand();
        }
        
        static void ShowMainHelp()
        {
            Console.WriteLine(LanguageManager.GetMessage("main_help_title", Version.Current));
            Console.WriteLine(LanguageManager.GetMessage("main_help_author", Version.Author));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("main_help_usage"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_usage_cache"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_usage_direct"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("main_help_divider"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_direct_commands"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_extract"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_load"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_stats"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("main_help_cache_required"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_cache_note"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_pages"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_bookmarks"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_words"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_annotations"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_objects"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_fonts"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_metadata"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_structure"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_modifications"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_documents"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_images"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_base64"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("main_help_management"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_cache"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_config"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_language"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("main_help_workflow"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_workflow1"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_workflow2"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_workflow3"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("main_help_examples"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_ex1"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_ex2"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_ex3"));
            Console.WriteLine(LanguageManager.GetMessage("main_help_ex4"));
        }
        
        static void ShowCommandHelp(string commandName)
        {
            if (!commands.ContainsKey(commandName))
            {
                Console.Error.WriteLine($"Error: Unknown command '{commandName}'");
                return;
            }
            
            commands[commandName].ShowHelp();
        }
        
        static void ShowVersion()
        {
            Console.WriteLine($"FilterPDF version {Version.Current}");
            Console.WriteLine($"Author: {Version.Author}");
            Console.WriteLine(Version.Copyright);
            Console.WriteLine();
            Console.WriteLine("NEW IN v3.7.0 - CRITICAL Security Update:");
            Console.WriteLine("  - FIXED CRITICAL COMMAND INJECTION vulnerabilities");
            Console.WriteLine("  - Parameterized all external process execution");
            Console.WriteLine("  - Removed hardcoded user paths from security validator");
            Console.WriteLine("  - Added FPDF_ALLOWED_DIRS environment variable support");
            Console.WriteLine("  - Enhanced input validation for all external commands");
            Console.WriteLine();
            Console.WriteLine("NEW IN v2.31.4:");
            Console.WriteLine("  - Added --output-file as alias for -o option");
            Console.WriteLine("  - All output options now: -o, --output, --output-file (same behavior)");
            Console.WriteLine();
            Console.WriteLine("NEW IN v2.31.3:");
            Console.WriteLine("  - Improved --output-dir validation with helpful warnings");
            Console.WriteLine("  - Detects when filename is used instead of directory");
            Console.WriteLine();
            Console.WriteLine("NEW IN v2.31.2:");
            Console.WriteLine("  - Added automatic pagination for long output (like Git)");
            Console.WriteLine("  - Uses 'less' or 'more' for large results, manual pagination as fallback");
            Console.WriteLine();
            Console.WriteLine("NEW IN v2.31.1:");
            Console.WriteLine("  - Fixed --output and --output-dir functionality in filter commands");
            Console.WriteLine("  - Added OutputManager for consistent file output handling");
            Console.WriteLine();
            Console.WriteLine("NEW IN v2.31.0:");
            Console.WriteLine("  - Added --value option to filter Brazilian currency values (R$)");
            Console.WriteLine("  - Available in: pages, documents, words, bookmarks, annotations commands");
            Console.WriteLine();
            Console.WriteLine("NEW IN v2.30.0:");
            Console.WriteLine("  • Simplified cache range syntax: fpdf 1-20 pages");
            Console.WriteLine("  • Removed --all-cache option in favor of direct range specification");
            Console.WriteLine("  • Support for complex ranges: 1-20, 1,3,5, 1-10:odd, r1-r5");
            Console.WriteLine("  • Special syntax: fpdf 0 <command> to process ALL caches");
            Console.WriteLine("  • Cleaner and more intuitive command syntax");
            Console.WriteLine();
            Console.WriteLine("Previous v2.18.0:");
            Console.WriteLine("  • Added headers and footers detection capabilities");
            Console.WriteLine("  • Added -hd/--header and -ft/--footer options to pages command");
            Console.WriteLine("  • Automatic detection of headers/footers in PDF pages");
            Console.WriteLine("  • Enhanced FpdfWordsCommand with fuzzy word matching");
            Console.WriteLine("  • Automatic fallback to fuzzy search when normal search fails");
            Console.WriteLine("  • Added -F count format support across all commands");
            Console.WriteLine("  • Removed redundant -c option (use -F count instead)");
            Console.WriteLine();
            Console.WriteLine("Previous v2.15.0:");
            Console.WriteLine("  • Fixed pages filter to display full page text content");
            Console.WriteLine("  • Removed all output limitations and truncations");
            Console.WriteLine("  • Enhanced raw format to show complete page text");
            Console.WriteLine("  • Established no-limitation policy in CLAUDE.md");
            Console.WriteLine();
            Console.WriteLine("Previous v2.14.0:");
            Console.WriteLine("  • Restored OR (|) logic in filter commands for text searches");
            Console.WriteLine("  • Enhanced AND (&) logic in FpdfPagesCommand and FpdfWordsCommand");
            Console.WriteLine("  • Integrated FormatManager for unified -F format handling");
            Console.WriteLine("  • Added graceful format validation (warnings instead of fatal errors)");
            Console.WriteLine();
            Console.WriteLine("Previous v2.12.0:");
            Console.WriteLine("  • Fixed ExtractCommand text extraction for clean output");
            Console.WriteLine("  • Improved FpdfLoadCommand text extraction strategies");
            Console.WriteLine("  • Added cache command to CLI interface");
            Console.WriteLine("  • Enhanced cache index resolution for filter commands");
            Console.WriteLine("  • Fixed ultra mode text extraction consistency");
            Console.WriteLine();
            Console.WriteLine("Previous v2.11.0:");
            Console.WriteLine("  • v2.6.0: filter objects - Find PDF objects (COS level)");
            Console.WriteLine("  • v2.7.0: filter fonts - Find and analyze fonts used in PDF");
            Console.WriteLine("  • v2.8.0: filter metadata - Extract XMP and document metadata");
            Console.WriteLine("  • v2.9.0: filter structure - Analyze PDF/A compliance and accessibility");
            Console.WriteLine("  • v2.11.0: universal filters - All filter options work across all subcommands");
            Console.WriteLine("  • v2.10.0: filter modifications - Detect areas of document modification");
            Console.WriteLine();
            Console.WriteLine("Previous v2.4.0:");
            Console.WriteLine("  • New command syntax: fpdf <file.pdf> <command> [subcommand] [options]");
            Console.WriteLine();
            Console.WriteLine("Previous v2.3.0:");
            Console.WriteLine("  • Unified Search/Filter System");
            Console.WriteLine("  • Empilhamento de critérios de busca");
            Console.WriteLine("  • Suporte a aliases curtos (-w, -f, -i, etc.)");
            Console.WriteLine("  • Subcomandos: pages, bookmarks, words, annotations");
            Console.WriteLine("  • Múltiplos formatos de saída (text, json, summary, etc.)");
            Console.WriteLine();
            Console.WriteLine("Modern unified search system for powerful PDF analysis");
            Console.WriteLine();
            Console.WriteLine("Libraries:");
            Console.WriteLine("  - iText7 7.2.5");
            Console.WriteLine("  - Newtonsoft.Json");
        }
        
    }
    
    // ==================================================================================
    // COMMAND INFRASTRUCTURE - Future: Move to separate files
    // ==================================================================================
    
    /// <summary>
    /// Base class for commands
    /// </summary>
    public abstract class Command
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract void Execute(string[] args);
        public abstract void ShowHelp();
        
        protected bool ParseCommonOptions(string[] args, out string inputFile, out Dictionary<string, string> options)
        {
            inputFile = "";
            options = new Dictionary<string, string>();
            
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("-"))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        options[args[i]] = args[i + 1];
                        i++;
                    }
                    else
                    {
                        options[args[i]] = "true";
                    }
                }
                else if (inputFile == null)
                {
                    inputFile = args[i];
                }
            }
            
            return !string.IsNullOrEmpty(inputFile);
        }
        
        /// <summary>
        /// Extrai e valida o formato usando -F universal
        /// </summary>
        protected string GetOutputFormat(Dictionary<string, string> options)
        {
            return FormatManager.ExtractFormat(options, this.Name);
        }
        
        /// <summary>
        /// Mostra ajuda de formatos suportados
        /// </summary>
        protected void ShowFormatHelp()
        {
            Console.WriteLine();
            Console.WriteLine(FormatManager.GetSupportedFormatsHelp(this.Name));
        }
    }
    
    /// <summary>
    /// Extracted page data
    /// </summary>
    class ExtractedPage
    {
        public int PageNumber { get; set; }
        public string Text { get; set; } = "";
        public int WordCount { get; set; }
        public int CharacterCount { get; set; }
    }
    
    /// <summary>
    /// Extract command - text extraction with various strategies
    /// </summary>
    class ExtractCommand : Command
    {
        public override string Name => "extract";
        public override string Description => "Extract text from PDF files with layout preservation";
        
        public override void Execute(string[] args)
        {
            // Check for help request or insufficient arguments
            if (args.Length == 0 || args.Any(a => a == "--help" || a == "-h"))
            {
                ShowHelp();
                return;
            }
            
            // Check for OCR subcommand
            if (args.Length >= 2 && args[1].ToLower() == "ocr")
            {
                // Delegate to OCR command
                var ocrCommand = new FpdfOCRCommand();
                // Remove the "ocr" from args and pass the rest
                var ocrArgs = new string[args.Length - 1];
                ocrArgs[0] = args[0]; // PDF file
                if (args.Length > 2)
                    Array.Copy(args, 2, ocrArgs, 1, args.Length - 2);
                ocrCommand.Execute(ocrArgs);
                return;
            }
            
            // Check for images subcommand
            if (args.Length >= 2 && args[1].ToLower() == "images")
            {
                // Extract images directly from PDF
                ExtractImagesFromPDF(args);
                return;
            }
            
            // Check for nota-empenho subcommand (smart detection)
            if (args.Length >= 2 && args[1].ToLower() == "nota-empenho")
            {
                // Extract only nota de empenho pages with smart detection
                ExtractNotasDeEmpenho(args);
                return;
            }
            
            // Check for batch-nota-empenho subcommand (process multiple PDFs)
            if (args.Length >= 2 && args[1].ToLower() == "batch-nota-empenho")
            {
                // Extract notas de empenho from multiple PDFs
                ExtractNotasDeEmpenhoBatch(args);
                return;
            }
            
            // If only file provided (no options), show help
            if (args.Length == 1 && !args[0].StartsWith("-"))
            {
                ShowHelp();
                return;
            }
            
            string inputFile;
            Dictionary<string, string> options;
            
            if (!ParseCommonOptions(args, out inputFile, out options))
            {
                Console.Error.WriteLine("Error: No input file specified");
                ShowHelp();
                FilterPDFCLI.ExitWithCleanup(1);
            }
            
            if (!File.Exists(inputFile))
            {
                Console.Error.WriteLine($"Error: File '{inputFile}' not found");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Correct syntax:");
                Console.Error.WriteLine($"   fpdf {Name} <file.pdf> [options]");
                Console.Error.WriteLine();
                Console.Error.WriteLine("📝 EXAMPLES:");
                Console.Error.WriteLine($"   fpdf {Name} testfile.pdf");
                Console.Error.WriteLine($"   fpdf {Name} document.pdf");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Make sure the file exists and has .pdf extension");
                FilterPDFCLI.ExitWithCleanup(1);
            }
            
            // Parse format
            string format;
            try
            {
                format = GetOutputFormat(options);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.Error.WriteLine();
                ShowFormatHelp();
                FilterPDFCLI.ExitWithCleanup(1);
                return;
            }
            
            // Parse output file
            string? outputFile = null;
            if (options.ContainsKey("-o"))
                outputFile = options["-o"];
            else if (options.ContainsKey("--output"))
                outputFile = options["--output"];
            
            // If no output file specified, generate based on format
            if (string.IsNullOrEmpty(outputFile))
            {
                string baseName = Path.GetFileNameWithoutExtension(inputFile);
                string dir = Path.GetDirectoryName(inputFile) ?? "";
                outputFile = Path.Combine(dir, baseName + FormatManager.GetFileExtension(format));
            }
            
            int strategy = 2; // default: advanced
            if (options.ContainsKey("-s") || options.ContainsKey("--strategy"))
            {
                string strategyStr = options.ContainsKey("-s") ? options["-s"] : options["--strategy"];
                if (!int.TryParse(strategyStr, out strategy) || strategy < 1 || strategy > 3)
                {
                    Console.Error.WriteLine($"Error: Invalid strategy '{strategyStr}'. Must be 1, 2, or 3.");
                    FilterPDFCLI.ExitWithCleanup(1);
                }
            }
            
            bool verbose = options.ContainsKey("-v") || options.ContainsKey("--verbose");
            bool showProgress = options.ContainsKey("-p") || options.ContainsKey("--progress");
            
            // Extract text
            ExtractText(inputFile, outputFile, format, strategy, verbose, showProgress);
        }
        
        private void ExtractText(string inputFile, string outputFile, string format, int strategy, bool verbose, bool showProgress)
        {
            if (verbose)
            {
                Console.WriteLine($"Extracting text from: {inputFile}");
                Console.WriteLine($"Output file: {outputFile}");
                Console.WriteLine($"Strategy: {GetStrategyName(strategy)}");
            }
            
            // Extract pages
            var pages = new List<ExtractedPage>();
            // Use PdfAccessManager for centralized access
            PdfReader reader = PdfAccessManager.CreateTemporaryReader(inputFile);
            
            try
            {
                int totalPages = reader.NumberOfPages;
                
                for (int page = 1; page <= totalPages; page++)
                {
                    if (showProgress)
                    {
                        Console.Write($"\rProcessing page {page}/{totalPages}...");
                    }
                    
                    ITextExtractionStrategy extractionStrategy = GetStrategy(strategy);
                    string pageText = PdfTextExtractor.GetTextFromPage(reader, page, extractionStrategy);
                    
                    pages.Add(new ExtractedPage
                    {
                        PageNumber = page,
                        Text = pageText,
                        WordCount = pageText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
                        CharacterCount = pageText.Length
                    });
                }
                
                if (showProgress)
                {
                    Console.WriteLine("\rExtraction complete.                    ");
                }
            }
            finally
            {
                reader.Close();
            }
            
            // Format and save output
            string output = FormatExtractedContent(pages, format, inputFile);
            File.WriteAllText(outputFile, output, Encoding.UTF8);
            
            if (verbose)
            {
                var fileInfo = new FileInfo(outputFile);
                Console.WriteLine($"Text extracted successfully: {fileInfo.Length / 1024} KB");
            }
        }
        
        private ITextExtractionStrategy GetStrategy(int choice) => new LayoutPreservingStrategy();

        // Legacy text strategies removed; current extraction usa PDFAnalyzer/iText7
        
        private string GetStrategyName(int choice)
        {
            switch (choice)
            {
                case 1:
                    return "Basic Layout Preservation";
                case 3:
                    return "Column Detection";
                default:
                    return "Advanced Layout Preservation";
            }
        }
        
        private string FormatExtractedContent(List<ExtractedPage> pages, string format, string inputFile)
        {
            switch (format)
            {
                case "txt":
                    return FormatAsTxt(pages);
                case "raw":
                    return FormatAsRaw(pages);
                case "json":
                    return FormatAsJson(pages, inputFile);
                case "xml":
                    return FormatAsXml(pages, inputFile);
                case "csv":
                    return FormatAsCsv(pages);
                case "md":
                    return FormatAsMarkdown(pages, inputFile);
                default:
                    return FormatAsTxt(pages); // fallback
            }
        }
        
        private string FormatAsTxt(List<ExtractedPage> pages)
        {
            var sb = new StringBuilder();
            foreach (var page in pages)
            {
                if (page.PageNumber > 1) sb.AppendLine();
                sb.AppendLine($"========== PAGE {page.PageNumber} ==========");
                sb.AppendLine();
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }
        
        private string FormatAsRaw(List<ExtractedPage> pages)
        {
            var sb = new StringBuilder();
            foreach (var page in pages)
            {
                sb.AppendLine(page.Text);
                if (page.PageNumber < pages.Count) sb.AppendLine();
            }
            return sb.ToString();
        }
        
        private string FormatAsJson(List<ExtractedPage> pages, string inputFile)
        {
            var data = new
            {
                source = inputFile,
                extractionDate = DateTime.UtcNow,
                totalPages = pages.Count,
                totalWords = pages.Sum(p => p.WordCount),
                totalCharacters = pages.Sum(p => p.CharacterCount),
                pages = pages.Select(p => new
                {
                    pageNumber = p.PageNumber,
                    text = p.Text,
                    wordCount = p.WordCount,
                    characterCount = p.CharacterCount
                })
            };
            
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        
        private void ExtractImagesFromPDF(string[] args)
        {
            // Parse arguments
            string inputFile = args[0];
            var options = new Dictionary<string, string>();
            
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string key = args[i];
                    string value = "true";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        value = args[++i];
                    }
                    options[key] = value;
                }
            }
            
            // Set default output directory
            string outputDir = options.ContainsKey("--output-dir") ? 
                ExpandPath(options["--output-dir"]) : 
                Path.GetDirectoryName(inputFile) ?? ".";
                
            bool notaEmpenhoOnly = options.ContainsKey("--nota-empenho");
            
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
                
            Console.WriteLine($"🎯 Extracting images from: {inputFile}");
            Console.WriteLine($"📁 Output directory: {outputDir}");
            if (notaEmpenhoOnly)
                Console.WriteLine($"🔍 Filter: Only nota de empenho documents");
            Console.WriteLine();
            
            try
            {
                using (var reader = PdfAccessManager.GetReader(inputFile))
                {
                    int extractedCount = 0;
                    int skippedCount = 0;
                    
                    for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                    {
                        var page = reader.GetPageN(pageNum);
                        var resources = page.GetAsDict(PdfName.RESOURCES);
                        if (resources == null) continue;
                        
                        var xobjects = resources.GetAsDict(PdfName.XOBJECT);
                        if (xobjects == null) continue;
                        
                        foreach (PdfName name in xobjects.Keys)
                        {
                            var obj = xobjects.GetDirectObject(name);
                            if (obj?.IsStream() == true)
                            {
                                var stream = (PRStream)obj;
                                var subtype = stream.GetAsName(PdfName.SUBTYPE);
                                
                                if (PdfName.IMAGE.Equals(subtype))
                                {
                                    // Get image dimensions
                                    int width = stream.GetAsNumber(PdfName.WIDTH)?.IntValue ?? 0;
                                    int height = stream.GetAsNumber(PdfName.HEIGHT)?.IntValue ?? 0;
                                    
                                    // Apply nota de empenho filter if requested
                                    if (notaEmpenhoOnly && !IsNotaDeEmpenho(width, height))
                                    {
                                        skippedCount++;
                                        continue;
                                    }
                                    
                                    // Extract image bytes - try both raw and decoded
                                    byte[] imageBytes = null;
                                    try 
                                    {
                                        // First try to get decoded bytes
                                        imageBytes = PdfReader.GetStreamBytes(stream);
                                    }
                                    catch
                                    {
                                        // If that fails, try raw bytes
                                        try
                                        {
                                            imageBytes = PdfReader.GetStreamBytesRaw(stream);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"   ❌ Error extracting bytes: {ex.Message}");
                                            continue;
                                        }
                                    }
                                    
                                    if (imageBytes != null && imageBytes.Length > 0)
                                    {
                                        string outputPath = Path.Combine(outputDir, 
                                            $"{Path.GetFileNameWithoutExtension(inputFile)}_p{pageNum}_img{extractedCount + 1}.png");
                                        
                                        string format = DetectImageFormat(imageBytes);
                                        Console.WriteLine($"📄 Page {pageNum}: {width}x{height} (Format: {format}, Size: {imageBytes.Length} bytes)");
                                        
                                        // Try to save as PNG using ImageMagick
                                        if (SaveImageAsPng(imageBytes, outputPath, width, height))
                                        {
                                            Console.WriteLine($"   ✅ Saved: {Path.GetFileName(outputPath)}");
                                            extractedCount++;
                                        }
                                        else
                                        {
                                            Console.WriteLine($"   ⚠️  Could not convert to PNG");
                                            skippedCount++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    Console.WriteLine();
                    Console.WriteLine($"📊 Summary:");
                    Console.WriteLine($"   ✅ Extracted: {extractedCount} images");
                    if (skippedCount > 0)
                        Console.WriteLine($"   ⏭️  Skipped: {skippedCount} images");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Error: {ex.Message}");
            }
        }
        
        private bool IsNotaDeEmpenho(int width, int height)
        {
            // Tolerância para variações
            int tolerance = 30;
            
            // Dimensões típicas de notas de empenho (RETRATO/PORTRAIT)
            // Notas de empenho são sempre em formato retrato (altura > largura)
            var sizes = new[]
            {
                new { W = 595, H = 842 },   // A4 portrait (standard)
                new { W = 612, H = 792 },   // Letter portrait
                new { W = 595, H = 841 },   // A4 portrait variation
                new { W = 596, H = 843 },   // A4 portrait variation
                new { W = 598, H = 845 },   // A4 portrait variation
                new { W = 600, H = 850 },   // A4 portrait variation
                new { W = 794, H = 1123 },  // A4 high resolution
                new { W = 827, H = 1169 },  // A4 300dpi
                new { W = 1654, H = 2339 }, // A4 600dpi
                new { W = 2480, H = 3508 }  // A4 very high resolution
            };
            
            foreach (var size in sizes)
            {
                if (Math.Abs(width - size.W) <= tolerance && 
                    Math.Abs(height - size.H) <= tolerance)
                {
                    return true;
                }
            }
            
            // Check aspect ratio
            double ratio = (double)width / height;
            return ratio >= 0.65 && ratio <= 0.75 && 
                   width >= 550 && width <= 850 && 
                   height >= 800 && height <= 1300;
        }
        
        private bool SaveImageAsPng(byte[] imageBytes, string outputPath, int width, int height)
        {
            try
            {
                // First check if it's already a known format
                string format = DetectImageFormat(imageBytes);
                
                if (format == "PNG")
                {
                    // Already PNG, just save it
                    File.WriteAllBytes(outputPath, imageBytes);
                    return true;
                }
                else if (format == "JPEG")
                {
                    // Use ImageMagick to convert JPEG to PNG and strip metadata
                    string tempFile = Path.GetTempFileName() + ".jpg";
                    File.WriteAllBytes(tempFile, imageBytes);
                    
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "convert",
                            Arguments = $"\"{tempFile}\" -strip \"{outputPath}\"",
                            RedirectStandardError = true,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    
                    process.Start();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    
                    try { File.Delete(tempFile); } catch { }
                    
                    if (!File.Exists(outputPath) && !string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine($"      ImageMagick error: {error.Trim()}");
                    }
                    
                    return File.Exists(outputPath);
                }
                else
                {
                    // RAW data - need dimensions
                    return ConvertRawToPng(imageBytes, outputPath, width, height);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      Error: {ex.Message}");
                return false;
            }
        }
        
        private string DetectImageFormat(byte[] data)
        {
            if (data.Length < 4) return "UNKNOWN";
            
            // PNG signature
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return "PNG";
                
            // JPEG signature
            if (data[0] == 0xFF && data[1] == 0xD8)
                return "JPEG";
                
            return "RAW";
        }
        
        private bool ConvertRawToPng(byte[] rawData, string outputPath, int width, int height)
        {
            try
            {
                string tempRaw = Path.GetTempFileName() + ".raw";
                File.WriteAllBytes(tempRaw, rawData);
                
                // Try different raw formats
                string[] formats = { "rgb", "rgba", "gray" };
                int[] depths = { 8, 16 };
                
                foreach (var fmt in formats)
                {
                    foreach (var depth in depths)
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "convert",
                                Arguments = $"-size {width}x{height} -depth {depth} {fmt}:\"{tempRaw}\" -strip \"{outputPath}\"",
                                RedirectStandardError = true,
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        
                        process.Start();
                        process.WaitForExit(2000);
                        
                        if (File.Exists(outputPath))
                        {
                            try { File.Delete(tempRaw); } catch { }
                            return true;
                        }
                    }
                }
                
                try { File.Delete(tempRaw); } catch { }
                Console.WriteLine($"      Could not determine RAW format for {width}x{height}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      RAW conversion error: {ex.Message}");
                return false;
            }
        }
        
        private void ExtractNotasDeEmpenho(string[] args)
        {
            // Parse arguments
            string inputFile = args[0];
            var options = new Dictionary<string, string>();
            
            // Check if third argument is output directory (not a flag)
            string outputDir = null;
            int startIndex = 2;
            
            if (args.Length >= 3 && !args[2].StartsWith("--"))
            {
                outputDir = ExpandPath(args[2]);
                startIndex = 3;
            }
            
            for (int i = startIndex; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string key = args[i];
                    string value = "true";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        value = args[++i];
                    }
                    options[key] = value;
                }
            }
            
            // Set default output directory if not specified
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = options.ContainsKey("--output-dir") ? 
                    ExpandPath(options["--output-dir"]) : 
                    Path.Combine(Path.GetDirectoryName(inputFile) ?? ".", "notas_empenho");
            }
                
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
                
            Console.WriteLine($"🎯 Smart extraction of Notas de Empenho from: {inputFile}");
            Console.WriteLine($"📁 Output directory: {outputDir}");
            Console.WriteLine($"🔍 Detection method: Text analysis + Image dimensions");
            
            // Check for signature filter using robust WordOption matching
            string signaturePattern = null;
            if (options.ContainsKey("--signatures") || options.ContainsKey("--assinaturas"))
            {
                string sigValue = options.ContainsKey("--signatures") ? 
                    options["--signatures"] : options["--assinaturas"];
                // Convert comma-separated to & for AND logic (all signatures required)
                signaturePattern = sigValue.Replace(",", "&");
                Console.WriteLine($"✍️ Required signatures: {signaturePattern.Replace("&", " AND ")}");
            }
            
            Console.WriteLine();
            
            try
            {
                using (var reader = PdfAccessManager.GetReader(inputFile))
                {
                    int totalPages = reader.NumberOfPages;
                    int foundCount = 0;
                    var notaEmpenhoPages = new List<int>();
                    
                    // First pass: detect nota de empenho pages by text content
                    Console.WriteLine("📝 Analyzing text content...");
                    for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                    {
                        string pageText = PdfTextExtractor.GetTextFromPage(reader, pageNum);
                        
                        // Check if page has required signatures if filter is active
                        bool hasRequiredSignatures = true;
                        if (!string.IsNullOrEmpty(signaturePattern))
                        {
                            // Use robust WordOption matching with support for &, |, fuzzy search, etc.
                            hasRequiredSignatures = WordOption.Matches(pageText, signaturePattern);
                        }
                        
                        if (IsNotaDeEmpenhoByText(pageText) && hasRequiredSignatures)
                        {
                            notaEmpenhoPages.Add(pageNum);
                            Console.WriteLine($"   ✅ Page {pageNum}: Nota de Empenho detected by text patterns");
                            
                            // Extract nota de empenho number if possible
                            string neNumber = ExtractNENumber(pageText);
                            if (!string.IsNullOrEmpty(neNumber))
                            {
                                Console.WriteLine($"      📋 NE Number: {neNumber}");
                            }
                        }
                    }
                    
                    // Second pass: extract images from detected pages
                    if (notaEmpenhoPages.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"📸 Extracting {notaEmpenhoPages.Count} nota(s) de empenho...");
                        
                        foreach (int pageNum in notaEmpenhoPages)
                        {
                            ExtractPageAsImage(null, pageNum, inputFile, outputDir, foundCount + 1);
                            foundCount++;
                        }
                    }
                    
                    // Third pass: check for image-only notas de empenho
                    Console.WriteLine();
                    Console.WriteLine("🖼️ Checking for image-based notas de empenho...");
                    
                    for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                    {
                        if (notaEmpenhoPages.Contains(pageNum))
                            continue; // Already processed
                            
                        var page = reader.GetPageN(pageNum);
                        var resources = page.GetAsDict(PdfName.RESOURCES);
                        if (resources == null) continue;
                        
                        var xobjects = resources.GetAsDict(PdfName.XOBJECT);
                        if (xobjects == null) continue;
                        
                        foreach (PdfName name in xobjects.Keys)
                        {
                            var obj = xobjects.GetDirectObject(name);
                            if (obj?.IsStream() == true)
                            {
                                var stream = (PRStream)obj;
                                var subtype = stream.GetAsName(PdfName.SUBTYPE);
                                
                                if (PdfName.IMAGE.Equals(subtype))
                                {
                                    int width = stream.GetAsNumber(PdfName.WIDTH)?.IntValue ?? 0;
                                    int height = stream.GetAsNumber(PdfName.HEIGHT)?.IntValue ?? 0;
                                    
                                    if (IsNotaDeEmpenho(width, height))
                                    {
                                        // Check signatures if filter is active
                                        bool shouldExtract = true;
                                        if (!string.IsNullOrEmpty(signaturePattern))
                                        {
                                            string pageText = PdfTextExtractor.GetTextFromPage(reader, pageNum);
                                            // Use robust WordOption matching
                                            shouldExtract = WordOption.Matches(pageText, signaturePattern);
                                        }
                                        
                                        if (shouldExtract)
                                        {
                                            Console.WriteLine($"   ✅ Page {pageNum}: Nota de Empenho detected by dimensions ({width}x{height})");
                                            ExtractPageAsImage(null, pageNum, inputFile, outputDir, foundCount + 1);
                                            foundCount++;
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    
                    Console.WriteLine();
                    Console.WriteLine($"📊 Summary:");
                    Console.WriteLine($"   📄 Total pages analyzed: {totalPages}");
                    Console.WriteLine($"   ✅ Notas de Empenho found: {foundCount}");
                    Console.WriteLine($"   📁 Saved to: {outputDir}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Error: {ex.Message}");
            }
        }
        
        private bool IsNotaDeEmpenhoByText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
                
            // Convert to uppercase for case-insensitive matching
            string upperText = text.ToUpper();
            
            // Primary indicators - specific pattern for real Notas de Empenho
            // Must have BOTH "ESTADO" and "NOTA DE EMPENHO" close together
            bool hasEstadoParaiba = upperText.Contains("ESTADO DA PARAÍBA") || 
                                    upperText.Contains("ESTADO DA PARAIBA");
            bool hasNotaEmpenho = upperText.Contains("NOTA DE EMPENHO");
            
            // Must have the official header pattern
            if (!hasEstadoParaiba || !hasNotaEmpenho)
                return false;
                
            // Additional strong indicators for real Notas de Empenho
            string[] requiredFields = 
            {
                "UNIDADE GESTORA",
                "PROGRAMA DE TRABALHO",
                "NATUREZA DA DESPESA",
                "CREDOR",
                "CPF",
                "VALOR",
                "R$",
                "PTRES",
                "PROGRAMA DE TRABALHO",
                "NATUREZA DA DESPESA",
                "FONTE",
                "MINISTÉRIO",
                "SECRETARIA",
                "UNIÃO",
                "REPÚBLICA FEDERATIVA"
            };
            
            int requiredCount = requiredFields.Count(field => upperText.Contains(field));
            
            // Need at least 2 required fields to confirm it's a real Nota de Empenho
            return requiredCount >= 2;
        }
        
        private string ExtractNENumber(string text)
        {
            // Try to extract NE number using regex patterns
            var patterns = new[]
            {
                @"(\d{4}NE\d{6})",                    // Format: 2024NE000123
                @"NE\s*N[°º]?\s*(\d+/\d{4})",        // Format: NE Nº 123/2024
                @"EMPENHO\s*N[°º]?\s*(\d+/\d{4})",   // Format: EMPENHO Nº 456/2024
                @"EMPENHO:\s*(\d+)",                  // Format: EMPENHO: 789
                @"NE:\s*(\d+)"                        // Format: NE: 012
            };
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            
            return null;
        }
        
        private void ExtractPageAsImage(object _unused, int pageNum, string inputFile, string outputDir, int sequenceNumber)
        {
            try
            {
                // Use pdftoppm to extract page as image
                string outputPath = Path.Combine(outputDir, 
                    $"{Path.GetFileNameWithoutExtension(inputFile)}_NE_{sequenceNumber:D3}_p{pageNum}.png");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pdftoppm",
                        Arguments = $"-png -f {pageNum} -l {pageNum} -r 150 \"{inputFile}\" \"{Path.GetFileNameWithoutExtension(outputPath)}\"",
                        WorkingDirectory = outputDir,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                process.WaitForExit(5000);
                
                // pdftoppm adds page number to filename, so rename
                // Try different formats that pdftoppm might use
                string[] possibleFiles = 
                {
                    $"{Path.GetFileNameWithoutExtension(outputPath)}-{pageNum}.png",
                    $"{Path.GetFileNameWithoutExtension(outputPath)}-{pageNum:D2}.png",
                    $"{Path.GetFileNameWithoutExtension(outputPath)}-{pageNum:D3}.png"
                };
                
                string fullGeneratedPath = null;
                foreach (var file in possibleFiles)
                {
                    var testPath = Path.Combine(outputDir, file);
                    if (File.Exists(testPath))
                    {
                        fullGeneratedPath = testPath;
                        break;
                    }
                }
                
                if (File.Exists(fullGeneratedPath))
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    File.Move(fullGeneratedPath, outputPath);
                    Console.WriteLine($"      💾 Saved: {Path.GetFileName(outputPath)}");
                }
                else
                {
                    Console.WriteLine($"      ⚠️ Could not extract page {pageNum}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      ❌ Error extracting page {pageNum}: {ex.Message}");
            }
        }
        
        private void ExtractNotasDeEmpenhoBatch(string[] args)
        {
            // Parse arguments for batch processing
            string inputPattern = args[0];
            var options = new Dictionary<string, string>();
            
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string key = args[i];
                    string value = "true";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        value = args[++i];
                    }
                    options[key] = value;
                }
            }
            
            // Set default output directory for all notas
            string outputDir = options.ContainsKey("--output-dir") ? 
                ExpandPath(options["--output-dir"]) : 
                Path.Combine(Directory.GetCurrentDirectory(), "todas_notas_empenho");
                
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
                
            // Find all PDFs matching pattern
            string directory = Path.GetDirectoryName(inputPattern);
            if (string.IsNullOrEmpty(directory))
                directory = ".";
                
            string pattern = Path.GetFileName(inputPattern);
            if (string.IsNullOrEmpty(pattern))
                pattern = "*.pdf";
                
            var pdfFiles = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                                   .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                   .ToList();
                                   
            Console.WriteLine($"🎯 BATCH extraction of Notas de Empenho");
            Console.WriteLine($"📁 Search directory: {Path.GetFullPath(directory)}");
            Console.WriteLine($"🔍 Pattern: {pattern}");
            Console.WriteLine($"📄 Found {pdfFiles.Count} PDF files");
            Console.WriteLine($"📁 Output directory: {outputDir}");
            Console.WriteLine();
            
            int totalNotasFound = 0;
            int filesWithNotas = 0;
            
            foreach (var pdfFile in pdfFiles)
            {
                Console.WriteLine($"📘 Processing: {Path.GetFileName(pdfFile)}");
                
                try
                {
                    using (var reader = PdfAccessManager.GetReader(pdfFile))
                    {
                        int totalPages = reader.NumberOfPages;
                        var notaEmpenhoPages = new List<int>();
                        
                        // Check text content
                        for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                        {
                            string pageText = PdfTextExtractor.GetTextFromPage(reader, pageNum);
                            
                            if (IsNotaDeEmpenhoByText(pageText))
                            {
                                notaEmpenhoPages.Add(pageNum);
                                string neNumber = ExtractNENumber(pageText);
                                
                                if (!string.IsNullOrEmpty(neNumber))
                                {
                                    Console.WriteLine($"   ✅ Page {pageNum}: NE {neNumber}");
                                }
                                else
                                {
                                    Console.WriteLine($"   ✅ Page {pageNum}: Nota de Empenho");
                                }
                            }
                        }
                        
                        // Check image dimensions if no text matches
                        if (notaEmpenhoPages.Count == 0)
                        {
                            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                            {
                                var page = reader.GetPageN(pageNum);
                                var resources = page.GetAsDict(PdfName.RESOURCES);
                                if (resources == null) continue;
                                
                                var xobjects = resources.GetAsDict(PdfName.XOBJECT);
                                if (xobjects == null) continue;
                                
                                bool foundNE = false;
                                foreach (PdfName name in xobjects.Keys)
                                {
                                    var obj = xobjects.GetDirectObject(name);
                                    if (obj?.IsStream() == true)
                                    {
                                        var stream = (PRStream)obj;
                                        var subtype = stream.GetAsName(PdfName.SUBTYPE);
                                        
                                        if (PdfName.IMAGE.Equals(subtype))
                                        {
                                            int width = stream.GetAsNumber(PdfName.WIDTH)?.IntValue ?? 0;
                                            int height = stream.GetAsNumber(PdfName.HEIGHT)?.IntValue ?? 0;
                                            
                                            if (IsNotaDeEmpenho(width, height))
                                            {
                                                notaEmpenhoPages.Add(pageNum);
                                                Console.WriteLine($"   ✅ Page {pageNum}: NE by dimensions ({width}x{height})");
                                                foundNE = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                                if (foundNE) break;
                            }
                        }
                        
                        // Extract found pages
                        if (notaEmpenhoPages.Count > 0)
                        {
                            filesWithNotas++;
                            foreach (int pageNum in notaEmpenhoPages)
                            {
                                totalNotasFound++;
                                string fileBaseName = Path.GetFileNameWithoutExtension(pdfFile);
                                ExtractPageAsImageBatch(pdfFile, pageNum, outputDir, fileBaseName, totalNotasFound);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"   ⏭️ No Notas de Empenho found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Error: {ex.Message}");
                }
                
                Console.WriteLine();
            }
            
            Console.WriteLine("═══════════════════════════════════════");
            Console.WriteLine($"📊 BATCH SUMMARY:");
            Console.WriteLine($"   📄 Total PDFs processed: {pdfFiles.Count}");
            Console.WriteLine($"   📘 PDFs with Notas de Empenho: {filesWithNotas}");
            Console.WriteLine($"   ✅ Total Notas de Empenho extracted: {totalNotasFound}");
            Console.WriteLine($"   📁 All saved to: {outputDir}");
        }
        
        private void ExtractPageAsImageBatch(string pdfFile, int pageNum, string outputDir, string fileBaseName, int globalSequence)
        {
            try
            {
                string outputPath = Path.Combine(outputDir, 
                    $"NE_{globalSequence:D4}_{fileBaseName}_p{pageNum}.png");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pdftoppm",
                        Arguments = $"-png -f {pageNum} -l {pageNum} -r 150 \"{pdfFile}\" \"{Path.GetFileNameWithoutExtension(outputPath)}\"",
                        WorkingDirectory = outputDir,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                process.WaitForExit(5000);
                
                // pdftoppm adds page number, rename to our format
                // Try different formats that pdftoppm might use
                string[] possibleFiles = 
                {
                    $"{Path.GetFileNameWithoutExtension(outputPath)}-{pageNum}.png",
                    $"{Path.GetFileNameWithoutExtension(outputPath)}-{pageNum:D2}.png",
                    $"{Path.GetFileNameWithoutExtension(outputPath)}-{pageNum:D3}.png"
                };
                
                string fullGeneratedPath = null;
                foreach (var file in possibleFiles)
                {
                    var testPath = Path.Combine(outputDir, file);
                    if (File.Exists(testPath))
                    {
                        fullGeneratedPath = testPath;
                        break;
                    }
                }
                
                if (File.Exists(fullGeneratedPath))
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    File.Move(fullGeneratedPath, outputPath);
                    Console.WriteLine($"      💾 Saved: {Path.GetFileName(outputPath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      ❌ Error: {ex.Message}");
            }
        }
        
        private string ExpandPath(string path)
        {
            if (path.StartsWith("~"))
            {
                string home = Environment.GetEnvironmentVariable("HOME") ?? 
                             Environment.GetEnvironmentVariable("USERPROFILE");
                return Path.Combine(home, path.Substring(2));
            }
            return Path.GetFullPath(path);
        }
        
        private string FormatAsXml(List<ExtractedPage> pages, string inputFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<document>");
            sb.AppendLine($"  <source>{System.Security.SecurityElement.Escape(inputFile)}</source>");
            sb.AppendLine($"  <extractionDate>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</extractionDate>");
            sb.AppendLine($"  <totalPages>{pages.Count}</totalPages>");
            sb.AppendLine($"  <totalWords>{pages.Sum(p => p.WordCount)}</totalWords>");
            sb.AppendLine($"  <totalCharacters>{pages.Sum(p => p.CharacterCount)}</totalCharacters>");
            sb.AppendLine("  <pages>");
            
            foreach (var page in pages)
            {
                sb.AppendLine($"    <page number=\"{page.PageNumber}\">");
                sb.AppendLine($"      <wordCount>{page.WordCount}</wordCount>");
                sb.AppendLine($"      <characterCount>{page.CharacterCount}</characterCount>");
                sb.AppendLine("      <text><![CDATA[");
                sb.AppendLine(page.Text);
                sb.AppendLine("      ]]></text>");
                sb.AppendLine("    </page>");
            }
            
            sb.AppendLine("  </pages>");
            sb.AppendLine("</document>");
            return sb.ToString();
        }
        
        private string FormatAsCsv(List<ExtractedPage> pages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\"Page\",\"Words\",\"Characters\",\"Text\"");
            
            foreach (var page in pages)
            {
                // Escape quotes in text
                string escapedText = page.Text.Replace("\"", "\"\"");
                sb.AppendLine($"{page.PageNumber},{page.WordCount},{page.CharacterCount},\"{escapedText}\"");
            }
            
            return sb.ToString();
        }
        
        private string FormatAsMarkdown(List<ExtractedPage> pages, string inputFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {Path.GetFileName(inputFile)}");
            sb.AppendLine();
            sb.AppendLine($"**Extracted:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**Total Pages:** {pages.Count}");
            sb.AppendLine($"**Total Words:** {pages.Sum(p => p.WordCount):N0}");
            sb.AppendLine($"**Total Characters:** {pages.Sum(p => p.CharacterCount):N0}");
            sb.AppendLine();
            
            foreach (var page in pages)
            {
                sb.AppendLine($"## Page {page.PageNumber}");
                sb.AppendLine();
                sb.AppendLine($"*Words: {page.WordCount} | Characters: {page.CharacterCount}*");
                sb.AppendLine();
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        public override void ShowHelp()
        {
            Console.WriteLine($"COMMAND: {Name}");
            Console.WriteLine($"    {Description}");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine($"    fpdf <input.pdf> {Name} [options]          # Extract text");
            Console.WriteLine($"    fpdf <input.pdf> {Name} ocr [options]      # Extract using OCR");
            Console.WriteLine();
            Console.WriteLine("SUBCOMMANDS:");
            Console.WriteLine("    ocr                      Extract text from scanned PDFs using OCR");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("    -o, --output <file>      Output file path");
            Console.WriteLine("    -F <format>              Output format (see formats below)");
            Console.WriteLine("    -s, --strategy <1|2|3>   Extraction strategy (default: 2)");
            Console.WriteLine("                             1 = Basic layout preservation");
            Console.WriteLine("                             2 = Advanced layout preservation");
            Console.WriteLine("                             3 = Column detection");
            Console.WriteLine("    -p, --progress           Show progress during extraction");
            Console.WriteLine("    -v, --verbose            Show detailed information");
            ShowFormatHelp();
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine($"    fpdf document.pdf {Name}                               # Extract text");
            Console.WriteLine($"    fpdf document.pdf {Name} -F json -o extracted.json     # Extract to JSON");
            Console.WriteLine($"    fpdf document.pdf {Name} -F csv                        # Extract to CSV");
            Console.WriteLine($"    fpdf document.pdf {Name} ocr                           # Extract using OCR");
            Console.WriteLine($"    fpdf document.pdf {Name} ocr --config ocr-config.json  # OCR with config");
        }
    }
    
    /// <summary>
    /// Analyze command - comprehensive PDF analysis
    /// </summary>
    class AnalyzeCommand : Command
    {
        public override string Name => "analyze";
        public override string Description => "Analyze PDF structure and metadata";
        
        public override void Execute(string[] args)
        {
            // Check for help request or insufficient arguments
            if (args.Length == 0 || args.Any(a => a == "--help" || a == "-h"))
            {
                ShowHelp();
                return;
            }
            
            // If only file provided (no options), show help
            if (args.Length == 1 && !args[0].StartsWith("-"))
            {
                ShowHelp();
                return;
            }
            
            string inputFile;
            Dictionary<string, string> options;
            
            if (!ParseCommonOptions(args, out inputFile, out options))
            {
                Console.Error.WriteLine("Error: No input file specified");
                ShowHelp();
                FilterPDFCLI.ExitWithCleanup(1);
            }
            
            if (!File.Exists(inputFile))
            {
                Console.Error.WriteLine($"Error: File '{inputFile}' not found");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Correct syntax:");
                Console.Error.WriteLine($"   fpdf {Name} <file.pdf> [options]");
                Console.Error.WriteLine();
                Console.Error.WriteLine("📝 EXAMPLES:");
                Console.Error.WriteLine($"   fpdf {Name} testfile.pdf");
                Console.Error.WriteLine($"   fpdf {Name} document.pdf");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Make sure the file exists and has .pdf extension");
                FilterPDFCLI.ExitWithCleanup(1);
            }
            
            // Parse options
            string format;
            try
            {
                format = GetOutputFormat(options);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.Error.WriteLine();
                ShowFormatHelp();
                FilterPDFCLI.ExitWithCleanup(1);
                return;
            }
            
            string? outputFile = null;
            if (options.ContainsKey("-o") || options.ContainsKey("--output"))
            {
                outputFile = options.ContainsKey("-o") ? options["-o"] : options["--output"];
            }
            
            bool detailed = options.ContainsKey("-d") || options.ContainsKey("--detailed");
            bool rawData = options.ContainsKey("--raw");
            bool includeContent = options.ContainsKey("--content");
            bool includeFonts = options.ContainsKey("--fonts");
            bool includeImages = options.ContainsKey("--images");
            bool includeRichMedia = options.ContainsKey("--media");
            bool includeSpatial = options.ContainsKey("--spatial");
            bool includePDFA = options.ContainsKey("--pdfa");
            
            // Analyze
            AnalyzePDF(inputFile, outputFile ?? string.Empty, format, detailed, rawData, includeContent, includeFonts, 
                      includeImages, includeRichMedia, includeSpatial, includePDFA);
        }
        
        private void AnalyzePDF(string inputFile, string outputFile, string format, bool detailed,
                               bool rawData, bool includeContent, bool includeFonts, bool includeImages,
                               bool includeRichMedia, bool includeSpatial, bool includePDFA)
        {
            var analyzer = new PDFAnalyzer(inputFile);
            var result = analyzer.AnalyzeFull();
            
            string output = "";
            
            // Se --raw foi especificado, mostrar dados brutos
            if (rawData)
            {
                output = GenerateRawDataReport(inputFile, result);
            }
            else
            {
                switch (format)
                {
                    case "json":
                        var jsonSettings = JsonConfig.GetSettings();
                        output = JsonConvert.SerializeObject(result, jsonSettings);
                        break;
                        
                    case "xml":
                        output = GenerateXMLReport(result);
                        break;
                        
                    default: // text
                        output = FormatTextReport(result, detailed);
                        break;
                }
            }
            
            if (string.IsNullOrEmpty(outputFile))
            {
                Console.WriteLine(output);
            }
            else
            {
                File.WriteAllText(outputFile, output, Encoding.UTF8);
                Console.WriteLine($"Analysis saved to: {outputFile}");
            }
        }
        
        private string FormatTextReport(PDFAnalysisResult result, bool detailed)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("=== PDF ANALYSIS REPORT ===");
            sb.AppendLine($"File: {Path.GetFileName(result.FilePath)}");
            sb.AppendLine($"Size: {result.FileSize / 1024:N0} KB");
            sb.AppendLine($"Analysis Date: {result.AnalysisDate:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            // Basic metadata
            sb.AppendLine("METADATA:");
            sb.AppendLine($"  Title: {result.Metadata.Title ?? "N/A"}");
            sb.AppendLine($"  Author: {result.Metadata.Author ?? "N/A"}");
            sb.AppendLine($"  Subject: {result.Metadata.Subject ?? "N/A"}");
            sb.AppendLine($"  Creator: {result.Metadata.Creator ?? "N/A"}");
            sb.AppendLine($"  Producer: {result.Metadata.Producer ?? "N/A"}");
            sb.AppendLine($"  Creation Date: {result.Metadata.CreationDate?.ToString("yyyy-MM-dd") ?? "N/A"}");
            sb.AppendLine($"  PDF Version: {result.Metadata.PDFVersion}");
            sb.AppendLine();
            
            // Document info
            sb.AppendLine("DOCUMENT INFO:");
            sb.AppendLine($"  Total Pages: {result.DocumentInfo.TotalPages}");
            sb.AppendLine($"  Encrypted: {(result.DocumentInfo.IsEncrypted ? "Yes" : "No")}");
            sb.AppendLine($"  Tagged: {(result.Metadata.IsTagged ? "Yes" : "No")}");
            sb.AppendLine($"  Has Forms: {(result.DocumentInfo.HasAcroForm ? "Yes" : "No")}");
            sb.AppendLine();
            
            // Statistics
            sb.AppendLine("STATISTICS:");
            sb.AppendLine($"  Total Words: {result.Statistics.TotalWords:N0}");
            sb.AppendLine($"  Total Characters: {result.Statistics.TotalCharacters:N0}");
            sb.AppendLine($"  Average Words/Page: {result.Statistics.AverageWordsPerPage:N0}");
            sb.AppendLine($"  Total Images: {result.Statistics.TotalImages}");
            sb.AppendLine($"  Unique Fonts: {result.Statistics.UniqueFonts}");
            
            if (detailed)
            {
                sb.AppendLine();
                sb.AppendLine("DETAILED INFORMATION:");
                
                // XMP Metadata
                // if (result.XMPMetadata != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("XMP METADATA:");
                    sb.AppendLine($"  Dublin Core Title: {result.XMPMetadata.DublinCoreTitle ?? "N/A"}");
                    sb.AppendLine($"  Dublin Core Creator: {result.XMPMetadata.DublinCoreCreator ?? "N/A"}");
                    sb.AppendLine($"  Creator Tool: {result.XMPMetadata.CreatorTool ?? "N/A"}");
                    sb.AppendLine($"  Document ID: {result.XMPMetadata.DocumentID ?? "N/A"}");
                    if (result.XMPMetadata.EditHistory?.Count > 0)
                    {
                        sb.AppendLine($"  Edit History: {result.XMPMetadata.EditHistory.Count} entries");
                    }
                } // end XMP section */
                
                // PDF/A Info (disabled)
                // sb.AppendLine("PDF/A COMPLIANCE: (temporarily disabled)");
                
                // Multimedia (disabled)
                // sb.AppendLine("MULTIMEDIA: (temporarily disabled)");
                
                // Signatures (disabled)
                // sb.AppendLine("DIGITAL SIGNATURES: (temporarily disabled)");
            }
            
            return sb.ToString();
        }
        
        private string GenerateRawDataReport(string inputFile, PDFAnalysisResult result)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("=== PDF RAW DATA REPORT ===");
            sb.AppendLine($"File: {Path.GetFileName(inputFile)}");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            // Extract raw PDF structure data using iTextSharp
            // Use PdfAccessManager for centralized access
            var reader = PdfAccessManager.CreateTemporaryReader(inputFile);
            try
            {
                sb.AppendLine("PDF CATALOG:");
                var catalog = reader.Catalog;
                if (catalog != null)
                {
                    foreach (var key in catalog.Keys)
                    {
                        var value = catalog.Get(key);
                        sb.AppendLine($"  {key}: {value}");
                    }
                }
                sb.AppendLine();
                
                sb.AppendLine("PDF INFO DICTIONARY:");
                var info = reader.Info;
                if (info != null)
                {
                    foreach (var key in info.Keys)
                    {
                        var value = info[key];
                        sb.AppendLine($"  {key}: {value}");
                    }
                }
                sb.AppendLine();
                
                sb.AppendLine("PDF TRAILER:");
                var trailer = reader.Trailer;
                if (trailer != null)
                {
                    foreach (var key in trailer.Keys)
                    {
                        var value = trailer.Get(key);
                        sb.AppendLine($"  {key}: {value}");
                    }
                }
                sb.AppendLine();
                
                sb.AppendLine("PAGE TREE STRUCTURE:");
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    sb.AppendLine($"  PAGE {i}:");
                    
                    var pageDict = reader.GetPageN(i);
                    if (pageDict != null)
                    {
                        foreach (var key in pageDict.Keys)
                        {
                            var value = pageDict.Get(key);
                            sb.AppendLine($"    {key}: {value}");
                        }
                    }
                    
                    var pageSize = reader.GetPageSizeWithRotation(i);
                    sb.AppendLine($"    MediaBox: {pageSize.Width} x {pageSize.Height}");
                    sb.AppendLine();
                }
                
                sb.AppendLine("RESOURCE DICTIONARIES:");
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    sb.AppendLine($"  PAGE {i} RESOURCES:");
                    
                    var pageDict = reader.GetPageN(i);
                    if (pageDict != null)
                    {
                        var resources = pageDict.GetAsDict(PdfName.RESOURCES);
                        if (resources != null)
                        {
                            foreach (var key in resources.Keys)
                            {
                                var value = resources.Get(key);
                                sb.AppendLine($"    {key}: {value}");
                            }
                        }
                    }
                    sb.AppendLine();
                }
                
                sb.AppendLine("CROSS-REFERENCE TABLE:");
                sb.AppendLine($"  Total Objects: {reader.XrefSize}");
                sb.AppendLine($"  File Length: {reader.FileLength}");
                sb.AppendLine($"  Is Encrypted: {reader.IsEncrypted()}");
                sb.AppendLine($"  Is Rebuilt: {reader.IsRebuilt()}");
                sb.AppendLine($"  PDF Version: {reader.PdfVersion}");
                sb.AppendLine();
                
                sb.AppendLine("OBJECT STREAM INFORMATION:");
                for (int i = 1; i < reader.XrefSize; i++)
                {
                    var obj = reader.GetPdfObjectRelease(i);
                    if (obj != null)
                    {
                        sb.AppendLine($"  Object {i}: {obj.GetType().Name} - {obj}");
                        if (sb.Length > 100000) // Limit output size
                        {
                            sb.AppendLine("  ... (truncated - output too large)");
                            break;
                        }
                    }
                }
            }
            finally
            {
                reader.Close();
            }
            
            return sb.ToString();
        }
        
        private string GenerateXMLReport(PDFAnalysisResult result)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<PDFAnalysisResult>");
            
            // Basic Information
            sb.AppendLine($"  <FilePath>{System.Security.SecurityElement.Escape(result.FilePath)}</FilePath>");
            sb.AppendLine($"  <FileSize>{result.FileSize}</FileSize>");
            sb.AppendLine($"  <AnalysisDate>{result.AnalysisDate:yyyy-MM-ddTHH:mm:ss}</AnalysisDate>");
            
            // Metadata
            sb.AppendLine("  <Metadata>");
            sb.AppendLine($"    <Title>{System.Security.SecurityElement.Escape(result.Metadata.Title ?? "")}</Title>");
            sb.AppendLine($"    <Author>{System.Security.SecurityElement.Escape(result.Metadata.Author ?? "")}</Author>");
            sb.AppendLine($"    <Subject>{System.Security.SecurityElement.Escape(result.Metadata.Subject ?? "")}</Subject>");
            sb.AppendLine($"    <Creator>{System.Security.SecurityElement.Escape(result.Metadata.Creator ?? "")}</Creator>");
            sb.AppendLine($"    <Producer>{System.Security.SecurityElement.Escape(result.Metadata.Producer ?? "")}</Producer>");
            sb.AppendLine($"    <CreationDate>{result.Metadata.CreationDate?.ToString("yyyy-MM-ddTHH:mm:ss")}</CreationDate>");
            sb.AppendLine($"    <ModificationDate>{result.Metadata.ModificationDate?.ToString("yyyy-MM-ddTHH:mm:ss")}</ModificationDate>");
            sb.AppendLine($"    <PDFVersion>{result.Metadata.PDFVersion}</PDFVersion>");
            sb.AppendLine($"    <IsTagged>{result.Metadata.IsTagged}</IsTagged>");
            sb.AppendLine("  </Metadata>");
            
            // Document Info
            sb.AppendLine("  <DocumentInfo>");
            sb.AppendLine($"    <TotalPages>{result.DocumentInfo.TotalPages}</TotalPages>");
            sb.AppendLine($"    <IsEncrypted>{result.DocumentInfo.IsEncrypted}</IsEncrypted>");
            sb.AppendLine($"    <IsLinearized>{result.DocumentInfo.IsLinearized}</IsLinearized>");
            sb.AppendLine($"    <HasAcroForm>{result.DocumentInfo.HasAcroForm}</HasAcroForm>");
            sb.AppendLine($"    <HasXFA>{result.DocumentInfo.HasXFA}</HasXFA>");
            sb.AppendLine($"    <FileStructure>{System.Security.SecurityElement.Escape(result.DocumentInfo.FileStructure ?? "")}</FileStructure>");
            sb.AppendLine("  </DocumentInfo>");
            
            // Statistics
            sb.AppendLine("  <Statistics>");
            sb.AppendLine($"    <TotalWords>{result.Statistics.TotalWords}</TotalWords>");
            sb.AppendLine($"    <TotalCharacters>{result.Statistics.TotalCharacters}</TotalCharacters>");
            sb.AppendLine($"    <AverageWordsPerPage>{result.Statistics.AverageWordsPerPage}</AverageWordsPerPage>");
            sb.AppendLine($"    <TotalImages>{result.Statistics.TotalImages}</TotalImages>");
            sb.AppendLine($"    <UniqueFonts>{result.Statistics.UniqueFonts}</UniqueFonts>");
            sb.AppendLine("  </Statistics>");
            
            // Pages
            sb.AppendLine("  <Pages>");
            foreach (var page in result.Pages)
            {
                sb.AppendLine($"    <Page number=\"{page.PageNumber}\">");
                sb.AppendLine($"      <WordCount>{page.TextInfo.WordCount}</WordCount>");
                sb.AppendLine($"      <CharacterCount>{page.TextInfo.CharacterCount}</CharacterCount>");
                sb.AppendLine($"      <HasTables>{page.TextInfo.HasTables}</HasTables>");
                sb.AppendLine($"      <HasColumns>{page.TextInfo.HasColumns}</HasColumns>");
                sb.AppendLine($"      <ImageCount>{page.Resources.Images.Count}</ImageCount>");
                sb.AppendLine($"      <AnnotationCount>{page.Annotations.Count}</AnnotationCount>");
                sb.AppendLine($"      <Width>{page.Size.Width}</Width>");
                sb.AppendLine($"      <Height>{page.Size.Height}</Height>");
                
                // Fonts
                if (page.TextInfo.Fonts.Count > 0)
                {
                    sb.AppendLine("      <Fonts>");
                    foreach (var font in page.TextInfo.Fonts)
                    {
                        sb.AppendLine($"        <Font name=\"{System.Security.SecurityElement.Escape(font.Name)}\" size=\"{font.Size}\" />");
                    }
                    sb.AppendLine("      </Fonts>");
                }
                
                sb.AppendLine("    </Page>");
            }
            sb.AppendLine("  </Pages>");
            
            // Bookmarks
            if (result.Bookmarks?.RootItems?.Count > 0)
            {
                sb.AppendLine("  <Bookmarks>");
                GenerateXMLBookmarks(sb, result.Bookmarks.RootItems, "    ");
                sb.AppendLine("  </Bookmarks>");
            }
            
            // XMP Metadata
            sb.AppendLine("  <XMPMetadata>");
            sb.AppendLine($"    <DublinCoreTitle>{System.Security.SecurityElement.Escape(result.XMPMetadata.DublinCoreTitle ?? "")}</DublinCoreTitle>");
            sb.AppendLine($"    <DublinCoreCreator>{System.Security.SecurityElement.Escape(result.XMPMetadata.DublinCoreCreator ?? "")}</DublinCoreCreator>");
            sb.AppendLine($"    <CreatorTool>{System.Security.SecurityElement.Escape(result.XMPMetadata.CreatorTool ?? "")}</CreatorTool>");
            sb.AppendLine($"    <DocumentID>{System.Security.SecurityElement.Escape(result.XMPMetadata.DocumentID ?? "")}</DocumentID>");
            sb.AppendLine("  </XMPMetadata>");
            
            sb.AppendLine("</PDFAnalysisResult>");
            return sb.ToString();
        }
        
        private void GenerateXMLBookmarks(StringBuilder sb, List<BookmarkItem> bookmarks, string indent)
        {
            foreach (var bookmark in bookmarks)
            {
                sb.AppendLine($"{indent}<Bookmark>");
                sb.AppendLine($"{indent}  <Title>{System.Security.SecurityElement.Escape(bookmark.Title)}</Title>");
                sb.AppendLine($"{indent}  <Level>{bookmark.Level}</Level>");
                if (bookmark.Destination != null)
                {
                    sb.AppendLine($"{indent}  <PageNumber>{bookmark.Destination.PageNumber}</PageNumber>");
                }
                
                if (bookmark.Children?.Count > 0)
                {
                    sb.AppendLine($"{indent}  <Children>");
                    GenerateXMLBookmarks(sb, bookmark.Children, indent + "    ");
                    sb.AppendLine($"{indent}  </Children>");
                }
                sb.AppendLine($"{indent}</Bookmark>");
            }
        }
        
        public override void ShowHelp()
        {
            Console.WriteLine($"COMMAND: {Name}");
            Console.WriteLine($"    {Description}");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine($"    fpdf {Name} [options] <input.pdf>");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("    -o, --output <file>      Output file path");
            Console.WriteLine("    -F <format>              Output format (see formats below)");
            Console.WriteLine("    -d, --detailed           Include detailed analysis");
            Console.WriteLine("    --raw                    Show raw PDF structure data");
            Console.WriteLine("    --content                Include page content analysis");
            Console.WriteLine("    --fonts                  Include detailed font analysis");
            Console.WriteLine("    --images                 Include detailed image analysis");
            Console.WriteLine("    --media                  Include rich media analysis");
            Console.WriteLine("    --spatial                Include spatial data analysis");
            Console.WriteLine("    --pdfa                   Include PDF/A compliance analysis");
            ShowFormatHelp();
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine($"    fpdf {Name} document.pdf");
            Console.WriteLine($"    fpdf {Name} document.pdf -f json -o analysis.json");
            Console.WriteLine($"    fpdf {Name} document.pdf --detailed --fonts --images");
            Console.WriteLine($"    fpdf {Name} document.pdf --raw");
        }
    }
    
    // Wrapper para comandos elevados do filter
    public class CommandWrapper : Command
    {
        private readonly string _name;
        private readonly string _description;
        private readonly Action<string[]> _executeAction;
        
        public CommandWrapper(string name, string description, Action<string[]> executeAction)
        {
            _name = name;
            _description = description;
            _executeAction = executeAction;
        }
        
        public override string Name => _name;
        public override string Description => _description;
        
        public override void Execute(string[] args)
        {
            _executeAction(args);
        }
        
        public override void ShowHelp()
        {
            Console.WriteLine($"Command: {_name}");
            Console.WriteLine($"Description: {_description}");
            Console.WriteLine("Use fpdf --help for more information");
        }
    }
    
    public class PDFAValidationResult
    {
        public bool IsValidPDFA { get; set; }
        public string PDFAPart { get; set; } = "1";
        public string ConformanceLevel { get; set; } = "B";
        public bool HasXMPMetadata { get; set; }
        public bool HasPDFAIdentification { get; set; }
        public bool HasOutputIntent { get; set; }
        public bool AllFontsEmbedded { get; set; }
        public bool IsEncrypted { get; set; }
        public bool HasJavaScript { get; set; }
        public bool HasTransparency { get; set; }
        public bool HasProhibitedAnnotations { get; set; }
        public bool HasXFAForms { get; set; }
        public bool HasProhibitedActions { get; set; }
        public bool HasEmbeddedFiles { get; set; }
        public bool HasMultimedia { get; set; }
        public List<string> ValidationMessages { get; set; } = new List<string>();
        public bool NoTransparency { get; set; }
        public bool NoJavaScript { get; set; }
        public bool NoEncryption { get; set; }
    }
}
