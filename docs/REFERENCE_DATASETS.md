# Referencias do Pipeline TJPB

Este documento lista as bases de referencia usadas pelo pipeline atual do TJPB.

## Pastas de referencia (fpdf-portable)

As copias ficam em:

- `src/PipelineTjpb/reference/valores/`
  - `tabela_honorarios.csv`
  - `honorarios_aliases.json`
- `src/PipelineTjpb/reference/peritos/`
  - `peritos_catalogo_relatorio.csv`
  - `peritos_catalogo_final.csv`
  - `peritos_catalogo_parquet.csv`
  - `peritos_catalogo.csv`
- `src/PipelineTjpb/reference/laudos/`
  - `laudos_por_especie*.csv/.tsv/.jsonl/.xlsx`
  - pastas com laudos por especie
- `src/PipelineTjpb/reference/laudos_hashes/`
  - `laudos_hashes.csv`
  - `laudos_hashes_unique.csv`

## Uso atual (estado atual)

- `tabela_honorarios.csv`: usada para inferir **ESPECIE_DA_PERICIA** e **VALOR_TABELADO_ANEXO_I**, cruzando valor arbitrado e especialidade do perito.
- `honorarios_aliases.json`: usado como alias de palavras-chave para apontar diretamente um ID da tabela.
- `peritos_catalogo_*.csv`: usado para completar **CPF_PERITO** e **ESPECIALIDADE** quando o despacho falhar (catalogo auxiliar).
- `laudos*`: **nao usados** no momento (sera refeito com novos laudos).

## Observacoes

- A base de peritos pode conter entradas ruidosas. O loader filtra nomes com email e linhas de texto que nao parecam nomes reais.
- A tabela de honorarios so entra quando existe valor arbitrado e especialidade mapeada para uma area da tabela.

