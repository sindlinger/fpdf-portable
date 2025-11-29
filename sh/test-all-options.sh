#!/bin/bash

# Script de teste completo para TODAS as opções do fpdf
# Verifica se todas as opções estão funcionando corretamente

echo "========================================="
echo "TESTE COMPLETO DE TODAS AS OPÇÕES DO FPDF"
echo "========================================="

# Função para testar um comando
test_command() {
    local cmd="$1"
    local desc="$2"
    echo -n "Testando: $desc... "
    
    if $cmd > /dev/null 2>&1; then
        echo "✅ OK"
        return 0
    else
        echo "❌ FALHOU"
        echo "  Comando: $cmd"
        return 1
    fi
}

# Contador de testes
TOTAL=0
PASSED=0
FAILED=0

# TESTE: Comando pages com todas as opções
echo ""
echo "📄 TESTANDO COMANDO PAGES"
echo "------------------------"

test_command "fpdf 1 pages --word 'RODRIGUES'" "pages --word"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --not-words 'BANCO'" "pages --not-words"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --regex '[0-9]+'" "pages --regex"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --min-words 300" "pages --min-words"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --max-words 500" "pages --max-words"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --first 5" "pages --first"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --last 3" "pages --last"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --page-range 1-10" "pages --page-range"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --blank" "pages --blank"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --image true" "pages --image"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --annotations false" "pages --annotations"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --tables false" "pages --tables"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --columns false" "pages --columns"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --orientation portrait" "pages --orientation"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --font Arial" "pages --font"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --font-bold true" "pages --font-bold"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --font-italic false" "pages --font-italic"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --format json" "pages --format json"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --format csv" "pages --format csv"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 pages --format xml" "pages --format xml"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando documents
echo ""
echo "📑 TESTANDO COMANDO DOCUMENTS"
echo "-----------------------------"

test_command "fpdf 1 documents" "documents básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 documents --min-pages 2" "documents --min-pages"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 documents --max-pages 10" "documents --max-pages"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 documents --format json" "documents --format json"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando words
echo ""
echo "📝 TESTANDO COMANDO WORDS"
echo "------------------------"

test_command "fpdf 1 words --top 10" "words --top"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 words --min-length 5" "words --min-length"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 words --frequency 2" "words --frequency"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 words --word 'justiça'" "words --word"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando bookmarks
echo ""
echo "🔖 TESTANDO COMANDO BOOKMARKS"
echo "-----------------------------"

test_command "fpdf 1 bookmarks" "bookmarks básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 bookmarks --level 1" "bookmarks --level"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 bookmarks --word 'capítulo'" "bookmarks --word"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando annotations
echo ""
echo "💬 TESTANDO COMANDO ANNOTATIONS"
echo "-------------------------------"

test_command "fpdf 1 annotations" "annotations básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 annotations --type Highlight" "annotations --type"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando fonts
echo ""
echo "🔤 TESTANDO COMANDO FONTS"
echo "------------------------"

test_command "fpdf 1 fonts" "fonts básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 fonts --name Helvetica" "fonts --name"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 fonts --type Type1" "fonts --type"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando metadata
echo ""
echo "ℹ️ TESTANDO COMANDO METADATA"
echo "---------------------------"

test_command "fpdf 1 metadata" "metadata básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 metadata --format json" "metadata --format json"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando structure
echo ""
echo "🏗️ TESTANDO COMANDO STRUCTURE"
echo "----------------------------"

test_command "fpdf 1 structure" "structure básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 structure --format json" "structure --format json"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando objects
echo ""
echo "📦 TESTANDO COMANDO OBJECTS"
echo "--------------------------"

test_command "fpdf 1 objects" "objects básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 objects --type Stream" "objects --type"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando images
echo ""
echo "🖼️ TESTANDO COMANDO IMAGES"
echo "-------------------------"

test_command "fpdf 1 images" "images básico"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf 1 images --min-width 100" "images --min-width"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# TESTE: Comando cache
echo ""
echo "💾 TESTANDO COMANDO CACHE"
echo "------------------------"

test_command "fpdf cache list" "cache list"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf cache stats" "cache stats"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

test_command "fpdf cache search RODRIGUES" "cache search"
((TOTAL++)); [ $? -eq 0 ] && ((PASSED++)) || ((FAILED++))

# RESULTADOS FINAIS
echo ""
echo "========================================="
echo "📊 RESULTADOS DOS TESTES"
echo "========================================="
echo "Total de testes: $TOTAL"
echo "✅ Passou: $PASSED"
echo "❌ Falhou: $FAILED"
echo ""

if [ $FAILED -eq 0 ]; then
    echo "🎉 TODOS OS TESTES PASSARAM!"
else
    echo "⚠️ ALGUNS TESTES FALHARAM!"
    echo "Por favor, verifique os comandos que falharam acima."
fi

echo "========================================="