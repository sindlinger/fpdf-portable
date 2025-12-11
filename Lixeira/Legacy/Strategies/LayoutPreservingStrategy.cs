using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Strategies
{
    /// <summary>
    /// Estratégia básica que preserva o layout usando análise de posição
    /// </summary>
    public class LayoutPreservingStrategy : ITextExtractionStrategy
    {
        private class TextChunk : IComparable<TextChunk>
        {
            public string Text { get; set; } = "";
            public Vector StartLocation { get; set; } = new Vector(0, 0, 0);
            public Vector EndLocation { get; set; } = new Vector(0, 0, 0);
            public float DistanceFromBaseline { get; set; }
            public int CharSpaceWidth { get; set; }

            public int CompareTo(TextChunk? other)
            {
                if (other == null) return 1;
                if (this == other) return 0;
                
                // Ordena primeiro por Y (de cima para baixo)
                float yDiff = this.StartLocation[Vector.I2] - other.StartLocation[Vector.I2];
                if (Math.Abs(yDiff) > 1f)
                {
                    return -yDiff.CompareTo(0);
                }
                
                // Se estão na mesma linha, ordena por X (esquerda para direita)
                return this.StartLocation[Vector.I1].CompareTo(other.StartLocation[Vector.I1]);
            }
        }

        private List<TextChunk> chunks = new List<TextChunk>();

        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        public void RenderImage(ImageRenderInfo renderInfo) { }

        public void RenderText(TextRenderInfo renderInfo)
        {
            Vector start = renderInfo.GetBaseline().GetStartPoint();
            Vector end = renderInfo.GetBaseline().GetEndPoint();
            
            chunks.Add(new TextChunk
            {
                Text = renderInfo.GetText(),
                StartLocation = start,
                EndLocation = end,
                DistanceFromBaseline = renderInfo.GetBaseline().GetLength(),
                CharSpaceWidth = (int)renderInfo.GetSingleSpaceWidth()
            });
        }

        public string GetResultantText()
        {
            // Ordena os chunks por posição
            chunks.Sort();
            
            if (chunks.Count == 0) return string.Empty;
            
            StringBuilder result = new StringBuilder();
            TextChunk? lastChunk = null;
            
            foreach (var chunk in chunks)
            {
                if (lastChunk != null)
                {
                    // Verifica se precisa adicionar quebra de linha
                    float yDiff = Math.Abs(lastChunk.StartLocation[Vector.I2] - chunk.StartLocation[Vector.I2]);
                    
                    if (yDiff > 1f) // Nova linha
                    {
                        result.AppendLine();
                        
                        // Se a diferença vertical for grande, pode ser um novo parágrafo
                        if (yDiff > 15f)
                        {
                            result.AppendLine();
                        }
                    }
                    else // Mesma linha
                    {
                        // Calcula espaços baseado na distância horizontal
                        float xGap = chunk.StartLocation[Vector.I1] - lastChunk.EndLocation[Vector.I1];
                        
                        if (xGap > chunk.CharSpaceWidth * 0.5f)
                        {
                            // Adiciona espaços proporcionais ao gap
                            int spaces = Math.Max(1, (int)(xGap / chunk.CharSpaceWidth));
                            result.Append(new string(' ', spaces));
                        }
                    }
                }
                
                result.Append(chunk.Text);
                lastChunk = chunk;
            }
            
            return result.ToString();
        }
    }
}