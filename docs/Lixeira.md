# Lixeira (artefatos legacy, excluídos do build)

Todos os arquivos que dependiam de iTextSharp ou ferramentas auxiliares antigas foram movidos para esta pasta e removidos da compilação (`<Compile Remove="Lixeira/**">` no `fpdf.csproj`). Use apenas para consulta histórica.

## Raiz da Lixeira
- `AdvancedPDFProcessor.cs` — pipeline avançado em iTextSharp para XMP/PDFA/OCG.
- `HexTextDecoder.cs` — decodificador de streams de texto em iTextSharp.
- `PdfAccessManager.cs` — cache de `PdfReader` (iTextSharp).
- `TextStrategyStubs.cs` — stubs de strategies de texto (iTextSharp).

## Subpastas
- `Legacy/Commands/*` — comandos antigos (extract images, last session detectors, etc.) em iTextSharp.
- `Legacy/Strategies/*` — strategies de layout/header/footer/fontes (iTextSharp).
- `Legacy/Utils/*` — utilidades antigas (cache, validação PDF/A, etc.).
- `tools/*` — scripts de análise/forense/teste baseados em iTextSharp (fora do build).

> Nota: Nenhum arquivo da Lixeira é compilado. Se precisar reativar algo, mova-o de volta e adicione iTextSharp novamente ao csproj.
