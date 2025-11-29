#!/bin/bash

# Script para extrair nota de empenho e converter para imagem/texto
# Uso: ./extract_nota_empenho.sh [pdf_number] [page_number]

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

# 3. Converter PDF para imagem PNG (alta qualidade)
echo "3. Convertendo PDF para imagem PNG..."
convert -density 300 -quality 100 "$output_dir/page_${page_num}.pdf" "$output_dir/page_${page_num}.png" 2>/dev/null

# 4. Converter PDF para imagem JPG (menor tamanho)
echo "4. Convertendo PDF para imagem JPG..."
convert -density 200 -quality 90 "$output_dir/page_${page_num}.pdf" "$output_dir/page_${page_num}.jpg" 2>/dev/null

# 5. Extrair texto usando OCR (se tesseract estiver instalado)
if command -v tesseract &> /dev/null; then
    echo "5. Extraindo texto com OCR..."
    tesseract "$output_dir/page_${page_num}.png" "$output_dir/page_${page_num}_ocr" -l por 2>/dev/null
    echo "   Texto extraído salvo em: $output_dir/page_${page_num}_ocr.txt"
else
    echo "5. Tesseract não instalado. Para OCR, instale com: sudo apt-get install tesseract-ocr tesseract-ocr-por"
fi

# 6. Tentar extrair texto diretamente do PDF
echo "6. Extraindo texto direto do PDF..."
pdftotext "$output_dir/page_${page_num}.pdf" "$output_dir/page_${page_num}_text.txt" 2>/dev/null

echo ""
echo "=== Arquivos gerados em $output_dir/ ==="
echo "  • page_${page_num}.b64    - Base64 da página"
echo "  • page_${page_num}.pdf    - PDF da página"
echo "  • page_${page_num}.png    - Imagem PNG (alta qualidade)"
echo "  • page_${page_num}.jpg    - Imagem JPG (comprimida)"

if [ -f "$output_dir/page_${page_num}_ocr.txt" ]; then
    echo "  • page_${page_num}_ocr.txt - Texto extraído por OCR"
fi

if [ -f "$output_dir/page_${page_num}_text.txt" ]; then
    echo "  • page_${page_num}_text.txt - Texto extraído do PDF"
    echo ""
    echo "=== Prévia do texto extraído ==="
    head -20 "$output_dir/page_${page_num}_text.txt"
fi