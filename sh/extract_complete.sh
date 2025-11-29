#!/bin/bash

# Script completo para buscar, extrair e converter notas de empenho
# Uso: ./extract_complete.sh

echo "=== Sistema de Extração de Notas de Empenho ==="
echo ""

# 1. Buscar todos os PDFs com páginas 744x1052
echo "1. Buscando PDFs com páginas no padrão de nota de empenho (744x1052)..."
echo ""

output_dir="notas_empenho_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$output_dir"

# Função para processar uma página
process_page() {
    local pdf_num=$1
    local page_num=$2
    local pdf_name=$3
    
    echo "  Processando: PDF #$pdf_num, Página $page_num..."
    
    # Criar subdiretório
    page_dir="$output_dir/pdf_${pdf_num}_page_${page_num}"
    mkdir -p "$page_dir"
    
    # Extrair como base64
    fpdf $pdf_num base64 --extract-page $page_num -F raw > "$page_dir/page.b64" 2>/dev/null
    
    # Converter para PDF
    base64 -d "$page_dir/page.b64" > "$page_dir/page.pdf"
    
    # Converter para imagem
    pdftoppm -png -r 200 -singlefile "$page_dir/page.pdf" "$page_dir/page" 2>/dev/null
    
    # OCR
    if command -v tesseract &> /dev/null; then
        tesseract "$page_dir/page.png" "$page_dir/text_ocr" -l por 2>/dev/null || \
        tesseract "$page_dir/page.png" "$page_dir/text_ocr" 2>/dev/null
    fi
    
    # Extrair texto direto
    pdftotext "$page_dir/page.pdf" "$page_dir/text_direct.txt" 2>/dev/null
    
    # Criar arquivo de informações
    echo "PDF: $pdf_name" > "$page_dir/info.txt"
    echo "PDF ID: $pdf_num" >> "$page_dir/info.txt"
    echo "Página: $page_num" >> "$page_dir/info.txt"
    echo "Data de extração: $(date)" >> "$page_dir/info.txt"
    echo "" >> "$page_dir/info.txt"
    
    # Tentar extrair informações chave do OCR
    if [ -f "$page_dir/text_ocr.txt" ]; then
        echo "=== Informações Extraídas ===" >> "$page_dir/info.txt"
        
        # Buscar CNPJ
        grep -E "[0-9]{2}[.,][0-9]{3}[.,][0-9]{3}/[0-9]{4}-[0-9]{2}" "$page_dir/text_ocr.txt" >> "$page_dir/info.txt" 2>/dev/null
        
        # Buscar valores
        grep -E "R\$|[0-9]+[.,][0-9]{2}" "$page_dir/text_ocr.txt" | head -5 >> "$page_dir/info.txt" 2>/dev/null
        
        # Buscar processo
        grep -i "processo" "$page_dir/text_ocr.txt" | head -2 >> "$page_dir/info.txt" 2>/dev/null
    fi
    
    echo "    ✓ Concluído: $page_dir/"
}

# Processar PDF 717 que sabemos que tem notas
echo "Processando PDF #717 (confirmado com notas de empenho)..."
process_page 717 33 "2023083124.pdf"
process_page 717 34 "2023083124.pdf"

echo ""
echo "Buscando outros PDFs com o mesmo padrão..."
found=0

# Buscar em uma amostra de PDFs
for i in {1..100}; do
    # Pular o 717 que já processamos
    if [ "$i" -eq 717 ]; then
        continue
    fi
    
    # Verificar se tem páginas com as dimensões corretas
    result=$(fpdf $i pages --width 744 --height 1052 -F json 2>/dev/null | grep -o '"pageNumber":[0-9]*' | cut -d: -f2)
    
    if [ ! -z "$result" ]; then
        filename=$(fpdf cache list 2>/dev/null | grep "^$i " | awk '{print $3}')
        echo "  Encontrado: PDF #$i ($filename)"
        
        # Processar cada página encontrada
        for page in $result; do
            process_page $i $page "$filename"
            ((found++))
        done
    fi
done

echo ""
echo "=== Resumo da Extração ==="
echo "Diretório de saída: $output_dir/"
echo "PDFs processados: PDF #717 + $found página(s) adicional(is)"
echo ""
echo "Para visualizar os resultados:"
echo "  ls -la $output_dir/"
echo ""
echo "Para ver uma imagem extraída:"
echo "  display $output_dir/pdf_717_page_34/page.png"
echo ""
echo "Para ver o texto extraído:"
echo "  cat $output_dir/pdf_717_page_34/text_ocr.txt"