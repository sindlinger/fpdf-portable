# FilterPDF - Guia de Compilação e Instalação

## 🚀 Compilação Rápida

### ⚠️ IMPORTANTE: Use APENAS o compile.sh!

```bash
# FORMA CORRETA DE COMPILAR:
./compile.sh

# O compile.sh automaticamente:
# ✅ Compila o projeto
# ✅ Instala no PATH
# ✅ Faz backup de versões antigas
# ✅ Testa todos os comandos
# ✅ Verifica a instalação
```

### Usando Makefile (Alternativa)

```bash
# Compilar apenas
make build

# Compilar e instalar no PATH
make install

# Outras opções úteis
make clean          # Limpar build
make info           # Ver informações do projeto
make version        # Ver versão instalada
make help           # Ver todos os comandos
```

### Usando dotnet diretamente

```bash
dotnet publish fpdf.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o bin/Release/publish
```

## 📦 Instalação

### Instalação Automática
**USE O `compile.sh`** - ele faz tudo automaticamente:
- ✅ Compila o projeto
- ✅ Encontra o fpdf atual no PATH
- ✅ Faz backup da versão anterior
- ✅ Substitui pelo novo executável
- ✅ Verifica a instalação
- ✅ Testa todos os comandos

### Instalação Manual
```bash
# RECOMENDADO: Use o compile.sh
./compile.sh

# OU se preferir manual:
make build
sudo cp bin/Release/publish/fpdf /usr/local/bin/
```

## 🛠️ Comandos Make Disponíveis

| Comando | Descrição |
|---------|-----------|
| `make build` | Compilar o projeto |
| `make install` | Compilar e instalar no PATH |
| `make install-only` | Instalar sem recompilar |
| `make clean` | Limpar arquivos de build |
| `make test` | Executar testes |
| `make debug` | Compilar versão debug |
| `make run ARGS='...'` | Executar localmente |
| `make info` | Informações do projeto |
| `make version` | Versão instalada |
| `make help` | Ver ajuda |

## 🔍 Verificação

```bash
# Verificar instalação
fpdf --version

# Ver localização
which fpdf

# Testar funcionalidade
fpdf --help
fpdf cache list
```

## 📊 Detalhes Técnicos

- **Runtime**: `linux-x64`
- **Tipo**: Self-contained single file
- **Tamanho**: ~69MB
- **Framework**: .NET 6.0
- **Localização padrão**: `/usr/local/bin/fpdf`

## 🔄 Atualizações

Para atualizar o fpdf:
```bash
# Método 1: Reinstalar
make install

# Método 2: Apenas substituir
make install-only
```

O script automaticamente faz backup da versão anterior com timestamp.

## 🧹 Limpeza

```bash
# Limpar builds
make clean

# Remover do PATH (manual)
sudo rm /usr/local/bin/fpdf
```

## ⚡ Exemplo Completo

```bash
# Clone/navegue para o diretório
cd /path/to/fpdf

# Compile e instale
make install

# Teste
fpdf --version
fpdf --help

# Use
fpdf document.pdf load
fpdf 1 pages --word "texto"
```