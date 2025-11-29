#!/bin/bash

# Script para desinstalar fpdf do sistema

set -e

# Cores
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${YELLOW}🗑️  Desinstalando fpdf...${NC}"

# Encontrar o fpdf no PATH
FPDF_LOCATION=$(which fpdf 2>/dev/null || true)

if [ -z "$FPDF_LOCATION" ]; then
    echo -e "${RED}❌ fpdf não encontrado no PATH${NC}"
    exit 1
fi

echo -e "${YELLOW}📍 Encontrado em: $FPDF_LOCATION${NC}"

# Verificar se existe backup
BACKUP_FILES=$(ls "${FPDF_LOCATION}.backup."* 2>/dev/null | head -1 || true)

if [ -n "$BACKUP_FILES" ]; then
    echo -e "${YELLOW}📦 Backup encontrado: $BACKUP_FILES${NC}"
    echo -e "${YELLOW}🔄 Restaurando versão anterior...${NC}"
    
    # Restaurar backup
    sudo cp "$BACKUP_FILES" "$FPDF_LOCATION"
    echo -e "${GREEN}✅ Versão anterior restaurada${NC}"
    
    # Mostrar versão restaurada
    if fpdf --version &> /dev/null; then
        echo -e "${GREEN}📊 Versão restaurada:${NC}"
        fpdf --version
    fi
    
else
    echo -e "${YELLOW}❓ Deseja remover completamente? (y/N)${NC}"
    read -r response
    
    if [[ "$response" =~ ^[Yy]$ ]]; then
        sudo rm "$FPDF_LOCATION"
        echo -e "${GREEN}✅ fpdf removido completamente${NC}"
    else
        echo -e "${YELLOW}ℹ️  Operação cancelada${NC}"
        exit 0
    fi
fi

# Verificar se foi removido/restaurado
if ! command -v fpdf &> /dev/null; then
    echo -e "${GREEN}✅ fpdf removido do sistema${NC}"
else
    echo -e "${GREEN}✅ fpdf restaurado para versão anterior${NC}"
fi

echo -e "${GREEN}🎉 Desinstalação concluída!${NC}"