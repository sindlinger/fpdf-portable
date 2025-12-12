# Instâncias do fpdf (onde estão os códigos e os binários)

- **Fonte oficial (iText 7, ativa):** `/mnt/b/dev/sei-dashboard/fpdf-portable`
  - Use este diretório para editar código e rodar `dotnet build -c Release`.
  - Pipeline, diffs e comandos novos estão aqui.
- **Symlink de conveniência:** `/mnt/b/dev/fpdf-portable`
  - Aponta para a fonte oficial acima (serve para quem trabalhava pelo caminho antigo em `dev/`).
- **Legado (iTextSharp, desatualizado):** `/mnt/b/dev/fpdf-portable-legacy`
  - Só para consulta/histórico. Não usar para build ou commits.
- **Binários publicados:** `bin/fpdf-*` (linux, win64, publish)
  - Artefatos gerados; não são fonte. Se precisar rebuild, use o diretório oficial e depois copie para `bin/`.

> Regra simples: código = `sei-dashboard/fpdf-portable`; legado fica em `fpdf-portable-legacy`; symlink mantém o caminho antigo sem risco de usar a base errada.
