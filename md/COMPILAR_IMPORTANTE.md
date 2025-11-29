# ⚠️ IMPORTANTE: INSTRUÇÕES DE COMPILAÇÃO

## 🚨 USE APENAS O compile.sh PARA COMPILAR!

### ✅ FORMA CORRETA:
```bash
./compile.sh
```

### ❌ NÃO USE:
- ~~build.sh~~ (removido - tinha erro de sintaxe)
- ~~dotnet build~~ (não instala corretamente)
- ~~dotnet publish~~ (não configura o PATH)

## 📋 O que o compile.sh faz:

1. **Limpa** artefatos antigos
2. **Compila** o projeto com todas as otimizações
3. **Instala** no PATH do usuário (~/.local/bin)
4. **Faz backup** de versões anteriores
5. **Testa** todos os comandos automaticamente
6. **Verifica** a instalação

## 🎯 Benefícios do compile.sh:

- ✅ Compilação otimizada
- ✅ Instalação automática
- ✅ Gerenciamento de versões
- ✅ Testes automáticos
- ✅ Configuração do PATH
- ✅ Backup automático

## 📊 Exemplo de uso:

```bash
# Navegar para o diretório
cd /mnt/b/dev-2/fpdf

# Compilar e instalar
./compile.sh

# Após compilar, executar:
hash -r

# Testar
fpdf --version
```

## ⚡ Após modificações no código:

Sempre que modificar o código, compile com:
```bash
./compile.sh
```

Isso garante que todas as mudanças sejam aplicadas corretamente!

---

**Autor:** Sistema de Build FilterPDF
**Versão:** 3.22.0
**Data:** 2024