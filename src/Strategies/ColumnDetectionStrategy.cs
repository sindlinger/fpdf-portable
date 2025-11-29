using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf.parser;

namespace FilterPDF.Strategies
{
    /// <summary>
    /// Estratégia que detecta e preserva layouts multi-coluna e tabelas
    /// </summary>
    public class ColumnDetectionStrategy : ITextExtractionStrategy
    {
        private class TextBlock
        {
            public string Text { get; set; } = "";
            public float Left { get; set; }
            public float Right { get; set; }
            public float Top { get; set; }
            public float Bottom { get; set; }
            public float FontSize { get; set; }
            public int ColumnIndex { get; set; } = -1;
        }

        private List<TextBlock> textBlocks = new List<TextBlock>();
        private const float COLUMN_GAP_THRESHOLD = 20f;
        private const float LINE_HEIGHT_TOLERANCE = 2f;

        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        public void RenderImage(ImageRenderInfo renderInfo) { }

        public void RenderText(TextRenderInfo renderInfo)
        {
            string text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            Vector bottomLeft = renderInfo.GetBaseline().GetStartPoint();
            Vector topRight = renderInfo.GetAscentLine().GetEndPoint();
            
            textBlocks.Add(new TextBlock
            {
                Text = text,
                Left = bottomLeft[Vector.I1],
                Right = topRight[Vector.I1],
                Top = topRight[Vector.I2],
                Bottom = bottomLeft[Vector.I2],
                FontSize = topRight[Vector.I2] - bottomLeft[Vector.I2]
            });
        }

        public string GetResultantText()
        {
            if (textBlocks.Count == 0) return string.Empty;

            // Detecta colunas
            var columns = DetectColumns();
            AssignBlocksToColumns(columns);

            // Agrupa blocos por linha
            var lines = GroupBlocksByLine();

            // Constrói o texto final
            return BuildFormattedText(lines, columns);
        }

        private List<float> DetectColumns()
        {
            // Agrupa blocos por posição X esquerda para detectar colunas
            var leftPositions = textBlocks
                .Select(b => b.Left)
                .GroupBy(x => Math.Round(x / COLUMN_GAP_THRESHOLD) * COLUMN_GAP_THRESHOLD)
                .Select(g => g.Min())
                .OrderBy(x => x)
                .ToList();

            // Filtra colunas muito próximas
            List<float> columns = new List<float>();
            float? lastColumn = null;

            foreach (var pos in leftPositions)
            {
                if (!lastColumn.HasValue || pos - lastColumn.Value > COLUMN_GAP_THRESHOLD)
                {
                    columns.Add(pos);
                    lastColumn = pos;
                }
            }

            return columns;
        }

        private void AssignBlocksToColumns(List<float> columns)
        {
            foreach (var block in textBlocks)
            {
                // Encontra a coluna mais próxima
                float minDistance = float.MaxValue;
                int columnIndex = 0;

                for (int i = 0; i < columns.Count; i++)
                {
                    float distance = Math.Abs(block.Left - columns[i]);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        columnIndex = i;
                    }
                }

                // Atribui apenas se estiver próximo o suficiente
                if (minDistance < COLUMN_GAP_THRESHOLD)
                {
                    block.ColumnIndex = columnIndex;
                }
            }
        }

        private List<List<TextBlock>> GroupBlocksByLine()
        {
            // Agrupa blocos por posição Y (linha)
            var lineGroups = textBlocks
                .GroupBy(b => Math.Round(b.Bottom / LINE_HEIGHT_TOLERANCE) * LINE_HEIGHT_TOLERANCE)
                .OrderByDescending(g => g.Key)
                .Select(g => g.OrderBy(b => b.ColumnIndex).ThenBy(b => b.Left).ToList())
                .ToList();

            return lineGroups;
        }

        private string BuildFormattedText(List<List<TextBlock>> lines, List<float> columns)
        {
            StringBuilder result = new StringBuilder();
            int maxColumnIndex = textBlocks.Max(b => b.ColumnIndex);
            
            foreach (var line in lines)
            {
                if (maxColumnIndex > 0) // Multi-coluna detectada
                {
                    result.Append(BuildMultiColumnLine(line, columns.Count));
                }
                else // Layout de coluna única
                {
                    result.Append(BuildSingleColumnLine(line));
                }
                
                result.AppendLine();
            }

            return result.ToString().TrimEnd();
        }

        private string BuildMultiColumnLine(List<TextBlock> lineBlocks, int columnCount)
        {
            // Cria array para armazenar texto de cada coluna
            string[] columnTexts = new string[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                columnTexts[i] = string.Empty;
            }

            // Agrupa texto por coluna
            foreach (var block in lineBlocks)
            {
                if (block.ColumnIndex >= 0 && block.ColumnIndex < columnCount)
                {
                    if (!string.IsNullOrEmpty(columnTexts[block.ColumnIndex]))
                    {
                        columnTexts[block.ColumnIndex] += " ";
                    }
                    columnTexts[block.ColumnIndex] += block.Text;
                }
            }

            // Determina largura de cada coluna
            int columnWidth = 30; // Largura padrão
            
            // Formata as colunas
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < columnCount; i++)
            {
                string text = columnTexts[i];
                if (text.Length > columnWidth)
                {
                    text = text.Substring(0, columnWidth - 3) + "...";
                }
                
                line.Append(text.PadRight(columnWidth));
                
                if (i < columnCount - 1)
                {
                    line.Append(" | "); // Separador de coluna
                }
            }

            return line.ToString();
        }

        private string BuildSingleColumnLine(List<TextBlock> lineBlocks)
        {
            StringBuilder line = new StringBuilder();
            TextBlock? previous = null;

            foreach (var block in lineBlocks)
            {
                if (previous != null)
                {
                    // Calcula espaços entre blocos
                    float gap = block.Left - previous.Right;
                    
                    if (gap > 3) // Gap significativo
                    {
                        int spaces = Math.Max(1, (int)(gap / 3));
                        line.Append(new string(' ', spaces));
                    }
                }

                line.Append(block.Text);
                previous = block;
            }

            return line.ToString();
        }
    }
}