using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace PDFLayoutPreservingConverter
{
    /// <summary>
    /// Deep PDF object analyzer - reads ALL objects, their types, and properties
    /// </summary>
    public class DeepPDFObjectAnalyzer
    {
        private PdfReader reader;
        private string filePath;
        
        public DeepPDFObjectAnalyzer(string filePath)
        {
            this.filePath = filePath;
            this.reader = new PdfReader(filePath);
        }
        
        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: DeepPDFObjectAnalyzer <pdf_file>");
                return;
            }
            
            var analyzer = new DeepPDFObjectAnalyzer(args[0]);
            analyzer.AnalyzeAllObjects();
        }
        
        public void AnalyzeAllObjects()
        {
            Console.WriteLine($"🔍 DEEP PDF OBJECT ANALYSIS");
            Console.WriteLine($"File: {Path.GetFileName(filePath)}");
            Console.WriteLine($"Total objects: {reader.XrefSize}");
            Console.WriteLine($"Number of pages: {reader.NumberOfPages}");
            Console.WriteLine();
            
            // Analyze document structure
            Console.WriteLine("📚 DOCUMENT STRUCTURE:");
            var trailer = reader.Trailer;
            Console.WriteLine($"Trailer: {trailer}");
            
            var root = trailer.GetAsDict(PdfName.ROOT);
            if (root != null)
            {
                Console.WriteLine($"Root (Catalog): Object {GetObjectNumber(root)}");
            }
            
            var info = trailer.GetAsDict(PdfName.INFO);
            if (info != null)
            {
                Console.WriteLine($"Info: Object {GetObjectNumber(info)}");
                AnalyzeInfoDictionary(info);
            }
            
            Console.WriteLine("\n📦 ANALYZING ALL OBJECTS:");
            Console.WriteLine(new string('=', 60));
            
            // Analyze each object
            for (int i = 0; i < reader.XrefSize; i++)
            {
                try
                {
                    var obj = reader.GetPdfObject(i);
                    if (obj != null)
                    {
                        Console.WriteLine($"\n🔸 Object {i}:");
                        AnalyzeObject(obj, i, "  ");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n❌ Object {i}: Error - {ex.Message}");
                }
            }
            
            // Analyze page content in detail
            Console.WriteLine("\n\n📄 PAGE CONTENT ANALYSIS:");
            Console.WriteLine(new string('=', 60));
            
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                Console.WriteLine($"\n📑 PAGE {pageNum}:");
                AnalyzePage(pageNum);
            }
            
            // Check for modifications
            Console.WriteLine("\n\n🔍 MODIFICATION ANALYSIS:");
            Console.WriteLine(new string('=', 60));
            CheckForModifications();
            
            reader.Close();
        }
        
        private void AnalyzeObject(PdfObject obj, int objNum, string indent)
        {
            if (obj == null)
            {
                Console.WriteLine($"{indent}Type: null");
                return;
            }
            
            // Check if indirect reference
            if (obj.IsIndirect())
            {
                var indRef = (PRIndirectReference)obj;
                Console.WriteLine($"{indent}Type: Indirect Reference");
                Console.WriteLine($"{indent}  Number: {indRef.Number}");
                Console.WriteLine($"{indent}  Generation: {indRef.Generation}");
                
                // Get the actual object
                var actualObj = reader.GetPdfObject(indRef.Number);
                if (actualObj != null)
                {
                    Console.WriteLine($"{indent}  Referenced object type: {actualObj.GetType().Name}");
                    AnalyzeObject(actualObj, indRef.Number, indent + "  ");
                }
                return;
            }
            
            Console.WriteLine($"{indent}Type: {obj.GetType().Name}");
            Console.WriteLine($"{indent}Direct: {!obj.IsIndirect()}");
            
            if (obj.IsBoolean())
            {
                Console.WriteLine($"{indent}Value: {((PdfBoolean)obj).BooleanValue}");
            }
            else if (obj.IsNumber())
            {
                Console.WriteLine($"{indent}Value: {((PdfNumber)obj).DoubleValue}");
            }
            else if (obj.IsString())
            {
                var str = (PdfString)obj;
                Console.WriteLine($"{indent}Value: {str.ToUnicodeString()}");
            }
            else if (obj.IsName())
            {
                Console.WriteLine($"{indent}Value: {((PdfName)obj).ToString()}");
            }
            else if (obj.IsArray())
            {
                var array = (PdfArray)obj;
                Console.WriteLine($"{indent}Size: {array.Size}");
                for (int i = 0; i < Math.Min(3, array.Size); i++)
                {
                    Console.WriteLine($"{indent}[{i}]:");
                    AnalyzeObject(array.GetDirectObject(i), -1, indent + "  ");
                }
                if (array.Size > 3)
                    Console.WriteLine($"{indent}... and {array.Size - 3} more elements");
            }
            else if (obj.IsDictionary())
            {
                var dict = (PdfDictionary)obj;
                Console.WriteLine($"{indent}Keys: {string.Join(", ", dict.Keys)}");
                
                // Check for specific types
                var type = dict.GetAsName(PdfName.TYPE);
                if (type != null)
                {
                    Console.WriteLine($"{indent}PDF Type: {type}");
                }
                
                var subtype = dict.GetAsName(PdfName.SUBTYPE);
                if (subtype != null)
                {
                    Console.WriteLine($"{indent}Subtype: {subtype}");
                }
                
                // Show some key values
                foreach (var key in dict.Keys)
                {
                    if (key.Equals(PdfName.TYPE) || key.Equals(PdfName.SUBTYPE))
                        continue;
                    
                    var value = dict.Get(key);
                    if (value != null && (value.IsString() || value.IsName() || value.IsNumber()))
                    {
                        Console.WriteLine($"{indent}{key}: {value}");
                    }
                }
            }
            else if (obj.IsStream())
            {
                var stream = (PRStream)obj;
                Console.WriteLine($"{indent}Stream dictionary: {stream.Keys.Count} keys");
                
                // Get stream properties
                var length = stream.GetAsNumber(PdfName.LENGTH);
                if (length != null)
                {
                    Console.WriteLine($"{indent}Length: {length.IntValue}");
                }
                
                var filter = stream.Get(PdfName.FILTER);
                if (filter != null)
                {
                    Console.WriteLine($"{indent}Filter: {filter}");
                }
                
                // Try to get decoded content
                try
                {
                    var bytes = PdfReader.GetStreamBytes(stream);
                    Console.WriteLine($"{indent}Decoded size: {bytes.Length} bytes");
                    
                    // Check if it's text content
                    var content = Encoding.ASCII.GetString(bytes);
                    if (content.Contains("BT") && content.Contains("ET"))
                    {
                        Console.WriteLine($"{indent}Contains text operations!");
                        
                        // Extract text operations
                        var lines = content.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("Tj") || line.Contains("TJ"))
                            {
                                Console.WriteLine($"{indent}  Text: {line.Trim()}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{indent}Cannot decode stream: {ex.Message}");
                }
            }
        }
        
        private void AnalyzePage(int pageNum)
        {
            var page = reader.GetPageN(pageNum);
            
            Console.WriteLine($"  Page dictionary: Object {GetObjectNumber(page)}");
            
            // Get page size
            var mediaBox = page.GetAsArray(PdfName.MEDIABOX);
            if (mediaBox != null)
            {
                Console.WriteLine($"  MediaBox: {mediaBox}");
            }
            
            // Get resources
            var resources = page.GetAsDict(PdfName.RESOURCES);
            if (resources != null)
            {
                Console.WriteLine($"  Resources: Object {GetObjectNumber(resources)}");
                
                // Check fonts
                var fonts = resources.GetAsDict(PdfName.FONT);
                if (fonts != null)
                {
                    Console.WriteLine($"  Fonts: {fonts.Keys.Count}");
                    foreach (var fontName in fonts.Keys)
                    {
                        var font = fonts.GetAsDict(fontName);
                        var basefont = font?.GetAsName(PdfName.BASEFONT);
                        Console.WriteLine($"    {fontName}: {basefont}");
                    }
                }
            }
            
            // Get content streams
            var contents = page.Get(PdfName.CONTENTS);
            if (contents != null)
            {
                Console.WriteLine($"  Content streams:");
                
                if (contents.IsArray())
                {
                    var array = (PdfArray)contents;
                    Console.WriteLine($"    Array with {array.Size} streams");
                    for (int i = 0; i < array.Size; i++)
                    {
                        var streamRef = array.GetDirectObject(i);
                        if (streamRef.IsIndirect())
                        {
                            var indRef = (PRIndirectReference)streamRef;
                            Console.WriteLine($"    [{i}] Object {indRef.Number} (gen {indRef.Generation})");
                            
                            // Analyze the stream content
                            AnalyzeContentStream(indRef.Number);
                        }
                    }
                }
                else if (contents.IsIndirect())
                {
                    var indRef = (PRIndirectReference)contents;
                    Console.WriteLine($"    Single stream: Object {indRef.Number} (gen {indRef.Generation})");
                    AnalyzeContentStream(indRef.Number);
                }
            }
            
            // Extract all text using strategy
            try
            {
                var strategy = new SimpleTextExtractionStrategy();
                var text = PdfTextExtractor.GetTextFromPage(reader, pageNum, strategy);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine($"  Extracted text preview: {text.Substring(0, Math.Min(200, text.Length))}...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error extracting text: {ex.Message}");
            }
        }
        
        private void AnalyzeContentStream(int objNum)
        {
            try
            {
                var obj = reader.GetPdfObject(objNum);
                if (obj != null && obj.IsStream())
                {
                    var stream = (PRStream)obj;
                    var bytes = PdfReader.GetStreamBytes(stream);
                    var content = Encoding.ASCII.GetString(bytes);
                    
                    // Count text operations
                    int tjCount = 0, tmCount = 0, tfCount = 0;
                    var lines = content.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("Tj")) tjCount++;
                        if (line.Contains("Tm")) tmCount++;
                        if (line.Contains("Tf")) tfCount++;
                    }
                    
                    Console.WriteLine($"      Stream {objNum}: {bytes.Length} bytes");
                    Console.WriteLine($"      Text operations: {tjCount} Tj, {tmCount} Tm, {tfCount} Tf");
                    
                    // Show sample text operations
                    foreach (var line in lines)
                    {
                        if (line.Contains("Tj") && line.Contains("("))
                        {
                            Console.WriteLine($"      Sample text: {line.Trim()}");
                            break;
                        }
                    }
                }
            }
            catch { }
        }
        
        private void AnalyzeInfoDictionary(PdfDictionary info)
        {
            Console.WriteLine("\n  Document Info:");
            foreach (var key in info.Keys)
            {
                var value = info.Get(key);
                if (value.IsString())
                {
                    Console.WriteLine($"    {key}: {((PdfString)value).ToUnicodeString()}");
                }
                else
                {
                    Console.WriteLine($"    {key}: {value}");
                }
            }
        }
        
        private void CheckForModifications()
        {
            // Check for objects with generation > 0
            int modifiedCount = 0;
            for (int i = 0; i < reader.XrefSize; i++)
            {
                try
                {
                    var obj = reader.GetPdfObjectRelease(i);
                    if (obj != null && obj.IsIndirect())
                    {
                        var indRef = (PRIndirectReference)obj;
                        if (indRef.Generation > 0)
                        {
                            modifiedCount++;
                            Console.WriteLine($"  Modified object: {i} (generation {indRef.Generation})");
                        }
                    }
                }
                catch { }
            }
            
            if (modifiedCount == 0)
            {
                Console.WriteLine("  No objects with generation > 0 found");
            }
            
            // Check for incremental updates
            var pdfBytes = File.ReadAllBytes(filePath);
            var content = Encoding.ASCII.GetString(pdfBytes);
            int eofCount = 0;
            int pos = 0;
            while ((pos = content.IndexOf("%%EOF", pos)) != -1)
            {
                eofCount++;
                pos += 5;
            }
            
            Console.WriteLine($"  %%EOF markers: {eofCount}");
            Console.WriteLine($"  Incremental updates: {(eofCount > 1 ? "Yes" : "No")}");
        }
        
        private int GetObjectNumber(PdfObject obj)
        {
            if (obj == null) return -1;
            
            for (int i = 0; i < reader.XrefSize; i++)
            {
                try
                {
                    var testObj = reader.GetPdfObject(i);
                    if (testObj == obj)
                        return i;
                }
                catch { }
            }
            return -1;
        }
    }
}