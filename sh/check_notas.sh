#!/bin/bash

echo "=== Verificando PDFs com páginas 744x1052 ==="
echo ""

for i in 1 2 3 4 5 10 20 30 40 50 100 200 300 400 500 600 700 717; do
    count=$(fpdf $i pages --width 744 --height 1052 -F count 2>/dev/null | grep 'PAGES_COUNT:' | awk '{print $2}')
    
    if [ ! -z "$count" ] && [ "$count" != "0" ]; then
        filename=$(fpdf cache list 2>/dev/null | grep "^$i " | awk '{print $3}')
        total=$(fpdf $i pages -F count 2>/dev/null | grep 'PAGES_COUNT:' | awk '{print $2}')
        
        echo "✓ PDF #$i: $filename"
        echo "  Total de páginas: $total"
        echo "  Páginas 744x1052: $count"
        
        if [ "$count" -ge "1" ] && [ "$count" -le "3" ]; then
            echo "  → Possível nota de empenho!"
        fi
        echo ""
    fi
done

echo "Análise concluída!"