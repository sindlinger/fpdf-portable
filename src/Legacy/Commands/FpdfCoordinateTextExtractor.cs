using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace FilterPDF
{
    /// <summary>
    /// Extrai texto com coordenadas precisas para comparação
    /// </summary>
    public class FpdfCoordinateTextExtractor : ITextExtractionStrategy
    {
        private List<TextChunk> chunks = new List<TextChunk>();
        
        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        
        public void RenderImage(ImageRenderInfo renderInfo) { }
        
        public void RenderText(TextRenderInfo renderInfo)
        {
            var baseline = renderInfo.GetBaseline();
            var startPoint = baseline.GetStartPoint();
            var endPoint = baseline.GetEndPoint();
            
            chunks.Add(new TextChunk
            {
                Text = renderInfo.GetText(),
                X = startPoint[Vector.I1],
                Y = startPoint[Vector.I2],
                EndX = endPoint[Vector.I1],
                EndY = endPoint[Vector.I2],
                FontName = renderInfo.GetFont()?.PostscriptFontName ?? "",
                FontSize = GetFontSize(renderInfo)
            });
        }
        
        public string GetResultantText()
        {
            // Ordenar chunks por posição (Y depois X)
            chunks.Sort((a, b) =>
            {
                if (Math.Abs(a.Y - b.Y) > 0.1f)
                    return b.Y.CompareTo(a.Y); // Y decrescente
                return a.X.CompareTo(b.X); // X crescente
            });
            
            var sb = new StringBuilder();
            foreach (var chunk in chunks)
            {
                sb.Append(chunk.Text);
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Obtém todos os chunks de texto com coordenadas
        /// </summary>
        public List<TextChunk> GetTextChunks()
        {
            return new List<TextChunk>(chunks);
        }
        
        /// <summary>
        /// Encontra texto em uma posição específica
        /// </summary>
        public string? GetTextAtPosition(float x, float y, float tolerance = 2.0f)
        {
            var matchingChunks = chunks.Where(c => 
                Math.Abs(c.X - x) <= tolerance && 
                Math.Abs(c.Y - y) <= tolerance
            ).ToList();
            
            if (matchingChunks.Any())
            {
                return string.Join("", matchingChunks.Select(c => c.Text));
            }
            
            return null;
        }
        
        /// <summary>
        /// Detecta sobreposições de texto
        /// </summary>
        public List<CoordinateTextOverlap> DetectOverlaps(float tolerance = 2.0f)
        {
            var overlaps = new List<CoordinateTextOverlap>();
            
            for (int i = 0; i < chunks.Count; i++)
            {
                for (int j = i + 1; j < chunks.Count; j++)
                {
                    var chunk1 = chunks[i];
                    var chunk2 = chunks[j];
                    
                    // Verificar se as coordenadas se sobrepõem
                    if (Math.Abs(chunk1.X - chunk2.X) <= tolerance &&
                        Math.Abs(chunk1.Y - chunk2.Y) <= tolerance)
                    {
                        overlaps.Add(new CoordinateTextOverlap
                        {
                            OriginalText = chunk1.Text,
                            OverlayText = chunk2.Text,
                            X = chunk2.X,
                            Y = chunk2.Y,
                            OriginalFont = chunk1.FontName,
                            OverlayFont = chunk2.FontName
                        });
                    }
                }
            }
            
            return overlaps;
        }
        
        /// <summary>
        /// Detecta mudanças de fonte na mesma linha
        /// </summary>
        public List<FontChangeInfo> DetectFontChanges(float lineTolerance = 2.0f)
        {
            var changes = new List<FontChangeInfo>();
            
            // Agrupar por linha
            var lines = chunks.GroupBy(c => Math.Round(c.Y / lineTolerance) * lineTolerance)
                             .OrderByDescending(g => g.Key);
            
            foreach (var line in lines)
            {
                var lineChunks = line.OrderBy(c => c.X).ToList();
                
                for (int i = 1; i < lineChunks.Count; i++)
                {
                    var prev = lineChunks[i - 1];
                    var curr = lineChunks[i];
                    
                    if (prev.FontName != curr.FontName)
                    {
                        changes.Add(new FontChangeInfo
                        {
                            BeforeText = prev.Text,
                            AfterText = curr.Text,
                            FromFont = prev.FontName,
                            ToFont = curr.FontName,
                            X = curr.X,
                            Y = curr.Y,
                            LineText = string.Join("", lineChunks.Select(c => c.Text))
                        });
                    }
                }
            }
            
            return changes;
        }
        
        /// <summary>
        /// Detecta gaps anormais no fluxo de texto
        /// </summary>
        public List<CoordinateTextGap> DetectTextGaps(float normalLineHeight = 12.0f)
        {
            var gaps = new List<CoordinateTextGap>();
            
            // Ordenar por Y decrescente
            var sortedChunks = chunks.OrderByDescending(c => c.Y).ToList();
            
            for (int i = 1; i < sortedChunks.Count; i++)
            {
                var prev = sortedChunks[i - 1];
                var curr = sortedChunks[i];
                
                var gap = prev.Y - curr.Y;
                
                // Se o gap é muito maior que o esperado
                if (gap > normalLineHeight * 1.5f)
                {
                    gaps.Add(new CoordinateTextGap
                    {
                        StartY = curr.Y,
                        EndY = prev.Y,
                        GapSize = gap,
                        TextBefore = prev.Text,
                        TextAfter = curr.Text
                    });
                }
            }
            
            return gaps;
        }
        
        private float GetFontSize(TextRenderInfo renderInfo)
        {
            try
            {
                var gs = ReflectionHelper.GetGraphicsState(renderInfo);
                if (gs != null)
                {
                    return gs.FontSize;
                }
            }
            catch { }
            
            return 12.0f; // Default
        }
    }
    
    /// <summary>
    /// Representa um pedaço de texto com suas coordenadas
    /// </summary>
    public class TextChunk
    {
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float EndX { get; set; }
        public float EndY { get; set; }
        public string FontName { get; set; } = "";
        public float FontSize { get; set; }
    }
    
    /// <summary>
    /// Representa uma sobreposição de texto
    /// </summary>
    public class CoordinateTextOverlap
    {
        public string OriginalText { get; set; } = "";
        public string OverlayText { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public string OriginalFont { get; set; } = "";
        public string OverlayFont { get; set; } = "";
    }
    
    /// <summary>
    /// Informação sobre mudança de fonte
    /// </summary>
    public class FontChangeInfo
    {
        public string BeforeText { get; set; } = "";
        public string AfterText { get; set; } = "";
        public string FromFont { get; set; } = "";
        public string ToFont { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public string LineText { get; set; } = "";
    }
    
    /// <summary>
    /// Representa um gap anormal no texto
    /// </summary>
    public class CoordinateTextGap
    {
        public float StartY { get; set; }
        public float EndY { get; set; }
        public float GapSize { get; set; }
        public string TextBefore { get; set; } = "";
        public string TextAfter { get; set; } = "";
    }
    
    /// <summary>
    /// Helper para acessar propriedades privadas via reflexão
    /// </summary>
    internal static class ReflectionHelper
    {
        public static GraphicsState? GetGraphicsState(TextRenderInfo renderInfo)
        {
            var gsField = renderInfo.GetType().GetField("gs", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
                
            if (gsField != null)
            {
                return gsField.GetValue(renderInfo) as GraphicsState;
            }
            
            return null;
        }
    }
}