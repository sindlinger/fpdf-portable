# PR Draft: Migrar cache para SQLite e habilitar FTS

## Motivação
- Unificar cache em uma única fonte (SQLite), aposentando `_cache.json` e `index.json`.
- Habilitar busca rápida com FTS5 e preparar terreno para GUI.

## O que foi feito
- Adicionado `System.Data.SQLite.Core` ao projeto.
- Novo helper `SqliteCacheStore` com auto-init de schema:
  - `caches`, `processes`, `documents`, `pages` (word_count gerado), `page_fts` (FTS5) + triggers de sync.
- `fpdf load` grava direto no SQLite (padrão `data/sqlite/sqlite-mcp.db`, opção `--db-path`), sem gerar `_cache.json`; skip por existência no DB.
- `CacheManager`/`CacheMemoryManager` passam a resolver caches como `db://<db>#<cache>`; listagem não usa mais `index.json`.
- `fpdf find` consulta somente SQLite (caches + pages).
- `fpdf ingest-db` importa `_cache.json` para SQLite via `UpsertCacheFromJson`.

## Como migrar
1) Rodar ingestão inicial dos caches existentes:
   ```bash
   fpdf ingest-db .cache --db-path data/sqlite/sqlite-mcp.db
   ```
2) Opcional: adicionar `.cache/` ao `.gitignore` e parar de usar/cachear JSON.
3) Usar normalmente:
   ```bash
   fpdf load <pdf|dir> --db-path data/sqlite/sqlite-mcp.db
   fpdf find <...> --db-path data/sqlite/sqlite-mcp.db
   ```

## Pendências / Próximos passos
- Build/teste local: `dotnet build` não rodou aqui (runtime ausente no ambiente de CI local). Validar em ambiente com dotnet instalado.
- Ajustar comandos restantes (pages/docs/words/etc.) para ler direto do SQLite (evitar JSON fallback).
- FTS na consulta do `find` (usar MATCH e highlight), views amigáveis para GUI.
- (Opcional) Estruturas adicionais: bbox/objetos (JSON1 ou R-Tree) e índices parciais/cobridores.
- GUI: iniciar branch/protótipo (Tauri/React ou Avalonia) consumindo SQLite.

## Notas de compatibilidade
- `db://<db>#<cache>` é o identificador de cache; `_cache.json` deixa de ser escrito pelo `load`.
- Schema é criado automaticamente se o DB não existir.
