## Guia rápido para o agente

- Todo processamento deve ser completo (sem versões “simplificadas”). Extraia sempre o máximo de campos e metadados disponíveis.
- Saída padrão: gravar no Postgres. Não geramos arquivos; JSON só para export eventual. Não usamos mais cache em disco.
- Raw completo retornado pelo C# fica em `raw_processes` (Postgres), em modo somente leitura (não sobrescrevemos entradas existentes); dados parseados ficam em `processes.json` (documentos aninhados no próprio processo). Tabela `documents` não é usada.
- Documentos pertencem somente ao processo em que foram inseridos (aninhados); não há mistura entre processos.
- Ao ampliar extrações (headers, footers, campos, forense), inclua tudo no JSON do processo para manter rastreabilidade.
