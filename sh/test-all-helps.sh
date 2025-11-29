#!/bin/bash

# Script para testar se TODOS os helps estão funcionando
# Deve ser executado após cada compilação

echo "========================================="
echo "TESTE DE TODOS OS HELPS DO FPDF"
echo "========================================="
echo ""

# Cores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Contador
TOTAL=0
PASSED=0
FAILED=0

# Função para testar help
test_help() {
    local cmd="$1"
    local desc="$2"
    local min_lines="$3"
    
    echo -n "Testando help: $desc... "
    
    # Executa o comando e conta as linhas
    output=$($cmd 2>&1)
    lines=$(echo "$output" | wc -l)
    
    # Verifica se tem o mínimo de linhas esperado
    if [ $lines -ge $min_lines ]; then
        echo -e "${GREEN}✅ OK${NC} ($lines linhas)"
        ((PASSED++))
        return 0
    else
        echo -e "${RED}❌ FALHOU${NC} (apenas $lines linhas, esperado >= $min_lines)"
        echo "  Comando: $cmd"
        ((FAILED++))
        return 1
    fi
    ((TOTAL++))
}

# TESTE 1: Help principal
echo -e "${YELLOW}📋 TESTANDO HELP PRINCIPAL${NC}"
echo "--------------------------------"
test_help "fpdf --help" "Help principal" 35
((TOTAL++))

# TESTE 2: Help do comando pages (mais importante - tem 30+ opções)
echo ""
echo -e "${YELLOW}📄 TESTANDO COMANDO PAGES${NC}"
echo "--------------------------------"
test_help "fpdf 1 pages --help" "pages --help" 80
((TOTAL++))

# Verifica opções específicas do pages
echo -n "  Verificando presença de opções principais... "
help_content=$(fpdf 1 pages --help 2>&1)
missing_options=""

# Lista de opções que DEVEM estar presentes
options_to_check=(
    "--word"
    "--not-words"
    "--regex"
    "--min-words"
    "--max-words"
    "--first"
    "--last"
    "--page-range"
    "--blank"
    "--image"
    "--annotations"
    "--tables"
    "--columns"
    "--orientation"
    "--font"
    "--font-bold"
    "--font-italic"
    "--format"
    "--output"
)

for option in "${options_to_check[@]}"; do
    if ! echo "$help_content" | grep -q -- "$option"; then
        missing_options="$missing_options $option"
    fi
done

if [ -z "$missing_options" ]; then
    echo -e "${GREEN}✅ Todas as opções presentes${NC}"
    ((PASSED++))
else
    echo -e "${RED}❌ Opções faltando:$missing_options${NC}"
    ((FAILED++))
fi
((TOTAL++))

# TESTE 3: Help do comando documents
echo ""
echo -e "${YELLOW}📑 TESTANDO COMANDO DOCUMENTS${NC}"
echo "--------------------------------"
test_help "fpdf 1 documents --help" "documents --help" 40
((TOTAL++))

# TESTE 4: Help do comando words
echo ""
echo -e "${YELLOW}📝 TESTANDO COMANDO WORDS${NC}"
echo "--------------------------------"
test_help "fpdf 1 words --help" "words --help" 20
((TOTAL++))

# TESTE 5: Help do comando bookmarks
echo ""
echo -e "${YELLOW}🔖 TESTANDO COMANDO BOOKMARKS${NC}"
echo "--------------------------------"
test_help "fpdf 1 bookmarks --help" "bookmarks --help" 35
((TOTAL++))

# TESTE 6: Help do comando annotations
echo ""
echo -e "${YELLOW}💬 TESTANDO COMANDO ANNOTATIONS${NC}"
echo "--------------------------------"
test_help "fpdf 1 annotations --help" "annotations --help" 35
((TOTAL++))

# TESTE 7: Help do comando fonts
echo ""
echo -e "${YELLOW}🔤 TESTANDO COMANDO FONTS${NC}"
echo "--------------------------------"
test_help "fpdf 1 fonts --help" "fonts --help" 15
((TOTAL++))

# TESTE 8: Help do comando metadata
echo ""
echo -e "${YELLOW}ℹ️ TESTANDO COMANDO METADATA${NC}"
echo "--------------------------------"
test_help "fpdf 1 metadata --help" "metadata --help" 15
((TOTAL++))

# TESTE 9: Help do comando structure
echo ""
echo -e "${YELLOW}🏗️ TESTANDO COMANDO STRUCTURE${NC}"
echo "--------------------------------"
test_help "fpdf 1 structure --help" "structure --help" 15
((TOTAL++))

# TESTE 10: Help do comando objects
echo ""
echo -e "${YELLOW}📦 TESTANDO COMANDO OBJECTS${NC}"
echo "--------------------------------"
test_help "fpdf 1 objects --help" "objects --help" 15
((TOTAL++))

# TESTE 11: Help do comando images
echo ""
echo -e "${YELLOW}🖼️ TESTANDO COMANDO IMAGES${NC}"
echo "--------------------------------"
test_help "fpdf 1 images --help" "images --help" 15
((TOTAL++))

# TESTE 12: Help do comando modifications
echo ""
echo -e "${YELLOW}📝 TESTANDO COMANDO MODIFICATIONS${NC}"
echo "--------------------------------"
test_help "fpdf 1 modifications --help" "modifications --help" 15
((TOTAL++))

# TESTE 13: Help do comando base64
echo ""
echo -e "${YELLOW}🔐 TESTANDO COMANDO BASE64${NC}"
echo "--------------------------------"
test_help "fpdf 1 base64 --help" "base64 --help" 15
((TOTAL++))

# TESTE 14: Help do comando cache
echo ""
echo -e "${YELLOW}💾 TESTANDO COMANDO CACHE${NC}"
echo "--------------------------------"
test_help "fpdf cache --help" "cache --help" 15
((TOTAL++))

# TESTE 15: Verificação de idioma PT
echo ""
echo -e "${YELLOW}🇧🇷 TESTANDO IDIOMA PORTUGUÊS${NC}"
echo "--------------------------------"
fpdf idioma pt > /dev/null 2>&1
help_pt=$(fpdf 1 pages --help 2>&1 | head -5)
if echo "$help_pt" | grep -q "FILTRAR PÁGINAS"; then
    echo -e "${GREEN}✅ Help em português funcionando${NC}"
    ((PASSED++))
else
    echo -e "${RED}❌ Help em português não está funcionando${NC}"
    ((FAILED++))
fi
((TOTAL++))

# TESTE 16: Verificação de idioma EN
echo ""
echo -e "${YELLOW}🇺🇸 TESTANDO IDIOMA INGLÊS${NC}"
echo "--------------------------------"
fpdf idioma en > /dev/null 2>&1
help_en=$(fpdf 1 pages --help 2>&1 | head -5)
if echo "$help_en" | grep -q "FILTER PAGES"; then
    echo -e "${GREEN}✅ Help em inglês funcionando${NC}"
    ((PASSED++))
else
    echo -e "${RED}❌ Help em inglês não está funcionando${NC}"
    ((FAILED++))
fi
((TOTAL++))

# Restaurar idioma para PT
fpdf idioma pt > /dev/null 2>&1

# RESULTADOS FINAIS
echo ""
echo "========================================="
echo -e "${YELLOW}📊 RESULTADOS DOS TESTES DE HELP${NC}"
echo "========================================="
echo "Total de testes: $TOTAL"
echo -e "${GREEN}✅ Passou: $PASSED${NC}"
echo -e "${RED}❌ Falhou: $FAILED${NC}"
echo ""

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}🎉 TODOS OS HELPS ESTÃO FUNCIONANDO!${NC}"
    exit 0
else
    echo -e "${RED}⚠️ ALGUNS HELPS NÃO ESTÃO FUNCIONANDO!${NC}"
    echo "Por favor, verifique os comandos que falharam acima."
    exit 1
fi