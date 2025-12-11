using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Strategies
{
    /// <summary>
    /// Estratégia avançada que preserva espaçamento, indentação e formatação complexa
    /// </summary>
    public class AdvancedLayoutStrategy : ITextExtractionStrategy
    {
        private class TextSegment
        {
            public string Text { get; set; } = "";
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float FontSize { get; set; }
            public float SpaceWidth { get; set; }
        }

        private SortedDictionary<float, List<TextSegment>> pageLayout;
        private const float LINE_TOLERANCE = 2f;
        private const float PARAGRAPH_GAP = 10f;

        public AdvancedLayoutStrategy()
        {
            pageLayout = new SortedDictionary<float, List<TextSegment>>(new DescendingComparer());
        }

        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        public void RenderImage(ImageRenderInfo renderInfo) { }

        public void RenderText(TextRenderInfo renderInfo)
        {
            string text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            Vector baseline = renderInfo.GetBaseline().GetStartPoint();
            float x = baseline[Vector.I1];
            float y = baseline[Vector.I2];
            
            // Arredonda Y para agrupar texto na mesma linha
            float roundedY = (float)Math.Round(y / LINE_TOLERANCE) * LINE_TOLERANCE;
            
            if (!pageLayout.ContainsKey(roundedY))
            {
                pageLayout[roundedY] = new List<TextSegment>();
            }
            
            pageLayout[roundedY].Add(new TextSegment
            {
                Text = text,
                X = x,
                Y = y,
                Width = renderInfo.GetBaseline().GetLength(),
                FontSize = GetFontSize(renderInfo),
                SpaceWidth = renderInfo.GetSingleSpaceWidth()
            });
        }

        private float GetFontSize(TextRenderInfo renderInfo)
        {
            try
            {
                // Estima o tamanho da fonte baseado na altura da baseline
                return renderInfo.GetBaseline().GetLength();
            }
            catch
            {
                // Valor padrão se falhar
                return 12f;
            }
        }

        public string GetResultantText()
        {
            StringBuilder result = new StringBuilder();
            float? previousY = null;
            float leftMargin = DetectLeftMargin();
            
            foreach (var lineEntry in pageLayout)
            {
                float currentY = lineEntry.Key;
                var segments = lineEntry.Value.OrderBy(s => s.X).ToList();
                
                if (segments.Count == 0) continue;
                
                // Detecta quebras de parágrafo
                if (previousY.HasValue)
                {
                    float yGap = previousY.Value - currentY;
                    
                    if (yGap > PARAGRAPH_GAP)
                    {
                        result.AppendLine(); // Linha extra para parágrafo
                    }
                }
                
                // Processa a linha
                string lineText = BuildLine(segments, leftMargin);
                result.AppendLine(lineText);
                
                previousY = currentY;
            }
            
            return result.ToString().TrimEnd();
        }

        private string BuildLine(List<TextSegment> segments, float leftMargin)
        {
            StringBuilder line = new StringBuilder();
            
            // Adiciona indentação se necessário
            if (segments.Count > 0)
            {
                float firstX = segments[0].X;
                float indent = firstX - leftMargin;
                
                if (indent > 5) // Indentação significativa
                {
                    int spaces = (int)(indent / segments[0].SpaceWidth);
                    line.Append(new string(' ', Math.Max(0, spaces)));
                }
            }
            
            // Constrói o texto da linha com espaçamento apropriado
            TextSegment? previous = null;
            
            foreach (var segment in segments)
            {
                if (previous != null)
                {
                    float gap = segment.X - (previous.X + previous.Width);
                    
                    if (gap > segment.SpaceWidth * 0.3f)
                    {
                        // Calcula número de espaços baseado no tamanho do gap
                        int spaces = Math.Max(1, (int)(gap / segment.SpaceWidth));
                        line.Append(new string(' ', spaces));
                    }
                }
                
                line.Append(segment.Text);
                previous = segment;
            }
            
            return line.ToString();
        }

        private float DetectLeftMargin()
        {
            // Detecta a margem esquerda mais comum
            var xPositions = pageLayout.Values
                .SelectMany(segments => segments)
                .Select(s => s.X)
                .Where(x => x > 0)
                .OrderBy(x => x)
                .ToList();
            
            if (xPositions.Count == 0) return 0;
            
            // Retorna a posição X mais à esquerda (com pequena tolerância)
            return xPositions.First();
        }

        private class DescendingComparer : IComparer<float>
        {
            public int Compare(float x, float y)
            {
                return y.CompareTo(x); // Ordem descendente (Y maior primeiro)
            }
        }
    }
}