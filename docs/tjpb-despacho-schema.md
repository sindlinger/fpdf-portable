# TJPB Despacho JSON Schema (Resumo)

Este documento descreve o schema esperado para a saida de cada PDF processado.

## Estrutura

- `pdf`: metadados do arquivo.
- `run`: metadados da execucao.
- `bookmarks`: lista de bookmarks (outline).
- `candidates`: janelas candidatas e scores.
- `documents`: lista de documentos do tipo `despacho`.
- `errors`: erros globais do PDF.

## Campos obrigatorios em `documents[].fields`

Sempre presentes (valor `"-"` se nao encontrado):

- PROCESSO_ADMINISTRATIVO
- PROCESSO_JUDICIAL
- VARA
- COMARCA
- PROMOVENTE
- PROMOVIDO
- PERITO
- CPF_PERITO
- ESPECIALIDADE
- ESPECIE_DA_PERICIA
- VALOR_ARBITRADO_JZ
- VALOR_ARBITRADO_DE
- VALOR_ARBITRADO_CM
- VALOR_TABELADO_ANEXO_I
- ADIANTAMENTO
- PERCENTUAL
- PARCELA
- DATA
- ASSINANTE
- NUM_PERITO (opcional, mas sempre presente com "-")

Cada campo inclui:

- `value`: string normalizada ou "-".
- `confidence`: 0..1.
- `method`: template_dmp | diffplex | regex | heuristic | filename_fallback | not_found.
- `evidence`: page1, bboxN e snippet (quando disponivel).

## Observacoes

- page1 eh 1-based.
- bboxN sempre normalizado 0..1.
- hashes SHA-256 sao gerados por banda e paragrafo (texto normalizado).
