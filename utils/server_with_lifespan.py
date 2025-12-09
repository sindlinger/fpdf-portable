from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import dataclass

from mcp.server.fastmcp import Context, FastMCP
import services

# --- Recurso Compartilhado (Exemplo) ---
# Imagine que isso seja um cache caro para inicializar.
class PatternCache:
    """Um cache para padrões de regex pré-compilados."""
    def __init__(self):
        self.cache = {}

    async def load(self):
        """Simula o carregamento de padrões na inicialização."""
        print("INFO:     Conectando ao cache de padrões...")
        self.cache["common_word"] = r'\b\w+\b'
        print("INFO:     Cache de padrões carregado.")

    async def close(self):
        """Simula a limpeza de recursos no desligamento."""
        print("INFO:     Desconectando do cache de padrões.")
        self.cache.clear()

    def get_pattern(self, key: str) -> str | None:
        return self.cache.get(key)

# --- Contexto da Aplicação ---
# Um objeto para manter nossos recursos inicializados de forma organizada.
@dataclass
class AppContext:
    """Contexto da aplicação com dependências tipadas."""
    pattern_cache: PatternCache

# --- Gerenciador de Ciclo de Vida (Lifespan) ---
@asynccontextmanager
async def app_lifespan(server: FastMCP) -> AsyncIterator[AppContext]:
    """Gerencia o ciclo de vida da aplicação."""
    # Código executado na inicialização do servidor
    cache = PatternCache()
    await cache.load()
    try:
        # Disponibiliza o contexto para as ferramentas
        yield AppContext(pattern_cache=cache)
    finally:
        # Código executado no desligamento do servidor
        await cache.close()

# --- Servidor MCP ---
# Passamos o `lifespan` para o servidor.
mcp = FastMCP(name="Regex Analysis Server with Lifespan", lifespan=app_lifespan)

# Registramos as ferramentas existentes
mcp.tool()(services.regex_rank)

# Nova ferramenta que USA o recurso compartilhado do lifespan
@mcp.tool()
def get_cached_pattern(ctx: Context, pattern_name: str) -> str:
    """Busca um padrão de regex pré-compilado do cache do servidor."""
    cache = ctx.request_context.lifespan_context.pattern_cache
    return cache.get_pattern(pattern_name) or "Padrão não encontrado."