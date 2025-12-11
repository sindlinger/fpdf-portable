using System;
using System.Collections.Generic;
using System.Linq;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Strategies
{
    /// <summary>
    /// Estratégia para capturar TODAS as fontes com TODOS os tamanhos usados
    /// </summary>
    public class CompleteFontAnalysisStrategy : ITextExtractionStrategy
    {
        private Dictionary<string, List<float>> fontSizes = new Dictionary<string, List<float>>();
        
        public void BeginTextBlock() { }
        
        public void EndTextBlock() { }
        
        public void RenderText(TextRenderInfo renderInfo)
        {
            try
            {
                var font = renderInfo.GetFont();
                if (font != null)
                {
                    var fontName = font.PostscriptFontName ?? "Unknown";
                    var fontSize = renderInfo.GetSingleSpaceWidth(); // Aproximação do tamanho
                    
                    if (!fontSizes.ContainsKey(fontName))
                    {
                        fontSizes[fontName] = new List<float>();
                    }
                    
                    bool alreadyExists = false;
                    foreach (var size in fontSizes[fontName])
                    {
                        if (Math.Abs(size - fontSize) < 0.1f)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                    
                    if (!alreadyExists)
                    {
                        fontSizes[fontName].Add(fontSize);
                    }
                }
            }
            catch { }
        }
        
        public void RenderImage(ImageRenderInfo renderInfo) { }
        
        public string GetResultantText()
        {
            return "";
        }
        
        public List<float> GetFontSizes(string fontName)
        {
            return fontSizes.ContainsKey(fontName) ? fontSizes[fontName] : new List<float>();
        }
        
        public Dictionary<string, List<float>> GetAllFontInstances()
        {
            return fontSizes;
        }
    }
}