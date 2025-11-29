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
    /// Detector baseado em TIMESTAMPS - extrai textos dos objetos mais recentes
    /// sem precisar comparar com versões anteriores
    /// </summary>
    public class TimestampBasedLastSessionDetector
    {
        private PdfReader reader;
        private string filePath;
        
        public TimestampBasedLastSessionDetector(PdfReader reader, string filePath)
        {
            this.reader = reader;
            this.filePath = filePath;
        }
        
        public TimestampAnalysisReport AnalyzeByTimestamp()
        {
            var report = new TimestampAnalysisReport();
            
            // Step 1: Get all object timestamps
            var objectTimestamps = GetAllObjectTimestamps();
            
            if (objectTimestamps.Count == 0)
            {
                report.Message = "No timestamped objects found";
                return report;
            }
            
            // Step 2: Group by timestamp to identify sessions
            var sessions = GroupObjectsBySession(objectTimestamps);
            report.TotalSessions = sessions.Count;
            
            // Step 3: Get the LAST session (most recent timestamp)
            var lastSession = sessions.OrderByDescending(s => s.Timestamp).First();
            report.LastSessionTimestamp = lastSession.Timestamp;
            report.LastSessionObjects = lastSession.Objects;
            
            // Step 4: Extract text from ALL objects in the last session
            foreach (var objInfo in lastSession.Objects)
            {
                var texts = ExtractTextsFromObject(objInfo.ObjectNumber);
                if (texts.Any())
                {
                    report.LastSessionTexts.Add(new TimestampedText
                    {
                        ObjectNumber = objInfo.ObjectNumber,
                        Timestamp = objInfo.Timestamp,
                        Texts = texts,
                        PageNumber = GetObjectPage(objInfo.ObjectNumber)
                    });
                }
            }
            
            // Step 5: Show timeline of modifications
            Console.WriteLine("\n📅 MODIFICATION TIMELINE:");
            foreach (var session in sessions.OrderBy(s => s.Timestamp))
            {
                Console.WriteLine($"\nSession at {session.Timestamp}:");
                Console.WriteLine($"  Modified objects: {string.Join(", ", session.Objects.Select(o => o.ObjectNumber))}");
            }
            
            return report;
        }
        
        /// <summary>
        /// Gets timestamps for all objects in the PDF
        /// </summary>
        private List<ObjectTimestamp> GetAllObjectTimestamps()
        {
            var timestamps = new List<ObjectTimestamp>();
            
            // Check document info
            var info = reader.Info;
            DateTime? creationDate = GetDateFromInfo(info, "CreationDate");
            DateTime? modDate = GetDateFromInfo(info, "ModDate");
            
            // Base timestamp for reference
            var baseTimestamp = creationDate ?? DateTime.Now;
            
            // Analyze each object
            for (int i = 1; i < reader.XrefSize; i++)
            {
                try
                {
                    var obj = reader.GetPdfObject(i);
                    if (obj == null) continue;
                    
                    // Check object generation
                    int generation = 0;
                    if (obj is PRIndirectReference)
                    {
                        generation = ((PRIndirectReference)obj).Generation;
                    }
                    
                    // Objects with generation > 0 were modified
                    if (generation > 0)
                    {
                        timestamps.Add(new ObjectTimestamp
                        {
                            ObjectNumber = i,
                            Generation = generation,
                            Timestamp = modDate ?? baseTimestamp.AddMinutes(generation),
                            IsModified = true
                        });
                    }
                    
                    // Check for timestamp in object dictionary
                    if (obj.IsDictionary())
                    {
                        var dict = (PdfDictionary)obj;
                        
                        // Check for /M (modification date)
                        var mDate = dict.GetAsString(PdfName.M);
                        if (mDate != null)
                        {
                            var objTimestamp = ParsePdfDate(mDate.ToString());
                            if (objTimestamp.HasValue)
                            {
                                timestamps.Add(new ObjectTimestamp
                                {
                                    ObjectNumber = i,
                                    Generation = generation,
                                    Timestamp = objTimestamp.Value,
                                    IsModified = true
                                });
                            }
                        }
                    }
                }
                catch { }
            }
            
            // If no modified objects found, check incremental updates
            if (timestamps.Count == 0)
            {
                var incrementalInfo = CheckForIncrementalUpdates();
                if (incrementalInfo.HasIncrementalUpdates)
                {
                    // Objects in last xref are considered modified
                    foreach (var objNum in incrementalInfo.LastXrefObjects)
                    {
                        timestamps.Add(new ObjectTimestamp
                        {
                            ObjectNumber = objNum,
                            Generation = 0,
                            Timestamp = modDate ?? baseTimestamp,
                            IsModified = true
                        });
                    }
                }
            }
            
            return timestamps;
        }
        
        /// <summary>
        /// Groups objects by modification session (same timestamp)
        /// </summary>
        private List<ModificationSession> GroupObjectsBySession(List<ObjectTimestamp> timestamps)
        {
            var sessions = new List<ModificationSession>();
            
            var grouped = timestamps.GroupBy(t => t.Timestamp);
            
            foreach (var group in grouped)
            {
                sessions.Add(new ModificationSession
                {
                    Timestamp = group.Key,
                    Objects = group.ToList()
                });
            }
            
            return sessions;
        }
        
        /// <summary>
        /// Extracts all texts from a specific object
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
                    
                    // Check if it's a content stream with text
                    if (content.Contains("BT") && content.Contains("ET"))
                    {
                        // Extract using iTextSharp's parser
                        var page = GetObjectPage(objectNumber);
                        if (page > 0)
                        {
                            try
                            {
                                var pageText = PdfTextExtractor.GetTextFromPage(reader, page);
                                if (!string.IsNullOrWhiteSpace(pageText))
                                {
                                    texts.Add(pageText);
                                }
                            }
                            catch
                            {
                                // Fallback to regex extraction
                                texts.AddRange(ExtractTextsWithRegex(content));
                            }
                        }
                        else
                        {
                            // Direct extraction
                            texts.AddRange(ExtractTextsWithRegex(content));
                        }
                    }
                }
                else if (obj.IsDictionary())
                {
                    var dict = (PdfDictionary)obj;
                    
                    // Check for annotation content
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
        /// Extract texts using regex (fallback method)
        /// </summary>
        private List<string> ExtractTextsWithRegex(string content)
        {
            var texts = new List<string>();
            
            // Regular text
            var textPattern = @"\((.*?)\)\s*Tj";
            var matches = Regex.Matches(content, textPattern);
            foreach (Match match in matches)
            {
                var text = UnescapePdfString(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(text))
                    texts.Add(text);
            }
            
            // Text arrays
            var arrayPattern = @"\[(.*?)\]\s*TJ";
            var arrayMatches = Regex.Matches(content, arrayPattern);
            foreach (Match match in arrayMatches)
            {
                var arrayContent = match.Groups[1].Value;
                var stringPattern = @"\((.*?)\)";
                var stringMatches = Regex.Matches(arrayContent, stringPattern);
                
                var combined = new StringBuilder();
                foreach (Match strMatch in stringMatches)
                {
                    combined.Append(UnescapePdfString(strMatch.Groups[1].Value));
                }
                
                if (combined.Length > 0)
                    texts.Add(combined.ToString());
            }
            
            return texts;
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
        
        /// <summary>
        /// Gets the page number for an object
        /// </summary>
        private int GetObjectPage(int objectNumber)
        {
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                var page = reader.GetPageN(pageNum);
                var contents = page.Get(PdfName.CONTENTS);
                
                if (IsObjectInContents(contents, objectNumber))
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
                var indRef = (PRIndirectReference)contents;
                if (indRef.Number == objNum)
                    return true;
            }
            
            return false;
        }
        
        private DateTime? GetDateFromInfo(Dictionary<string, string> info, string key)
        {
            if (info != null && info.ContainsKey(key))
            {
                return ParsePdfDate(info[key]);
            }
            return null;
        }
        
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
        
        private IncrementalUpdateInfo CheckForIncrementalUpdates()
        {
            var info = new IncrementalUpdateInfo();
            var pdfBytes = File.ReadAllBytes(filePath);
            var content = Encoding.ASCII.GetString(pdfBytes);
            
            // Count %%EOF markers
            var eofCount = Regex.Matches(content, @"%%EOF").Count;
            info.HasIncrementalUpdates = eofCount > 1;
            
            if (info.HasIncrementalUpdates)
            {
                // Find last xref
                var lastXrefMatch = Regex.Match(content, @"xref\s*(.*?)trailer", RegexOptions.Singleline | RegexOptions.RightToLeft);
                if (lastXrefMatch.Success)
                {
                    info.LastXrefObjects = ParseXrefSection(lastXrefMatch.Groups[1].Value);
                }
            }
            
            return info;
        }
        
        private List<int> ParseXrefSection(string xrefContent)
        {
            var objects = new List<int>();
            var lines = xrefContent.Split('\n');
            
            int currentObj = 0;
            int count = 0;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                if (Regex.IsMatch(trimmed, @"^\d+\s+\d+$"))
                {
                    var parts = trimmed.Split(' ');
                    currentObj = int.Parse(parts[0]);
                    count = int.Parse(parts[1]);
                }
                else if (Regex.IsMatch(trimmed, @"^\d{10}\s+\d{5}\s+[fn]$") && count > 0)
                {
                    if (trimmed.EndsWith("n") && currentObj > 0)
                    {
                        objects.Add(currentObj);
                    }
                    currentObj++;
                    count--;
                }
            }
            
            return objects;
        }
    }
    
    public class TimestampAnalysisReport
    {
        public string Message { get; set; }
        public int TotalSessions { get; set; }
        public DateTime LastSessionTimestamp { get; set; }
        public List<ObjectTimestamp> LastSessionObjects { get; set; } = new List<ObjectTimestamp>();
        public List<TimestampedText> LastSessionTexts { get; set; } = new List<TimestampedText>();
    }
    
    public class ObjectTimestamp
    {
        public int ObjectNumber { get; set; }
        public int Generation { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsModified { get; set; }
    }
    
    public class ModificationSession
    {
        public DateTime Timestamp { get; set; }
        public List<ObjectTimestamp> Objects { get; set; }
    }
    
    public class TimestampedText
    {
        public int ObjectNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public List<string> Texts { get; set; }
        public int PageNumber { get; set; }
    }
    
    public class IncrementalUpdateInfo
    {
        public bool HasIncrementalUpdates { get; set; }
        public List<int> LastXrefObjects { get; set; } = new List<int>();
    }
}