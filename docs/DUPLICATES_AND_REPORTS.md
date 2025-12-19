# Duplicatas, relatorios e regras operacionais

## Contexto (Postgres)

- `raw_processes` e somente leitura; nao sobrescrevemos entradas existentes.
- `processes.json` guarda o resultado parseado (documento aninhado no processo).
- Hoje ha casos em que `raw_processes` > `processes`, tipicamente por:
  - Mesmo processo com PDFs diferentes (versoes).
  - Reprocessamento de um mesmo processo com novas paginas/boletins.
  - Colisao por numero de processo (upsert em `processes`).

## Risco atual

- Processos podem ser sobrescritos no `processes.json` quando o mesmo numero aparece mais de uma vez.
- Isso reduz a amostra util (ex.: 505 raw vs 447 processes).
- A deduplicacao so por `process_number` nao preserva historico.

## Estrategia recomendada (a implementar)

1) **Deteccao de duplicatas**
   - Duplicata forte: mesmo `process_number` com `pdf_sha256` diferente.
   - Duplicata fraca: mesmo `file_name` ou `file_size` com `pdf_sha256` igual.
   - Duplicata por origem: mesmo `source_path` com `process_number` diferente (indicador de erro).

2) **Persistencia sem perda**
   - Criar tabela `process_versions` (ou `process_duplicates`):
     - `process_number`, `pdf_sha256`, `file_name`, `source_path`, `created_at`
     - `raw_id` (FK de `raw_processes`) e/ou `process_id`
   - Manter `processes` como **ponteiro para a ultima versao** (campo `current_version_sha`).

3) **Relatorio de duplicatas**
   - Relatorio em CSV/XLSX para auditoria, com grupos por `process_number`.
   - Sinalizar casos em que `processes` perdeu versoes.

## Relatorio de bbox (auditoria)

Script: `utils/bbox_report.py`

- Gera **CSV e XLSX** com todas as evidencias (`bboxN`) dos campos extraidos.
- Inclui **outliers** por altura (Y) para identificar anomalias.
- Exemplo:

```bash
python utils/bbox_report.py --source-contains data/inbox --out-dir data/out-test
```

Saidas:
- `data/out-test/bbox_report_YYYYMMDD_HHMMSS.csv`
- `data/out-test/bbox_report_YYYYMMDD_HHMMSS.xlsx`

## Regras operacionais de extracao (resumo)

- **Identificacao do despacho**: bookmarks sao a fonte primaria; heuristicas e diff so como backup.
- **VALOR_ARBITRADO_JZ**: **somente** da **primeira pagina** (topo + inicio do corpo).
- **VALOR_ARBITRADO_DE**: **somente** da **segunda pagina** (bottom/rodape).
  - Sempre coletar **todos os paragrafos** do bottom da 2a pagina.
  - Se houver 3+ paginas, usar bottom da **ultima pagina** para reforco.
- **DATA**: sempre em **formato brasileiro** (`dd/MM/yyyy`), preferindo rodape.
  - Validar para nao confundir com data de nascimento (descartar se muito antiga).
- **Assinatura**: serve apenas como sinal de despacho (nao e campo).

