# Document Segmentation Presets

Este diretório contém presets de configuração para identificar limites entre documentos em PDFs multi-documento.

## Estratégias Disponíveis

### 1. **text_patterns** - Padrões Textuais
Busca por palavras/frases específicas no início e fim das páginas.

### 2. **font_analysis** - Análise de Fontes
Detecta mudanças significativas no conjunto de fontes usadas.

### 3. **page_numbering** - Numeração de Páginas
Identifica reinício de numeração (ex: volta para página 1).

### 4. **blank_pages** - Páginas em Branco
Usa páginas vazias ou quase vazias como separadores.

### 5. **layout_changes** - Mudanças de Layout
Detecta alterações drásticas na estrutura da página.

### 6. **header_footer** - Cabeçalhos e Rodapés
Analisa mudanças em elementos repetitivos.

### 7. **metadata_hints** - Dicas de Metadados
Usa bookmarks, annotations ou estrutura do PDF.

### 8. **content_flow** - Fluxo de Conteúdo
Verifica continuidade textual entre páginas.

## Configurações Importantes

- **Estratégias Conflitantes**: 
  - `blank_pages` + `content_flow` (páginas em branco quebram fluxo)
  - `page_numbering` + `header_footer` (podem dar sinais contraditórios)

- **Recomendações**:
  - Use no máximo 3-4 estratégias simultaneamente
  - Priorize estratégias que se complementam
  - Ajuste os pesos conforme seu tipo de documento

## Como Usar

1. Escolha um preset existente ou crie o seu
2. Ative/desative estratégias com `"enabled": true/false`
3. Configure os padrões de início/fim se usar `text_patterns`
4. Ajuste thresholds e pesos conforme necessário