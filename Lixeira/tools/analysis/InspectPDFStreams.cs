using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;

class InspectPDFStreams
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: InspectPDFStreams <pdf_file>");
            return;
        }
        
        var reader = new PdfReader(args[0]);
        
        Console.WriteLine($"PDF: {args[0]}");
        Console.WriteLine($"Pages: {reader.NumberOfPages}");
        Console.WriteLine($"Objects: {reader.XrefSize}");
        Console.WriteLine();
        
        // Inspect all stream objects
        for (int i = 0; i < reader.XrefSize; i++)
        {
            try
            {
                var obj = reader.GetPdfObject(i);
                if (obj != null && obj.IsStream())
                {
                    var stream = (PRStream)obj;
                    var bytes = PdfReader.GetStreamBytes(stream);
                    var content = Encoding.ASCII.GetString(bytes);
                    
                    // Check if contains text operations
                    if (content.Contains("Tj") || content.Contains("TJ"))
                    {
                        Console.WriteLine($"\n=== Object {i} - Contains Text Operations ===");
                        Console.WriteLine($"Stream size: {bytes.Length} bytes");
                        
                        // Show first 500 chars
                        var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                        Console.WriteLine($"Preview:\n{preview}");
                        
                        // Count text operations
                        var tjCount = Regex.Matches(content, @"Tj\s").Count;
                        var tjArrayCount = Regex.Matches(content, @"TJ\s").Count;
                        var hexCount = Regex.Matches(content, @"<[0-9A-Fa-f]+>\s*Tj").Count;
                        var stringCount = Regex.Matches(content, @"\([^)]*\)\s*Tj").Count;
                        
                        Console.WriteLine($"\nText operations:");
                        Console.WriteLine($"  Tj (single text): {tjCount}");
                        Console.WriteLine($"  TJ (text array): {tjArrayCount}");
                        Console.WriteLine($"  Hex text (<xxxx> Tj): {hexCount}");
                        Console.WriteLine($"  String text ((text) Tj): {stringCount}");
                        
                        // Show some hex examples
                        if (hexCount > 0)
                        {
                            Console.WriteLine("\nHex text examples:");
                            var hexMatches = Regex.Matches(content, @"<([0-9A-Fa-f]+)>\s*Tj");
                            for (int j = 0; j < Math.Min(5, hexMatches.Count); j++)
                            {
                                Console.WriteLine($"  {hexMatches[j].Value}");
                            }
                        }
                        
                        // Show page association
                        for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                        {
                            var page = reader.GetPageN(pageNum);
                            var contents = page.Get(PdfName.CONTENTS);
                            
                            if (IsStreamInContent(contents, i))
                            {
                                Console.WriteLine($"\nBelongs to Page {pageNum}");
                                
                                // Get resources
                                var resources = page.GetAsDict(PdfName.RESOURCES);
                                if (resources != null)
                                {
                                    var fonts = resources.GetAsDict(PdfName.FONT);
                                    if (fonts != null)
                                    {
                                        Console.WriteLine($"Available fonts: {string.Join(", ", fonts.Keys)}");
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }
        
        reader.Close();
    }
    
    static bool IsStreamInContent(PdfObject contents, int streamNum)
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
                    if (indRef.Number == streamNum)
                        return true;
                }
            }
        }
        else if (contents.IsIndirect())
        {
            var indRef = (PRIndirectReference)contents;
            if (indRef.Number == streamNum)
                return true;
        }
        
        return false;
    }
}