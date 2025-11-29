#!/bin/bash

# Script para analisar PDFs escaneados (notas de empenho)
# Uso: ./analyze_scanned.sh [inicio] [fim] [threshold]

START=${1:-1}
END=${2:-100}
THRESHOLD=${3:-90}

echo "========================================="
echo "Análise de PDFs Escaneados (Notas de Empenho)"
echo "Range: $START-$END | Threshold: $THRESHOLD%"
echo "========================================="
echo

total_pdfs=0
total_scanned=0
total_pages=0

echo "PDF_ID | Total_Pages | Scanned_Pages | Percentage | Status"
echo "-------|-------------|---------------|------------|--------"

for i in $(seq $START $END); do
    result=$(fpdf $i scanned --threshold $THRESHOLD --format json 2>/dev/null)
    
    if [ $? -eq 0 ]; then
        total=$(echo "$result" | grep '"total_pages"' | sed 's/.*: \([0-9]*\).*/\1/')
        scanned=$(echo "$result" | grep '"scanned_pages_count"' | sed 's/.*: \([0-9]*\).*/\1/')
        
        if [ -n "$total" ] && [ -n "$scanned" ]; then
            percentage=$(( scanned * 100 / total ))
            total_pdfs=$((total_pdfs + 1))
            total_pages=$((total_pages + scanned))
            
            # Determinar status baseado na porcentagem
            status="PARCIAL"
            if [ $percentage -ge 90 ]; then
                status="ESCANEADO"
                total_scanned=$((total_scanned + 1))
            elif [ $percentage -eq 0 ]; then
                status="TEXTO"
            fi
            
            printf "%-7s| %-12s| %-14s| %10s%% | %s\n" \
                   "$i" "$total" "$scanned" "$percentage" "$status"
        fi
    fi
done

echo
echo "========================================="
echo "RESUMO:"
echo "- PDFs analisados: $total_pdfs"
echo "- PDFs totalmente escaneados (>90%): $total_scanned"
echo "- Total de páginas escaneadas: $total_pages"
echo "========================================="