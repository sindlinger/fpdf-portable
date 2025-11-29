#!/bin/bash

# Script limpo para extrair imagens sem mensagens desnecessárias

echo "🎯 Extração de Imagens - Modo Limpo"
echo "===================================="

# Configurações
OUTPUT_DIR="$HOME/DE_cache/extracted_images"
FPDF_CMD="./publish/fpdf"

# Criar diretório de saída
mkdir -p "$OUTPUT_DIR"

echo "📁 Diretório de saída: $OUTPUT_DIR"
echo ""

# Função para extrair imagens com filtro de saída
extract_images() {
    local indices="$1"
    local min_height="$2"
    
    echo "🔍 Extraindo imagens dos índices $indices com altura mínima $min_height..."
    
    # Executar comando e filtrar saídas desnecessárias
    $FPDF_CMD $indices images --min-height $min_height -F png --output-dir "$OUTPUT_DIR" 2>&1 | \
        grep -v "Provável imagem escaneada" | \
        grep -v "Páginas analisadas:" | \
        grep -v "Páginas com imagens escaneadas:" | \
        grep -v "Texto detectado:" | \
        grep -v "^\[INFO\]" | \
        grep -v "^$" | \
        head -20
    
    echo ""
}

# Teste 1: Extrair do índice 1
echo "📸 Teste 1: Extraindo imagens do índice 1"
extract_images "1" "500"

# Teste 2: Extrair dos índices 1-3
echo "📸 Teste 2: Extraindo imagens dos índices 1-3"
extract_images "1-3" "500"

# Verificar resultados
echo "📊 Resultados da Extração:"
echo "=========================="

if [ -d "$OUTPUT_DIR" ]; then
    file_count=$(find "$OUTPUT_DIR" -name "*.png" -o -name "*.jpg" 2>/dev/null | wc -l)
    
    if [ $file_count -gt 0 ]; then
        echo "✅ Sucesso! $file_count imagem(ns) extraída(s):"
        echo ""
        
        # Listar arquivos extraídos
        find "$OUTPUT_DIR" -name "*.png" -o -name "*.jpg" 2>/dev/null | while read file; do
            if [ -f "$file" ]; then
                size=$(stat -f%z "$file" 2>/dev/null || stat -c%s "$file" 2>/dev/null || echo "unknown")
                echo "  • $(basename "$file") - $size bytes"
            fi
        done
    else
        echo "⚠️  Nenhuma imagem foi extraída."
        echo ""
        echo "Possíveis razões:"
        echo "  • Os índices do cache não contêm imagens"
        echo "  • As imagens não atendem ao critério de altura mínima"
        echo "  • Problema na extração (verifique os logs)"
    fi
else
    echo "❌ Erro: Diretório de saída não foi criado"
fi

echo ""
echo "🏁 Extração concluída!"
echo "📁 Imagens salvas em: $OUTPUT_DIR"