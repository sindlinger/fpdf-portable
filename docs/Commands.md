# Comandos (iText 7) – referência rápida

## Forense / Diff
- `fpdf diff --template base.pdf --target filled.pdf [--format json|txt]`  
  Texto/linhas presentes só no target (campos preenchidos).
- `fpdf true-diff --a base.pdf --b novo.pdf [--format json|txt]`  
  Diff textual com bbox entre A e B.
- `fpdf last-session --a base.pdf --b novo.pdf [--format json|txt]`  
  Texto, linhas e imagens novas em B.
- `fpdf enhanced-last-session --a base.pdf --b novo.pdf [--format json|txt]`  
  Inclui form fields além de texto/linhas/imagens.
- `fpdf ts-last-session --a base.pdf --b novo.pdf [--format json|txt]`  
  Sugere última sessão por timestamps (Creation/ModDate).
- `fpdf forensic-batch --pairs pares.txt --out-dir out/`  
  Executa true-diff, last-session, enhanced, ts-last-session para cada linha base;novo.

## Análise / Diagnóstico
- `fpdf inspect --input file.pdf [--limit N]` — lista objetos id/tipo/subtipo.
- `fpdf deep-objects --input file.pdf [--limit N]` — objetos com Type/Subtype/len.
- `fpdf streams --input file.pdf [--limit N]` — streams com subtipo/tamanho.
- `fpdf inspect-stream --input file.pdf --id N` — bytes de um stream específico.
- `fpdf structure-analyze --input file.pdf` — mediaBox/cropBox/rotação por página.
- `fpdf visualize-structure --input file.pdf` — árvore de outlines (títulos + página).
- `fpdf analyze-objects --input file.pdf [--limit N]` — Type/Subtype/Length de objetos.
- `fpdf to-unicode --input file.pdf` — imprime mapas ToUnicode das fontes.
- `fpdf find-to-unicode --input file.pdf` — lista fontes com/sem ToUnicode.
- `fpdf show-moddate --input file.pdf` — mostra CreationDate/ModDate.

## Pipeline (TJPB)
- `fpdf pipeline-tjpb --input-dir <dir> [--output fpdf.json] [--split-anexos] [--only-despachos] [--signer-contains <texto>]`  
  - Lê PDFs (ou caches *.json) do diretório, segmenta por bookmarks (fallback heurístico) e grava tudo no Postgres (tabelas `processes` e `documents` – nunca mistura documentos de processos diferentes).  
  - Armazenamento: raw completo fica em `raw_processes` (via `fpdf load`); resultado parseado (metadados, campos, palavras com bbox, rodapés, etc.) vai para `documents.meta`.  
  - Exportação para arquivo JSON é opcional (`--output`); por padrão não gera arquivo.  
  - Campos principais por documento: `process, pdf_path, doc_label, doc_type, start_page, end_page, doc_pages, total_pages, text, fonts, images, page_size, has_signature_image, word_count, char_count, text_density, blank_ratio, words (bbox+fonte+estilo), header, footer, bookmarks, anexos_bookmarks, doc_summary (origens, signer, datas, SEI), fields (regex/YAML), forensics`.

## Geração de teste
- `fpdf create-test --output test.pdf` — PDF simples com campos simulados.
- `fpdf create-modified-test --base base.pdf --modified mod.pdf` — par base/modificado para diffs.
- `fpdf demo-issue --output issue.pdf` — PDF com casos desafiadores (colunas/imagem placeholder).
