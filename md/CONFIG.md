# Configuração do fpdf

O fpdf suporta múltiplas formas de configuração para facilitar o uso em diferentes ambientes.

## 📁 Métodos de Configuração (em ordem de prioridade)

1. **Variáveis de Ambiente** (maior prioridade)
2. **Arquivo `.env`**
3. **Arquivo `fpdf.config.json`**
4. **Valores padrão** (menor prioridade)

## 🔧 Configuração Rápida

### Método 1: Arquivo `.env` (Recomendado)

1. Copie o arquivo de exemplo:
```bash
cp .env.example .env
```

2. Edite o arquivo `.env`:
```bash
# Diretórios permitidos (separe múltiplos com :)
FPDF_ALLOWED_DIRS=/mnt/c/Users/seu_usuario/pdfs:/outro/diretorio

# Número de workers para processamento paralelo
FPDF_DEFAULT_WORKERS=16

# Diretório de cache
FPDF_CACHE_DIR=.cache
```

### Método 2: Arquivo `fpdf.config.json`

1. Copie o arquivo de exemplo:
```bash
cp fpdf.config.json.example fpdf.config.json
```

2. Edite o arquivo `fpdf.config.json`:
```json
{
  "Security": {
    "AllowedDirectories": [
      "/mnt/c/Users/seu_usuario/pdfs",
      "/outro/diretorio"
    ],
    "DisablePathValidation": false,
    "MaxFileSize": 524288000
  },
  "Performance": {
    "DefaultWorkers": 16
  }
}
```

### Método 3: Variável de Ambiente (Temporário)

```bash
# Para uma única execução
export FPDF_ALLOWED_DIRS="/mnt/c/Users/pichau/Desktop/geral_pdf/pdf_cache"
fpdf load images-only --input-dir "/mnt/c/Users/pichau/Desktop/geral_pdf/pdf_cache"

# Ou inline
FPDF_ALLOWED_DIRS="/caminho/pdfs" fpdf load documento.pdf
```

### Método 4: Adicionar ao `.bashrc` (Permanente)

```bash
echo 'export FPDF_ALLOWED_DIRS="/mnt/c/Users/pichau/Desktop/geral_pdf/pdf_cache"' >> ~/.bashrc
source ~/.bashrc
```

## 📋 Opções de Configuração

### Segurança

| Opção | Descrição | Padrão |
|-------|-----------|---------|
| `FPDF_ALLOWED_DIRS` | Diretórios permitidos para acesso (separados por :) | Diretório atual |
| `FPDF_DISABLE_PATH_VALIDATION` | Desabilitar validação de caminho (⚠️ CUIDADO!) | false |
| `FPDF_MAX_FILE_SIZE_MB` | Tamanho máximo de arquivo em MB | 500 |

### Performance

| Opção | Descrição | Padrão |
|-------|-----------|---------|
| `FPDF_DEFAULT_WORKERS` | Número padrão de workers | 4 |

### Cache

| Opção | Descrição | Padrão |
|-------|-----------|---------|
| `FPDF_CACHE_DIR` | Diretório para arquivos de cache | .cache |

## 🔍 Locais de Configuração

O fpdf procura por arquivos de configuração nesta ordem:

1. `./fpdf.config.json` (diretório atual)
2. `./.fpdf/config.json` (subdiretório oculto)
3. `~/.fpdf/config.json` (diretório home do usuário)
4. `./.env` (diretório atual)
5. `./.fpdf.env` (diretório atual)
6. `~/.fpdf.env` (diretório home do usuário)

## 🎯 Exemplos de Uso

### Para processar PDFs de um diretório Windows no WSL:

1. Crie um arquivo `.env`:
```env
FPDF_ALLOWED_DIRS=/mnt/c/Users/pichau/Desktop/geral_pdf/pdf_cache
FPDF_DEFAULT_WORKERS=16
```

2. Execute o comando:
```bash
fpdf load images-only --input-dir "/mnt/c/Users/pichau/Desktop/geral_pdf/pdf_cache"
```

### Para múltiplos diretórios:

```env
# No arquivo .env
FPDF_ALLOWED_DIRS=/mnt/c/Documents:/mnt/d/PDFs:/home/user/pdfs
```

Ou no `fpdf.config.json`:
```json
{
  "Security": {
    "AllowedDirectories": [
      "/mnt/c/Documents",
      "/mnt/d/PDFs",
      "/home/user/pdfs"
    ]
  }
}
```

## ⚠️ Segurança

- **NUNCA** desabilite `FPDF_DISABLE_PATH_VALIDATION` em ambientes de produção
- Sempre especifique diretórios explícitos em `FPDF_ALLOWED_DIRS`
- O fpdf bloqueia automaticamente acesso a diretórios sensíveis do sistema

## 🆘 Resolução de Problemas

### Erro: "Access denied: Path failed security validation"

**Solução**: Adicione o diretório ao `FPDF_ALLOWED_DIRS`:
```bash
export FPDF_ALLOWED_DIRS="/seu/diretorio/com/pdfs"
```

### Como verificar configuração atual

Execute com `--verbose` para ver as configurações carregadas:
```bash
fpdf load documento.pdf --verbose
```

As mensagens mostrarão:
```
[INFO] Configuration loaded from: .env
[INFO] Environment file loaded from: .env
```