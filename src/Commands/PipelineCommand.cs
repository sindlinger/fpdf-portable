using System;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Comando pipeline com subcomandos. Atual: apenas "tjpb".
    /// Uso: fpdf pipeline tjpb --input-dir ... --output ...
    /// </summary>
    public class PipelineCommand : Command
    {
        public override string Name => "pipeline";
        public override string Description => "Comandos de pipeline (subcomando requerido: tjpb)";

        public override void Execute(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            var sub = args[0].ToLowerInvariant();
            var tail = args.Length > 1 ? args[1..] : Array.Empty<string>();

            switch (sub)
            {
                case "tjpb":
                    new PipelineTjpbCommand().Execute(tail);
                    break;
                default:
                    Console.WriteLine($"Subcomando de pipeline não suportado: {sub}");
                    ShowHelp();
                    break;
            }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf pipeline tjpb --input-dir <dir> --output fpdf.json");
        }
    }
}
