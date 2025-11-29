# FilterPDF Installation Guide

## Quick Install

```bash
# Build and install
./compile.sh
./install.sh
```

## Build Process

**IMPORTANTE: Use apenas o `compile.sh` para compilar!**

O script `compile.sh`:
1. Limpa builds anteriores
2. Compila um executável único auto-contido
3. Cria `publish/fpdf` (~70MB, inclui todas as dependências)
4. Instala automaticamente e testa todos os comandos

## Installation Process

The `install.sh` script:
1. **Detects and removes symlinks** (prevents version conflicts)
2. **Backs up old versions** (saves as .backup)
3. **Installs to correct locations**:
   - `~/.local/bin/fpdf` (user install)
   - `/usr/local/bin/fpdf` (system-wide, optional)
4. **Verifies installation** (checks for lingering symlinks)

## Common Issues

### Still seeing old version?
You probably have a symlink pointing to an old binary.

Check with:
```bash
ls -la $(which fpdf)
```

If it shows a symlink (->), remove it and reinstall:
```bash
rm $(which fpdf)
./install.sh
```

### Multiple installations
Check all installations:
```bash
which -a fpdf
```

Remove unwanted ones and run `./install.sh`

## Manual Installation

If you prefer manual control:
```bash
# Build
dotnet publish fpdf.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/

# Install (NO SYMLINKS!)
cp publish/fpdf ~/.local/bin/fpdf
chmod +x ~/.local/bin/fpdf
```

## Version Management

Always use `./install.sh` after building to ensure:
- No symlink conflicts
- Proper version replacement
- Backup of old versions
- Verification of installation

## Development Workflow

For developers:
```bash
# Make changes
# ...

# Rebuild and reinstall
./compile.sh

# O compile.sh automaticamente:
# 1. Compila o projeto
# 2. Substitui versões antigas
# 3. Gerencia symlinks
# 4. Verifica a instalação
# 5. Testa todos os comandos
```