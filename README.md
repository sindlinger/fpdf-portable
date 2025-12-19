# TJPB Despacho Extractor (FPDF)

Este projeto inclui um extrator robusto para documentos de DESPACHO do TJPB (DIESP), com segmentacao por bandas, paragracao por bbox, hashes, evidencias e saida estruturada. A saida padrao vai para o Postgres (processes.json). Arquivos JSON so sao gerados quando o usuario solicita com `--out` ou `--dump`.

## Como rodar

1. Ajuste o `config.yaml` (na raiz) conforme necessario.
2. Copie os PDFs para `data/inbox` (ou use `--inbox`).
3. Execute:

```bash
fpdf tjpb-despacho-extractor extract --inbox data/inbox --config config.yaml --verbose
```

### Opcoes

- `--inbox <dir>`: pasta com PDFs (default: `data/inbox`).
- `--config <file>`: caminho do YAML (default: `./config.yaml`).
- `--out <dir>`: exporta JSON por PDF (opcional).
- `--dump`: gera dumps de paginas/bandas/paragrafos (opcional).
- `--process <num>`: filtra por processo no Postgres.
- `--pg <uri>`: string de conexao Postgres.
- `--skip-load`: nao re-ingere PDFs do inbox (usa apenas raw_processes).

### Relatorio rapido (sensibilidade/especificidade)

Gera um resumo de cobertura por campo a partir do `processes.json` no Postgres:

```bash
fpdf tjpb-despacho-extractor report --config config.yaml --pg "Host=localhost;Username=fpdf;Password=fpdf;Database=fpdf" --limit 100
```

Opcional: `--json` para saida estruturada.

## Documentacao adicional

- `docs/DUPLICATES_AND_REPORTS.md` — duplicatas, relatorios de bbox e regras operacionais.
- `docs/tjpb-despacho-schema.md` — schema completo do JSON.
- `docs/REFERENCE_DATASETS.md` — datasets auxiliares (peritos, honorarios, etc.).

## Saida e schema

A saida (por PDF) segue o schema abaixo, gravado em `processes.json` no Postgres:

```json
{
  "pdf": {
    "fileName": "...",
    "filePath": "...",
    "pages": 2,
    "sha256": "..."
  },
  "run": {
    "startedAt": "...",
    "finishedAt": "...",
    "configVersion": "...",
    "toolVersions": { "fpdf": "..." }
  },
  "bookmarks": [ { "title": "...", "page1": 1, "page0": 0 } ],
  "candidates": [
    {
      "startPage1": 10,
      "endPage1": 11,
      "scoreDmp": 0.82,
      "scoreDiffPlex": 0.79,
      "anchorsHit": ["HEADER_TJPB", "DIRETORIA_ESPECIAL"],
      "density": { "p10": 0.31, "p11": 0.28 },
      "signals": { "hasRobson": true, "hasCRC": true }
    }
  ],
  "documents": [
    {
      "docType": "despacho",
      "startPage1": 10,
      "endPage1": 11,
      "matchScore": 0.82,
      "bands": [
        {
          "page1": 10,
          "band": "header",
          "text": "...",
          "hashSha256": "...",
          "bboxN": { "x0": 0.0, "y0": 0.85, "x1": 1.0, "y1": 1.0 }
        }
      ],
      "paragraphs": [
        {
          "page1": 10,
          "index": 0,
          "text": "...",
          "hashSha256": "...",
          "bboxN": { "x0": 0.08, "y0": 0.35, "x1": 0.92, "y1": 0.52 }
        }
      ],
      "fields": {
        "PROCESSO_ADMINISTRATIVO": {
          "value": "...",
          "confidence": 0.92,
          "method": "regex",
          "evidence": { "page1": 10, "bboxN": {"x0":0.1,"y0":0.4,"x1":0.6,"y1":0.45}, "snippet": "..." }
        }
      },
      "warnings": []
    }
  ],
  "errors": []
}
```

## Config (config.yaml)

- `thresholds`: limites de densidade, paginas, bandas, paragracao.
- `anchors`: textos-chave para detectar DESPACHO.
- `regex`: padroes de processo, CPF, dinheiro e data.
- `priorities`: labels mais provaveis para cada campo.
- `field_strategies`: carrega regras YAML por campo (diretorio/arquivos) para estrategia especifica.
- `reference`: caminhos de catalogos auxiliares (peritos, honorarios).

## Testes

Ha testes minimos para regex/normalizacao e matching. Para executar:

```bash
dotnet test tests/TjpbDespachoExtractor.Tests/TjpbDespachoExtractor.Tests.csproj
```

## Observacoes

- OCR nao e usado por padrao (apenas texto nativo).
- `raw_processes` e somente leitura; resultados ficam em `processes.json`.
- Campos ausentes retornam `"-"` com `method="not_found"`.
