#!/bin/bash

# Script melhorado para extrair nota de empenho e converter para imagem/texto
# Uso: ./extract_and_convert.sh [pdf_number] [page_number]

if [ $# -lt 2 ]; then
    echo "Uso: $0 [número_pdf] [número_página]"
    echo "Exemplo: $0 717 34"
    exit 1
fi

pdf_num=$1
page_num=$2
output_dir="extracted_notas"

# Criar diretório de saída
mkdir -p "$output_dir"

echo "=== Extraindo página $page_num do PDF #$pdf_num ==="

# 1. Extrair página como base64
echo "1. Extraindo página como base64..."
fpdf $pdf_num base64 --extract-page $page_num -F raw > "$output_dir/page_${page_num}.b64" 2>/dev/null

# 2. Converter base64 para PDF
echo "2. Convertendo base64 para PDF..."
base64 -d "$output_dir/page_${page_num}.b64" > "$output_dir/page_${page_num}.pdf"

# 3. Converter PDF para imagem usando pdftoppm (mais confiável)
echo "3. Convertendo PDF para imagem..."
if command -v pdftoppm &> /dev/null; then
    # PNG de alta qualidade
    pdftoppm -png -r 300 -singlefile "$output_dir/page_${page_num}.pdf" "$output_dir/page_${page_num}"
    mv "$output_dir/page_${page_num}.png" "$output_dir/page_${page_num}_hq.png" 2>/dev/null
    
    # JPG de qualidade média
    pdftoppm -jpeg -r 200 -singlefile "$output_dir/page_${page_num}.pdf" "$output_dir/page_${page_num}"
    mv "$output_dir/page_${page_num}.jpg" "$output_dir/page_${page_num}_med.jpg" 2>/dev/null
    
    echo "   ✓ Imagens geradas com sucesso"
else
    echo "   ⚠ pdftoppm não instalado. Instale com: sudo apt-get install poppler-utils"
fi

# 4. Extrair texto usando OCR (se tesseract estiver instalado)
if command -v tesseract &> /dev/null; then
    echo "4. Extraindo texto com OCR..."
    if [ -f "$output_dir/page_${page_num}_hq.png" ]; then
        # OCR em português
        tesseract "$output_dir/page_${page_num}_hq.png" "$output_dir/page_${page_num}_ocr" -l por 2>/dev/null
        
        # Se não tiver português, tentar sem especificar idioma
        if [ ! -f "$output_dir/page_${page_num}_ocr.txt" ] || [ ! -s "$output_dir/page_${page_num}_ocr.txt" ]; then
            echo "   Tentando OCR sem idioma específico..."
            tesseract "$output_dir/page_${page_num}_hq.png" "$output_dir/page_${page_num}_ocr" 2>/dev/null
        fi
        
        if [ -f "$output_dir/page_${page_num}_ocr.txt" ] && [ -s "$output_dir/page_${page_num}_ocr.txt" ]; then
            echo "   ✓ Texto extraído por OCR"
        else
            echo "   ⚠ OCR não conseguiu extrair texto"
        fi
    else
        echo "   ⚠ Imagem não disponível para OCR"
    fi
else
    echo "4. Tesseract não instalado."
    echo "   Para OCR, instale com:"
    echo "   sudo apt-get install tesseract-ocr tesseract-ocr-por"
fi

# 5. Tentar extrair texto diretamente do PDF
echo "5. Extraindo texto direto do PDF..."
if command -v pdftotext &> /dev/null; then
    pdftotext "$output_dir/page_${page_num}.pdf" "$output_dir/page_${page_num}_text.txt" 2>/dev/null
    
    if [ -s "$output_dir/page_${page_num}_text.txt" ]; then
        echo "   ✓ Texto extraído do PDF"
    else
        echo "   ⚠ PDF não contém texto extraível (provavelmente é imagem)"
    fi
else
    echo "   ⚠ pdftotext não instalado"
fi

echo ""
echo "=== Arquivos gerados em $output_dir/ ==="
echo "  • page_${page_num}.b64     - Base64 da página"
echo "  • page_${page_num}.pdf     - PDF da página"

if [ -f "$output_dir/page_${page_num}_hq.png" ]; then
    echo "  • page_${page_num}_hq.png  - Imagem PNG (alta qualidade, 300 DPI)"
fi

if [ -f "$output_dir/page_${page_num}_med.jpg" ]; then
    echo "  • page_${page_num}_med.jpg - Imagem JPG (média qualidade, 200 DPI)"
fi

if [ -f "$output_dir/page_${page_num}_ocr.txt" ] && [ -s "$output_dir/page_${page_num}_ocr.txt" ]; then
    echo "  • page_${page_num}_ocr.txt - Texto extraído por OCR"
    echo ""
    echo "=== Prévia do texto OCR (primeiras 10 linhas) ==="
    head -10 "$output_dir/page_${page_num}_ocr.txt"
fi

if [ -f "$output_dir/page_${page_num}_text.txt" ] && [ -s "$output_dir/page_${page_num}_text.txt" ]; then
    echo "  • page_${page_num}_text.txt - Texto extraído do PDF"
    echo ""
    echo "=== Prévia do texto direto (primeiras 10 linhas) ==="
    head -10 "$output_dir/page_${page_num}_text.txt"
fi

echo ""
echo "=== Informações sobre a imagem ==="
if [ -f "$output_dir/page_${page_num}_hq.png" ]; then
    identify "$output_dir/page_${page_num}_hq.png" 2>/dev/null || file "$output_dir/page_${page_num}_hq.png"
fi