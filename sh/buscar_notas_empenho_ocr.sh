#!/bin/bash

# Script para buscar páginas 744x1052 em múltiplos PDFs e extrair texto com OCR
# Usa apenas comandos internos do FilterPDF

echo "=== Busca de Notas de Empenho com OCR Integrado ==="
echo ""

# Criar diretório de destino
output_dir="$HOME/DE_cache"
mkdir -p "$output_dir"

echo "📁 Diretório de saída: $output_dir"
echo ""

# Função para processar um PDF
process_pdf() {
    local pdf_id=$1
    
    echo "🔍 Verificando PDF #$pdf_id..."
    
    # Buscar páginas com dimensões 744x1052 usando comando interno
    local pages_result=$(fpdf $pdf_id pages --width 744 --height 1052 -F json 2>/dev/null)
    
    if [ $? -eq 0 ] && [ ! -z "$pages_result" ]; then
        # Verificar se encontrou páginas
        local page_count=$(echo "$pages_result" | grep -c '"pageNumber"')
        
        if [ $page_count -gt 0 ]; then
            # Obter nome do arquivo
            local filename=$(fpdf cache list 2>/dev/null | grep "^$pdf_id " | awk '{print $3}')
            
            echo "   ✅ Encontrado: $page_count página(s) em $filename"
            
            # Extrair números das páginas
            local page_numbers=$(echo "$pages_result" | grep -o '"pageNumber":[0-9]*' | cut -d: -f2)
            
            for page in $page_numbers; do
                echo "   📄 Página $page - Aplicando OCR..."
                
                # Criar nome do arquivo de saída
                local safe_filename=$(echo "$filename" | sed 's/[^a-zA-Z0-9._-]/_/g')
                local output_file="$output_dir/pdf_${pdf_id}_page_${page}_${safe_filename%.pdf}_ocr.txt"
                
                # Usar comando base64 com OCR integrado
                fpdf $pdf_id base64 --extract-page $page -F ocr > "$output_file" 2>/dev/null
                
                if [ $? -eq 0 ] && [ -s "$output_file" ]; then
                    echo "   ✅ OCR extraído: $(basename "$output_file")"
                    
                    # Mostrar informações extraídas
                    echo "   📊 Informações encontradas:"
                    
                    # Buscar CNPJ
                    local cnpj=$(grep -E "[0-9]{2}[.,][0-9]{3}[.,][0-9]{3}/[0-9]{4}-[0-9]{2}" "$output_file" | head -1)
                    if [ ! -z "$cnpj" ]; then
                        echo "      CNPJ: $cnpj"
                    fi
                    
                    # Buscar valores
                    local valor=$(grep -E "R\$|[0-9]+[.,][0-9]{2}" "$output_file" | grep -E "[0-9]+[.,][0-9]{2}" | head -1)
                    if [ ! -z "$valor" ]; then
                        echo "      Valor: $valor"
                    fi
                    
                    # Buscar nomes (linhas com letras maiúsculas)
                    local responsavel=$(grep -E "^[A-Z][a-z]+ [A-Z][a-z]+ [A-Z][a-z]+" "$output_file" | head -1)
                    if [ ! -z "$responsavel" ]; then
                        echo "      Responsável: $responsavel"
                    fi
                    
                    echo ""
                    return 0
                else
                    echo "   ❌ Falha no OCR"
                    rm -f "$output_file"
                fi
            done
        fi
    fi
    
    return 1
}

# Verificar quantos PDFs estão no cache
echo "📋 Verificando PDFs no cache..."
total_pdfs=$(fpdf cache list 2>/dev/null | wc -l)
echo "   Total de PDFs no cache: $total_pdfs"
echo ""

if [ $total_pdfs -eq 0 ]; then
    echo "❌ Nenhum PDF encontrado no cache!"
    echo "💡 Use 'fpdf load' para carregar PDFs primeiro"
    exit 1
fi

# Processar até 10 PDFs ou encontrar 10 páginas
processed_count=0
found_pages=0
max_pages=10

echo "🚀 Iniciando busca por notas de empenho..."
echo ""

for ((i=1; i<=total_pdfs && found_pages<max_pages; i++)); do
    if process_pdf $i; then
        ((processed_count++))
        ((found_pages++))
    fi
    
    # Mostrar progresso a cada 10 PDFs
    if [ $((i % 10)) -eq 0 ]; then
        echo "📊 Progresso: $i/$total_pdfs PDFs verificados, $found_pages página(s) encontrada(s)"
        echo ""
    fi
done

echo ""
echo "=== Resumo Final ==="
echo "📊 PDFs verificados: $i"
echo "📄 Páginas com OCR extraído: $found_pages"
echo "📁 Arquivos salvos em: $output_dir"
echo ""

if [ $found_pages -gt 0 ]; then
    echo "📋 Arquivos gerados:"
    ls -la "$output_dir"/*.txt 2>/dev/null | tail -10
    echo ""
    
    echo "🔍 Para ver um exemplo completo:"
    first_file=$(ls -t "$output_dir"/*.txt 2>/dev/null | head -1)
    if [ ! -z "$first_file" ]; then
        echo "   cat \"$first_file\""
        echo ""
        echo "📄 Prévia do último arquivo processado:"
        echo "======================================="
        head -15 "$first_file" | grep -v "^\s*$"
        echo "======================================="
    fi
    
    echo ""
    echo "🎯 Busca concluída com sucesso!"
    echo "💡 Todos os arquivos estão em: $output_dir/"
else
    echo "❌ Nenhuma página com dimensões 744x1052 foi encontrada."
    echo "💡 Verifique se os PDFs contêm notas de empenho"
fi