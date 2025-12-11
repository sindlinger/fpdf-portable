using System;
using System.IO;
using System.Text;
using iTextSharp.text.pdf;

class VisualizePDFStructure
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: VisualizePDFStructure <pdf_file>");
            return;
        }
        
        var reader = new PdfReader(args[0]);
        
        Console.WriteLine("🔍 VISUALIZAÇÃO DA ESTRUTURA PDF\n");
        
        // Mostrar incremental updates
        var pdfBytes = File.ReadAllBytes(args[0]);
        var content = Encoding.ASCII.GetString(pdfBytes);
        var eofCount = 0;
        var pos = 0;
        while ((pos = content.IndexOf("%%EOF", pos)) != -1)
        {
            eofCount++;
            pos += 5;
        }
        
        Console.WriteLine($"📊 ESTATÍSTICAS:");
        Console.WriteLine($"Marcadores %%EOF: {eofCount}");
        Console.WriteLine($"Incremental Updates: {(eofCount > 1 ? "SIM" : "NÃO")}");
        Console.WriteLine($"Total de objetos: {reader.XrefSize}");
        Console.WriteLine();
        
        // Mostrar estrutura da página
        Console.WriteLine("📄 ESTRUTURA DA PÁGINA 1:\n");
        
        var page = reader.GetPageN(1);
        var pageRef = reader.GetPageOrigRef(1);
        
        Console.WriteLine($"PÁGINA (objeto {pageRef?.Number ?? -1})");
        Console.WriteLine("│");
        
        // Contents
        var contents = page.Get(PdfName.CONTENTS);
        if (contents != null)
        {
            Console.WriteLine("├─ /Contents");
            if (contents.IsArray())
            {
                var array = (PdfArray)contents;
                for (int i = 0; i < array.Size; i++)
                {
                    var item = array.GetDirectObject(i);
                    if (item.IsIndirect())
                    {
                        var indRef = (PRIndirectReference)item;
                        Console.WriteLine($"│  ├─ {indRef.Number} 0 R → Stream de conteúdo");
                        ShowStreamPreview(reader, indRef.Number);
                    }
                }
            }
            else if (contents.IsIndirect())
            {
                var indRef = (PRIndirectReference)contents;
                Console.WriteLine($"│  └─ {indRef.Number} 0 R → Stream de conteúdo");
                ShowStreamPreview(reader, indRef.Number);
            }
        }
        
        // Resources
        Console.WriteLine("│");
        Console.WriteLine("└─ /Resources");
        var resources = page.GetAsDict(PdfName.RESOURCES);
        if (resources != null)
        {
            // Fonts
            var fonts = resources.GetAsDict(PdfName.FONT);
            if (fonts != null)
            {
                Console.WriteLine("   ├─ /Font");
                foreach (var fontName in fonts.Keys)
                {
                    var font = fonts.Get(fontName);
                    if (font.IsIndirect())
                    {
                        var indRef = (PRIndirectReference)font;
                        var fontDict = (PdfDictionary)reader.GetPdfObject(indRef.Number);
                        var baseFont = fontDict?.GetAsName(PdfName.BASEFONT);
                        Console.WriteLine($"   │  └─ {fontName} → {indRef.Number} 0 R ({baseFont})");
                    }
                }
            }
        }
        
        // Annotations
        var annots = page.GetAsArray(PdfName.ANNOTS);
        if (annots != null && annots.Size > 0)
        {
            Console.WriteLine("   │");
            Console.WriteLine("   └─ /Annots");
            for (int i = 0; i < annots.Size; i++)
            {
                var annot = annots.GetDirectObject(i);
                if (annot.IsIndirect())
                {
                    var indRef = (PRIndirectReference)annot;
                    Console.WriteLine($"      └─ {indRef.Number} 0 R → Anotação");
                    ShowAnnotationInfo(reader, indRef.Number);
                }
            }
        }
        
        // Mostrar objetos modificados
        Console.WriteLine("\n\n🔄 OBJETOS MODIFICADOS (generation > 0):");
        var foundModified = false;
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
                        foundModified = true;
                        Console.WriteLine($"  Objeto {i} - Geração {indRef.Generation}");
                    }
                }
            }
            catch { }
        }
        
        if (!foundModified)
        {
            Console.WriteLine("  Nenhum objeto com generation > 0");
        }
        
        reader.Close();
    }
    
    static void ShowStreamPreview(PdfReader reader, int objNum)
    {
        try
        {
            var obj = reader.GetPdfObject(objNum);
            if (obj != null && obj.IsStream())
            {
                var stream = (PRStream)obj;
                var bytes = PdfReader.GetStreamBytes(stream);
                var content = Encoding.ASCII.GetString(bytes);
                
                if (content.Contains("BT") && content.Contains("ET"))
                {
                    Console.WriteLine($"│     └─ Contém texto:");
                    
                    // Extrair primeira operação de texto
                    var btPos = content.IndexOf("BT");
                    var etPos = content.IndexOf("ET", btPos);
                    if (btPos >= 0 && etPos > btPos)
                    {
                        var textOps = content.Substring(btPos, Math.Min(etPos - btPos + 2, 100));
                        var lines = textOps.Split('\n');
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                Console.WriteLine($"│        {line.Trim()}");
                                if (line.Contains("Tj"))
                                    break;
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }
    
    static void ShowAnnotationInfo(PdfReader reader, int objNum)
    {
        try
        {
            var obj = reader.GetPdfObject(objNum);
            if (obj != null && obj.IsDictionary())
            {
                var dict = (PdfDictionary)obj;
                
                // Tipo
                var subtype = dict.GetAsName(PdfName.SUBTYPE);
                if (subtype != null)
                {
                    Console.WriteLine($"         Tipo: {subtype}");
                }
                
                // Conteúdo
                var contents = dict.GetAsString(PdfName.CONTENTS);
                if (contents != null)
                {
                    var text = contents.ToUnicodeString();
                    if (text.Length > 50)
                        text = text.Substring(0, 50) + "...";
                    Console.WriteLine($"         Texto: \"{text}\"");
                }
                
                // Timestamp
                var mDate = dict.Get(PdfName.M);
                if (mDate != null)
                {
                    Console.WriteLine($"         📅 Timestamp: {mDate}");
                }
            }
        }
        catch { }
    }
}