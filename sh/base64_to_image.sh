#!/bin/bash

# Script para converter base64 de PDF para imagem
# Uso: ./base64_to_image.sh arquivo.b64 output.png

if [ $# -lt 2 ]; then
    echo "Uso: $0 arquivo.b64 output.png"
    echo "Exemplo: $0 nota_empenho.b64 nota_empenho.png"
    exit 1
fi

input_b64=$1
output_image=$2

echo "Convertendo base64 para PDF temporário..."
# Decodificar base64 para PDF
base64 -d "$input_b64" > temp_pdf.pdf

echo "Convertendo PDF para imagem..."
# Converter PDF para imagem usando ImageMagick
# -density 300: resolução em DPI (maior = melhor qualidade)
# -quality 100: qualidade da imagem de saída
convert -density 300 -quality 100 temp_pdf.pdf "$output_image"

# Limpar arquivo temporário
rm temp_pdf.pdf

echo "✓ Imagem salva em: $output_image"