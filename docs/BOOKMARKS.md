# Bookmarks, cache bruto e dados parseados

Objetivo: guardar **tudo** que o PDFAnalyzer devolve e, ao mesmo tempo, manter
no banco apenas o que a aplicação realmente usa.

## Onde fica cada coisa

- **Snapshot bruto completo**: `tmp/cache/<arquivo>.raw.json`
  - Gerado pelo comando `load`.
  - Inclui Bookmarks, Annotations, Layers/OCGs, EmbeddedFiles, Resources,
    PageText, Lines, Footers, metadados, etc.
  - É temporário; pode ser apagado após depuração.

- **Dados parseados**: Postgres via `PgDocStore`.
  - Guarda texto, linhas, footers por página, bookmarks resumidos, hashes,
    recursos, etc.
  - Não replica o JSON bruto; apenas referencia `file_name/doc_id`.

## Fluxo do `load`

1) Lê PDF(s) do input.
2) Analisa com iText7 uma única vez.
3) Escreve o bruto em `tmp/cache/<arquivo>.raw.json`.
4) Persiste somente os campos parseados no Postgres (sem o bruto).

## Inspecionar bookmarks brutos

```bash
jq '.Bookmarks.RootItems[] | {title:.Title, page:.Destination.PageNumber}' \
  tmp/cache/000206642024815.raw.json
```

O objeto `Bookmarks` contém:
- `RootItems` na ordem original; cada item tem `Title`, `Destination.PageNumber`,
  `Level`, `Children`.
- `TotalCount` e `MaxDepth`.

## Por que separar

- O JSON bruto é grande e só serve para depuração/enriquecimento eventual.
- O banco fica enxuto e rápido para consultas e para o pipeline.
- Se precisarmos de um campo novo, reabrimos o `.raw.json`, extraímos e
  gravamos no Postgres sem reprocessar o PDF.
