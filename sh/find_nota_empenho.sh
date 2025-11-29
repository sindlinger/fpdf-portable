#!/bin/bash

echo "=== Buscando PDFs com possível Nota de Empenho ==="
echo "Critérios: 2 páginas com dimensões 744x1052"
echo ""

# Contador
found=0
total=0

# Buscar em todos os PDFs do cache
for i in {1..1000}; do
    # Verificar se o cache existe
    if fpdf $i pages --count 2>/dev/null | grep -q "PAGES_COUNT: 2"; then
        # Tem 2 páginas, agora verificar as dimensões
        if fpdf $i pages --width 744 --height 1052 2>/dev/null | grep -q "Page 1" && \
           fpdf $i pages --width 744 --height 1052 2>/dev/null | grep -q "Page 2"; then
            # Encontrou!
            filename=$(fpdf cache list 2>/dev/null | grep "^$i " | awk '{print $3}')
            if [ ! -z "$filename" ]; then
                echo "✓ PDF #$i: $filename - PROVÁVEL NOTA DE EMPENHO (2 páginas 744x1052)"
                ((found++))
            fi
        fi
    fi
    ((total++))
    
    # Parar se não encontrar mais caches
    if ! fpdf cache list 2>/dev/null | grep -q "^$i "; then
        break
    fi
done

echo ""
echo "=== Resumo ==="
echo "Total analisados: $total"
echo "Possíveis notas de empenho encontradas: $found"