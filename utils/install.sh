#!/bin/bash

echo "📦 Instalando fpdf..."

# Verificar se tem o executável compilado
if [ ! -f "bin/fpdf" ]; then
    echo "❌ Erro: Arquivo bin/fpdf não encontrado"
    echo "Execute primeiro: ./build.sh"
    exit 1
fi

# Copiar para /usr/local/bin com sudo
echo "🔧 Instalando em /usr/local/bin/fpdf (requer sudo)..."
sudo cp bin/fpdf /usr/local/bin/fpdf
sudo chmod +x /usr/local/bin/fpdf

# Verificar instalação
if [ -f "/usr/local/bin/fpdf" ]; then
    echo "✅ fpdf instalado com sucesso!"
    echo ""
    echo "Testando instalação..."
    fpdf --version
    echo ""
    echo "Use 'fpdf --help' para ver os comandos disponíveis"
else
    echo "❌ Erro na instalação"
    exit 1
fi