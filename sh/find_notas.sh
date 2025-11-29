#\!/bin/bash

echo '=== Buscando PDFs com páginas 744x1052 (possíveis notas de empenho) ==='
echo ''

found=0
for i in $(seq 1 1000); do
    # Verificar se o cache existe
    if \! fpdf cache list 2>/dev/null | grep -q "^$i "; then
        break
    fi
    
    # Buscar páginas com dimensões 744x1052
    result=$(fpdf $i pages --width 744 --height 1052 -F count 2>/dev/null | grep 'PAGES_COUNT:' | awk '{print $2}')
    
    if [ \! -z "$result" ] && [ "$result" -gt 0 ]; then
        filename=$(fpdf cache list 2>/dev/null | grep "^$i " | awk '{print $3}')
        echo "✓ PDF #$i: $filename - $result página(s) 744x1052"
        ((found++))
    fi
done

echo ''
echo "Total de PDFs com possível nota de empenho: $found"
