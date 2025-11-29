#!/bin/bash

echo "🔍 Buscando páginas escaneadas que podem conter notas de empenho..."
echo ""

# Buscar em todos os PDFs que contêm "empenho"
for i in {1..20}; do
    # Primeiro verifica se contém a palavra "empenho"
    result=$(fpdf $i words --word "empenho" 2>/dev/null | grep "Found.*word")
    
    if [[ ! -z "$result" ]]; then
        echo "📄 PDF #$i contém 'empenho'"
        
        # Agora verifica páginas escaneadas (precisaria do arquivo original)
        # Por enquanto, apenas mostra onde encontrou
        fpdf $i words --word "empenho" 2>/dev/null | grep -E "Page [0-9]+" | head -3
        echo ""
    fi
done

echo ""
echo "💡 Para extrair páginas que são imagens de um PDF específico:"
echo "   fpdf <arquivo.pdf> extract-images -e -o ./imagens"