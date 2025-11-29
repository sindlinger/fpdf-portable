#!/bin/bash
echo "🔍 TESTANDO PALAVRAS RELACIONADAS COM BASE64"
echo "============================================"
echo

for word in "nota" "requisicao" "pagamento" "orcament" "reserva" "honorarios" "perito"; do
    echo "=== Testando palavra: $word ==="
    
    count=$(./bin/Release/net6.0/linux-x64/fpdf 1 filter base64 --word "$word" -F count 2>/dev/null | grep "BASE64_STRINGS_COUNT" | grep -o "[0-9]*" | head -1)
    
    if [ -n "$count" ] && [ "$count" != "0" ]; then
        echo "🎯 '$word': $count base64 encontrado(s)!"
        # Mostrar detalhes do primeiro resultado
        ./bin/Release/net6.0/linux-x64/fpdf 1 filter base64 --word "$word" 2>/dev/null | head -20
        echo
    else
        echo "❌ '$word': Nenhum base64 próximo"
    fi
    echo
done