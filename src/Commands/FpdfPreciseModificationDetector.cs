using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace FilterPDF
{
    /// <summary>
    /// Detector preciso de modificações que analisa objetos individuais
    /// e extrai apenas o texto que foi realmente modificado
    /// </summary>
    public class FpdfPreciseModificationDetector
    {
        private PdfReader reader;
        private string filePath;
        private byte[] pdfBytes;
        
        // Configurações de detecção
        private const float COORDINATE_TOLERANCE = 2.0f;
        private const float FONT_PROXIMITY_THRESHOLD = 50.0f;
        private const float LINE_HEIGHT_MULTIPLIER = 1.5f;
        
        public FpdfPreciseModificationDetector(PdfReader reader, string filePath)
        {
            this.reader = reader;
            this.filePath = filePath;
            this.pdfBytes = File.ReadAllBytes(filePath);
        }
        
        /// <summary>
        /// Analisa modificações com precisão de objeto
        /// </summary>
        public PreciseModificationReport AnalyzeModifications()
        {
            var report = new PreciseModificationReport();
            
            // Step 1: Identificar objetos modificados
            var modifiedObjects = IdentifyModifiedObjects();
            report.TotalModifiedObjects = modifiedObjects.Count;
            
            if (modifiedObjects.Count == 0)
            {
                report.HasModifications = false;
                return report;
            }
            
            report.HasModifications = true;
            
            // Step 2: Analisar cada objeto modificado
            foreach (var objInfo in modifiedObjects)
            {
                var modification = AnalyzeObjectModification(objInfo);
                if (modification != null)
                {
                    report.Modifications.Add(modification);
                }
            }
            
            // Step 3: Detectar padrões de modificação
            DetectModificationPatterns(report);
            
            // Step 4: Calcular confidence score
            CalculateConfidenceScores(report);
            
            return report;
        }
        
        /// <summary>
        /// Identifica todos os objetos modificados no PDF
        /// </summary>
        private List<PreciseModifiedObjectInfo> IdentifyModifiedObjects()
        {
            var modifiedObjects = new List<PreciseModifiedObjectInfo>();
            
            // Método 1: Objetos com generation > 0
            for (int i = 1; i < reader.XrefSize; i++)
            {
                try
                {
                    var obj = reader.GetPdfObject(i);
                    if (obj != null && obj is PRIndirectReference)
                    {
                        var indRef = (PRIndirectReference)obj;
                        if (indRef.Generation > 0)
                        {
                            modifiedObjects.Add(new PreciseModifiedObjectInfo
                            {
                                ObjectNumber = i,
                                Generation = indRef.Generation,
                                DetectionMethod = "Generation > 0"
                            });
                        }
                    }
                }
                catch { }
            }
            
            // Método 2: Objetos no último incremental update
            var incrementalObjects = GetObjectsFromLastUpdate();
            foreach (var objNum in incrementalObjects)
            {
                if (!modifiedObjects.Any(m => m.ObjectNumber == objNum))
                {
                    modifiedObjects.Add(new PreciseModifiedObjectInfo
                    {
                        ObjectNumber = objNum,
                        Generation = 0,
                        DetectionMethod = "Incremental Update"
                    });
                }
            }
            
            return modifiedObjects;
        }
        
        /// <summary>
        /// Analisa modificação específica de um objeto
        /// </summary>
        private ObjectModification? AnalyzeObjectModification(PreciseModifiedObjectInfo objInfo)
        {
            try
            {
                var obj = reader.GetPdfObject(objInfo.ObjectNumber);
                if (obj == null) return null;
                
                var modification = new ObjectModification
                {
                    ObjectNumber = objInfo.ObjectNumber,
                    Generation = objInfo.Generation,
                    DetectionMethod = objInfo.DetectionMethod
                };
                
                // Determinar tipo de objeto
                if (obj.IsStream())
                {
                    modification.ObjectType = "Stream";
                    AnalyzeStreamModification(modification, (PRStream)obj);
                }
                else if (obj.IsDictionary())
                {
                    var dict = (PdfDictionary)obj;
                    modification.ObjectType = DetermineObjectType(dict);
                    AnalyzeDictionaryModification(modification, dict);
                }
                
                // Encontrar em qual página está
                modification.PageNumber = FindObjectPage(objInfo.ObjectNumber);
                
                return modification;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao analisar objeto {objInfo.ObjectNumber}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Analisa modificações em streams (onde geralmente está o texto)
        /// </summary>
        private void AnalyzeStreamModification(ObjectModification modification, PRStream stream)
        {
            var bytes = PdfReader.GetStreamBytes(stream);
            var content = Encoding.ASCII.GetString(bytes);
            
            // Detectar operações de texto
            if (content.Contains("BT") && content.Contains("ET"))
            {
                modification.ContainsText = true;
                
                // Extrair textos e suas coordenadas
                var textOperations = ExtractTextOperations(content);
                
                foreach (var textOp in textOperations)
                {
                    var textMod = new TextModification
                    {
                        Text = textOp.Text,
                        X = textOp.X,
                        Y = textOp.Y,
                        FontName = textOp.Font,
                        FontSize = textOp.FontSize
                    };
                    
                    // Verificar se é sobreposição
                    if (IsTextOverlay(textOp, modification.PageNumber))
                    {
                        textMod.ModificationType = ModificationType.Overlay;
                        textMod.OverlaidText = GetTextAtPosition(textOp.X, textOp.Y, modification.PageNumber);
                    }
                    else
                    {
                        textMod.ModificationType = ModificationType.Addition;
                    }
                    
                    modification.TextModifications.Add(textMod);
                }
                
                // Detectar mudanças de fonte
                var fontChanges = DetectFontChanges(textOperations);
                modification.FontChanges = fontChanges.Count;
                
                // Detectar gaps no fluxo
                var gaps = DetectTextFlowGaps(textOperations);
                modification.FlowGaps = gaps.Count;
            }
        }
        
        /// <summary>
        /// Extrai operações de texto com coordenadas
        /// </summary>
        private List<TextOperation> ExtractTextOperations(string streamContent)
        {
            var operations = new List<TextOperation>();
            var lines = streamContent.Split('\n');
            
            float currentX = 0, currentY = 0;
            string currentFont = "";
            float currentFontSize = 0;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Posicionamento de texto (Td)
                var tdMatch = Regex.Match(line, @"([\d.-]+)\s+([\d.-]+)\s+Td");
                if (tdMatch.Success)
                {
                    currentX += float.Parse(tdMatch.Groups[1].Value);
                    currentY += float.Parse(tdMatch.Groups[2].Value);
                }
                
                // Matriz de texto (Tm)
                var tmMatch = Regex.Match(line, @"[\d.-]+\s+[\d.-]+\s+[\d.-]+\s+[\d.-]+\s+([\d.-]+)\s+([\d.-]+)\s+Tm");
                if (tmMatch.Success)
                {
                    currentX = float.Parse(tmMatch.Groups[1].Value);
                    currentY = float.Parse(tmMatch.Groups[2].Value);
                }
                
                // Fonte (Tf)
                var tfMatch = Regex.Match(line, @"/([\w\d]+)\s+([\d.]+)\s+Tf");
                if (tfMatch.Success)
                {
                    currentFont = tfMatch.Groups[1].Value;
                    currentFontSize = float.Parse(tfMatch.Groups[2].Value);
                }
                
                // Texto (Tj)
                var tjMatch = Regex.Match(line, @"\((.*?)\)\s*Tj");
                if (tjMatch.Success)
                {
                    operations.Add(new TextOperation
                    {
                        Text = UnescapePdfString(tjMatch.Groups[1].Value),
                        X = currentX,
                        Y = currentY,
                        Font = currentFont,
                        FontSize = currentFontSize
                    });
                }
                
                // Texto hexadecimal
                var hexMatch = Regex.Match(line, @"<([0-9A-Fa-f]+)>\s*Tj");
                if (hexMatch.Success)
                {
                    operations.Add(new TextOperation
                    {
                        Text = $"[HEX:{hexMatch.Groups[1].Value}]",
                        X = currentX,
                        Y = currentY,
                        Font = currentFont,
                        FontSize = currentFontSize,
                        IsHex = true
                    });
                }
            }
            
            return operations;
        }
        
        /// <summary>
        /// Verifica se texto está sobrepondo outro
        /// </summary>
        private bool IsTextOverlay(TextOperation textOp, int pageNumber)
        {
            if (pageNumber <= 0) return false;
            
            try
            {
                // Usar estratégia de extração para obter todos os textos da página
                var strategy = new SimpleTextExtractionStrategy();
                var pageText = PdfTextExtractor.GetTextFromPage(reader, pageNumber, strategy);
                
                // Verificar se o texto da operação já existe na página (indicando overlay)
                if (!string.IsNullOrEmpty(pageText) && textOp != null && !string.IsNullOrEmpty(textOp.Text))
                {
                    // Se o texto já existe na página na mesma posição aproximada,
                    // pode ser uma tentativa de sobrepor/ocultar texto existente
                    if (pageText.Contains(textOp.Text))
                    {
                        // TODO: Aqui seria ideal usar CoordinateTextExtractor para verificar
                        // se o texto está exatamente na mesma posição (sobreposição real)
                        // Por ora, assumimos que texto duplicado pode indicar tentativa de overlay
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Detecta mudanças abruptas de fonte
        /// </summary>
        private List<PreciseFontChange> DetectFontChanges(List<TextOperation> operations)
        {
            var changes = new List<PreciseFontChange>();
            
            for (int i = 1; i < operations.Count; i++)
            {
                var prev = operations[i - 1];
                var curr = operations[i];
                
                if (prev.Font != curr.Font)
                {
                    // Verificar se é mudança abrupta (mesma linha)
                    if (Math.Abs(curr.Y - prev.Y) < COORDINATE_TOLERANCE)
                    {
                        changes.Add(new PreciseFontChange
                        {
                            Position = i,
                            FromFont = prev.Font,
                            ToFont = curr.Font,
                            X = curr.X,
                            Y = curr.Y
                        });
                    }
                }
            }
            
            return changes;
        }
        
        /// <summary>
        /// Detecta gaps no fluxo de texto
        /// </summary>
        private List<TextFlowGap> DetectTextFlowGaps(List<TextOperation> operations)
        {
            var gaps = new List<TextFlowGap>();
            
            // Agrupar por linha (Y similar)
            var lines = operations.GroupBy(op => Math.Round(op.Y / 10) * 10)
                                 .OrderByDescending(g => g.Key);
            
            float previousY = float.MaxValue;
            foreach (var line in lines)
            {
                var currentY = line.First().Y;
                
                if (previousY != float.MaxValue)
                {
                    var gap = previousY - currentY;
                    var expectedGap = line.First().FontSize * LINE_HEIGHT_MULTIPLIER;
                    
                    if (gap > expectedGap * 1.5)
                    {
                        gaps.Add(new TextFlowGap
                        {
                            StartY = currentY,
                            EndY = previousY,
                            GapSize = gap,
                            ExpectedSize = expectedGap
                        });
                    }
                }
                
                previousY = currentY;
            }
            
            return gaps;
        }
        
        /// <summary>
        /// Detecta padrões gerais de modificação
        /// </summary>
        private void DetectModificationPatterns(PreciseModificationReport report)
        {
            // Padrão 1: Múltiplas modificações na mesma página
            var pageGroups = report.Modifications.GroupBy(m => m.PageNumber);
            foreach (var group in pageGroups.Where(g => g.Count() > 3))
            {
                report.Patterns.Add(new ModificationPattern
                {
                    Type = "Multiple modifications on same page",
                    Description = $"Page {group.Key} has {group.Count()} modifications",
                    Severity = "High"
                });
            }
            
            // Padrão 2: Mudanças de fonte consistentes
            var fontChanges = report.Modifications.Where(m => m.FontChanges > 0);
            if (fontChanges.Count() > 2)
            {
                report.Patterns.Add(new ModificationPattern
                {
                    Type = "Consistent font changes",
                    Description = $"{fontChanges.Count()} objects with font changes detected",
                    Severity = "Medium"
                });
            }
            
            // Padrão 3: Texto sobreposto
            var overlays = report.Modifications
                .Where(m => m.TextModifications.Any(t => t.ModificationType == ModificationType.Overlay));
            if (overlays.Any())
            {
                report.Patterns.Add(new ModificationPattern
                {
                    Type = "Text overlay detected",
                    Description = $"{overlays.Count()} objects contain overlaid text",
                    Severity = "High"
                });
            }
        }
        
        /// <summary>
        /// Calcula confidence scores para cada modificação
        /// </summary>
        private void CalculateConfidenceScores(PreciseModificationReport report)
        {
            foreach (var mod in report.Modifications)
            {
                float score = 0.5f; // Base score
                
                // Generation > 0 aumenta confiança
                if (mod.Generation > 0)
                    score += 0.2f;
                
                // Texto sobreposto é alta confiança
                if (mod.TextModifications.Any(t => t.ModificationType == ModificationType.Overlay))
                    score += 0.3f;
                
                // Mudanças de fonte aumentam confiança
                if (mod.FontChanges > 0)
                    score += 0.1f * Math.Min(mod.FontChanges, 3);
                
                // Gaps no fluxo aumentam confiança
                if (mod.FlowGaps > 0)
                    score += 0.1f;
                
                mod.ConfidenceScore = Math.Min(score, 1.0f);
            }
            
            // Confidence geral do relatório
            if (report.Modifications.Any())
            {
                report.OverallConfidence = report.Modifications.Average(m => m.ConfidenceScore);
            }
        }
        
        // Métodos auxiliares
        
        private List<int> GetObjectsFromLastUpdate()
        {
            var objects = new List<int>();
            var content = Encoding.ASCII.GetString(pdfBytes);
            
            // Encontrar último xref
            var lastXrefMatch = Regex.Match(content, @"xref\s*(.*?)trailer", 
                RegexOptions.Singleline | RegexOptions.RightToLeft);
            
            if (lastXrefMatch.Success)
            {
                var xrefContent = lastXrefMatch.Groups[1].Value;
                var lines = xrefContent.Split('\n');
                
                int currentObj = 0;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (Regex.IsMatch(trimmed, @"^\d+\s+\d+$"))
                    {
                        var parts = trimmed.Split(' ');
                        currentObj = int.Parse(parts[0]);
                    }
                    else if (Regex.IsMatch(trimmed, @"^\d{10}\s+\d{5}\s+n$"))
                    {
                        if (currentObj > 0)
                            objects.Add(currentObj);
                        currentObj++;
                    }
                }
            }
            
            return objects;
        }
        
        private string DetermineObjectType(PdfDictionary dict)
        {
            var type = dict.GetAsName(PdfName.TYPE);
            if (type != null)
                return type.ToString();
            
            if (dict.Contains(PdfName.CONTENTS))
                return "/Page-like";
            if (dict.Contains(PdfName.BASEFONT))
                return "/Font";
            
            return "/Dictionary";
        }
        
        private int FindObjectPage(int objectNumber)
        {
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                var page = reader.GetPageN(pageNum);
                
                // Verificar se é a própria página
                var pageRef = reader.GetPageOrigRef(pageNum);
                if (pageRef != null && pageRef.Number == objectNumber)
                    return pageNum;
                
                // Verificar contents
                var contents = page.Get(PdfName.CONTENTS);
                if (IsObjectInContents(contents, objectNumber))
                    return pageNum;
                
                // Verificar anotações
                var annots = page.GetAsArray(PdfName.ANNOTS);
                if (annots != null)
                {
                    for (int i = 0; i < annots.Size; i++)
                    {
                        var item = annots.GetDirectObject(i);
                        if (item.IsIndirect() && ((PRIndirectReference)item).Number == objectNumber)
                            return pageNum;
                    }
                }
            }
            
            return 0;
        }
        
        private bool IsObjectInContents(PdfObject contents, int objNum)
        {
            if (contents == null) return false;
            
            if (contents.IsArray())
            {
                var array = (PdfArray)contents;
                for (int i = 0; i < array.Size; i++)
                {
                    var item = array.GetDirectObject(i);
                    if (item.IsIndirect() && ((PRIndirectReference)item).Number == objNum)
                        return true;
                }
            }
            else if (contents.IsIndirect())
            {
                return ((PRIndirectReference)contents).Number == objNum;
            }
            
            return false;
        }
        
        private string GetTextAtPosition(float x, float y, int pageNumber)
        {
            // Implementação simplificada - em produção usaria LocationTextExtractionStrategy
            return "[Original text]";
        }
        
        private string UnescapePdfString(string pdfString)
        {
            return pdfString
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\(", "(")
                .Replace("\\)", ")")
                .Replace("\\\\", "\\");
        }
        
        private void AnalyzeDictionaryModification(ObjectModification modification, PdfDictionary dict)
        {
            // Analisar tipo de dicionário
            var type = dict.GetAsName(PdfName.TYPE);
            if (type != null)
            {
                if (type.Equals(PdfName.ANNOT))
                {
                    // Anotação
                    var contents = dict.GetAsString(PdfName.CONTENTS);
                    if (contents != null)
                    {
                        modification.ContainsText = true;
                        modification.TextModifications.Add(new TextModification
                        {
                            Text = contents.ToUnicodeString(),
                            ModificationType = ModificationType.Annotation
                        });
                    }
                }
            }
        }
    }
    
    // Classes de suporte
    
    public class PreciseModificationReport
    {
        public bool HasModifications { get; set; }
        public int TotalModifiedObjects { get; set; }
        public List<ObjectModification> Modifications { get; set; } = new List<ObjectModification>();
        public List<ModificationPattern> Patterns { get; set; } = new List<ModificationPattern>();
        public float OverallConfidence { get; set; }
    }
    
    public class PreciseModifiedObjectInfo
    {
        public int ObjectNumber { get; set; }
        public int Generation { get; set; }
        public string DetectionMethod { get; set; } = "";
    }
    
    public class ObjectModification
    {
        public int ObjectNumber { get; set; }
        public int Generation { get; set; }
        public string ObjectType { get; set; } = "";
        public string DetectionMethod { get; set; } = "";
        public int PageNumber { get; set; }
        public bool ContainsText { get; set; }
        public List<TextModification> TextModifications { get; set; } = new List<TextModification>();
        public int FontChanges { get; set; }
        public int FlowGaps { get; set; }
        public float ConfidenceScore { get; set; }
    }
    
    public class TextModification
    {
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public string FontName { get; set; } = "";
        public float FontSize { get; set; }
        public ModificationType ModificationType { get; set; }
        public string OverlaidText { get; set; } = "";
    }
    
    public class TextOperation
    {
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public string Font { get; set; } = "";
        public float FontSize { get; set; }
        public bool IsHex { get; set; }
    }
    
    public class PreciseFontChange
    {
        public int Position { get; set; }
        public string FromFont { get; set; } = "";
        public string ToFont { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
    }
    
    public class TextFlowGap
    {
        public float StartY { get; set; }
        public float EndY { get; set; }
        public float GapSize { get; set; }
        public float ExpectedSize { get; set; }
    }
    
    public class ModificationPattern
    {
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public string Severity { get; set; } = "";
    }
    
    public enum ModificationType
    {
        Addition,
        Overlay,
        Deletion,
        Replacement,
        Annotation
    }
}