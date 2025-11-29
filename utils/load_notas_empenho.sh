#!/bin/bash

# Script para carregar seletivamente apenas páginas que são potenciais notas de empenho
# Estratégia: Detectar páginas escaneadas (imagens) que provavelmente são notas de empenho

set -e

# Cores
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${GREEN}╔════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║   Detector de Notas de Empenho - fpdf      ║${NC}"
echo -e "${GREEN}╚════════════════════════════════════════════╝${NC}"
echo

# Verificar argumentos
if [ $# -eq 0 ]; then
    echo -e "${RED}Uso: $0 <arquivo.pdf ou diretório>${NC}"
    echo "Exemplos:"
    echo "  $0 processo.pdf"
    echo "  $0 /caminho/para/pdfs/"
    exit 1
fi

INPUT="$1"
CACHE_DIR=".cache"
TEMP_DIR="/tmp/fpdf_empenho_$$"
mkdir -p "$TEMP_DIR"

# Função para analisar um PDF
analyze_pdf() {
    local pdf_file="$1"
    local pdf_name=$(basename "$pdf_file")
    
    echo -e "${YELLOW}Analisando: $pdf_name${NC}"
    
    # Primeiro, fazer um load com imagens para analisar
    echo "  1. Carregando PDF com detecção de imagens..."
    fpdf "$pdf_file" load custom --include-image-data --no-javascript --no-embedded-files --no-multimedia 2>/dev/null || true
    
    # Encontrar o arquivo de cache gerado
    local cache_file=$(find "$CACHE_DIR" -name "*_cache.json" -mmin -1 | grep -i "$(basename "$pdf_file" .pdf)" | head -1)
    
    if [ -z "$cache_file" ]; then
        echo -e "  ${RED}❌ Não foi possível criar cache${NC}"
        return
    fi
    
    echo "  2. Analisando páginas para detectar notas de empenho..."
    
    # Extrair informações das páginas usando jq ou python
    python3 << EOF
import json
import re

with open("$cache_file", 'r', encoding='utf-8') as f:
    data = json.load(f)

empenho_pages = []
total_pages = len(data.get('Pages', []))

for page in data.get('Pages', []):
    page_num = page.get('PageNumber', 0)
    
    # Critérios para detectar nota de empenho:
    # 1. Página tem imagem(s)
    # 2. Pouco ou nenhum texto extraível (< 100 caracteres)
    # 3. Página inteira é uma imagem (scanned page)
    
    text_length = len(page.get('TextInfo', {}).get('PageText', ''))
    image_count = len(page.get('Resources', {}).get('Images', []))
    
    # Verificar se há indicadores de página escaneada
    is_scanned = False
    for img in page.get('Resources', {}).get('Images', []):
        if img.get('IsScannedPage', False):
            is_scanned = True
            break
    
    # Verificar se o pouco texto que existe contém palavras-chave
    text = page.get('TextInfo', {}).get('PageText', '').lower()
    has_keywords = any(word in text for word in ['empenho', 'ne', 'dotação', 'orçamentária', 'credor'])
    
    # Página é candidata se:
    # - É escaneada OU
    # - Tem imagem e pouco texto OU
    # - Tem palavras-chave relacionadas
    if is_scanned or (image_count > 0 and text_length < 100) or has_keywords:
        empenho_pages.append(page_num)
        print(f"  ✓ Página {page_num}: Possível nota de empenho (scanned={is_scanned}, images={image_count}, text_len={text_length})")

if empenho_pages:
    print(f"\n  📊 Encontradas {len(empenho_pages)} páginas suspeitas de {total_pages} total")
    print(f"  📄 Páginas: {empenho_pages}")
    
    # Salvar lista de páginas
    with open("$TEMP_DIR/$(basename "$pdf_file" .pdf)_empenho_pages.txt", 'w') as f:
        f.write(','.join(map(str, empenho_pages)))
else:
    print(f"  ❌ Nenhuma página suspeita de nota de empenho encontrada")
EOF
    
    echo
}

# Processar entrada
if [ -f "$INPUT" ]; then
    # É um arquivo
    analyze_pdf "$INPUT"
elif [ -d "$INPUT" ]; then
    # É um diretório
    echo -e "${YELLOW}Processando diretório: $INPUT${NC}"
    for pdf in "$INPUT"/*.pdf; do
        if [ -f "$pdf" ]; then
            analyze_pdf "$pdf"
        fi
    done
else
    echo -e "${RED}Erro: $INPUT não é um arquivo nem diretório válido${NC}"
    exit 1
fi

# Resumo final
echo -e "${GREEN}════════════════════════════════════════${NC}"
echo -e "${GREEN}Análise Concluída!${NC}"

if [ -d "$TEMP_DIR" ] && [ "$(ls -A $TEMP_DIR)" ]; then
    echo -e "${YELLOW}Arquivos com possíveis notas de empenho:${NC}"
    for result in "$TEMP_DIR"/*_empenho_pages.txt; do
        if [ -f "$result" ]; then
            pdf_name=$(basename "$result" _empenho_pages.txt)
            pages=$(cat "$result")
            echo "  • $pdf_name: páginas $pages"
        fi
    done
    
    echo
    echo -e "${YELLOW}💡 Dicas para extrair as notas:${NC}"
    echo "  1. Use: fpdf <cache_index> images"
    echo "  2. Use: fpdf <cache_index> ocr  (para extrair texto)"
    echo "  3. Use: fpdf <cache_index> pages -F json  (para ver detalhes)"
fi

# Limpar temporários
rm -rf "$TEMP_DIR"