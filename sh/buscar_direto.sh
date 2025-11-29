#!/bin/bash

# Script direto - busca páginas 744x1052 e extrai com OCR
echo "=== Busca Direta de Páginas 744x1052 com OCR ==="

# Criar diretório
mkdir -p ~/DE_cache

# Sabemos que PDF 717 tem - vamos testar ele primeiro
echo "🔍 Testando PDF #717 (sabemos que tem nota de empenho)..."

# Buscar páginas 744x1052 no PDF 717
fpdf 717 pages --width 744 --height 1052

echo ""
echo "📄 Extraindo página 34 do PDF 717 com OCR..."

# Extrair com OCR e salvar
fpdf 717 base64 --extract-page 34 -F ocr > ~/DE_cache/pdf_717_page_34_ocr.txt

echo "✅ Salvo em: ~/DE_cache/pdf_717_page_34_ocr.txt"
echo ""

# Mostrar o que foi extraído
echo "📝 Texto extraído:"
echo "=================="
head -20 ~/DE_cache/pdf_717_page_34_ocr.txt
echo "=================="

echo ""
echo "🎯 Teste direto concluído!"
echo "📁 Arquivo salvo em: ~/DE_cache/"