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
    /// Intelligence Service Forensic Analyzer
    /// Detects texts inserted during the last PDF editing session
    /// Critical for document authenticity verification and tampering detection
    /// </summary>
    public class FpdfLastSessionTextDetector
    {
        private PdfReader reader;
        private string filePath = "";
        private byte[] pdfBytes = new byte[0];
        
        public FpdfLastSessionTextDetector(PdfReader reader, string filePath)
        {
            this.reader = reader;
            this.filePath = filePath;
            this.pdfBytes = File.ReadAllBytes(filePath);
        }
        
        /// <summary>
        /// Identifies all texts added in the last editing session
        /// </summary>
        public LastSessionAnalysisReport AnalyzeLastSession(bool useTimestampMethod = true)
        {
            var report = new LastSessionAnalysisReport();
            
            // Try timestamp-based method first (more accurate)
            if (useTimestampMethod)
            {
                var timestampReport = AnalyzeByTimestamp();
                if (timestampReport.HasResults)
                {
                    return timestampReport;
                }
            }
            
            // Fallback to incremental update method
            var incrementalUpdates = DetectIncrementalUpdates();
            if (incrementalUpdates.Count == 0)
            {
                // No incremental updates - analyze modified objects
                report.LastSessionTexts = AnalyzeModifiedObjectsForText();
                report.SessionType = "Single Session Document";
                return report;
            }
            
            // Step 2: Extract objects from last incremental update
            var lastUpdate = incrementalUpdates.Last();
            report.LastUpdateInfo = lastUpdate;
            report.SessionType = "Incremental Update";
            
            // Step 3: Parse the last update section
            var lastUpdateContent = ExtractUpdateContent(lastUpdate);
            var modifiedObjects = ParseModifiedObjects(lastUpdateContent);
            
            // Step 3b: Also extract all objects referenced in the last xref
            foreach (var objNum in lastUpdate.ModifiedObjects)
            {
                // Check if we already have this object
                if (!modifiedObjects.Any(o => o.ObjectNumber == objNum))
                {
                    // Try to extract it from the update content first
                    var objPattern = $@"({objNum})\s+(\d+)\s+obj\s*(.*?)\s*endobj";
                    var objMatch = Regex.Match(lastUpdateContent, objPattern, RegexOptions.Singleline);
                    if (objMatch.Success)
                    {
                        modifiedObjects.Add(new LastSessionObjectInfo
                        {
                            ObjectNumber = objNum,
                            Generation = int.Parse(objMatch.Groups[2].Value),
                            Content = objMatch.Groups[3].Value,
                            Type = DetermineObjectType(objMatch.Groups[3].Value)
                        });
                    }
                    else
                    {
                        // If not in update content, try to get from PdfReader
                        try
                        {
                            var pdfObj = reader.GetPdfObject(objNum);
                            if (pdfObj != null)
                            {
                                var objInfo = new LastSessionObjectInfo
                                {
                                    ObjectNumber = objNum,
                                    Generation = 0
                                };
                                
                                if (pdfObj.IsStream())
                                {
                                    objInfo.Type = "Stream";
                                    var stream = (PRStream)pdfObj;
                                    var bytes = PdfReader.GetStreamBytes(stream);
                                    // Store the decoded stream content directly
                                    objInfo.Content = Encoding.ASCII.GetString(bytes);
                                    
                                    // For text extraction, we'll need the resources
                                    objInfo.StreamObject = stream;
                                }
                                else if (pdfObj.IsDictionary())
                                {
                                    var dict = (PdfDictionary)pdfObj;
                                    objInfo.Type = DetermineObjectType(dict.ToString());
                                    objInfo.Content = dict.ToString();
                                }
                                
                                modifiedObjects.Add(objInfo);
                            }
                        }
                        catch { }
                    }
                }
            }
            
            // Step 4: Analyze each modified object for text content
            report.LastSessionTexts = AnalyzeObjectsForNewText(modifiedObjects);
            
            // Step 5: Cross-reference with page content
            report.AffectedPages = IdentifyAffectedPages(modifiedObjects);
            
            // Step 6: Extract actual text changes
            foreach (var pageNum in report.AffectedPages)
            {
                var pageTexts = ExtractPageTextFromObjects(pageNum, modifiedObjects);
                if (pageTexts.Any())
                {
                    report.PageTextAdditions[pageNum] = pageTexts;
                }
            }
            
            return report;
        }
        
        /// <summary>
        /// Detects all incremental updates in the PDF
        /// </summary>
        private List<IncrementalUpdateInfo> DetectIncrementalUpdates()
        {
            var updates = new List<IncrementalUpdateInfo>();
            var content = Encoding.ASCII.GetString(pdfBytes);
            
            // Find all %%EOF markers
            var eofPattern = @"%%EOF";
            var eofMatches = Regex.Matches(content, eofPattern);
            
            if (eofMatches.Count <= 1)
                return updates; // No incremental updates
            
            // Find all xref sections - more flexible pattern
            var xrefPattern = @"xref";
            var xrefPositions = new List<Match>();
            var match = Regex.Match(content, xrefPattern);
            while (match.Success)
            {
                xrefPositions.Add(match);
                match = match.NextMatch();
            }
            
            // Match xref sections with EOF markers
            for (int i = 0; i < eofMatches.Count; i++)
            {
                var update = new IncrementalUpdateInfo
                {
                    UpdateNumber = i + 1,
                    EndOffset = eofMatches[i].Index + eofMatches[i].Length
                };
                
                if (i > 0)
                {
                    update.StartOffset = eofMatches[i - 1].Index + eofMatches[i - 1].Length;
                }
                else
                {
                    update.StartOffset = 0;
                }
                
                // Find xref in this update
                foreach (var xrefMatch in xrefPositions)
                {
                    if (xrefMatch.Index >= update.StartOffset && xrefMatch.Index < update.EndOffset)
                    {
                        update.XrefOffset = xrefMatch.Index;
                        
                        // Extract xref content until trailer
                        var trailerPos = content.IndexOf("trailer", xrefMatch.Index);
                        if (trailerPos > xrefMatch.Index)
                        {
                            var xrefContent = content.Substring(xrefMatch.Index, trailerPos - xrefMatch.Index + 7); // +7 to include "trailer"
                            update.ModifiedObjects = ParseXrefSection(xrefContent);
                        }
                        break;
                    }
                }
                
                updates.Add(update);
            }
            
            return updates;
        }
        
        /// <summary>
        /// Parses xref section to get modified object numbers
        /// </summary>
        private List<int> ParseXrefSection(string xrefContent)
        {
            var objectNumbers = new List<int>();
            var lines = xrefContent.Split('\n');
            
            int currentObj = 0;
            int remainingCount = 0;
            bool isInLastUpdate = true; // We're parsing the last update's xref
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                // Parse header line (e.g., "0 6" or "4 1")
                if (Regex.IsMatch(trimmed, @"^\d+\s+\d+$"))
                {
                    var parts = trimmed.Split(' ');
                    currentObj = int.Parse(parts[0]);
                    remainingCount = int.Parse(parts[1]);
                    
                    // Skip the first entry if it's "0 1" (free object list)
                    if (currentObj == 0 && remainingCount == 1)
                    {
                        // This is the free object header, skip its entry
                        continue;
                    }
                }
                // Parse entry line (e.g., "0000000015 00000 n")
                else if (Regex.IsMatch(trimmed, @"^\d{10}\s+\d{5}\s+[fn]$") && remainingCount > 0)
                {
                    var parts = trimmed.Split(' ');
                    var generation = int.Parse(parts[1]);
                    var inUse = parts[2] == "n";
                    
                    // In the last xref of an incremental update, ALL listed objects
                    // (except object 0) are considered modified
                    if (inUse && currentObj > 0 && isInLastUpdate)
                    {
                        // Only add objects from the last update with generation 0
                        // (newer generations indicate multiple modifications)
                        if (generation == 0)
                        {
                            objectNumbers.Add(currentObj);
                        }
                    }
                    
                    currentObj++;
                    remainingCount--;
                }
                else if (trimmed == "trailer")
                {
                    // Stop parsing when we hit trailer
                    break;
                }
            }
            
            return objectNumbers;
        }
        
        /// <summary>
        /// Extracts content of a specific update
        /// </summary>
        private string ExtractUpdateContent(IncrementalUpdateInfo update)
        {
            var length = update.EndOffset - update.StartOffset;
            var updateBytes = new byte[length];
            Array.Copy(pdfBytes, update.StartOffset, updateBytes, 0, length);
            return Encoding.ASCII.GetString(updateBytes);
        }
        
        /// <summary>
        /// Parses modified objects from update content
        /// </summary>
        private List<LastSessionObjectInfo> ParseModifiedObjects(string updateContent)
        {
            var objects = new List<LastSessionObjectInfo>();
            
            // Pattern to find object definitions
            var objPattern = @"(\d+)\s+(\d+)\s+obj\s*(.*?)\s*endobj";
            var matches = Regex.Matches(updateContent, objPattern, RegexOptions.Singleline);
            
            foreach (Match match in matches)
            {
                var obj = new LastSessionObjectInfo
                {
                    ObjectNumber = int.Parse(match.Groups[1].Value),
                    Generation = int.Parse(match.Groups[2].Value),
                    Content = match.Groups[3].Value
                };
                
                // Determine object type
                if (obj.Content.Contains("/Page") && !obj.Content.Contains("/Pages"))
                    obj.Type = "Page";
                else if (obj.Content.Contains("stream"))
                    obj.Type = "Stream";
                else if (obj.Content.Contains("/Font"))
                    obj.Type = "Font";
                else if (obj.Content.Contains("/Annot"))
                    obj.Type = "Annotation";
                else
                    obj.Type = DetermineObjectType(obj.Content);
                
                objects.Add(obj);
            }
            
            return objects;
        }
        
        /// <summary>
        /// Analyzes objects for text content
        /// </summary>
        private List<TextAddition> AnalyzeObjectsForNewText(List<LastSessionObjectInfo> objects)
        {
            var textAdditions = new List<TextAddition>();
            
            foreach (var obj in objects)
            {
                if (obj.Type == "Stream")
                {
                    // Extract text from stream
                    var text = ExtractTextFromStream(obj.Content);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textAdditions.Add(new TextAddition
                        {
                            ObjectNumber = obj.ObjectNumber,
                            Generation = obj.Generation,
                            Text = text,
                            Type = "Content Stream"
                        });
                    }
                }
                else if (obj.Type == "Annotation")
                {
                    // Extract annotation text
                    var annotText = ExtractAnnotationText(obj.Content);
                    if (!string.IsNullOrWhiteSpace(annotText))
                    {
                        textAdditions.Add(new TextAddition
                        {
                            ObjectNumber = obj.ObjectNumber,
                            Generation = obj.Generation,
                            Text = annotText,
                            Type = "Annotation"
                        });
                    }
                }
            }
            
            return textAdditions;
        }
        
        /// <summary>
        /// Determines object type from content
        /// </summary>
        private string DetermineObjectType(string content)
        {
            if (content.Contains("stream"))
                return "Stream";
            else if (content.Contains("/Page") && !content.Contains("/Pages"))
                return "Page";
            else if (content.Contains("/Font"))
                return "Font";
            else if (content.Contains("/Annot"))
                return "Annotation";
            else
                return "Other";
        }
        
        /// <summary>
        /// Extracts text from a content stream
        /// </summary>
        private string ExtractTextFromStream(string streamContent)
        {
            var text = new StringBuilder();
            
            // Handle both formats: with stream/endstream markers or just content
            string streamData = streamContent;
            var streamMatch = Regex.Match(streamContent, @"stream\s*(.*?)\s*endstream", RegexOptions.Singleline);
            if (streamMatch.Success)
            {
                streamData = streamMatch.Groups[1].Value;
            }
            
            // Use HexTextDecoder for comprehensive text extraction
            var decoder = new HexTextDecoder(reader);
            var extractedTexts = decoder.ExtractAllTexts(streamData, null);
            
            foreach (var extractedText in extractedTexts)
            {
                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    text.Append(extractedText + " ");
                }
            }
            
            return text.ToString().Trim();
        }
        
        /// <summary>
        /// Unescapes PDF string literals
        /// </summary>
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
        
        /// <summary>
        /// Extracts text from annotation
        /// </summary>
        private string ExtractAnnotationText(string annotContent)
        {
            var text = new StringBuilder();
            
            // Extract /Contents
            var contentsMatch = Regex.Match(annotContent, @"/Contents\s*\((.*?)\)");
            if (contentsMatch.Success)
            {
                text.AppendLine("Contents: " + UnescapePdfString(contentsMatch.Groups[1].Value));
            }
            
            // Extract /T (Title)
            var titleMatch = Regex.Match(annotContent, @"/T\s*\((.*?)\)");
            if (titleMatch.Success)
            {
                text.AppendLine("Title: " + UnescapePdfString(titleMatch.Groups[1].Value));
            }
            
            // Extract /Subj (Subject)
            var subjMatch = Regex.Match(annotContent, @"/Subj\s*\((.*?)\)");
            if (subjMatch.Success)
            {
                text.AppendLine("Subject: " + UnescapePdfString(subjMatch.Groups[1].Value));
            }
            
            return text.ToString().Trim();
        }
        
        /// <summary>
        /// Identifies which pages were affected
        /// </summary>
        private List<int> IdentifyAffectedPages(List<LastSessionObjectInfo> objects)
        {
            var pages = new HashSet<int>();
            
            foreach (var obj in objects)
            {
                if (obj.Type == "Page")
                {
                    // This is a page object - extract page number
                    var pageNum = GetPageNumberFromObject(obj.ObjectNumber);
                    if (pageNum > 0)
                        pages.Add(pageNum);
                }
                else if (obj.Type == "Stream")
                {
                    // Find which page this stream belongs to
                    var pageNum = FindPageForContentStream(obj.ObjectNumber);
                    if (pageNum > 0)
                        pages.Add(pageNum);
                }
            }
            
            return pages.OrderBy(p => p).ToList();
        }
        
        /// <summary>
        /// Gets page number from object number
        /// </summary>
        private int GetPageNumberFromObject(int objectNumber)
        {
            try
            {
                var obj = reader.GetPdfObject(objectNumber);
                if (obj != null && obj.IsDictionary())
                {
                    var dict = (PdfDictionary)obj;
                    
                    // Check if this object is a page itself
                    if (dict.Contains(PdfName.TYPE) && PdfName.PAGE.Equals(dict.Get(PdfName.TYPE)))
                    {
                        // Try to find page number through page tree
                        for (int i = 1; i <= reader.NumberOfPages; i++)
                        {
                            var page = reader.GetPageN(i);
                            if (page == dict)
                            {
                                return i;
                            }
                        }
                    }
                    
                    // Try to find page number through page tree for other objects
                    for (int i = 1; i <= reader.NumberOfPages; i++)
                    {
                        var page = reader.GetPageN(i);
                        if (page != null)
                        {
                            // Check if this page object matches
                            var pageRef = reader.GetPageOrigRef(i);
                            if (pageRef != null && pageRef.Number == objectNumber)
                            {
                                return i;
                            }
                        }
                    }
                }
            }
            catch { }
            return 0;
        }
        
        /// <summary>
        /// Finds which page a content stream belongs to
        /// </summary>
        private int FindPageForContentStream(int streamObjectNumber)
        {
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                var page = reader.GetPageN(pageNum);
                var contents = page.Get(PdfName.CONTENTS);
                
                if (contents != null)
                {
                    if (contents.IsArray())
                    {
                        var array = (PdfArray)contents;
                        for (int i = 0; i < array.Size; i++)
                        {
                            var item = array.GetDirectObject(i);
                            if (item.IsIndirect())
                            {
                                var indRef = (PRIndirectReference)item;
                                if (indRef.Number == streamObjectNumber)
                                    return pageNum;
                            }
                        }
                    }
                    else if (contents.IsIndirect())
                    {
                        var indRef = (PRIndirectReference)contents;
                        if (indRef.Number == streamObjectNumber)
                            return pageNum;
                    }
                }
            }
            return 0;
        }
        
        /// <summary>
        /// Extracts text additions for a specific page
        /// </summary>
        private List<string> ExtractPageTextFromObjects(int pageNum, List<LastSessionObjectInfo> objects)
        {
            var texts = new List<string>();
            
            // Get page object
            var page = reader.GetPageN(pageNum);
            var contents = page.Get(PdfName.CONTENTS);
            
            if (contents != null)
            {
                var contentObjects = new List<int>();
                
                if (contents.IsArray())
                {
                    var array = (PdfArray)contents;
                    for (int i = 0; i < array.Size; i++)
                    {
                        var item = array.GetDirectObject(i);
                        if (item.IsIndirect())
                        {
                            contentObjects.Add(((PRIndirectReference)item).Number);
                        }
                    }
                }
                else if (contents.IsIndirect())
                {
                    contentObjects.Add(((PRIndirectReference)contents).Number);
                }
                
                // Check which content objects were modified
                foreach (var objNum in contentObjects)
                {
                    var modObj = objects.FirstOrDefault(o => o.ObjectNumber == objNum);
                    if (modObj != null && modObj.Type == "Stream")
                    {
                        // Try to get resources for this page
                        var resources = page.GetAsDict(PdfName.RESOURCES);
                        
                        // If we have the actual stream object, use it for better extraction
                        if (modObj.StreamObject != null)
                        {
                            try
                            {
                                var bytes = PdfReader.GetStreamBytes(modObj.StreamObject);
                                var content = Encoding.ASCII.GetString(bytes);
                                
                                var decoder = new HexTextDecoder(reader);
                                var extractedTexts = decoder.ExtractAllTexts(content, resources);
                                texts.AddRange(extractedTexts.Where(t => !string.IsNullOrWhiteSpace(t)));
                            }
                            catch
                            {
                                // Fall back to content string
                                var text = ExtractTextFromStream(modObj.Content);
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    texts.Add(text);
                                }
                            }
                        }
                        else
                        {
                            var text = ExtractTextFromStream(modObj.Content);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                texts.Add(text);
                            }
                        }
                    }
                }
            }
            
            return texts;
        }
        
        /// <summary>
        /// Analyzes modified objects when no incremental updates exist
        /// </summary>
        private List<TextAddition> AnalyzeModifiedObjectsForText()
        {
            var textAdditions = new List<TextAddition>();
            
            for (int i = 0; i < reader.XrefSize; i++)
            {
                try
                {
                    var obj = reader.GetPdfObject(i);
                    if (obj != null && obj is PRIndirectReference)
                    {
                        var indRef = (PRIndirectReference)obj;
                        if (indRef.Generation > 0)
                        {
                            // This object was modified
                            var pdfObj = reader.GetPdfObjectRelease(i);
                            if (pdfObj != null)
                            {
                                if (pdfObj.IsStream())
                                {
                                    // Try to extract text from stream
                                    var stream = (PRStream)pdfObj;
                                    var bytes = PdfReader.GetStreamBytes(stream);
                                    var content = Encoding.ASCII.GetString(bytes);
                                    var text = ExtractTextFromStream(content);
                                    
                                    if (!string.IsNullOrWhiteSpace(text))
                                    {
                                        textAdditions.Add(new TextAddition
                                        {
                                            ObjectNumber = i,
                                            Generation = indRef.Generation,
                                            Text = text,
                                            Type = "Modified Stream"
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            
            return textAdditions;
        }
        
        /// <summary>
        /// Analyzes last session based on timestamps
        /// </summary>
        private LastSessionAnalysisReport AnalyzeByTimestamp()
        {
            var report = new LastSessionAnalysisReport();
            report.SessionType = "Timestamp Analysis";
            
            // Get document dates
            var info = reader.Info;
            DateTime? modDate = GetDateFromInfo(info, "ModDate");
            DateTime? creationDate = GetDateFromInfo(info, "CreationDate");
            
            if (!modDate.HasValue || modDate == creationDate)
            {
                report.HasResults = false;
                return report;
            }
            
            // Find objects modified at the last modification time
            var modifiedObjects = new List<int>();
            
            // Check for objects with generation > 0
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
                            modifiedObjects.Add(i);
                        }
                    }
                }
                catch { }
            }
            
            // If no modified objects found by generation, check incremental updates
            if (modifiedObjects.Count == 0)
            {
                var updates = DetectIncrementalUpdates();
                if (updates.Count > 0)
                {
                    var lastUpdate = updates.Last();
                    modifiedObjects.AddRange(lastUpdate.ModifiedObjects);
                }
            }
            
            // Extract texts from modified objects
            foreach (var objNum in modifiedObjects)
            {
                try
                {
                    var texts = ExtractTextsFromObject(objNum);
                    if (texts.Any())
                    {
                        var pageNum = GetObjectPage(objNum);
                        foreach (var text in texts)
                        {
                            report.LastSessionTexts.Add(new TextAddition
                            {
                                ObjectNumber = objNum,
                                Text = text,
                                Type = "Modified Object",
                                PageNumber = pageNum
                            });
                        }
                        
                        if (pageNum > 0 && !report.AffectedPages.Contains(pageNum))
                        {
                            report.AffectedPages.Add(pageNum);
                        }
                    }
                }
                catch { }
            }
            
            report.HasResults = report.LastSessionTexts.Count > 0;
            report.LastModificationDate = modDate;
            report.AffectedPages.Sort();
            
            return report;
        }
        
        /// <summary>
        /// Extract texts from a specific object
        /// </summary>
        private List<string> ExtractTextsFromObject(int objectNumber)
        {
            var texts = new List<string>();
            
            try
            {
                var obj = reader.GetPdfObject(objectNumber);
                if (obj == null) return texts;
                
                if (obj.IsStream())
                {
                    var stream = (PRStream)obj;
                    var bytes = PdfReader.GetStreamBytes(stream);
                    var content = Encoding.ASCII.GetString(bytes);
                    
                    // Use HexTextDecoder
                    var decoder = new HexTextDecoder(reader);
                    var pageNum = GetObjectPage(objectNumber);
                    PdfDictionary? resources = null;
                    
                    if (pageNum > 0)
                    {
                        var page = reader.GetPageN(pageNum);
                        resources = page.GetAsDict(PdfName.RESOURCES);
                    }
                    
                    texts = decoder.ExtractAllTexts(content, resources);
                }
                else if (obj.IsDictionary())
                {
                    var dict = (PdfDictionary)obj;
                    
                    // Check for annotation
                    if (dict.Get(PdfName.TYPE)?.Equals(PdfName.ANNOT) == true)
                    {
                        var contents = dict.GetAsString(PdfName.CONTENTS);
                        if (contents != null)
                        {
                            texts.Add($"[Annotation] {contents.ToUnicodeString()}");
                        }
                    }
                }
            }
            catch { }
            
            return texts;
        }
        
        /// <summary>
        /// Get page number for an object
        /// </summary>
        private int GetObjectPage(int objectNumber)
        {
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                var page = reader.GetPageN(pageNum);
                
                // Check page itself
                var pageRef = reader.GetPageOrigRef(pageNum);
                if (pageRef != null && pageRef.Number == objectNumber)
                    return pageNum;
                
                // Check contents
                var contents = page.Get(PdfName.CONTENTS);
                if (IsContentInPage(contents, objectNumber))
                    return pageNum;
                
                // Check annotations
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
        
        /// <summary>
        /// Check if object is in page contents
        /// </summary>
        private bool IsContentInPage(PdfObject contents, int objNum)
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
                var indRef = (PRIndirectReference)contents;
                if (indRef.Number == objNum)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Get date from document info
        /// </summary>
        private DateTime? GetDateFromInfo(Dictionary<string, string> info, string key)
        {
            if (info != null && info.ContainsKey(key))
            {
                return ParsePdfDate(info[key]);
            }
            return null;
        }
        
        /// <summary>
        /// Parse PDF date format
        /// </summary>
        private DateTime? ParsePdfDate(string pdfDate)
        {
            if (string.IsNullOrEmpty(pdfDate))
                return null;
            
            // PDF date format: D:YYYYMMDDHHmmSSOHH'mm
            var datePattern = @"D:(\d{4})(\d{2})(\d{2})(\d{2})?(\d{2})?(\d{2})?";
            var match = Regex.Match(pdfDate, datePattern);
            
            if (match.Success)
            {
                try
                {
                    int year = int.Parse(match.Groups[1].Value);
                    int month = int.Parse(match.Groups[2].Value);
                    int day = int.Parse(match.Groups[3].Value);
                    int hour = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
                    int minute = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 0;
                    int second = match.Groups[6].Success ? int.Parse(match.Groups[6].Value) : 0;
                    
                    return new DateTime(year, month, day, hour, minute, second);
                }
                catch { }
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Report of last session analysis
    /// </summary>
    public class LastSessionAnalysisReport
    {
        public string SessionType { get; set; } = "";
        public IncrementalUpdateInfo LastUpdateInfo { get; set; } = new IncrementalUpdateInfo();
        public List<TextAddition> LastSessionTexts { get; set; } = new List<TextAddition>();
        public List<int> AffectedPages { get; set; } = new List<int>();
        public Dictionary<int, List<string>> PageTextAdditions { get; set; } = new Dictionary<int, List<string>>();
        public bool HasResults { get; set; }
        public DateTime? LastModificationDate { get; set; }
    }
    
    /// <summary>
    /// Information about incremental update
    /// </summary>
    public class IncrementalUpdateInfo
    {
        public int UpdateNumber { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public int XrefOffset { get; set; }
        public List<int> ModifiedObjects { get; set; } = new List<int>();
    }
    
    /// <summary>
    /// Modified object information for last session
    /// </summary>
    public class LastSessionObjectInfo
    {
        public int ObjectNumber { get; set; }
        public int Generation { get; set; }
        public string Type { get; set; } = "";
        public string Content { get; set; } = "";
        public PRStream? StreamObject { get; set; }
    }
    
    /// <summary>
    /// Text addition information
    /// </summary>
    public class TextAddition
    {
        public int ObjectNumber { get; set; }
        public int Generation { get; set; }
        public string Text { get; set; } = "";
        public string Type { get; set; } = "";
        public int PageNumber { get; set; }
    }
}