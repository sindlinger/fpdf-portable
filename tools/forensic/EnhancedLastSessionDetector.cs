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
    /// Enhanced LastSessionTextDetector that uses iTextSharp's text extraction
    /// for better handling of complex encodings like hex-encoded text
    /// </summary>
    public class EnhancedLastSessionTextDetector
    {
        private PdfReader reader;
        private string filePath;
        private byte[] pdfBytes;
        
        public EnhancedLastSessionTextDetector(PdfReader reader, string filePath)
        {
            this.reader = reader;
            this.filePath = filePath;
            this.pdfBytes = File.ReadAllBytes(filePath);
        }
        
        public LastSessionAnalysisReport AnalyzeLastSession()
        {
            var report = new LastSessionAnalysisReport();
            
            // Step 1: Identify incremental updates
            var incrementalUpdates = DetectIncrementalUpdates();
            
            if (incrementalUpdates.Count == 0)
            {
                report.SessionType = "Single Session Document";
                report.LastSessionTexts = new List<TextAddition>();
                return report;
            }
            
            // Step 2: Get last update info
            var lastUpdate = incrementalUpdates.Last();
            report.LastUpdateInfo = lastUpdate;
            report.SessionType = "Incremental Update";
            
            // Step 3: Extract texts from modified pages
            report.LastSessionTexts = ExtractTextsFromModifiedPages(lastUpdate);
            report.AffectedPages = lastUpdate.ModifiedPages;
            
            // Step 4: Organize by page
            foreach (var textAddition in report.LastSessionTexts)
            {
                if (!report.PageTextAdditions.ContainsKey(textAddition.PageNumber))
                {
                    report.PageTextAdditions[textAddition.PageNumber] = new List<string>();
                }
                report.PageTextAdditions[textAddition.PageNumber].Add(textAddition.Text);
            }
            
            return report;
        }
        
        private List<IncrementalUpdateInfo> DetectIncrementalUpdates()
        {
            var updates = new List<IncrementalUpdateInfo>();
            var content = Encoding.ASCII.GetString(pdfBytes);
            
            // Find all %%EOF markers
            var eofPattern = @"%%EOF";
            var eofMatches = Regex.Matches(content, eofPattern);
            
            if (eofMatches.Count <= 1)
                return updates;
            
            // For each incremental update
            for (int i = 1; i < eofMatches.Count; i++)
            {
                var update = new IncrementalUpdateInfo
                {
                    UpdateNumber = i,
                    StartOffset = eofMatches[i - 1].Index + eofMatches[i - 1].Length,
                    EndOffset = eofMatches[i].Index + eofMatches[i].Length
                };
                
                // Extract update content
                var updateContent = content.Substring(update.StartOffset, update.EndOffset - update.StartOffset);
                
                // Parse xref to find modified objects
                var xrefMatch = Regex.Match(updateContent, @"xref\s*(.*?)trailer", RegexOptions.Singleline);
                if (xrefMatch.Success)
                {
                    update.ModifiedObjects = ParseXrefSection(xrefMatch.Groups[1].Value);
                    
                    // Determine which pages were affected
                    update.ModifiedPages = DetermineAffectedPages(update.ModifiedObjects);
                }
                
                updates.Add(update);
            }
            
            return updates;
        }
        
        private List<int> ParseXrefSection(string xrefContent)
        {
            var objectNumbers = new List<int>();
            var lines = xrefContent.Split('\n');
            
            int currentObj = 0;
            int count = 0;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                // Header line (e.g., "0 6")
                if (Regex.IsMatch(trimmed, @"^\d+\s+\d+$"))
                {
                    var parts = trimmed.Split(' ');
                    currentObj = int.Parse(parts[0]);
                    count = int.Parse(parts[1]);
                }
                // Entry line
                else if (Regex.IsMatch(trimmed, @"^\d{10}\s+\d{5}\s+[fn]$") && count > 0)
                {
                    var inUse = trimmed.EndsWith("n");
                    if (inUse && currentObj > 0)
                    {
                        objectNumbers.Add(currentObj);
                    }
                    currentObj++;
                    count--;
                }
            }
            
            return objectNumbers;
        }
        
        private List<int> DetermineAffectedPages(List<int> modifiedObjects)
        {
            var affectedPages = new HashSet<int>();
            
            foreach (var objNum in modifiedObjects)
            {
                // Check if this object is a page or content stream
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    
                    // Check if modified object is the page itself
                    var pageRef = reader.GetPageOrigRef(pageNum);
                    if (pageRef != null && pageRef.Number == objNum)
                    {
                        affectedPages.Add(pageNum);
                        continue;
                    }
                    
                    // Check if modified object is in page contents
                    var contents = page.Get(PdfName.CONTENTS);
                    if (IsObjectInContents(contents, objNum))
                    {
                        affectedPages.Add(pageNum);
                    }
                }
            }
            
            return affectedPages.OrderBy(p => p).ToList();
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
                    if (item.IsIndirect())
                    {
                        var indRef = (PRIndirectReference)item;
                        if (indRef.Number == objNum)
                            return true;
                    }
                }
            }
            else if (contents.IsIndirect())
            {
                var indRef = (PRIndirectReference)contents;
                if (indRef.Number == objNum)
                    return true;
            }
            
            return false;
        }
        
        private List<TextAddition> ExtractTextsFromModifiedPages(IncrementalUpdateInfo lastUpdate)
        {
            var textAdditions = new List<TextAddition>();
            
            // For each affected page, extract all text
            foreach (var pageNum in lastUpdate.ModifiedPages)
            {
                try
                {
                    // Use iTextSharp's text extraction
                    var pageText = PdfTextExtractor.GetTextFromPage(reader, pageNum);
                    
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        // For forensic purposes, we consider all text on modified pages
                        // as potentially added in the last session
                        textAdditions.Add(new TextAddition
                        {
                            PageNumber = pageNum,
                            Text = pageText,
                            Type = "Page Content",
                            ObjectNumber = 0,
                            Generation = 0
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Log extraction error but continue
                    Console.Error.WriteLine($"Error extracting text from page {pageNum}: {ex.Message}");
                }
            }
            
            // Also check for modified annotations
            foreach (var objNum in lastUpdate.ModifiedObjects)
            {
                try
                {
                    var obj = reader.GetPdfObject(objNum);
                    if (obj != null && obj.IsDictionary())
                    {
                        var dict = (PdfDictionary)obj;
                        var type = dict.GetAsName(PdfName.TYPE);
                        
                        if (type != null && type.Equals(PdfName.ANNOT))
                        {
                            // Extract annotation text
                            var contents = dict.GetAsString(PdfName.CONTENTS);
                            if (contents != null)
                            {
                                textAdditions.Add(new TextAddition
                                {
                                    ObjectNumber = objNum,
                                    Text = contents.ToUnicodeString(),
                                    Type = "Annotation",
                                    PageNumber = GetAnnotationPage(dict)
                                });
                            }
                        }
                    }
                }
                catch { }
            }
            
            return textAdditions;
        }
        
        private int GetAnnotationPage(PdfDictionary annotation)
        {
            // Try to find which page this annotation belongs to
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                var page = reader.GetPageN(pageNum);
                var annots = page.GetAsArray(PdfName.ANNOTS);
                
                if (annots != null)
                {
                    for (int i = 0; i < annots.Size; i++)
                    {
                        var annotRef = annots.GetDirectObject(i);
                        if (annotRef.Equals(annotation))
                        {
                            return pageNum;
                        }
                    }
                }
            }
            
            return 0;
        }
        
        // Enhanced update info with page tracking
        public class IncrementalUpdateInfo
        {
            public int UpdateNumber { get; set; }
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
            public List<int> ModifiedObjects { get; set; } = new List<int>();
            public List<int> ModifiedPages { get; set; } = new List<int>();
            public int XrefOffset { get; set; }
        }
    }
}