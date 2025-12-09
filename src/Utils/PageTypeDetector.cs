using System;
using System.Linq;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Utils
{
    /// <summary>
    /// Detecta o tipo de conteúdo de páginas PDF (texto real vs imagem escaneada)
    /// </summary>
    public class PageTypeDetector
    {
        public enum PageType
        {
            Text,           // Página com texto real extraível
            ScannedImage,   // Página que é 100% imagem escaneada
            Mixed,          // Página com texto e imagens
            Empty           // Página vazia
        }

        public class PageAnalysis
        {
            public int PageNumber { get; set; }
            public PageType Type { get; set; }
            public int TextCharCount { get; set; }
            public int ImageCount { get; set; }
            public bool HasExtractableText { get; set; }
            public bool NeedsOCR { get; set; }
            public double TextCoverage { get; set; } // Porcentagem da página coberta por texto
            public string Description { get; set; } = "";
        }

        /// <summary>
        /// Analisa uma página específica do PDF
        /// </summary>
        public static PageAnalysis AnalyzePage(PdfReader reader, int pageNumber)
        {
            var analysis = new PageAnalysis
            {
                PageNumber = pageNumber,
                Type = PageType.Empty,
                TextCharCount = 0,
                ImageCount = 0,
                HasExtractableText = false,
                NeedsOCR = false,
                TextCoverage = 0
            };

            try
            {
                // Extrai texto da página
                string pageText = PdfTextExtractor.GetTextFromPage(reader, pageNumber);
                analysis.TextCharCount = pageText?.Trim().Length ?? 0;
                analysis.HasExtractableText = analysis.TextCharCount > 10; // Mais de 10 chars = tem texto

                // Analisa recursos da página (imagens, etc.)
                var pageDict = reader.GetPageN(pageNumber);
                var resources = pageDict.GetAsDict(PdfName.RESOURCES);
                
                if (resources != null)
                {
                    // Conta imagens (XObjects)
                    var xobjects = resources.GetAsDict(PdfName.XOBJECT);
                    if (xobjects != null)
                    {
                        foreach (var key in xobjects.Keys)
                        {
                            var xobj = xobjects.GetAsIndirectObject(key);
                            if (xobj != null)
                            {
                                var stream = (PRStream)PdfReader.GetPdfObject(xobj);
                                var subtype = stream.GetAsName(PdfName.SUBTYPE);
                                if (PdfName.IMAGE.Equals(subtype))
                                {
                                    analysis.ImageCount++;
                                }
                            }
                        }
                    }
                }

                // Determina o tipo da página
                if (analysis.TextCharCount == 0 && analysis.ImageCount == 0)
                {
                    analysis.Type = PageType.Empty;
                    analysis.Description = "Página vazia";
                }
                else if (analysis.TextCharCount > 50 && analysis.ImageCount == 0)
                {
                    analysis.Type = PageType.Text;
                    analysis.Description = "Página de texto puro";
                }
                else if (analysis.TextCharCount < 50 && analysis.ImageCount > 0)
                {
                    analysis.Type = PageType.ScannedImage;
                    analysis.NeedsOCR = true;
                    analysis.Description = "Página escaneada (necessita OCR)";
                }
                else if (analysis.TextCharCount > 50 && analysis.ImageCount > 0)
                {
                    analysis.Type = PageType.Mixed;
                    analysis.Description = "Página mista (texto + imagens)";
                }
                else if (analysis.TextCharCount <= 50 && analysis.TextCharCount > 0)
                {
                    // Pouco texto pode indicar uma página mal OCR-izada
                    analysis.Type = PageType.ScannedImage;
                    analysis.NeedsOCR = true;
                    analysis.Description = "Provável página escaneada com OCR ruim";
                }

                // Calcula cobertura de texto (aproximada)
                if (analysis.TextCharCount > 0)
                {
                    // Estimativa: ~2000 chars = página cheia de texto
                    analysis.TextCoverage = Math.Min(100, (analysis.TextCharCount / 2000.0) * 100);
                }
            }
            catch (Exception ex)
            {
                analysis.Description = $"Erro ao analisar: {ex.Message}";
            }

            return analysis;
        }

        /// <summary>
        /// Analisa todas as páginas de um PDF
        /// </summary>
        public static PageAnalysis[] AnalyzeAllPages(PdfReader reader)
        {
            int pageCount = reader.NumberOfPages;
            var analyses = new PageAnalysis[pageCount];

            for (int i = 1; i <= pageCount; i++)
            {
                analyses[i - 1] = AnalyzePage(reader, i);
            }

            return analyses;
        }

        /// <summary>
        /// Retorna um resumo da análise do PDF completo
        /// </summary>
        public static string GetSummary(PageAnalysis[] analyses)
        {
            int textPages = analyses.Count(a => a.Type == PageType.Text);
            int scannedPages = analyses.Count(a => a.Type == PageType.ScannedImage);
            int mixedPages = analyses.Count(a => a.Type == PageType.Mixed);
            int emptyPages = analyses.Count(a => a.Type == PageType.Empty);
            int needsOCR = analyses.Count(a => a.NeedsOCR);

            return $@"
📊 Análise do PDF:
  • Total de páginas: {analyses.Length}
  • Páginas de texto: {textPages}
  • Páginas escaneadas: {scannedPages} {(scannedPages > 0 ? "⚠️ (necessitam OCR)" : "")}
  • Páginas mistas: {mixedPages}
  • Páginas vazias: {emptyPages}
  • Páginas que precisam de OCR: {needsOCR}
";
        }
    }
}