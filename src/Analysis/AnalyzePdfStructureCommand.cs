using System;
using System.Linq;
using iText.Kernel.Pdf;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Analisa a estrutura de páginas: mediaBox, cropBox, rotation.
    /// Uso: fpdf structure-analyze --input file.pdf
    /// </summary>
    public class AnalyzePdfStructureCommand : Command
    {
        public override string Name => "structure-analyze";
        public override string Description => "Analisa boxes e rotação das páginas (iText7)";

        public override void Execute(string[] args)
        {
            if (!ParseCommonOptions(args, out var inputFile, out var opts))
                return;
            if (string.IsNullOrWhiteSpace(inputFile))
            {
                Console.WriteLine("Informe --input <file.pdf>");
                return;
            }

            using var doc = new PdfDocument(new PdfReader(inputFile));
            for (int p = 1; p <= doc.GetNumberOfPages(); p++)
            {
                var page = doc.GetPage(p);
                var media = page.GetMediaBox();
                var crop = page.GetCropBox();
                Console.WriteLine($"Página {p}: rot={page.GetRotation()} media=({media.GetLeft()}, {media.GetBottom()}, {media.GetRight()}, {media.GetTop()}) crop=({crop.GetLeft()}, {crop.GetBottom()}, {crop.GetRight()}, {crop.GetTop()})");
            }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf structure-analyze --input file.pdf");
        }
    }
}
