# AdvancedHeaderFooterStrategy7.cs

Listener iText7 que detecta texto em regiões de header ou footer via `PdfCanvasProcessor`.

Como funciona:
- Recebe `extractHeader` (true/false) e `pageHeight`.
- Em cada evento `RENDER_TEXT`, captura `x`/`y` do baseline e seleciona apenas texto no topo (>= 90% da altura) ou rodapé (<= 10%).
- Ordena os chunks por Y decrescente, depois X crescente, e concatena com quebras de linha quando muda a faixa de Y.
- Resultado é lido por `PDFAnalyzer.DetectHeadersFooters`.

Uso:
- `var listener = new AdvancedHeaderFooterStrategy7(true, pageHeight);`
- `new PdfCanvasProcessor(listener).ProcessPageContent(page);`
- `listener.GetResultantText()` retorna o texto filtrado.
