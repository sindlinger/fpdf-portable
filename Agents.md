## Guia rápido para o agente

- Todo processamento deve ser completo (sem versões “simplificadas”). Extraia sempre o máximo de campos e metadados disponíveis.
- Saída padrão: gravar no Postgres. Arquivos JSON só para exportação opcional (--output); não persistimos caches JSON em disco.
- Raw completo retornado pelo C# fica em `raw_processes` (Postgres); dados parseados ficam em `processes` e `documents` (relacionados por `process_id`).
- Documentos nunca se misturam entre processos: cada inserção de documentos faz `DELETE` prévio por `process_id`.
- Ao ampliar extrações (headers, footers, campos, forense), inclua tudo em `documents.meta` para manter rastreabilidade.

