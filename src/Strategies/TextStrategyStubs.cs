using System;
using System.Collections.Generic;
using System.Text;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Strategies
{
    // Minimal stub strategies to keep CLI/PDFAnalyzer working while we port to iText7
    public class LayoutPreservingStrategy : ITextExtractionStrategy
    {
        protected StringBuilder sb = new StringBuilder();
        public virtual void BeginTextBlock() { }
        public virtual void EndTextBlock() { }
        public virtual void RenderImage(ImageRenderInfo renderInfo) { }
        public virtual void RenderText(TextRenderInfo renderInfo)
        {
            sb.Append(renderInfo.GetText());
            sb.Append('\n');
        }
        public virtual string GetResultantText() => sb.ToString();
    }

    public class ColumnDetectionStrategy : LayoutPreservingStrategy { }
    public class AdvancedLayoutStrategy : LayoutPreservingStrategy { }

    public class AdvancedHeaderFooterStrategy : LayoutPreservingStrategy
    {
        public AdvancedHeaderFooterStrategy(bool isHeader, float pageHeight) { }
    }

    public class CompleteFontAnalysisStrategy : ITextExtractionStrategy
    {
        private readonly Dictionary<string, HashSet<float>> _sizes = new();
        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        public void RenderImage(ImageRenderInfo renderInfo) { }
        public void RenderText(TextRenderInfo renderInfo)
        {
            var fontName = renderInfo.GetFont()?.PostscriptFontName ?? "";
            if (!_sizes.ContainsKey(fontName)) _sizes[fontName] = new HashSet<float>();
            // fallback: use font size from renderInfo if available
            _sizes[fontName].Add(12f);
        }
        public string GetResultantText() => string.Empty;
        public HashSet<float> GetFontSizes(string fontName) => _sizes.ContainsKey(fontName) ? _sizes[fontName] : new HashSet<float>();
    }
}
