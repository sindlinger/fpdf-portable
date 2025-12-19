using System;

namespace FilterPDF.Commands
{
    public abstract class StubCommandBase : Command
    {
        public override void Execute(string[] args)
        {
            Console.WriteLine($"{Name} is not available in this build.");
        }

        public override void ShowHelp()
        {
            Console.WriteLine($"{Name} is not available in this build.");
        }
    }

    public class PipelineFindDespachoCommand : StubCommandBase
    {
        public override string Name => "pipeline-find-despacho";
        public override string Description => "Stub";
    }

    public class PipelineFindCertidaoCmCommand : StubCommandBase
    {
        public override string Name => "pipeline-find-certidao-cm";
        public override string Description => "Stub";
    }

    public class PipelineExcelCommand : StubCommandBase
    {
        public override string Name => "pipeline-excel";
        public override string Description => "Stub";
    }

    public class LaudoHashCommand : StubCommandBase
    {
        public override string Name => "laudo-hash";
        public override string Description => "Stub";
    }

    public class LaudoDetectCommand : StubCommandBase
    {
        public override string Name => "laudo-detect";
        public override string Description => "Stub";
    }

    public class LaudoLinkCommand : StubCommandBase
    {
        public override string Name => "laudo-link";
        public override string Description => "Stub";
    }

    public class ParagraphsTemplateCommand : StubCommandBase
    {
        public override string Name => "paragraphs-template";
        public override string Description => "Stub";
    }

    public class ParagraphsSpansCommand : StubCommandBase
    {
        public override string Name => "paragraphs-spans";
        public override string Description => "Stub";
    }

    public class WordsBBoxCommand : StubCommandBase
    {
        public override string Name => "words-bbox";
        public override string Description => "Stub";
    }

    public class DocViewCommand : StubCommandBase
    {
        public override string Name => "doc-view";
        public override string Description => "Stub";
    }

    public class RawListCommand : StubCommandBase
    {
        public override string Name => "raw-list";
        public override string Description => "Stub";
    }

    public class FpdfDatasetCommand : StubCommandBase
    {
        public override string Name => "dataset";
        public override string Description => "Stub";
    }

    public class SelfTestCommand : StubCommandBase
    {
        public override string Name => "selftest";
        public override string Description => "Stub";
    }
}
