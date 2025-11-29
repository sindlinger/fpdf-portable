using System;
using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;

class ExtractFullToUnicode
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ExtractFullToUnicode <pdf_file>");
            return;
        }
        
        var reader = new PdfReader(args[0]);
        
        // Get page 1 resources
        var page = reader.GetPageN(1);
        var resources = page.GetAsDict(PdfName.RESOURCES);
        var fonts = resources.GetAsDict(PdfName.FONT);
        
        // Extract F10 ToUnicode (object 46)
        Console.WriteLine("=== F10 ToUnicode (Bold) ===");
        ExtractToUnicode(reader, 46);
        
        Console.WriteLine("\n=== F11 ToUnicode (Regular) ===");
        ExtractToUnicode(reader, 48);
        
        reader.Close();
    }
    
    static void ExtractToUnicode(PdfReader reader, int objNum)
    {
        var obj = reader.GetPdfObject(objNum);
        if (obj != null && obj.IsStream())
        {
            var stream = (PRStream)obj;
            var bytes = PdfReader.GetStreamBytes(stream);
            var content = Encoding.ASCII.GetString(bytes);
            
            Console.WriteLine($"Full content ({bytes.Length} bytes):");
            Console.WriteLine(content);
            
            // Parse bfrange entries
            Console.WriteLine("\n=== Parsed Mappings ===");
            var bfrangePattern = @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\[([^\]]+)\]";
            var matches = Regex.Matches(content, bfrangePattern);
            
            foreach (Match match in matches)
            {
                var start = match.Groups[1].Value;
                var end = match.Groups[2].Value;
                var mappings = match.Groups[3].Value;
                
                Console.WriteLine($"\nRange <{start}> to <{end}>:");
                
                // Extract individual mappings
                var hexPattern = @"<([0-9A-Fa-f]+)>";
                var hexMatches = Regex.Matches(mappings, hexPattern);
                
                var startNum = Convert.ToInt32(start, 16);
                var endNum = Convert.ToInt32(end, 16);
                
                for (int i = 0; i < hexMatches.Count && i <= (endNum - startNum); i++)
                {
                    var srcCode = (startNum + i).ToString("X4");
                    var dstHex = hexMatches[i].Groups[1].Value;
                    var dstChar = (char)Convert.ToInt32(dstHex, 16);
                    
                    Console.WriteLine($"  <{srcCode}> -> <{dstHex}> = '{dstChar}'");
                    
                    if (i >= 10 && hexMatches.Count > 20)
                    {
                        Console.WriteLine($"  ... and {hexMatches.Count - 10} more");
                        break;
                    }
                }
            }
        }
    }
}