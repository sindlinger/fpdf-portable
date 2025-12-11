# PDFAnalyzer.cs

Responsável por orquestrar a análise completa de PDFs. Hoje faz:
- Abre iText7 (`PdfDocument`) e iTextSharp (`PdfReader`) em paralelo; iText7 é preferido para texto, linhas, metadados, anotações, form fields, headers/footers, fontes e recursos. iTextSharp fica só como fallback legado.
- `AnalyzeFull()` produz `PDFAnalysisResult` com texto da página (`TextInfo.PageText`), linhas (`TextInfo.Lines` via `IText7LineCollector`), fontes/tamanhos (`FontSizeCollector7`), anotações, campos de formulário, referências de documento, headers/footers, recursos, segurança e sumarização.
- Fallback de texto legado pode ser forçado com `FPDF_TEXT_LEGACY=1`.
- Headers/footers: usa `AdvancedHeaderFooterStrategy7` (CanvasProcessor) para regiões top/bottom.
- Fontes: tamanhos coletados via eventos de renderização (`FontSizeCollector7`); detalhes adicionais lidos do dicionário de recursos.
- Mantém caminhos de compatibilidade (legacy) para não quebrar comandos antigos enquanto a remoção do iTextSharp não é concluída.

Pontos de integração:
- Chamado pelo comando `documents` e pelo pipeline de cache (`FpdfLoadCommand`) para gerar JSON estruturado.
- Consumido por segmentação e pipelines regex externos que dependem de `pages_detail[].lines[]` e `PageText`.
