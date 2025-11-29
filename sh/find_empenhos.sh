#!/bin/bash

# Script para encontrar prováveis notas de empenho em PDFs
# Baseado em padrões: páginas escaneadas no final do documento

echo "================================================"
echo "Busca por Notas de Empenho"
echo "================================================"
echo

# Parâmetros
START=${1:-1}
END=${2:-100}
MIN_PAGES=${3:-2}    # Mínimo de páginas consecutivas
MAX_PAGES=${4:-10}   # Máximo de páginas consecutivas

echo "Analisando PDFs: $START a $END"
echo "Critério: $MIN_PAGES-$MAX_PAGES páginas escaneadas no final"
echo
echo "ID  | Total | Escaneadas | Últimas | Provável Empenho?"
echo "----|-------|------------|---------|------------------"

found_count=0
total_analyzed=0

for i in $(seq $START $END); do
    # Pega informações do PDF
    result=$(fpdf $i scanned --threshold 70 --format json 2>/dev/null)
    
    if [ $? -eq 0 ] && [ -n "$result" ]; then
        total_pages=$(echo "$result" | jq -r '.total_pages // 0')
        scanned_count=$(echo "$result" | jq -r '.scanned_pages_count // 0')
        
        if [ "$total_pages" -gt 0 ]; then
            total_analyzed=$((total_analyzed + 1))
            
            # Pega as páginas escaneadas
            scanned_pages=$(echo "$result" | jq -r '.scanned_pages[].page // empty' 2>/dev/null)
            
            # Verifica se há sequência no final
            if [ -n "$scanned_pages" ]; then
                # Pega as últimas páginas escaneadas
                last_pages=$(echo "$scanned_pages" | tail -$MAX_PAGES)
                last_count=$(echo "$last_pages" | wc -l)
                
                # Verifica se a última página escaneada está perto do fim
                last_scanned=$(echo "$scanned_pages" | tail -1)
                distance_from_end=$((total_pages - last_scanned))
                
                # Critérios para nota de empenho:
                # 1. Tem páginas escaneadas
                # 2. Últimas páginas são escaneadas
                # 3. Sequência de 2-10 páginas
                # 4. Está no final (últimas 30% do documento)
                
                is_empenho="NÃO"
                final_threshold=$((total_pages * 70 / 100))  # últimos 30%
                
                if [ "$last_scanned" -ge "$final_threshold" ] && \
                   [ "$last_count" -ge "$MIN_PAGES" ] && \
                   [ "$last_count" -le "$MAX_PAGES" ] && \
                   [ "$distance_from_end" -le 5 ]; then
                    is_empenho="SIM ✓"
                    found_count=$((found_count + 1))
                    
                    # Mostra detalhes
                    printf "%-4s| %-6s| %-11s| %-8s| %s\n" \
                           "$i" "$total_pages" "$scanned_count" "$last_count" "$is_empenho"
                    
                    # Extrai range das páginas do empenho
                    first_empenho=$((last_scanned - last_count + 1))
                    echo "    └─> Páginas do empenho: $first_empenho-$last_scanned"
                elif [ "$scanned_count" -gt 0 ]; then
                    # Mostra PDFs com páginas escaneadas mas que não parecem empenho
                    printf "%-4s| %-6s| %-11s| %-8s| %s\n" \
                           "$i" "$total_pages" "$scanned_count" "$last_count" "$is_empenho"
                fi
            fi
        fi
    fi
done

echo
echo "================================================"
echo "RESUMO:"
echo "- PDFs analisados: $total_analyzed"
echo "- Prováveis notas de empenho: $found_count"
echo "================================================"
echo
echo "DICA: Para extrair as imagens das notas encontradas,"
echo "      use: fpdf [ID] images --pages [RANGE]"