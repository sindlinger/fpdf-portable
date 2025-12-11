using System;
using System.Globalization;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;

class ShowModDate
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ShowModDate <pdf_file>");
            return;
        }
        
        var reader = new PdfReader(args[0]);
        
        Console.WriteLine("📋 ANÁLISE DE MODDATE (Data de Modificação)\n");
        
        // Pegar o Info Dictionary
        var info = reader.Info;
        
        Console.WriteLine("🔍 Info Dictionary completo:");
        foreach (var key in info.Keys)
        {
            Console.WriteLine($"  {key}: {info[key]}");
        }
        
        Console.WriteLine("\n📅 DATAS DO DOCUMENTO:");
        
        // CreationDate
        if (info.ContainsKey("CreationDate"))
        {
            var creationDate = info["CreationDate"];
            Console.WriteLine($"\nCreationDate (criação):");
            Console.WriteLine($"  Raw: {creationDate}");
            var parsedCreation = ParsePdfDate(creationDate);
            if (parsedCreation.HasValue)
            {
                Console.WriteLine($"  Formatado: {parsedCreation.Value:dd/MM/yyyy HH:mm:ss}");
            }
        }
        
        // ModDate
        if (info.ContainsKey("ModDate"))
        {
            var modDate = info["ModDate"];
            Console.WriteLine($"\nModDate (última modificação):");
            Console.WriteLine($"  Raw: {modDate}");
            var parsedMod = ParsePdfDate(modDate);
            if (parsedMod.HasValue)
            {
                Console.WriteLine($"  Formatado: {parsedMod.Value:dd/MM/yyyy HH:mm:ss}");
            }
        }
        
        // Análise
        Console.WriteLine("\n📊 ANÁLISE:");
        
        if (info.ContainsKey("CreationDate") && info.ContainsKey("ModDate"))
        {
            var creation = ParsePdfDate(info["CreationDate"]);
            var modification = ParsePdfDate(info["ModDate"]);
            
            if (creation.HasValue && modification.HasValue)
            {
                if (creation.Value == modification.Value)
                {
                    Console.WriteLine("✅ Datas IGUAIS = PDF nunca foi modificado");
                    Console.WriteLine("   Este é um documento original sem alterações");
                }
                else
                {
                    Console.WriteLine("⚠️ Datas DIFERENTES = PDF foi modificado!");
                    Console.WriteLine($"   Criado: {creation.Value:dd/MM/yyyy HH:mm:ss}");
                    Console.WriteLine($"   Modificado: {modification.Value:dd/MM/yyyy HH:mm:ss}");
                    
                    var diff = modification.Value - creation.Value;
                    Console.WriteLine($"   Tempo entre criação e modificação: {diff.Days} dias, {diff.Hours} horas");
                }
            }
        }
        
        // Verificar incremental updates
        Console.WriteLine("\n🔄 INCREMENTAL UPDATES:");
        var pdfBytes = System.IO.File.ReadAllBytes(args[0]);
        var content = System.Text.Encoding.ASCII.GetString(pdfBytes);
        var eofCount = Regex.Matches(content, "%%EOF").Count;
        
        Console.WriteLine($"  Marcadores %%EOF: {eofCount}");
        if (eofCount > 1)
        {
            Console.WriteLine($"  ✅ Este PDF tem {eofCount - 1} incremental update(s)");
        }
        else
        {
            Console.WriteLine("  ❌ Sem incremental updates");
        }
        
        Console.WriteLine("\n💡 CONCLUSÃO:");
        Console.WriteLine("ModDate é a data/hora da última vez que o PDF foi salvo.");
        Console.WriteLine("Se ModDate = CreationDate, o PDF nunca foi editado.");
        Console.WriteLine("Se ModDate > CreationDate, o PDF foi modificado após criação.");
        
        reader.Close();
    }
    
    static DateTime? ParsePdfDate(string pdfDate)
    {
        if (string.IsNullOrEmpty(pdfDate))
            return null;
        
        // Formato: D:YYYYMMDDHHmmSSOHH'mm'
        // Exemplo: D:20240715143000-03'00'
        
        // Remover o D: inicial
        if (pdfDate.StartsWith("D:"))
            pdfDate = pdfDate.Substring(2);
        
        // Extrair componentes
        var pattern = @"^(\d{4})(\d{2})(\d{2})(\d{2})?(\d{2})?(\d{2})?([+-Z])?([\d']+)?";
        var match = Regex.Match(pdfDate, pattern);
        
        if (match.Success)
        {
            try
            {
                int year = int.Parse(match.Groups[1].Value);
                int month = int.Parse(match.Groups[2].Value);
                int day = int.Parse(match.Groups[3].Value);
                int hour = match.Groups[4].Success && match.Groups[4].Value != "" ? int.Parse(match.Groups[4].Value) : 0;
                int minute = match.Groups[5].Success && match.Groups[5].Value != "" ? int.Parse(match.Groups[5].Value) : 0;
                int second = match.Groups[6].Success && match.Groups[6].Value != "" ? int.Parse(match.Groups[6].Value) : 0;
                
                var date = new DateTime(year, month, day, hour, minute, second);
                
                // Timezone (simplificado - não vamos converter)
                if (match.Groups[7].Success)
                {
                    var tz = match.Groups[7].Value;
                    var tzOffset = match.Groups[8].Value;
                    // Por enquanto, ignoramos timezone
                }
                
                return date;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao parsear data: {ex.Message}");
            }
        }
        
        return null;
    }
}