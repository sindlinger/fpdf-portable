#!/bin/bash
echo "🔍 TESTANDO BASE64 EM TODOS OS CACHES"
echo "====================================="
echo

for i in {1..20}; do
    echo "=== CACHE $i ==="
    
    # Contar base64 total
    count=$(./bin/Release/net6.0/linux-x64/fpdf $i filter base64 -F count 2>/dev/null | grep "BASE64_STRINGS_COUNT" | grep -o "[0-9]*" | head -1)
    
    if [ -n "$count" ] && [ "$count" != "0" ]; then
        echo "✅ Cache $i: $count base64 string(s) encontrada(s)"
        
        # Testar com filtro empenho
        empenho_count=$(./bin/Release/net6.0/linux-x64/fpdf $i filter base64 --word "empenho" -F count 2>/dev/null | grep "BASE64_STRINGS_COUNT" | grep -o "[0-9]*" | head -1)
        
        if [ -n "$empenho_count" ] && [ "$empenho_count" != "0" ]; then
            echo "🎯 BINGO! Cache $i tem $empenho_count base64 perto de 'empenho'!"
            echo "   Comando: ./bin/Release/net6.0/linux-x64/fpdf $i filter base64 --word \"empenho\" -F json"
        fi
    else
        echo "❌ Cache $i: Nenhum base64 encontrado"
    fi
    echo
done

echo "🔍 Testando palavras relacionadas em caches com base64..."
echo

for i in {1..10}; do
    count=$(./bin/Release/net6.0/linux-x64/fpdf $i filter base64 -F count 2>/dev/null | grep "BASE64_STRINGS_COUNT" | grep -o "[0-9]*" | head -1)
    
    if [ -n "$count" ] && [ "$count" != "0" ]; then
        echo "=== CACHE $i (tem $count base64) ==="
        
        for word in "nota" "pagamento" "requisicao" "orcament"; do
            word_count=$(./bin/Release/net6.0/linux-x64/fpdf $i filter base64 --word "$word" -F count 2>/dev/null | grep "BASE64_STRINGS_COUNT" | grep -o "[0-9]*" | head -1)
            
            if [ -n "$word_count" ] && [ "$word_count" != "0" ]; then
                echo "   🎯 '$word': $word_count base64 encontrado(s)!"
            fi
        done
        echo
    fi
done