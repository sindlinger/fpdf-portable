using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Strategies
{
    /// <summary>
    /// Extrai texto por linha com fonte/tamanho/estilo e coordenadas básicas.
    /// Agrupa os TextRenderInfo por Y aproximado (tolerância) para formar linhas.
    /// </summary>
    public class LineFontExtractionStrategy : ITextExtractionStrategy
    {
        private class Line
        {
            public float Y;
            public List<TextRenderInfo> Infos = new List<TextRenderInfo>();
        }

        private readonly List<Line> lines = new List<Line>();
        private readonly float yTolerance;

        public LineFontExtractionStrategy(float yTolerance = 1.5f)
        {
            this.yTolerance = yTolerance;
        }

        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        public void RenderImage(ImageRenderInfo renderInfo) { }

        public void RenderText(TextRenderInfo renderInfo)
        {
            var baseline = renderInfo.GetBaseline();
            float y = baseline.GetStartPoint()[1];

            // Tenta achar linha existente com Y próximo
            foreach (var line in lines)
            {
                if (Math.Abs(line.Y - y) <= yTolerance)
                {
                    line.Infos.Add(renderInfo);
                    return;
                }
            }

            // Nova linha
            lines.Add(new Line { Y = y, Infos = new List<TextRenderInfo> { renderInfo } });
        }

        public string GetResultantText()
        {
            // Não usamos o texto concatenado; retornamos linhas via helper.
            return string.Empty;
        }

        public List<LineInfo> GetLines()
        {
            var result = new List<LineInfo>();
            foreach (var line in lines)
            {
                if (line.Infos.Count == 0)
                    continue;

                // Ordenar pela posição X
                var ordered = line.Infos.OrderBy(i => i.GetBaseline().GetStartPoint()[0]).ToList();
                var text = string.Concat(ordered.Select(i => i.GetText()));

                // Bounding box da linha
                float x0 = ordered.First().GetBaseline().GetStartPoint()[0];
                float x1 = ordered.Last().GetAscentLine().GetEndPoint()[0];
                float y0 = ordered.Min(i => i.GetDescentLine().GetStartPoint()[1]);
                float y1 = ordered.Max(i => i.GetAscentLine().GetEndPoint()[1]);

                // Escolher a fonte/tamanho do primeiro fragmento
                var info = ordered.First();
                var font = info.GetFont();
                string fontName = font.PostscriptFontName;
                float size = Math.Abs(info.GetAscentLine().GetStartPoint()[1] - info.GetDescentLine().GetStartPoint()[1]);

                int renderMode = info.GetTextRenderMode();
                bool underline = false; // enum não disponível nesta versão; manter falso
                bool bold = fontName.ToLower().Contains("bold") || fontName.ToLower().Contains("black") || fontName.ToLower().Contains("heavy");
                bool italic = fontName.ToLower().Contains("italic") || fontName.ToLower().Contains("oblique") || fontName.ToLower().Contains("slant");
                float charSpacing = 0;
                float wordSpacing = 0;
                float horizScaling = 0;
                float rise = 0;

                // Hash da linha normalizada
                string norm = text.Trim().ToLowerInvariant();
                string hash;
                using (var sha1 = System.Security.Cryptography.SHA1.Create())
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(norm);
                    hash = BitConverter.ToString(sha1.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                }

                result.Add(new LineInfo
                {
                    Text = text,
                    Font = fontName,
                    Size = size,
                    Bold = bold,
                    Italic = italic,
                    Underline = underline,
                    RenderMode = renderMode,
                    CharSpacing = charSpacing,
                    WordSpacing = wordSpacing,
                    HorizontalScaling = horizScaling,
                    Rise = rise,
                    LineHash = hash,
                    X0 = x0,
                    Y0 = y0,
                    X1 = x1,
                    Y1 = y1,
                });
            }

            // Ordena de cima para baixo
            return result.OrderByDescending(l => l.Y0).ToList();
        }
    }
}
