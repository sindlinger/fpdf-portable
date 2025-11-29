#!/bin/bash

echo "🔨 Compilando FilterPDF..."
dotnet publish FilterPDFC#.csproj -c Release

if [ $? -eq 0 ]; then
    echo "📦 Copiando executável para ./bin/fpdf"
    cp bin/Release/net6.0/linux-x64/fpdf bin/fpdf
    chmod +x bin/fpdf
    
    echo "✅ Build concluído com sucesso!"
    echo "   Executável disponível em: ./bin/fpdf"
    
    # Testar se funciona
    echo "🧪 Testando executável..."
    if ./bin/fpdf --help > /dev/null 2>&1; then
        echo "✅ Executável funcionando corretamente"
    else
        echo "❌ Erro: Executável não está funcionando"
        exit 1
    fi
else
    echo "❌ Erro na compilação"
    exit 1
fi
