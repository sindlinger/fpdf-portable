#!/bin/bash

# Script para extrair 10 páginas de notas de empenho (744x1052) com OCR
echo "=== Extração de 10 Notas de Empenho com OCR ==="
echo ""

# Criar diretório
mkdir -p ~/DE_cache

# Lista de PDFs para testar (começando com o 717 que sabemos que funciona)
pdfs_to_test=(717 716 718 719 720 715 714 713 712 711)

extracted_count=0
target_count=10

echo "🔍 Buscando páginas 744x1052 em PDFs específicos..."
echo ""

for pdf_id in "${pdfs_to_test[@]}"; do
    if [ $extracted_count -ge $target_count ]; then
        break
    fi
    
    echo "📋 Verificando PDF #$pdf_id..."
    
    # Buscar páginas 744x1052 neste PDF (formato JSON para parsing)
    pages_json=$(fpdf $pdf_id pages --width 744 --height 1052 -F json 2>/dev/null)
    
    if [ $? -eq 0 ] && [ ! -z "$pages_json" ]; then
        # Extrair números das páginas do JSON
        page_numbers=$(echo "$pages_json" | grep -o '"pageNumber":[0-9]*' | cut -d: -f2)
        
        if [ ! -z "$page_numbers" ]; then
            echo "   ✅ Encontradas páginas: $page_numbers"
            
            # Processar cada página encontrada
            for page in $page_numbers; do
                if [ $extracted_count -ge $target_count ]; then
                    break
                fi
                
                echo "   📄 Extraindo página $page com OCR..."
                
                # Extrair com OCR
                output_file="~/DE_cache/pdf_${pdf_id}_page_${page}_nota_empenho.txt"
                fpdf $pdf_id base64 --extract-page $page -F ocr > "$output_file" 2>/dev/null
                
                if [ $? -eq 0 ] && [ -s "$output_file" ]; then
                    echo "   ✅ Salvo: pdf_${pdf_id}_page_${page}_nota_empenho.txt"
                    
                    # Extrair informações chave rapidamente
                    cnpj=$(grep -E "[0-9]{2}[.,][0-9]{3}[.,][0-9]{3}/[0-9]{4}-[0-9]{2}" "$output_file" | head -1)
                    valor=$(grep -E "[0-9]+[.,][0-9]{2}" "$output_file" | grep -E "R\$|[0-9]{2,3}[.,][0-9]{2}" | head -1)
                    
                    if [ ! -z "$cnpj" ]; then
                        echo "      🏢 CNPJ: $cnpj"
                    fi
                    if [ ! -z "$valor" ]; then
                        echo "      💰 Valor: $valor"
                    fi
                    
                    ((extracted_count++))
                    echo "      📊 Progresso: $extracted_count/$target_count"
                    echo ""
                else
                    echo "   ❌ Falha no OCR"
                    rm -f "$output_file"
                fi
            done
        else
            echo "   ⚪ Nenhuma página 744x1052 encontrada"
        fi
    else
        echo "   ❌ Erro ao verificar PDF #$pdf_id"
    fi
done

echo ""
echo "=== Resumo Final ==="
echo "📊 Páginas extraídas com OCR: $extracted_count"
echo "📁 Diretório: ~/DE_cache/"
echo ""

if [ $extracted_count -gt 0 ]; then
    echo "📋 Arquivos gerados:"
    ls -la ~/DE_cache/*.txt 2>/dev/null
    echo ""
    
    echo "🔍 Exemplo do último arquivo:"
    last_file=$(ls -t ~/DE_cache/*.txt 2>/dev/null | head -1)
    if [ ! -z "$last_file" ]; then
        echo "───────────────────────────────────────"
        head -15 "$last_file" | grep -v "^\s*$" | tail -10
        echo "───────────────────────────────────────"
    fi
    
    echo ""
    echo "🎯 Extração concluída!"
    echo "💡 Para ver todos os arquivos: ls ~/DE_cache/"
else
    echo "❌ Nenhuma página foi extraída com sucesso."
    echo "💡 Verifique se os PDFs estão carregados no cache"
fi