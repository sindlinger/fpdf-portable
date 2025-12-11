using System;
using System.Collections.Generic;
using System.Text;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Strategies
{
    /// <summary>
    /// Estratégia para extrair headers e footers baseado na posição Y na página
    /// </summary>
    public class AdvancedHeaderFooterStrategy : ITextExtractionStrategy
    {
        private readonly bool extractHeaders;
        private readonly List<TextChunk> textChunks = new List<TextChunk>();
        private float pageHeight = 842f; // Altura padrão A4
        
        public AdvancedHeaderFooterStrategy(bool extractHeaders, float pageHeight = 842f)
        {
            this.extractHeaders = extractHeaders;
            this.pageHeight = pageHeight;
        }
        
        public void BeginTextBlock() { }
        
        public void EndTextBlock() { }
        
        public void RenderText(TextRenderInfo renderInfo)
        {
            try
            {
                var text = renderInfo.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var baseline = renderInfo.GetBaseline();
                    var startPoint = baseline.GetStartPoint();
                    var y = startPoint[1]; // Y coordinate  
                    var x = startPoint[0]; // X coordinate
                    
                    // Determinar se está na região de header ou footer
                    bool isInTargetRegion = false;
                    
                    if (extractHeaders)
                    {
                        // Headers: região superior (últimos 10% da página)
                        isInTargetRegion = y > (pageHeight * 0.90f);
                    }
                    else
                    {
                        // Footers: região inferior (primeiros 10% da página)
                        isInTargetRegion = y < (pageHeight * 0.10f);
                    }
                    
                    if (isInTargetRegion)
                    {
                        textChunks.Add(new TextChunk(text, x, y));
                    }
                }
            }
            catch { }
        }
        
        public void RenderImage(ImageRenderInfo renderInfo) { }
        
        public string GetResultantText()
        {
            if (textChunks.Count == 0) return "";
            
            // Ordenar por posição Y (top-down) e depois X (left-right)
            textChunks.Sort((a, b) => 
            {
                int yComp = b.Y.CompareTo(a.Y); // Y decrescente (top-down)
                return yComp != 0 ? yComp : a.X.CompareTo(b.X); // X crescente (left-right)
            });
            
            var result = new StringBuilder();
            float lastY = float.MaxValue;
            
            foreach (var chunk in textChunks)
            {
                // Nova linha se Y mudou significativamente
                if (Math.Abs(lastY - chunk.Y) > 5f && result.Length > 0)
                {
                    result.AppendLine();
                }
                
                result.Append(chunk.Text);
                if (!chunk.Text.EndsWith(" ")) result.Append(" ");
                
                lastY = chunk.Y;
            }
            
            return result.ToString().Trim();
        }
        
        private class TextChunk
        {
            public string Text { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            
            public TextChunk(string text, float x, float y)
            {
                Text = text;
                X = x;
                Y = y;
            }
        }
    }
}