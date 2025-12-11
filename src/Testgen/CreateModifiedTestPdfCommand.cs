using System;
using System.IO;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Gera um par PDF base + modificado para testes de diff.
    /// Uso: fpdf create-modified-test --base base.pdf --modified mod.pdf
    /// </summary>
    public class CreateModifiedTestPdfCommand : Command
    {
        public override string Name => "create-modified-test";
        public override string Description => "Gera PDF base e PDF modificado para testar diff";

        public override void Execute(string[] args)
        {
            string basePath = "base.pdf";
            string modPath = "mod.pdf";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--base" && i + 1 < args.Length) basePath = args[i + 1];
                if (args[i] == "--modified" && i + 1 < args.Length) modPath = args[i + 1];
            }

            // Base
            using (var writer = new PdfWriter(basePath))
            using (var pdf = new PdfDocument(writer))
            {
                var doc = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
                doc.Add(new Paragraph("Documento base"));
                doc.Add(new Paragraph("Campo: Nome: ____________"));
                doc.Add(new Paragraph("Campo: Processo: 0000000-00.0000.0.00.0000"));
                doc.Close();
            }

            // Modificado
            using (var writer = new PdfWriter(modPath))
            using (var pdf = new PdfDocument(writer))
            {
                var doc = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
                doc.Add(new Paragraph("Documento base"));
                doc.Add(new Paragraph("Campo: Nome: Fulano de Tal"));
                doc.Add(new Paragraph("Campo: Processo: 1234567-89.2025.8.15.0001"));
                doc.Add(new Paragraph("Observação: texto extra."));
                doc.Close();
            }

            Console.WriteLine($"Gerados: {Path.GetFullPath(basePath)} e {Path.GetFullPath(modPath)}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf create-modified-test --base base.pdf --modified mod.pdf");
        }
    }
}
