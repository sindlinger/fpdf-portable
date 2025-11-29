using System;
using System.Text;
using iTextSharp.text.pdf;

class AnalyzePDFObjectStructure
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: AnalyzePDFObjectStructure <pdf_file>");
            return;
        }
        
        var reader = new PdfReader(args[0]);
        
        Console.WriteLine("=== ANÁLISE DETALHADA DE TIMESTAMPS E REFERÊNCIAS ===\n");
        
        Console.WriteLine("📋 RESPOSTA DIRETA:");
        Console.WriteLine("1. Timestamps NÃO são dos objetos individuais");
        Console.WriteLine("2. Timestamps estão no documento (CreationDate, ModDate)");
        Console.WriteLine("3. Objetos têm GENERATION NUMBER que indica modificação");
        Console.WriteLine("4. Referências indiretas conectam objetos\n");
        
        // Analisar Info Dictionary
        Console.WriteLine("📅 TIMESTAMPS DO DOCUMENTO:");
        var info = reader.Info;
        foreach (var key in info.Keys)
        {
            if (key.Contains("Date"))
            {
                Console.WriteLine($"  {key}: {info[key]}");
            }
        }
        
        Console.WriteLine("\n🔍 ESTRUTURA DE OBJETOS PDF:");
        Console.WriteLine("Formato: <object_num> <generation_num> obj");
        Console.WriteLine("Exemplo: 5 0 obj = objeto 5, geração 0 (original)");
        Console.WriteLine("         5 1 obj = objeto 5, geração 1 (modificado)\n");
        
        // Mostrar alguns objetos
        Console.WriteLine("📦 EXEMPLOS DE OBJETOS E SUAS REFERÊNCIAS:\n");
        
        for (int i = 1; i < Math.Min(20, reader.XrefSize); i++)
        {
            try
            {
                var obj = reader.GetPdfObject(i);
                if (obj == null) continue;
                
                Console.WriteLine($"=== Objeto {i} ===");
                
                // Verificar generation
                int generation = 0;
                if (obj is PRIndirectReference)
                {
                    var indRef = (PRIndirectReference)obj;
                    generation = indRef.Generation;
                }
                
                Console.WriteLine($"Generation: {generation} {(generation > 0 ? "(MODIFICADO!)" : "(original)")}");
                Console.WriteLine($"Tipo: {obj.GetType().Name}");
                
                if (obj.IsStream())
                {
                    var stream = (PRStream)obj;
                    Console.WriteLine("É um STREAM (pode conter texto, imagem, etc.)");
                    
                    // Verificar se tem conteúdo de texto
                    var bytes = PdfReader.GetStreamBytes(stream);
                    var content = Encoding.ASCII.GetString(bytes);
                    
                    if (content.Contains("BT") && content.Contains("ET"))
                    {
                        Console.WriteLine("  ✓ Contém operações de texto (BT...ET)");
                    }
                    
                    // Mostrar filtros
                    var filter = stream.Get(PdfName.FILTER);
                    if (filter != null)
                    {
                        Console.WriteLine($"  Filtro: {filter}");
                    }
                }
                else if (obj.IsDictionary())
                {
                    var dict = (PdfDictionary)obj;
                    Console.WriteLine("É um DICTIONARY");
                    
                    // Verificar tipo
                    var type = dict.GetAsName(PdfName.TYPE);
                    if (type != null)
                    {
                        Console.WriteLine($"  Tipo PDF: {type}");
                    }
                    
                    // Mostrar algumas chaves
                    Console.WriteLine($"  Chaves: {string.Join(", ", dict.Keys)}");
                    
                    // Verificar timestamp individual (raro)
                    var modDate = dict.Get(PdfName.M);
                    if (modDate != null)
                    {
                        Console.WriteLine($"  ⚠️ TEM TIMESTAMP PRÓPRIO: {modDate}");
                    }
                }
                
                // Mostrar como é referenciado
                Console.WriteLine($"Referência: {i} 0 R (indirect reference)");
                
                // Encontrar onde é usado
                Console.WriteLine("Usado em:");
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    if (IsObjectReferencedInPage(page, i))
                    {
                        Console.WriteLine($"  - Página {pageNum}");
                        
                        // Verificar tipo de uso
                        var contents = page.Get(PdfName.CONTENTS);
                        if (IsObjectInContents(contents, i))
                        {
                            Console.WriteLine("    (como conteúdo de página)");
                        }
                        
                        var resources = page.GetAsDict(PdfName.RESOURCES);
                        if (resources != null && IsObjectInResources(resources, i))
                        {
                            Console.WriteLine("    (como recurso - fonte, imagem, etc.)");
                        }
                    }
                }
                
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao analisar objeto {i}: {ex.Message}\n");
            }
        }
        
        Console.WriteLine("\n📝 RESUMO IMPORTANTE:");
        Console.WriteLine("1. TIMESTAMPS:");
        Console.WriteLine("   - Normalmente só no documento (CreationDate, ModDate)");
        Console.WriteLine("   - Raramente objetos têm timestamp próprio (/M)");
        Console.WriteLine("   - Anotações podem ter timestamp");
        Console.WriteLine("\n2. COMO SABEMOS QUE FOI MODIFICADO:");
        Console.WriteLine("   - Generation number > 0");
        Console.WriteLine("   - Aparece no último xref (incremental update)");
        Console.WriteLine("   - Data de modificação do documento");
        Console.WriteLine("\n3. REFERÊNCIAS INDIRETAS:");
        Console.WriteLine("   - Formato: 'N 0 R' onde N é o número do objeto");
        Console.WriteLine("   - Páginas referenciam conteúdo");
        Console.WriteLine("   - Conteúdo pode referenciar fontes, imagens");
        Console.WriteLine("   - Tudo é conectado por referências");
        
        reader.Close();
    }
    
    static bool IsObjectReferencedInPage(PdfDictionary page, int objNum)
    {
        // Verificar em todo o dicionário da página
        return CheckDictionaryForReference(page, objNum);
    }
    
    static bool CheckDictionaryForReference(PdfDictionary dict, int objNum)
    {
        foreach (var key in dict.Keys)
        {
            var value = dict.Get(key);
            if (value != null)
            {
                if (value.IsIndirect())
                {
                    var indRef = (PRIndirectReference)value;
                    if (indRef.Number == objNum)
                        return true;
                }
                else if (value.IsArray())
                {
                    var array = (PdfArray)value;
                    for (int i = 0; i < array.Size; i++)
                    {
                        var item = array.GetDirectObject(i);
                        if (item.IsIndirect() && ((PRIndirectReference)item).Number == objNum)
                            return true;
                    }
                }
                else if (value.IsDictionary())
                {
                    if (CheckDictionaryForReference((PdfDictionary)value, objNum))
                        return true;
                }
            }
        }
        return false;
    }
    
    static bool IsObjectInContents(PdfObject contents, int objNum)
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
            return ((PRIndirectReference)contents).Number == objNum;
        }
        
        return false;
    }
    
    static bool IsObjectInResources(PdfDictionary resources, int objNum)
    {
        return CheckDictionaryForReference(resources, objNum);
    }
}