# Makefile para FilterPDF (fpdf)

.PHONY: all build install clean test help

# Variáveis
PROJECT_NAME = fpdf
BUILD_DIR = bin/Release/publish
CONFIG = Release
RUNTIME = linux-x64

# Comando padrão
all: build

# Compilar o projeto
build:
	@echo "🔨 Compilando $(PROJECT_NAME)..."
	@rm -rf bin/Release
	@dotnet clean -c $(CONFIG) > /dev/null 2>&1 || true
	@dotnet publish $(PROJECT_NAME).csproj \
		-c $(CONFIG) \
		-r $(RUNTIME) \
		--self-contained true \
		-p:PublishSingleFile=true \
		-p:PublishTrimmed=false \
		-p:DebugType=None \
		-p:DebugSymbols=false \
		-o $(BUILD_DIR)
	@chmod +x $(BUILD_DIR)/$(PROJECT_NAME)
	@echo "✅ Compilação concluída!"
	@echo "Executável: $$(pwd)/$(BUILD_DIR)/$(PROJECT_NAME)"
	@du -h $(BUILD_DIR)/$(PROJECT_NAME) | cut -f1 | xargs echo "Tamanho:"

# Compilar e instalar no PATH
install: build
	@echo "📦 Instalando $(PROJECT_NAME)..."
	@./build-and-install.sh

# Apenas instalar (assumindo que já foi compilado)
install-only:
	@if [ ! -f "$(BUILD_DIR)/$(PROJECT_NAME)" ]; then \
		echo "❌ Erro: Execute 'make build' primeiro"; \
		exit 1; \
	fi
	@CURRENT_FPDF=$$(which $(PROJECT_NAME) 2>/dev/null || true); \
	if [ -n "$$CURRENT_FPDF" ]; then \
		echo "🔄 Substituindo $$CURRENT_FPDF"; \
		sudo cp $(BUILD_DIR)/$(PROJECT_NAME) "$$CURRENT_FPDF"; \
	else \
		echo "📦 Instalando em /usr/local/bin"; \
		sudo cp $(BUILD_DIR)/$(PROJECT_NAME) /usr/local/bin/; \
	fi
	@echo "✅ $(PROJECT_NAME) instalado com sucesso!"

# Limpar arquivos de build
clean:
	@echo "🧹 Limpando arquivos de build..."
	@rm -rf bin obj
	@dotnet clean > /dev/null 2>&1 || true
	@echo "✅ Limpeza concluída!"

# Executar testes
test: build
	@echo "🧪 Executando testes..."
	@dotnet test --no-build --verbosity quiet
	@echo "✅ Testes concluídos!"

# Compilação de desenvolvimento (debug)
debug:
	@echo "🔧 Compilando versão de debug..."
	@dotnet build -c Debug
	@echo "✅ Debug build concluído!"

# Mostrar informações do projeto
info:
	@echo "📊 Informações do projeto:"
	@echo "Nome: $(PROJECT_NAME)"
	@echo "Runtime: $(RUNTIME)"
	@echo "Configuração: $(CONFIG)"
	@echo "Diretório de build: $(BUILD_DIR)"
	@if [ -f "$(BUILD_DIR)/$(PROJECT_NAME)" ]; then \
		echo "Status: Compilado"; \
		du -h $(BUILD_DIR)/$(PROJECT_NAME) | cut -f1 | xargs echo "Tamanho:"; \
	else \
		echo "Status: Não compilado"; \
	fi
	@INSTALLED_FPDF=$$(which $(PROJECT_NAME) 2>/dev/null || echo "Não instalado"); \
	echo "Instalado em: $$INSTALLED_FPDF"

# Executar o programa localmente (sem instalar)
run: build
	@./$(BUILD_DIR)/$(PROJECT_NAME) $(ARGS)

# Mostrar versão instalada
version:
	@$(PROJECT_NAME) --version 2>/dev/null || echo "❌ fpdf não está instalado ou não está no PATH"

# Desinstalar fpdf
uninstall:
	@./uninstall.sh

# Mostrar ajuda
help:
	@echo "Comandos disponíveis:"
	@echo "  make build        - Compilar o projeto"
	@echo "  make install      - Compilar e instalar no PATH"
	@echo "  make install-only - Instalar sem recompilar"
	@echo "  make clean        - Limpar arquivos de build"
	@echo "  make test         - Executar testes"
	@echo "  make debug        - Compilar versão debug"
	@echo "  make run ARGS='...' - Executar localmente"
	@echo "  make info         - Mostrar informações do projeto"
	@echo "  make version      - Mostrar versão instalada"
	@echo "  make uninstall    - Desinstalar fpdf do sistema"
	@echo "  make help         - Mostrar esta ajuda"