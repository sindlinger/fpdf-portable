# Guia: Extração de Texto de Base64

## Resposta: O que pode ser aplicado ao Base64 para extrair texto?

Do base64 você pode extrair texto usando **duas abordagens principais**:

### 1. **Extração Direta de Texto (pdftotext)**
Para PDFs que têm texto embutido (não são imagens escaneadas):
```bash
# Pipeline: Base64 → PDF → Texto
base64 -d arquivo.b64 | pdftotext - texto_extraido.txt
```

### 2. **OCR - Reconhecimento Óptico de Caracteres (Tesseract)**
Para PDFs escaneados ou que são essencialmente imagens:
```bash
# Pipeline: Base64 → PDF → Imagem → OCR → Texto
base64 -d arquivo.b64 | pdftoppm -png -r 300 - - | tesseract - output_text -l por
```

## Pipeline Completo de Extração

```
Base64 → Decodificar → PDF → Converter → Imagem → OCR → Texto
```

## Exemplo Prático com Nota de Empenho

### 1. Extrair página como Base64
```bash
fpdf 717 base64 --extract-page 34 -F raw > nota_empenho.b64
```

### 2. Aplicar extração de texto
```bash
./base64_to_text.sh nota_empenho.b64
```

### Resultado
O script produzirá dois arquivos:
- `nota_empenho_texto_direto.txt` - Se houver texto embutido
- `nota_empenho_texto_ocr.txt` - Texto extraído por OCR

## Ferramentas Necessárias

1. **pdftotext** (poppler-utils)
   - Extrai texto embutido de PDFs
   - Instalação: `sudo apt install poppler-utils`

2. **pdftoppm** (poppler-utils)
   - Converte PDF em imagem
   - Vem junto com poppler-utils

3. **Tesseract OCR**
   - Reconhece texto em imagens
   - Instalação: `sudo apt install tesseract-ocr tesseract-ocr-por`

4. **base64** (coreutils)
   - Decodifica base64
   - Já vem instalado por padrão

## Script Completo

O script `base64_to_text.sh` automatiza todo o processo:

```bash
#!/bin/bash
# Uso: ./base64_to_text.sh arquivo.b64

input_b64=$1
basename="${input_b64%.b64}"

# Método 1: Extração direta
base64 -d "$input_b64" | pdftotext - "${basename}_texto_direto.txt"

# Método 2: OCR
base64 -d "$input_b64" | pdftoppm -png -r 300 -singlefile - temp_image
tesseract temp_image.png "${basename}_texto_ocr" -l por
```

## Informações Extraídas de Notas de Empenho

Com OCR, conseguimos extrair:
- CNPJ: `29.979.036/0162-25`
- Instituição: `INSS INST NACIONAL DO SEGURO SOCIAL`
- Endereço: `RUA BARAO DO ABIAMW 73, CENTRO, JOÃO PESSOA PB`
- Valores: `R$ 74,00`
- Processo: `080096 2-59, 2021,8,15,0131`
- Responsáveis: `Ronivaldo de Oliveira Barros`, `Jussara Leite Souza Alcantara`

## Resumo

**Base64 → Texto** pode ser feito através de:
1. **pdftotext**: Rápido e preciso para PDFs com texto
2. **OCR (Tesseract)**: Necessário para PDFs escaneados

Ambos os métodos são aplicados automaticamente pelo script `base64_to_text.sh`.