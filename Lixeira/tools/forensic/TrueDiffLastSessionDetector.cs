using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace PDFLayoutPreservingConverter
{
    /// <summary>
    /// TRUE DIFF Last Session Detector - Compara versões anteriores com atuais
    /// para extrair APENAS o que foi realmente adicionado/modificado
    /// </summary>
    public class TrueDiffLastSessionDetector
    {
        private string filePath;
        private byte[] pdfBytes;
        
        public TrueDiffLastSessionDetector(string filePath)
        {
            this.filePath = filePath;
            this.pdfBytes = File.ReadAllBytes(filePath);
        }
        
        public TrueDiffAnalysisReport AnalyzeLastSession()
        {
            var report = new TrueDiffAnalysisReport();
            
            // Step 1: Find all %%EOF positions
            var eofPositions = FindAllEOFPositions();
            
            if (eofPositions.Count <= 1)
            {
                report.SessionType = "Single Session Document";
                report.HasModifications = false;
                return report;
            }
            
            report.SessionType = "Incremental Update Document";
            report.HasModifications = true;
            report.TotalVersions = eofPositions.Count;
            
            // Step 2: Extract previous version (before last %%EOF)
            var previousVersionBytes = new byte[eofPositions[eofPositions.Count - 2]];
            Array.Copy(pdfBytes, 0, previousVersionBytes, 0, previousVersionBytes.Length);
            
            // Step 3: Create readers for both versions
            PdfReader previousReader = null;
            PdfReader currentReader = null;
            
            try
            {
                // Save previous version to temp file
                var tempFile = Path.GetTempFileName();
                File.WriteAllBytes(tempFile, previousVersionBytes);
                
                previousReader = new PdfReader(tempFile);
                currentReader = new PdfReader(filePath);
                
                // Step 4: Compare objects between versions
                report.ObjectDifferences = CompareObjectsBetweenVersions(previousReader, currentReader);
                
                // Step 5: Extract text differences
                report.TextDifferences = ExtractTextDifferences(previousReader, currentReader, report.ObjectDifferences);
                
                // Step 6: Identify what was specifically added
                foreach (var diff in report.TextDifferences)
                {
                    if (diff.Type == DifferenceType.Added)
                    {
                        report.AddedTexts.Add(diff.NewText);
                    }
                    else if (diff.Type == DifferenceType.Modified)
                    {
                        // Extract only the new part
                        var addedPart = ExtractAddedPortion(diff.OldText, diff.NewText);
                        if (!string.IsNullOrEmpty(addedPart))
                        {
                            report.AddedTexts.Add(addedPart);
                        }
                    }
                }
                
                // Cleanup
                File.Delete(tempFile);
            }
            finally
            {
                previousReader?.Close();
                currentReader?.Close();
            }
            
            return report;
        }
        
        private List<int> FindAllEOFPositions()
        {
            var positions = new List<int>();
            var content = Encoding.ASCII.GetString(pdfBytes);
            
            int pos = 0;
            while ((pos = content.IndexOf("%%EOF", pos)) != -1)
            {
                positions.Add(pos + 5); // Include %%EOF
                pos += 5;
            }
            
            return positions;
        }
        
        private List<ObjectDifference> CompareObjectsBetweenVersions(PdfReader previous, PdfReader current)
        {
            var differences = new List<ObjectDifference>();
            var checkedObjects = new HashSet<int>();
            
            // Check all objects in current version
            for (int i = 1; i < current.XrefSize; i++)
            {
                checkedObjects.Add(i);
                
                try
                {
                    var currentObj = current.GetPdfObject(i);
                    if (currentObj == null) continue;
                    
                    PdfObject previousObj = null;
                    if (i < previous.XrefSize)
                    {
                        try
                        {
                            previousObj = previous.GetPdfObject(i);
                        }
                        catch { }
                    }
                    
                    if (previousObj == null && currentObj != null)
                    {
                        // New object
                        differences.Add(new ObjectDifference
                        {
                            ObjectNumber = i,
                            Type = DifferenceType.Added,
                            Description = "New object created"
                        });
                    }
                    else if (previousObj != null && currentObj != null)
                    {
                        // Compare objects
                        if (!AreObjectsEqual(previousObj, currentObj))
                        {
                            differences.Add(new ObjectDifference
                            {
                                ObjectNumber = i,
                                Type = DifferenceType.Modified,
                                Description = "Object modified"
                            });
                        }
                    }
                }
                catch { }
            }
            
            // Check for deleted objects
            for (int i = 1; i < previous.XrefSize; i++)
            {
                if (!checkedObjects.Contains(i))
                {
                    try
                    {
                        var previousObj = previous.GetPdfObject(i);
                        if (previousObj != null)
                        {
                            differences.Add(new ObjectDifference
                            {
                                ObjectNumber = i,
                                Type = DifferenceType.Deleted,
                                Description = "Object deleted"
                            });
                        }
                    }
                    catch { }
                }
            }
            
            return differences;
        }
        
        private bool AreObjectsEqual(PdfObject obj1, PdfObject obj2)
        {
            // Simple comparison - could be enhanced
            if (obj1.Type != obj2.Type) return false;
            
            if (obj1.IsStream() && obj2.IsStream())
            {
                try
                {
                    var bytes1 = PdfReader.GetStreamBytes((PRStream)obj1);
                    var bytes2 = PdfReader.GetStreamBytes((PRStream)obj2);
                    
                    if (bytes1.Length != bytes2.Length) return false;
                    
                    for (int i = 0; i < bytes1.Length; i++)
                    {
                        if (bytes1[i] != bytes2[i]) return false;
                    }
                    
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            
            return obj1.ToString() == obj2.ToString();
        }
        
        private List<TextDifference> ExtractTextDifferences(PdfReader previous, PdfReader current, List<ObjectDifference> objectDiffs)
        {
            var textDiffs = new List<TextDifference>();
            
            // For each modified page, extract and compare text
            var modifiedPages = new HashSet<int>();
            
            foreach (var diff in objectDiffs)
            {
                // Find which pages are affected
                for (int pageNum = 1; pageNum <= current.NumberOfPages; pageNum++)
                {
                    if (IsObjectRelatedToPage(current, diff.ObjectNumber, pageNum))
                    {
                        modifiedPages.Add(pageNum);
                    }
                }
            }
            
            // Compare text on each modified page
            foreach (var pageNum in modifiedPages)
            {
                string previousText = "";
                string currentText = "";
                
                try
                {
                    if (pageNum <= previous.NumberOfPages)
                    {
                        previousText = PdfTextExtractor.GetTextFromPage(previous, pageNum);
                    }
                    
                    currentText = PdfTextExtractor.GetTextFromPage(current, pageNum);
                    
                    if (previousText != currentText)
                    {
                        textDiffs.Add(new TextDifference
                        {
                            PageNumber = pageNum,
                            Type = string.IsNullOrEmpty(previousText) ? DifferenceType.Added : DifferenceType.Modified,
                            OldText = previousText,
                            NewText = currentText
                        });
                    }
                }
                catch { }
            }
            
            return textDiffs;
        }
        
        private bool IsObjectRelatedToPage(PdfReader reader, int objNum, int pageNum)
        {
            try
            {
                var page = reader.GetPageN(pageNum);
                
                // Check if it's the page object itself
                var pageRef = reader.GetPageOrigRef(pageNum);
                if (pageRef != null && pageRef.Number == objNum)
                    return true;
                
                // Check contents
                var contents = page.Get(PdfName.CONTENTS);
                if (contents != null)
                {
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
                    else if (contents.IsIndirect() && ((PRIndirectReference)contents).Number == objNum)
                    {
                        return true;
                    }
                }
                
                // Check annotations
                var annots = page.GetAsArray(PdfName.ANNOTS);
                if (annots != null)
                {
                    for (int i = 0; i < annots.Size; i++)
                    {
                        var item = annots.GetDirectObject(i);
                        if (item.IsIndirect() && ((PRIndirectReference)item).Number == objNum)
                            return true;
                    }
                }
            }
            catch { }
            
            return false;
        }
        
        private string ExtractAddedPortion(string oldText, string newText)
        {
            if (string.IsNullOrEmpty(oldText))
                return newText;
            
            // Simple approach - find where old text ends in new text
            if (newText.StartsWith(oldText))
            {
                return newText.Substring(oldText.Length).Trim();
            }
            
            // More complex diff would go here
            // For now, return everything after the last matching portion
            
            // Find longest common prefix
            int commonLength = 0;
            for (int i = 0; i < Math.Min(oldText.Length, newText.Length); i++)
            {
                if (oldText[i] == newText[i])
                    commonLength++;
                else
                    break;
            }
            
            if (commonLength > 0 && commonLength < newText.Length)
            {
                return newText.Substring(commonLength).Trim();
            }
            
            return newText; // Fallback to full new text
        }
    }
    
    public class TrueDiffAnalysisReport
    {
        public string SessionType { get; set; }
        public bool HasModifications { get; set; }
        public int TotalVersions { get; set; }
        public List<ObjectDifference> ObjectDifferences { get; set; } = new List<ObjectDifference>();
        public List<TextDifference> TextDifferences { get; set; } = new List<TextDifference>();
        public List<string> AddedTexts { get; set; } = new List<string>();
    }
    
    public class ObjectDifference
    {
        public int ObjectNumber { get; set; }
        public DifferenceType Type { get; set; }
        public string Description { get; set; }
    }
    
    public class TextDifference
    {
        public int PageNumber { get; set; }
        public DifferenceType Type { get; set; }
        public string OldText { get; set; }
        public string NewText { get; set; }
    }
    
    public enum DifferenceType
    {
        Added,
        Modified,
        Deleted
    }
}