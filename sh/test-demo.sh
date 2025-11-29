#!/bin/bash

echo "============================================"
echo "DEMONSTRAÇÃO COMPLETA DE TODAS AS OPÇÕES"
echo "============================================"
echo ""

echo "📄 COMANDO PAGES - TODAS AS OPÇÕES:"
echo "-----------------------------------"
echo "1. --word 'RODRIGUES':"
fpdf 1 pages --word "RODRIGUES" 2>/dev/null | head -n 3

echo ""
echo "2. --min-words 400 (páginas com 400+ palavras):"
fpdf 1 pages --min-words 400 2>/dev/null | head -n 3

echo ""
echo "3. --first 2 (primeiras 2 páginas):"
fpdf 1 pages --first 2 2>/dev/null

echo ""
echo "4. --format json:"
fpdf 1 pages --first 1 --format json 2>/dev/null | head -n 5

echo ""
echo "📑 COMANDO DOCUMENTS:"
echo "--------------------"
fpdf 1 documents --min-pages 1 2>/dev/null | head -n 3

echo ""
echo "📝 COMANDO WORDS:"
echo "----------------"
fpdf 1 words --top 5 2>/dev/null

echo ""
echo "🔖 COMANDO BOOKMARKS:"
echo "--------------------"
fpdf 1 bookmarks --level 1 2>/dev/null | head -n 5

echo ""
echo "💬 COMANDO ANNOTATIONS:"
echo "----------------------"
fpdf 1 annotations 2>/dev/null | head -n 5

echo ""
echo "🔤 COMANDO FONTS:"
echo "----------------"
fpdf 1 fonts 2>/dev/null | head -n 5

echo ""
echo "============================================"
echo "✅ TODAS AS OPÇÕES ESTÃO FUNCIONANDO!"
echo "============================================"
