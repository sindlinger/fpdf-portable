# PDFAnalyzer (iText 7)

Responsável pela análise completa do PDF. Dependências: apenas iText 7 (`itext7`), PdfCanvasProcessor e listeners customizados.

## Fluxo principal (`AnalyzeFull`)
- Abre `PdfDocument` via `PdfAccessManager7` (cache).
- Extrai: `Metadata`, `XMPMetadata` (básico), `DocumentInfo`, `Pages`, `Security`, `Resources`, `Statistics`, `Accessibility`, `Layers`, `Signatures`, `ColorProfiles`, `Bookmarks`, `PDFACompliance` heurística e `Multimedia` (lista vazia placeholder).

## Por página
- Tamanho/rotação.
- Texto bruto: `PdfTextExtractor.GetTextFromPage`.
- Fontes usadas com tamanhos: listener `FontSizeCollector7`.
- Linhas: `IText7LineCollector` (texto, fonte, estilo, bbox, renderMode, wordSpacing, horizScaling; charSpacing/rise 0).
- Palavras: `IText7WordCollector` com bbox.
- Recursos: imagens (XObject Image), contagem de fontes, campos de formulário (via `PdfAcroForm`).
- Anotações: tipo/autor/contents/bbox.
- Headers/Footers: `AdvancedHeaderFooterStrategy7`.
- Referências (regex SEI/Processo/Ofício/Anexo).

## Outras seções
- Segurança: flags de `PdfReader`.
- ResourcesSummary: total de imagens/fontes/forms e anexos via NameTree `EmbeddedFiles`.
- Accessibility: `IsTaggedPDF`, idioma do catálogo.
- Layers: OCGs do catálogo.
- Assinaturas: campos `Sig` do AcroForm.
- ColorProfiles: entries de ColorSpace por página (superficial).
- Bookmarks: usa `PdfOutline` e resolve página por comparação de objeto da dest array.

## Limitações conhecidas
- Destinos de bookmark apenas por página; tipo genérico.
- ColorProfiles e PDFACompliance são heurísticos/superficiais.
- `Multimedia` ainda não implementado (lista vazia).
- charSpacing/rise não expostos pela API atual (mantidos 0).

## Extensões futuras
- Enriquecer XMP/PDFA (usar iText pdfa checker ou XMP parser).
- Destinos de bookmark com zoom/coords.
- Detectar JavaScript, RichMedia, e perfis de cor ICC completos.
