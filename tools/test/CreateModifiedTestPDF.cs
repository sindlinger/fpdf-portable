using System;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;

class CreateModifiedTestPDF
{
    public static void Main()
    {
        Console.WriteLine("Criando PDF com modificações para teste...");
        
        // Step 1: Create original PDF
        var originalPath = "test_original.pdf";
        var modifiedPath = "test_modified.pdf";
        
        CreateOriginalPDF(originalPath);
        Console.WriteLine($"✓ PDF original criado: {originalPath}");
        
        // Step 2: Modify the PDF (incremental update)
        ModifyPDF(originalPath, modifiedPath);
        Console.WriteLine($"✓ PDF modificado criado: {modifiedPath}");
        
        Console.WriteLine("\nPDFs de teste criados com sucesso!");
        Console.WriteLine("Use o comando para testar:");
        Console.WriteLine($"  bin/fpdf {modifiedPath} filter lastsession -F json");
    }
    
    static void CreateOriginalPDF(string path)
    {
        var doc = new Document();
        var writer = PdfWriter.GetInstance(doc, new FileStream(path, FileMode.Create));
        
        doc.Open();
        
        // Add metadata
        doc.AddTitle("Contrato de Teste");
        doc.AddAuthor("Sistema de Testes");
        doc.AddCreationDate();
        
        // Add content
        doc.Add(new Paragraph("CONTRATO DE PRESTAÇÃO DE SERVIÇOS", 
            new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD)));
        doc.Add(new Paragraph(" "));
        doc.Add(new Paragraph("Este contrato foi criado em " + DateTime.Now.ToString("dd/MM/yyyy")));
        doc.Add(new Paragraph(" "));
        doc.Add(new Paragraph("1. CLÁUSULA PRIMEIRA - DO OBJETO"));
        doc.Add(new Paragraph("O presente contrato tem por objeto a prestação de serviços de consultoria."));
        doc.Add(new Paragraph(" "));
        doc.Add(new Paragraph("2. CLÁUSULA SEGUNDA - DO PAGAMENTO"));
        doc.Add(new Paragraph("O pagamento será realizado em 30 dias após a prestação do serviço."));
        
        doc.Close();
    }
    
    static void ModifyPDF(string originalPath, string outputPath)
    {
        // Read existing PDF
        var reader = new PdfReader(originalPath);
        
        // Create stamper for incremental update
        var stamper = new PdfStamper(reader, new FileStream(outputPath, FileMode.Create), '\0', true);
        
        // Get first page
        var canvas = stamper.GetOverContent(1);
        
        // Add new text (this creates incremental update)
        canvas.BeginText();
        canvas.SetFontAndSize(BaseFont.CreateFont(BaseFont.HELVETICA_BOLD, BaseFont.CP1252, BaseFont.NOT_EMBEDDED), 12);
        canvas.SetColorFill(BaseColor.RED);
        canvas.SetTextMatrix(50, 200);
        canvas.ShowText("ADENDO: Cláusula adicional incluída em " + DateTime.Now.ToString("dd/MM/yyyy"));
        canvas.EndText();
        
        // Add another modification
        canvas.BeginText();
        canvas.SetTextMatrix(50, 180);
        canvas.ShowText("3. CLÁUSULA TERCEIRA - DOS PRAZOS REVISADOS");
        canvas.EndText();
        
        // Add annotation (comment)
        var annotation = PdfAnnotation.CreateText(
            stamper.Writer,
            new Rectangle(100, 400, 200, 450),
            "Revisão Legal",
            "Este contrato foi revisado pelo departamento jurídico em " + DateTime.Now.ToString("dd/MM/yyyy"),
            true,
            "Comment"
        );
        stamper.AddAnnotation(annotation, 1);
        
        // Update metadata
        var info = reader.Info;
        info["ModDate"] = "D:" + DateTime.Now.ToString("yyyyMMddHHmmss");
        stamper.MoreInfo = info;
        
        stamper.Close();
        reader.Close();
    }
}