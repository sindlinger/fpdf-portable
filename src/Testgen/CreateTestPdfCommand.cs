using System;
using System.IO;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Gera um PDF simples para testes (texto + campos). Uso: fpdf create-test --output out.pdf
    /// </summary>
    public class CreateTestPdfCommand : Command
    {
        public override string Name => "create-test";
        public override string Description => "Gera um PDF de teste com texto e campos (iText7)";

        public override void Execute(string[] args)
        {
            string output = "test.pdf";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--output" && i + 1 < args.Length)
                    output = args[i + 1];
            }

            using var writer = new PdfWriter(output);
            using var pdf = new PdfDocument(writer);
            var doc = new Document(pdf, iText.Kernel.Geom.PageSize.A4);

            doc.Add(new Paragraph("PDF de teste gerado pelo create-test (iText7)").SetBold());
            doc.Add(new Paragraph("Campo: Nome: ____________"));
            doc.Add(new Paragraph("Campo: Processo: 0000000-00.0000.0.00.0000"));
            doc.Add(new Paragraph("Texto de exemplo em duas linhas."));

            doc.Close();
            Console.WriteLine($"Gerado {Path.GetFullPath(output)}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf create-test --output out.pdf");
        }
    }
}
