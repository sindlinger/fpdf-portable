#!/bin/bash

# Script de compilação e instalação do fpdf
# Compila o projeto e instala o executável no PATH

set -e  # Sair em caso de erro

# Cores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}╔════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║     FilterPDF Build & Install Script    ║${NC}"
echo -e "${GREEN}╚════════════════════════════════════════╝${NC}"
echo

# Verificar se dotnet está instalado
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}❌ Erro: dotnet não está instalado${NC}"
    exit 1
fi

# Diretório do projeto
PROJECT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$PROJECT_DIR"

echo -e "${YELLOW}📦 Limpando build anterior...${NC}"
rm -rf bin/Release
dotnet clean -c Release > /dev/null 2>&1 || true

echo -e "${YELLOW}🔨 Compilando projeto...${NC}"
dotnet publish fpdf.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o bin/Release/publish

if [ ! -f "bin/Release/publish/fpdf" ]; then
    echo -e "${RED}❌ Erro: Executável não foi gerado${NC}"
    exit 1
fi

# Tornar executável
chmod +x bin/Release/publish/fpdf

echo -e "${GREEN}✅ Compilação concluída com sucesso!${NC}"

# Encontrar o fpdf atual no PATH
CURRENT_FPDF=$(which fpdf 2>/dev/null || true)

if [ -n "$CURRENT_FPDF" ]; then
    echo -e "${YELLOW}📍 fpdf encontrado em: $CURRENT_FPDF${NC}"
    
    # Verificar se é um link simbólico
    if [ -L "$CURRENT_FPDF" ]; then
        REAL_PATH=$(readlink -f "$CURRENT_FPDF")
        echo -e "${YELLOW}   → Link simbólico para: $REAL_PATH${NC}"
    fi
    
    # Fazer backup do executável atual
    BACKUP_NAME="${CURRENT_FPDF}.backup.$(date +%Y%m%d_%H%M%S)"
    echo -e "${YELLOW}💾 Fazendo backup: $BACKUP_NAME${NC}"
    sudo cp "$CURRENT_FPDF" "$BACKUP_NAME"
    
    # Substituir o executável
    echo -e "${YELLOW}🔄 Substituindo fpdf...${NC}"
    sudo cp bin/Release/publish/fpdf "$CURRENT_FPDF"
    
    echo -e "${GREEN}✅ fpdf atualizado com sucesso!${NC}"
else
    # Se não existe, instalar em /usr/local/bin
    INSTALL_DIR="/usr/local/bin"
    echo -e "${YELLOW}📍 fpdf não encontrado no PATH${NC}"
    echo -e "${YELLOW}📦 Instalando em: $INSTALL_DIR${NC}"
    
    if [ -w "$INSTALL_DIR" ]; then
        cp bin/Release/publish/fpdf "$INSTALL_DIR/"
    else
        sudo cp bin/Release/publish/fpdf "$INSTALL_DIR/"
    fi
    
    echo -e "${GREEN}✅ fpdf instalado com sucesso!${NC}"
fi

# Verificar a instalação
echo
echo -e "${YELLOW}🔍 Verificando instalação...${NC}"

# Mostrar versão
if fpdf --version &> /dev/null; then
    echo -e "${GREEN}✅ fpdf está funcionando corretamente${NC}"
    echo
    fpdf --version
else
    echo -e "${YELLOW}⚠️  fpdf instalado mas não está respondendo ao --version${NC}"
fi

# Mostrar localização final
FINAL_LOCATION=$(which fpdf)
echo
echo -e "${GREEN}📍 Localização final: $FINAL_LOCATION${NC}"

# Mostrar tamanho do executável
FILE_SIZE=$(du -h "$FINAL_LOCATION" | cut -f1)
echo -e "${GREEN}📊 Tamanho do executável: $FILE_SIZE${NC}"

echo
echo -e "${GREEN}╔════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║        Instalação Concluída!           ║${NC}"
echo -e "${GREEN}╚════════════════════════════════════════╝${NC}"
echo
echo "Uso: fpdf --help"