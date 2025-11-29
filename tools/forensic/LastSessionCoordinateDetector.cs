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
    /// Detects coordinates of text additions in last session
    /// Then extracts text from current PDF at those coordinates
    /// </summary>
    public class LastSessionCoordinateDetector
    {
        private PdfReader reader;
        private string filePath;
        private byte[] pdfBytes;
        
        public LastSessionCoordinateDetector(PdfReader reader, string filePath)
        {
            this.reader = reader;
            this.filePath = filePath;
            this.pdfBytes = File.ReadAllBytes(filePath);
        }
        
        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: LastSessionCoordinateDetector <pdf_file>");
                return;
            }
            
            var reader = new PdfReader(args[0]);
            var detector = new LastSessionCoordinateDetector(reader, args[0]);
            var report = detector.AnalyzeLastSession();
            
            Console.WriteLine("🔍 COORDINATE-BASED LAST SESSION DETECTION");
            Console.WriteLine($"File: {Path.GetFileName(args[0])}");
            Console.WriteLine();
            
            if (report.AddedTextRegions.Count == 0)
            {
                Console.WriteLine("❌ No text additions detected in last session.");
            }
            else
            {
                Console.WriteLine($"✅ Found {report.AddedTextRegions.Count} text addition regions:");
                Console.WriteLine();
                
                foreach (var region in report.AddedTextRegions)
                {
                    Console.WriteLine($"📍 Page {region.PageNumber}, Position ({region.X:F2}, {region.Y:F2}):");
                    Console.WriteLine($"   Object: {region.ObjectNumber}");
                    Console.WriteLine($"   Text: \"{region.ExtractedText}\"");
                    Console.WriteLine();
                }
            }
            
            reader.Close();
        }
        
        public CoordinateBasedReport AnalyzeLastSession()
        {
            var report = new CoordinateBasedReport();
            
            // Step 1: Find incremental updates
            var incrementalUpdates = DetectIncrementalUpdates();
            if (incrementalUpdates.Count == 0)
            {
                report.SessionType = "Single Session";
                return report;
            }
            
            report.SessionType = "Incremental Update";
            var lastUpdate = incrementalUpdates.Last();
            
            // Step 2: Find modified objects that are content streams
            var modifiedStreamObjects = new List<int>();
            foreach (var objNum in lastUpdate.ModifiedObjects)
            {
                try
                {
                    var obj = reader.GetPdfObject(objNum);
                    if (obj != null && obj.IsStream())
                    {
                        modifiedStreamObjects.Add(objNum);
                    }
                }
                catch { }
            }
            
            // Step 3: For each modified stream, find its coordinates
            foreach (var streamObjNum in modifiedStreamObjects)
            {
                // Find which page uses this stream
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    var contents = page.Get(PdfName.CONTENTS);
                    
                    bool usesThisStream = false;
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
                                    if (indRef.Number == streamObjNum)
                                    {
                                        usesThisStream = true;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (contents.IsIndirect())
                        {
                            var indRef = (PRIndirectReference)contents;
                            if (indRef.Number == streamObjNum)
                            {
                                usesThisStream = true;
                            }
                        }
                    }
                    
                    if (usesThisStream)
                    {
                        // Extract coordinates from the stream
                        var coordinates = ExtractTextCoordinatesFromStream(streamObjNum);
                        
                        // For each coordinate, extract text from current PDF
                        foreach (var coord in coordinates)
                        {
                            var extractedText = ExtractTextAtCoordinate(pageNum, coord.X, coord.Y);
                            
                            report.AddedTextRegions.Add(new TextRegion
                            {
                                PageNumber = pageNum,
                                ObjectNumber = streamObjNum,
                                X = coord.X,
                                Y = coord.Y,
                                ExtractedText = extractedText
                            });
                        }
                    }
                }
            }
            
            return report;
        }
        
        private List<IncrementalUpdateInfo> DetectIncrementalUpdates()
        {
            var updates = new List<IncrementalUpdateInfo>();
            var content = Encoding.ASCII.GetString(pdfBytes);
            
            // Find all %%EOF markers
            var eofPositions = new List<int>();
            int pos = 0;
            while ((pos = content.IndexOf("%%EOF", pos)) != -1)
            {
                eofPositions.Add(pos);
                pos += 5;
            }
            
            if (eofPositions.Count <= 1)
                return updates;
            
            // Find all xref positions
            var xrefPositions = new List<int>();
            pos = 0;
            while ((pos = content.IndexOf("xref", pos)) != -1)
            {
                xrefPositions.Add(pos);
                pos += 4;
            }
            
            // Build updates
            for (int i = 0; i < eofPositions.Count; i++)
            {
                var update = new IncrementalUpdateInfo
                {
                    UpdateNumber = i + 1,
                    EndOffset = eofPositions[i] + 5
                };
                
                if (i > 0)
                    update.StartOffset = eofPositions[i - 1] + 5;
                else
                    update.StartOffset = 0;
                
                // Find xref in this update
                foreach (var xrefPos in xrefPositions)
                {
                    if (xrefPos >= update.StartOffset && xrefPos < update.EndOffset)
                    {
                        update.XrefOffset = xrefPos;
                        
                        // Extract xref content
                        var trailerPos = content.IndexOf("trailer", xrefPos);
                        if (trailerPos > xrefPos)
                        {
                            var xrefContent = content.Substring(xrefPos, trailerPos - xrefPos + 7);
                            update.ModifiedObjects = ParseXrefSection(xrefContent);
                        }
                        break;
                    }
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
            int remainingCount = 0;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                if (Regex.IsMatch(trimmed, @"^\d+\s+\d+$"))
                {
                    var parts = trimmed.Split(' ');
                    currentObj = int.Parse(parts[0]);
                    remainingCount = int.Parse(parts[1]);
                    
                    if (currentObj == 0 && remainingCount == 1)
                        continue;
                }
                else if (Regex.IsMatch(trimmed, @"^\d{10}\s+\d{5}\s+[fn]$") && remainingCount > 0)
                {
                    var parts = trimmed.Split(' ');
                    var inUse = parts[2] == "n";
                    
                    if (inUse && currentObj > 0)
                    {
                        objectNumbers.Add(currentObj);
                    }
                    
                    currentObj++;
                    remainingCount--;
                }
                else if (trimmed == "trailer")
                {
                    break;
                }
            }
            
            return objectNumbers;
        }
        
        private List<TextCoordinate> ExtractTextCoordinatesFromStream(int objNum)
        {
            var coordinates = new List<TextCoordinate>();
            
            try
            {
                var obj = reader.GetPdfObject(objNum);
                if (obj != null && obj.IsStream())
                {
                    var stream = (PRStream)obj;
                    var bytes = PdfReader.GetStreamBytes(stream);
                    var content = Encoding.ASCII.GetString(bytes);
                    
                    // Find text positioning commands (Tm)
                    var tmPattern = @"1\s+0\s+0\s+1\s+(\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)\s+Tm";
                    var tmMatches = Regex.Matches(content, tmPattern);
                    
                    foreach (Match match in tmMatches)
                    {
                        float x = float.Parse(match.Groups[1].Value);
                        float y = float.Parse(match.Groups[2].Value);
                        
                        coordinates.Add(new TextCoordinate { X = x, Y = y });
                    }
                    
                    // Also check for Td commands (relative positioning)
                    var tdPattern = @"(\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)\s+Td";
                    var tdMatches = Regex.Matches(content, tdPattern);
                    
                    float currentX = 0, currentY = 0;
                    foreach (Match match in tdMatches)
                    {
                        currentX += float.Parse(match.Groups[1].Value);
                        currentY += float.Parse(match.Groups[2].Value);
                        
                        coordinates.Add(new TextCoordinate { X = currentX, Y = currentY });
                    }
                }
            }
            catch { }
            
            return coordinates;
        }
        
        private string ExtractTextAtCoordinate(int pageNum, float x, float y)
        {
            try
            {
                // Use a custom text extraction strategy that looks for text at specific coordinates
                var strategy = new CoordinateTextExtractionStrategy(x, y, 50); // 50 point tolerance
                var text = PdfTextExtractor.GetTextFromPage(reader, pageNum, strategy);
                return text.Trim();
            }
            catch
            {
                return "";
            }
        }
    }
    
    // Custom text extraction strategy
    public class CoordinateTextExtractionStrategy : ITextExtractionStrategy
    {
        private float targetX;
        private float targetY;
        private float tolerance;
        private StringBuilder result = new StringBuilder();
        
        public CoordinateTextExtractionStrategy(float x, float y, float tolerance)
        {
            this.targetX = x;
            this.targetY = y;
            this.tolerance = tolerance;
        }
        
        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        
        public void RenderText(TextRenderInfo renderInfo)
        {
            var baseline = renderInfo.GetBaseline();
            var x = baseline.GetStartPoint()[0];
            var y = baseline.GetStartPoint()[1];
            
            // Check if this text is near our target coordinates
            if (Math.Abs(x - targetX) <= tolerance && Math.Abs(y - targetY) <= tolerance)
            {
                result.Append(renderInfo.GetText());
            }
        }
        
        public void RenderImage(ImageRenderInfo renderInfo) { }
        
        public string GetResultantText()
        {
            return result.ToString();
        }
    }
    
    // Data structures
    public class CoordinateBasedReport
    {
        public string SessionType { get; set; }
        public List<TextRegion> AddedTextRegions { get; set; } = new List<TextRegion>();
    }
    
    public class TextRegion
    {
        public int PageNumber { get; set; }
        public int ObjectNumber { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public string ExtractedText { get; set; }
    }
    
    public class TextCoordinate
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}