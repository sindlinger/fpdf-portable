using System;
using iText.Kernel.Pdf;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Imprime estrutura de outlines e kids do catalog, para depuração.
    /// Uso: fpdf visualize-structure --input file.pdf
    /// </summary>
    public class VisualizePdfStructureCommand : Command
    {
        public override string Name => "visualize-structure";
        public override string Description => "Mostra árvore básica de outlines/structure root";

        public override void Execute(string[] args)
        {
            if (!ParseCommonOptions(args, out var inputFile, out var opts)) return;
            if (string.IsNullOrWhiteSpace(inputFile)) { Console.WriteLine("Informe --input <file.pdf>"); return; }

            using var doc = new PdfDocument(new PdfReader(inputFile));
            var outlines = doc.GetOutlines(false);
            if (outlines == null)
            {
                Console.WriteLine("Sem outlines.");
                return;
            }
            PrintOutline(outlines, 0, doc);
        }

        private void PrintOutline(PdfOutline outline, int level, PdfDocument doc)
        {
            foreach (var child in outline.GetAllChildren())
            {
                var dest = child.GetDestination();
                int page = 0;
                var destObj = dest?.GetPdfObject();
                if (destObj is PdfArray arr)
                {
                    for (int p = 1; p <= doc.GetNumberOfPages(); p++)
                        if (arr.Get(0).Equals(doc.GetPage(p).GetPdfObject())) { page = p; break; }
                }
                Console.WriteLine(new string(' ', level * 2) + "- " + child.GetTitle() + (page > 0 ? $" (p{page})" : ""));
                PrintOutline(child, level + 1, doc);
            }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf visualize-structure --input file.pdf");
        }
    }
}
