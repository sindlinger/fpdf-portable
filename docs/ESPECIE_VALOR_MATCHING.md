# Regra de Enquadramento por Valor e Especialidade

## Objetivo

Inferir **ESPECIE_DA_PERICIA** e **VALOR_TABELADO_ANEXO_I** usando:

- Valor arbitrado (juiz ou diretor)
- Especialidade do perito
- Tabela oficial de honorarios periciais (Anexo I)

## Logica atual

1) Extrair **PERITO**, **CPF_PERITO** e **ESPECIALIDADE** do despacho.
2) Se faltar CPF ou especialidade, buscar no **catalogo de peritos**.
3) Selecionar valor base para enquadramento:
   - Preferir `VALOR_ARBITRADO_DE` (Diretoria Especial).
   - `VALOR_ARBITRADO_JZ` so entra se `allow_valor_jz=true` no config.
4) Mapear **ESPECIALIDADE** para **AREA** da tabela de honorarios.
5) Dentro da AREA, escolher o valor **mais proximo** do valor arbitrado.
6) Aceitar o match se a diferenca percentual for <= `value_tolerance_pct`.

## O que nao fazemos agora

- Nao usamos os laudos existentes para identificar especie (esses arquivos serao refeitos).
- Nao inferimos especie apenas por valor (sem especialidade valida).

## Configuracao (config.yaml)

```
reference:
  peritos_catalog_paths:
    - "src/PipelineTjpb/reference/peritos/peritos_catalogo_relatorio.csv"
  honorarios:
    table_path: "src/PipelineTjpb/reference/valores/tabela_honorarios.csv"
    aliases_path: "src/PipelineTjpb/reference/valores/honorarios_aliases.json"
    value_tolerance_pct: 0.15
    prefer_valor_de: true
    allow_valor_jz: true
    area_map:
      - area: "CIENCIAS CONTABEIS"
        keywords: ["contab", "contador", "grafotec", "grafoscop"]
```
