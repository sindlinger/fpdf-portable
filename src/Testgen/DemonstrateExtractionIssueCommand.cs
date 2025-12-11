using System;
using System.IO;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Cria um PDF com casos desafiadores (texto em duas colunas, imagem) para testar extração.
    /// Uso: fpdf demo-issue --output issue.pdf
    /// </summary>
    public class DemonstrateExtractionIssueCommand : Command
    {
        public override string Name => "demo-issue";
        public override string Description => "Gera PDF com casos desafiadores (colunas, imagem)";

        public override void Execute(string[] args)
        {
            string output = "issue.pdf";
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--output" && i + 1 < args.Length) output = args[i + 1];

            using var writer = new PdfWriter(output);
            using var pdf = new PdfDocument(writer);
            var doc = new Document(pdf, iText.Kernel.Geom.PageSize.A4);

            doc.Add(new Paragraph("Coluna 1: Lorem ipsum dolor sit amet, consectetur adipiscing elit."));
            doc.Add(new Paragraph("Coluna 2: Donec feugiat, velit vel placerat interdum, nisl sem."));
            doc.Add(new Paragraph("Imagem abaixo (placeholder)."));

            // Placeholder de imagem: desenha um retângulo como texto
            doc.Add(new Paragraph("[Imagem 200x100]").SetBold());

            doc.Close();
            Console.WriteLine($"Gerado {Path.GetFullPath(output)}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf demo-issue --output issue.pdf");
        }
    }
}
