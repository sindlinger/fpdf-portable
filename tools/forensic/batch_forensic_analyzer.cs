using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace PDFLayoutPreservingConverter
{
    /// <summary>
    /// Batch forensic analyzer for processing multiple PDFs
    /// </summary>
    public class BatchForensicAnalyzer
    {
        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: batch_forensic_analyzer <directory_path> [options]");
                Console.WriteLine("Options:");
                Console.WriteLine("  --lastsession    Analyze last session modifications");
                Console.WriteLine("  --timestamps     Analyze all timestamps");
                Console.WriteLine("  --modifications  Detect all modifications");
                Console.WriteLine("  --output <file>  Save results to JSON file");
                Console.WriteLine("  --filter <text>  Only process files containing text");
                Console.WriteLine("  --verbose        Show detailed progress");
                return;
            }

            string directoryPath = args[0];
            bool analyzeLastSession = args.Contains("--lastsession");
            bool analyzeTimestamps = args.Contains("--timestamps");
            bool analyzeModifications = args.Contains("--modifications");
            bool verbose = args.Contains("--verbose");
            string outputFile = GetOption(args, "--output");
            string filterText = GetOption(args, "--filter");

            // Default to lastsession if no specific analysis requested
            if (!analyzeLastSession && !analyzeTimestamps && !analyzeModifications)
            {
                analyzeLastSession = true;
            }

            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"❌ Directory not found: {directoryPath}");
                return;
            }

            Console.WriteLine($"🔍 Starting batch forensic analysis of: {directoryPath}");
            var results = new List<BatchAnalysisResult>();
            
            // Get all PDF files
            var pdfFiles = Directory.GetFiles(directoryPath, "*.pdf", SearchOption.AllDirectories);
            Console.WriteLine($"📊 Found {pdfFiles.Length} PDF files");

            int processed = 0;
            int suspicious = 0;
            int errors = 0;

            foreach (var pdfFile in pdfFiles)
            {
                try
                {
                    if (verbose)
                        Console.WriteLine($"\n📄 Processing: {Path.GetFileName(pdfFile)}");
                    
                    var result = new BatchAnalysisResult
                    {
                        FilePath = pdfFile,
                        FileName = Path.GetFileName(pdfFile),
                        FileSize = new FileInfo(pdfFile).Length,
                        AnalysisDate = DateTime.Now
                    };

                    // Run requested analyses
                    if (analyzeLastSession)
                    {
                        var lastSessionResult = RunLastSessionAnalysis(pdfFile, verbose);
                        result.LastSessionAnalysis = lastSessionResult;
                        if (lastSessionResult.HasModifications)
                        {
                            suspicious++;
                            result.IsSuspicious = true;
                            result.SuspiciousReason = "Last session modifications detected";
                        }
                    }

                    if (analyzeTimestamps)
                    {
                        var timestampResult = RunTimestampAnalysis(pdfFile, verbose);
                        result.TimestampAnalysis = timestampResult;
                        if (timestampResult.HasAnomalies)
                        {
                            suspicious++;
                            result.IsSuspicious = true;
                            result.SuspiciousReason = "Timestamp anomalies detected";
                        }
                    }

                    if (analyzeModifications)
                    {
                        var modificationResult = RunModificationAnalysis(pdfFile, verbose);
                        result.ModificationAnalysis = modificationResult;
                        if (modificationResult.HasModifications)
                        {
                            suspicious++;
                            result.IsSuspicious = true;
                            result.SuspiciousReason = "Document modifications detected";
                        }
                    }

                    // Apply text filter if specified
                    if (!string.IsNullOrEmpty(filterText))
                    {
                        if (!result.ContainsText(filterText))
                        {
                            if (verbose)
                                Console.WriteLine($"  ⏩ Skipped (no match for '{filterText}')");
                            continue;
                        }
                    }

                    results.Add(result);
                    processed++;

                    if (!verbose && processed % 10 == 0)
                    {
                        Console.Write($"\r🔄 Progress: {processed}/{pdfFiles.Length} files processed...");
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    if (verbose)
                        Console.WriteLine($"  ❌ Error: {ex.Message}");
                    
                    results.Add(new BatchAnalysisResult
                    {
                        FilePath = pdfFile,
                        FileName = Path.GetFileName(pdfFile),
                        Error = ex.Message,
                        HasError = true
                    });
                }
            }

            Console.WriteLine($"\n\n📊 Analysis Complete!");
            Console.WriteLine($"✅ Processed: {processed} files");
            Console.WriteLine($"⚠️  Suspicious: {suspicious} files");
            Console.WriteLine($"❌ Errors: {errors} files");

            // Show suspicious files
            var suspiciousFiles = results.Where(r => r.IsSuspicious).ToList();
            if (suspiciousFiles.Any())
            {
                Console.WriteLine($"\n🚨 Suspicious Files ({suspiciousFiles.Count}):");
                foreach (var file in suspiciousFiles.Take(10))
                {
                    Console.WriteLine($"  - {file.FileName}: {file.SuspiciousReason}");
                    if (file.LastSessionAnalysis?.TextsAdded > 0)
                    {
                        Console.WriteLine($"    Added texts: {file.LastSessionAnalysis.TextsAdded}");
                    }
                }
                if (suspiciousFiles.Count > 10)
                {
                    Console.WriteLine($"  ... and {suspiciousFiles.Count - 10} more");
                }
            }

            // Save results if output file specified
            if (!string.IsNullOrEmpty(outputFile))
            {
                SaveResults(results, outputFile);
                Console.WriteLine($"\n💾 Results saved to: {outputFile}");
            }

            // Generate summary report
            GenerateSummaryReport(results);
        }

        private static LastSessionSummary RunLastSessionAnalysis(string pdfFile, bool verbose)
        {
            var summary = new LastSessionSummary();
            
            try
            {
                // Build command
                var args = new[] { "filter", "lastsession", pdfFile, "-F", "json" };
                var command = new UnifiedFilterCommand();
                
                // Capture output
                var originalOut = Console.Out;
                var writer = new StringWriter();
                Console.SetOut(writer);
                
                command.Execute(args);
                
                Console.SetOut(originalOut);
                var jsonOutput = writer.ToString();
                
                // Parse JSON result
                if (!string.IsNullOrEmpty(jsonOutput))
                {
                    dynamic result = JsonConvert.DeserializeObject(jsonOutput);
                    if (result?.LastSessionTexts != null)
                    {
                        summary.HasModifications = true;
                        summary.TextsAdded = result.LastSessionTexts.Count;
                        summary.SessionType = result.SessionType?.ToString() ?? "Unknown";
                        
                        // Extract sample texts
                        foreach (var text in result.LastSessionTexts)
                        {
                            summary.SampleTexts.Add(text.Text?.ToString() ?? "");
                            if (summary.SampleTexts.Count >= 3) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    Console.WriteLine($"    Error in last session analysis: {ex.Message}");
            }
            
            return summary;
        }

        private static TimestampSummary RunTimestampAnalysis(string pdfFile, bool verbose)
        {
            var summary = new TimestampSummary();
            
            try
            {
                var args = new[] { "filter", "timestamps", pdfFile, "-F", "json" };
                var command = new UnifiedFilterCommand();
                
                var originalOut = Console.Out;
                var writer = new StringWriter();
                Console.SetOut(writer);
                
                command.Execute(args);
                
                Console.SetOut(originalOut);
                var jsonOutput = writer.ToString();
                
                if (!string.IsNullOrEmpty(jsonOutput))
                {
                    dynamic result = JsonConvert.DeserializeObject(jsonOutput);
                    if (result?.IncrementalUpdates != null)
                    {
                        summary.IncrementalUpdates = result.IncrementalUpdates.Count;
                        summary.HasAnomalies = summary.IncrementalUpdates > 0;
                    }
                    if (result?.ObjectsWithHighGeneration != null)
                    {
                        summary.ModifiedObjects = result.ObjectsWithHighGeneration.Count;
                        if (summary.ModifiedObjects > 0)
                            summary.HasAnomalies = true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    Console.WriteLine($"    Error in timestamp analysis: {ex.Message}");
            }
            
            return summary;
        }

        private static ModificationSummary RunModificationAnalysis(string pdfFile, bool verbose)
        {
            var summary = new ModificationSummary();
            
            try
            {
                var args = new[] { "filter", "modifications", pdfFile, "-F", "json" };
                var command = new UnifiedFilterCommand();
                
                var originalOut = Console.Out;
                var writer = new StringWriter();
                Console.SetOut(writer);
                
                command.Execute(args);
                
                Console.SetOut(originalOut);
                var jsonOutput = writer.ToString();
                
                if (!string.IsNullOrEmpty(jsonOutput))
                {
                    dynamic result = JsonConvert.DeserializeObject(jsonOutput);
                    if (result?.ModificationAreas != null)
                    {
                        summary.HasModifications = result.ModificationAreas.Count > 0;
                        summary.ModificationCount = result.ModificationAreas.Count;
                        
                        foreach (var mod in result.ModificationAreas)
                        {
                            summary.ModificationTypes.Add(mod.Type?.ToString() ?? "Unknown");
                            if (summary.ModificationTypes.Count >= 5) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    Console.WriteLine($"    Error in modification analysis: {ex.Message}");
            }
            
            return summary;
        }

        private static void SaveResults(List<BatchAnalysisResult> results, string outputFile)
        {
            var json = JsonConvert.SerializeObject(results, Formatting.Indented);
            File.WriteAllText(outputFile, json);
        }

        private static void GenerateSummaryReport(List<BatchAnalysisResult> results)
        {
            Console.WriteLine("\n📋 Summary Report:");
            Console.WriteLine("=================");
            
            // Group by suspicion reason
            var byReason = results.Where(r => r.IsSuspicious)
                                 .GroupBy(r => r.SuspiciousReason)
                                 .OrderByDescending(g => g.Count());
            
            foreach (var group in byReason)
            {
                Console.WriteLine($"\n{group.Key}: {group.Count()} files");
                
                // Show top modified files
                var topModified = group.Where(r => r.LastSessionAnalysis?.TextsAdded > 0)
                                     .OrderByDescending(r => r.LastSessionAnalysis.TextsAdded)
                                     .Take(5);
                
                foreach (var file in topModified)
                {
                    Console.WriteLine($"  - {file.FileName} ({file.LastSessionAnalysis.TextsAdded} texts added)");
                }
            }
            
            // File size statistics
            if (results.Any())
            {
                var totalSize = results.Sum(r => r.FileSize);
                var avgSize = results.Average(r => r.FileSize);
                
                Console.WriteLine($"\n📊 File Statistics:");
                Console.WriteLine($"  Total size: {FormatFileSize(totalSize)}");
                Console.WriteLine($"  Average size: {FormatFileSize((long)avgSize)}");
                Console.WriteLine($"  Largest file: {FormatFileSize(results.Max(r => r.FileSize))}");
            }
        }

        private static string GetOption(string[] args, string option)
        {
            var index = Array.IndexOf(args, option);
            if (index >= 0 && index < args.Length - 1)
                return args[index + 1];
            return null;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    // Result classes
    public class BatchAnalysisResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public DateTime AnalysisDate { get; set; }
        public bool IsSuspicious { get; set; }
        public string SuspiciousReason { get; set; }
        public LastSessionSummary LastSessionAnalysis { get; set; }
        public TimestampSummary TimestampAnalysis { get; set; }
        public ModificationSummary ModificationAnalysis { get; set; }
        public string Error { get; set; }
        public bool HasError { get; set; }
        
        public bool ContainsText(string text)
        {
            if (LastSessionAnalysis?.SampleTexts != null)
            {
                return LastSessionAnalysis.SampleTexts.Any(t => 
                    t.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return false;
        }
    }

    public class LastSessionSummary
    {
        public bool HasModifications { get; set; }
        public string SessionType { get; set; }
        public int TextsAdded { get; set; }
        public List<string> SampleTexts { get; set; } = new List<string>();
    }

    public class TimestampSummary
    {
        public bool HasAnomalies { get; set; }
        public int IncrementalUpdates { get; set; }
        public int ModifiedObjects { get; set; }
    }

    public class ModificationSummary
    {
        public bool HasModifications { get; set; }
        public int ModificationCount { get; set; }
        public List<string> ModificationTypes { get; set; } = new List<string>();
    }
}