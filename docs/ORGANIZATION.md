# Organização do fpdf-portable

## Raiz
- `fpdf-linux` → symlink para `bin/publish-linux/fpdf` (self-contained Linux x64).
- `fpdf-win.exe` → symlink para `bin/publish-win/fpdf.exe` (self-contained Windows x64).
- `fpdf.csproj` / `src/` → código-fonte (iText 7).

## Binários
- `bin/publish-linux/` — build self-contained Linux (`dotnet publish -c Release -r linux-x64 --self-contained true`).
- `bin/publish-win/` — build self-contained Windows (`dotnet publish -c Release -r win-x64 --self-contained true`).
- `bin/Release/net6.0/` — build padrão (dependente de runtime).

## Documentação
- `docs/Commands.md` — referência rápida dos comandos.
- `docs/PDFAnalyzer.md` — o que o analyzer extrai por página.
- `docs/INSTANCES.md` — onde estão as instâncias/legado/symlink.
- `docs/ORGANIZATION.md` — este arquivo.

## Uso rápido
- Linux: `./fpdf-linux --help`
- Windows: `./fpdf-win.exe --help`
- Pipeline TJPB: `./fpdf-linux pipeline tjpb --input-dir <dir> --output fpdf.json`

## Observações
- iTextSharp foi removido; tudo roda com iText 7.
- `ingest-db` removido; para popular cache use `fpdf load <pdf|dir> --db-path ...`.
