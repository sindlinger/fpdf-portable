using System;
using System.Collections.Generic;
using System.Linq;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace FilterPDF
{
    /// <summary>
    /// Detecta áreas de modificação em PDFs através de análise forense
    /// </summary>
    public class FpdfModificationDetector
    {
        private PdfReader reader;
        
        public FpdfModificationDetector(PdfReader reader)
        {
            this.reader = reader;
        }
        
        /// <summary>
        /// Detecta possíveis áreas de modificação analisando:
        /// 1. Objetos com diferentes gerações
        /// 2. Múltiplas camadas de texto na mesma posição
        /// 3. Fontes diferentes em áreas próximas
        /// 4. Descontinuidades na estrutura do texto
        /// </summary>
        public List<ModificationArea> DetectModifications(int pageNumber)
        {
            var modifications = new List<ModificationArea>();
            
            // 1. Analisar gerações de objetos
            var objectGenerations = AnalyzeObjectGenerations(pageNumber);
            
            // 2. Detectar sobreposições de texto
            var overlaps = DetectTextOverlaps(pageNumber);
            
            // 3. Analisar mudanças abruptas de fonte
            var fontChanges = DetectAbruptFontChanges(pageNumber);
            
            // 4. Detectar descontinuidades no fluxo de texto
            var textGaps = DetectTextFlowDiscontinuities(pageNumber);
            
            // Combinar todas as detecções
            modifications.AddRange(objectGenerations);
            modifications.AddRange(overlaps);
            modifications.AddRange(fontChanges);
            modifications.AddRange(textGaps);
            
            return modifications;
        }
        
        /// <summary>
        /// Analisa gerações de objetos - objetos com geração > 0 foram modificados
        /// </summary>
        private List<ModificationArea> AnalyzeObjectGenerations(int pageNumber)
        {
            var modifications = new List<ModificationArea>();
            var page = reader.GetPageN(pageNumber);
            
            // Analisar recursos da página
            var resources = page.GetAsDict(PdfName.RESOURCES);
            if (resources != null)
            {
                CheckObjectGeneration(resources, pageNumber, "Resources", modifications);
            }
            
            // Analisar conteúdo da página
            var contents = page.Get(PdfName.CONTENTS);
            if (contents != null)
            {
                if (contents.IsArray())
                {
                    var contentsArray = (PdfArray)contents;
                    for (int i = 0; i < contentsArray.Size; i++)
                    {
                        var streamRef = contentsArray.GetAsIndirectObject(i);
                        if (streamRef != null)
                        {
                            var generation = streamRef.Generation;
                            if (generation > 0)
                            {
                                modifications.Add(new ModificationArea
                                {
                                    PageNumber = pageNumber,
                                    Type = "Content Stream Modified",
                                    Description = $"Content stream {i} has generation {generation} (modified)",
                                    ObjectNumber = streamRef.Number,
                                    Generation = generation
                                });
                            }
                        }
                    }
                }
                else if (contents.IsIndirect())
                {
                    var indRef = (PRIndirectReference)contents;
                    if (indRef.Generation > 0)
                    {
                        modifications.Add(new ModificationArea
                        {
                            PageNumber = pageNumber,
                            Type = "Page Content Modified",
                            Description = $"Page content has generation {indRef.Generation}",
                            ObjectNumber = indRef.Number,
                            Generation = indRef.Generation
                        });
                    }
                }
            }
            
            return modifications;
        }
        
        private void CheckObjectGeneration(PdfObject obj, int pageNumber, string context, List<ModificationArea> modifications)
        {
            if (obj != null && obj.IsIndirect())
            {
                var indRef = (PRIndirectReference)obj;
                if (indRef.Generation > 0)
                {
                    modifications.Add(new ModificationArea
                    {
                        PageNumber = pageNumber,
                        Type = $"{context} Modified",
                        Description = $"{context} object has generation {indRef.Generation}",
                        ObjectNumber = indRef.Number,
                        Generation = indRef.Generation
                    });
                }
            }
        }
        
        /// <summary>
        /// Detecta sobreposições de texto - indica possível mascaramento
        /// </summary>
        private List<ModificationArea> DetectTextOverlaps(int pageNumber)
        {
            var modifications = new List<ModificationArea>();
            var strategy = new ModificationRenderListener();
            
            PdfReaderContentParser parser = new PdfReaderContentParser(reader);
            parser.ProcessContent(pageNumber, strategy);
            
            var overlaps = strategy.GetOverlappingTexts();
            foreach (var overlap in overlaps)
            {
                modifications.Add(new ModificationArea
                {
                    PageNumber = pageNumber,
                    Type = "Text Overlap",
                    Description = $"Multiple text layers at position ({overlap.X:F2}, {overlap.Y:F2})",
                    X = overlap.X,
                    Y = overlap.Y,
                    Width = overlap.Width,
                    Height = overlap.Height
                });
            }
            
            return modifications;
        }
        
        /// <summary>
        /// Detecta mudanças abruptas de fonte no meio de palavras/frases
        /// </summary>
        private List<ModificationArea> DetectAbruptFontChanges(int pageNumber)
        {
            var modifications = new List<ModificationArea>();
            var strategy = new FontAnalysisStrategy();
            
            PdfReaderContentParser parser = new PdfReaderContentParser(reader);
            parser.ProcessContent(pageNumber, strategy);
            
            var fontChanges = strategy.GetAbruptFontChanges();
            foreach (var change in fontChanges)
            {
                modifications.Add(new ModificationArea
                {
                    PageNumber = pageNumber,
                    Type = "Abrupt Font Change",
                    Description = $"Font changed from {change.OldFont} to {change.NewFont} at ({change.X:F2}, {change.Y:F2})",
                    X = change.X,
                    Y = change.Y
                });
            }
            
            return modifications;
        }
        
        /// <summary>
        /// Detecta descontinuidades no fluxo de texto
        /// </summary>
        private List<ModificationArea> DetectTextFlowDiscontinuities(int pageNumber)
        {
            var modifications = new List<ModificationArea>();
            var strategy = new TextFlowAnalysisStrategy();
            
            PdfReaderContentParser parser = new PdfReaderContentParser(reader);
            parser.ProcessContent(pageNumber, strategy);
            
            var gaps = strategy.GetTextGaps();
            foreach (var gap in gaps)
            {
                modifications.Add(new ModificationArea
                {
                    PageNumber = pageNumber,
                    Type = "Text Flow Gap",
                    Description = $"Unusual gap in text flow at ({gap.X:F2}, {gap.Y:F2})",
                    X = gap.X,
                    Y = gap.Y,
                    Width = gap.Width
                });
            }
            
            return modifications;
        }
    }
    
    public class ModificationArea
    {
        public int PageNumber { get; set; }
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int ObjectNumber { get; set; }
        public int Generation { get; set; }
    }
    
    /// <summary>
    /// Estratégia para detectar sobreposições de texto
    /// </summary>
    public class ModificationRenderListener : ITextExtractionStrategy
    {
        private List<TextChunk> textChunks = new List<TextChunk>();
        
        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        
        public void RenderText(TextRenderInfo renderInfo)
        {
            var baseline = renderInfo.GetBaseline();
            var startPoint = baseline.GetStartPoint();
            var endPoint = baseline.GetEndPoint();
            
            textChunks.Add(new TextChunk
            {
                Text = renderInfo.GetText(),
                X = startPoint[0],
                Y = startPoint[1],
                Width = endPoint[0] - startPoint[0],
                Height = renderInfo.GetAscentLine().GetEndPoint()[1] - renderInfo.GetDescentLine().GetEndPoint()[1],
                Font = renderInfo.GetFont()?.PostscriptFontName ?? "Unknown"
            });
        }
        
        public void RenderImage(ImageRenderInfo renderInfo) { }
        
        public string GetResultantText() => "";
        
        public List<TextOverlap> GetOverlappingTexts()
        {
            var overlaps = new List<TextOverlap>();
            
            // Agrupar textos por posição similar
            var threshold = 2.0f; // tolerância de 2 pontos
            
            for (int i = 0; i < textChunks.Count; i++)
            {
                for (int j = i + 1; j < textChunks.Count; j++)
                {
                    var chunk1 = textChunks[i];
                    var chunk2 = textChunks[j];
                    
                    // Verificar se os textos se sobrepõem
                    if (Math.Abs(chunk1.X - chunk2.X) < threshold && 
                        Math.Abs(chunk1.Y - chunk2.Y) < threshold &&
                        chunk1.Text != chunk2.Text)
                    {
                        overlaps.Add(new TextOverlap
                        {
                            X = chunk1.X,
                            Y = chunk1.Y,
                            Width = Math.Max(chunk1.Width, chunk2.Width),
                            Height = Math.Max(chunk1.Height, chunk2.Height),
                            Text1 = chunk1.Text,
                            Text2 = chunk2.Text
                        });
                    }
                }
            }
            
            return overlaps;
        }
        
        private class TextChunk
        {
            public string Text { get; set; } = "";
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public string Font { get; set; } = "";
        }
    }
    
    public class TextOverlap
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string Text1 { get; set; } = "";
        public string Text2 { get; set; } = "";
    }
    
    /// <summary>
    /// Estratégia para análise de mudanças de fonte
    /// </summary>
    public class FontAnalysisStrategy : ITextExtractionStrategy
    {
        private string? lastFont = null;
        private float lastX = 0;
        private float lastY = 0;
        private List<FontChange> fontChanges = new List<FontChange>();
        
        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        
        public void RenderText(TextRenderInfo renderInfo)
        {
            var currentFont = renderInfo.GetFont()?.PostscriptFontName ?? "Unknown";
            var baseline = renderInfo.GetBaseline();
            var currentX = baseline.GetStartPoint()[0];
            var currentY = baseline.GetStartPoint()[1];
            
            // Se mudou a fonte e está próximo do texto anterior
            if (lastFont != null && lastFont != currentFont)
            {
                var distance = Math.Sqrt(Math.Pow(currentX - lastX, 2) + Math.Pow(currentY - lastY, 2));
                if (distance < 50) // Dentro de 50 pontos
                {
                    fontChanges.Add(new FontChange
                    {
                        OldFont = lastFont,
                        NewFont = currentFont,
                        X = currentX,
                        Y = currentY
                    });
                }
            }
            
            lastFont = currentFont;
            lastX = currentX;
            lastY = currentY;
        }
        
        public void RenderImage(ImageRenderInfo renderInfo) { }
        public string GetResultantText() => "";
        
        public List<FontChange> GetAbruptFontChanges() => fontChanges;
    }
    
    public class FontChange
    {
        public string OldFont { get; set; } = "";
        public string NewFont { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
    }
    
    /// <summary>
    /// Estratégia para análise de fluxo de texto
    /// </summary>
    public class TextFlowAnalysisStrategy : ITextExtractionStrategy
    {
        private float lastX = -1;
        private float lastY = -1;
        private List<TextGap> gaps = new List<TextGap>();
        
        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        
        public void RenderText(TextRenderInfo renderInfo)
        {
            var baseline = renderInfo.GetBaseline();
            var currentX = baseline.GetStartPoint()[0];
            var currentY = baseline.GetStartPoint()[1];
            
            if (lastX >= 0 && lastY >= 0)
            {
                // Se estamos na mesma linha (Y similar)
                if (Math.Abs(currentY - lastY) < 2)
                {
                    var gap = currentX - lastX;
                    // Se o gap é maior que o esperado para espaço entre palavras
                    if (gap > 20 && gap < 200) // Gap suspeito
                    {
                        gaps.Add(new TextGap
                        {
                            X = lastX,
                            Y = currentY,
                            Width = gap
                        });
                    }
                }
            }
            
            lastX = baseline.GetEndPoint()[0];
            lastY = currentY;
        }
        
        public void RenderImage(ImageRenderInfo renderInfo) { }
        public string GetResultantText() => "";
        
        public List<TextGap> GetTextGaps() => gaps;
    }
    
    public class TextGap
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
    }
}