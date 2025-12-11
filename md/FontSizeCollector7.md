# FontSizeCollector7.cs

Listener iText7 para mapear tamanhos de fonte por nome.

Como funciona:
- Implementa `IEventListener`, captura apenas eventos `RENDER_TEXT`.
- Para cada `TextRenderInfo`, lê o `FontProgram` e registra `info.GetFontSize()` em um `Dictionary<string, HashSet<float>>` injetado pelo chamador.

Uso típico:
```csharp
var map = new Dictionary<string, HashSet<float>>(StringComparer.OrdinalIgnoreCase);
var collector = new FontSizeCollector7(map);
var proc = new PdfCanvasProcessor(collector);
proc.ProcessPageContent(page);
// map["Helvetica"] → tamanhos usados na página
```

Integração:
- Chamado por `PDFAnalyzer.ExtractAllPageFontsWithSizes` para preencher `TextInfo.Fonts` com tamanhos reais observados na página.
