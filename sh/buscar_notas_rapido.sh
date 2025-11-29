#!/bin/bash

# Script otimizado para buscar notas de empenho - foca em PDFs específicos
# Usa apenas comandos internos do FilterPDF

echo "=== Busca Rápida de Notas de Empenho com OCR ==="
echo ""

# Criar diretório de destino
output_dir="$HOME/DE_cache"
mkdir -p "$output_dir"

echo "📁 Diretório de saída: $output_dir"
echo ""

# Lista de PDFs prioritários (incluindo o 717 que sabemos que tem)
priority_pdfs=(717 716 718 719 720 715 714 713 712 711 710 709 708 707 706)

# Função para processar um PDF rapidamente
process_pdf_fast() {
    local pdf_id=$1
    
    echo "🔍 PDF #$pdf_id..."
    
    # Buscar páginas com dimensões 744x1052
    local pages_result=$(fpdf $pdf_id pages --width 744 --height 1052 -F json 2>/dev/null)
    
    if [ $? -eq 0 ] && echo "$pages_result" | grep -q '"pageNumber"'; then
        # Obter nome do arquivo
        local filename=$(fpdf cache list 2>/dev/null | grep "^$pdf_id " | awk '{print $3}')
        
        echo "   ✅ ENCONTRADO em $filename"
        
        # Extrair número da primeira página encontrada
        local page=$(echo "$pages_result" | grep -o '"pageNumber":[0-9]*' | head -1 | cut -d: -f2)
        
        echo "   📄 Extraindo página $page com OCR..."
        
        # Criar nome do arquivo de saída
        local safe_filename=$(echo "$filename" | sed 's/[^a-zA-Z0-9._-]/_/g')
        local output_file="$output_dir/pdf_${pdf_id}_page_${page}_${safe_filename%.pdf}.txt"
        
        # Aplicar OCR
        fpdf $pdf_id base64 --extract-page $page -F ocr > "$output_file" 2>/dev/null
        
        if [ $? -eq 0 ] && [ -s "$output_file" ]; then
            echo "   ✅ Texto extraído!"
            
            # Extrair informações chave
            echo "   📊 Dados encontrados:"
            
            # CNPJ
            local cnpj=$(grep -E "[0-9]{2}[.,][0-9]{3}[.,][0-9]{3}/[0-9]{4}-[0-9]{2}" "$output_file" | head -1)
            [ ! -z "$cnpj" ] && echo "      🏢 CNPJ: $cnpj"
            
            # Valores monetários
            local valores=$(grep -E "R\$|[0-9]+[.,][0-9]{2}" "$output_file" | grep -E "[0-9]{1,3}[.,][0-9]{2}" | head -2)
            if [ ! -z "$valores" ]; then
                echo "      💰 Valores:"
                echo "$valores" | sed 's/^/         /'
            fi
            
            # Nomes de responsáveis
            local nomes=$(grep -E "^[A-Z][a-z]+ [A-Z][a-z]+ [A-Z][a-z]+" "$output_file" | head -2)
            if [ ! -z "$nomes" ]; then
                echo "      👤 Responsáveis:"
                echo "$nomes" | sed 's/^/         /'
            fi
            
            # Órgão
            local inss=$(grep -i "INSS\|INSTITUTO\|SEGURO" "$output_file" | head -1)
            [ ! -z "$inss" ] && echo "      🏛️  Órgão: $inss"
            
            echo ""
            return 0
        else
            echo "   ❌ Falha no OCR"
            rm -f "$output_file"
        fi
    fi
    
    return 1
}

echo "🎯 Verificando PDFs prioritários (incluindo #717)..."
echo ""

found_count=0
total_files=0

# Processar PDFs prioritários primeiro
for pdf_id in "${priority_pdfs[@]}"; do
    if [ $found_count -ge 10 ]; then
        break
    fi
    
    if process_pdf_fast $pdf_id; then
        ((found_count++))
    fi
    ((total_files++))
done

# Se ainda não encontrou 10, buscar em outros PDFs próximos
if [ $found_count -lt 10 ]; then
    echo "🔍 Buscando em PDFs adicionais..."
    echo ""
    
    for ((i=700; i<=750 && found_count<10; i++)); do
        # Pular se já foi verificado
        if [[ " ${priority_pdfs[@]} " =~ " ${i} " ]]; then
            continue
        fi
        
        if process_pdf_fast $i; then
            ((found_count++))
        fi
        ((total_files++))
    done
fi

echo ""
echo "=== Resumo Final ==="
echo "📊 PDFs verificados: $total_files"
echo "📄 Notas de empenho encontradas: $found_count"
echo "📁 Arquivos salvos em: $output_dir"
echo ""

if [ $found_count -gt 0 ]; then
    echo "📋 Arquivos gerados:"
    ls -la "$output_dir"/*.txt 2>/dev/null | tail -5
    echo ""
    
    echo "🔍 Exemplo de conteúdo extraído:"
    latest_file=$(ls -t "$output_dir"/*.txt 2>/dev/null | head -1)
    if [ ! -z "$latest_file" ]; then
        echo "   Arquivo: $(basename "$latest_file")"
        echo "   ────────────────────────────────────"
        head -10 "$latest_file" | grep -v "^\s*$" | head -8
        echo "   ────────────────────────────────────"
    fi
    
    echo ""
    echo "🎯 Busca concluída!"
    echo "💡 Para ver todos os arquivos: ls -la $output_dir/"
else
    echo "❌ Nenhuma nota de empenho encontrada nos PDFs verificados."
    echo "💡 Tente expandir a busca ou verificar outros intervalos de PDFs"
fi