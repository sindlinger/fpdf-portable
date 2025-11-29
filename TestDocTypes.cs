using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FilterPDF
{
    public class TestDocTypesCommand
    {
        private readonly PDFAnalysisResult analysisResult;
        
        public TestDocTypesCommand(PDFAnalysisResult result)
        {
            analysisResult = result;
        }

        public void Execute()
        {
            var sw = Stopwatch.StartNew();
            
            Console.WriteLine($"[TEST] Starting doctypes test...");
            Console.WriteLine($"[TEST] Pages count: {analysisResult.Pages.Count}");
            
            // SEM NENHUM REGEX - apenas loop básico
            foreach (var page in analysisResult.Pages)
            {
                var text = page.TextInfo?.PageText ?? "";
                if (text.Contains("despacho", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[TEST] Found despacho on page {page.PageNumber}");
                }
            }
            
            sw.Stop();
            Console.WriteLine($"[TEST] Processing took: {sw.ElapsedMilliseconds}ms");
        }
    }
}