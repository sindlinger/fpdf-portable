# TextStrategyStubs.cs (legado)

Stubs mínimos baseados em iTextSharp para manter compatibilidade com caminhos antigos enquanto a migração completa para iText7 não termina.

Inclui:
- `LayoutPreservingStrategy`, `ColumnDetectionStrategy`, `AdvancedLayoutStrategy`: concatenam texto simples.
- `AdvancedHeaderFooterStrategy`: construtor vazio, usado apenas em fallback.
- `CompleteFontAnalysisStrategy`: coleta tamanhos de fonte aproximados (valor fixo) para compatibilizar rotinas antigas.

Status:
- Mantido apenas para não quebrar comandos/flags legados; objetivo é removê-lo quando todas as estratégias forem reescritas em iText7.
