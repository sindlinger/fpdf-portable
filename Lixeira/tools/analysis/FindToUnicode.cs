using System;
using System.Text;
using iTextSharp.text.pdf;

class FindToUnicode
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: FindToUnicode <pdf_file>");
            return;
        }
        
        var reader = new PdfReader(args[0]);
        
        // Check all pages for fonts with ToUnicode
        for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
        {
            Console.WriteLine($"\n=== Page {pageNum} ===");
            var page = reader.GetPageN(pageNum);
            var resources = page.GetAsDict(PdfName.RESOURCES);
            
            if (resources != null)
            {
                var fonts = resources.GetAsDict(PdfName.FONT);
                if (fonts != null)
                {
                    foreach (var fontName in fonts.Keys)
                    {
                        var font = fonts.GetAsDict(fontName);
                        if (font != null)
                        {
                            Console.WriteLine($"\nFont: {fontName}");
                            var basefont = font.GetAsName(PdfName.BASEFONT);
                            if (basefont != null)
                                Console.WriteLine($"  BaseFont: {basefont}");
                            
                            var encoding = font.Get(PdfName.ENCODING);
                            if (encoding != null)
                                Console.WriteLine($"  Encoding: {encoding}");
                            
                            var toUnicode = font.Get(PdfName.TOUNICODE);
                            if (toUnicode != null)
                            {
                                Console.WriteLine($"  ToUnicode: YES (object {GetObjectNumber(toUnicode)})");
                                
                                if (toUnicode.IsIndirect())
                                {
                                    var stream = (PRStream)PdfReader.GetPdfObject(toUnicode);
                                    if (stream != null)
                                    {
                                        var bytes = PdfReader.GetStreamBytes(stream);
                                        var content = Encoding.ASCII.GetString(bytes);
                                        
                                        Console.WriteLine($"  ToUnicode size: {bytes.Length} bytes");
                                        
                                        // Show preview
                                        var preview = content.Length > 300 ? content.Substring(0, 300) + "..." : content;
                                        Console.WriteLine($"  ToUnicode preview:\n{preview}");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"  ToUnicode: NO");
                            }
                        }
                    }
                }
            }
        }
        
        reader.Close();
    }
    
    static int GetObjectNumber(PdfObject obj)
    {
        if (obj.IsIndirect())
        {
            var indRef = (PRIndirectReference)obj;
            return indRef.Number;
        }
        return -1;
    }
}