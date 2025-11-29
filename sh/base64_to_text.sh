#!/bin/bash

# Script para extrair texto diretamente de base64
# Uso: ./base64_to_text.sh arquivo.b64

if [ $# -lt 1 ]; then
    echo "Uso: $0 arquivo.b64"
    echo "Exemplo: $0 nota_empenho.b64"
    exit 1
fi

input_b64=$1
basename="${input_b64%.b64}"

echo "=== Extraindo texto de Base64 ==="
echo ""

# Método 1: Se o PDF tem texto embutido (não é imagem)
echo "1. Tentando extrair texto direto do PDF..."
base64 -d "$input_b64" | pdftotext - "${basename}_texto_direto.txt" 2>/dev/null

if [ -s "${basename}_texto_direto.txt" ]; then
    echo "   ✓ Texto extraído com sucesso!"
    echo "   Arquivo: ${basename}_texto_direto.txt"
    echo ""
    echo "=== Prévia do texto ==="
    head -20 "${basename}_texto_direto.txt"
else
    echo "   ⚠ PDF não contém texto (é uma imagem)"
    rm -f "${basename}_texto_direto.txt"
fi

echo ""

# Método 2: OCR (se o PDF é uma imagem)
echo "2. Aplicando OCR para extrair texto da imagem..."

# Converter base64 para imagem temporária
base64 -d "$input_b64" | pdftoppm -png -r 300 -singlefile - temp_image 2>/dev/null

if [ -f "temp_image.png" ]; then
    # Aplicar OCR
    tesseract temp_image.png "${basename}_texto_ocr" -l por 2>/dev/null || \
    tesseract temp_image.png "${basename}_texto_ocr" 2>/dev/null
    
    if [ -s "${basename}_texto_ocr.txt" ]; then
        echo "   ✓ Texto extraído por OCR!"
        echo "   Arquivo: ${basename}_texto_ocr.txt"
        echo ""
        echo "=== Prévia do texto OCR ==="
        head -20 "${basename}_texto_ocr.txt"
    else
        echo "   ⚠ OCR não conseguiu extrair texto"
    fi
    
    # Limpar arquivo temporário
    rm -f temp_image.png
else
    echo "   ⚠ Não foi possível converter para imagem"
fi

echo ""
echo "=== Resumo ===" 
echo "Base64 pode ser transformado em texto usando:"
echo "1. pdftotext - para PDFs com texto embutido"
echo "2. OCR (Tesseract) - para PDFs escaneados/imagem"
echo ""
echo "Pipeline completo: Base64 → PDF → Imagem → OCR → Texto"