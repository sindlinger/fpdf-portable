from mcp.server.fastmcp import FastMCP
from typing import List, Dict, Any, Union
import services

# 1. Crie a instância do servidor MCP.
# Ele cuidará de toda a comunicação e protocolo.
mcp = FastMCP(name="Regex Analysis Server")

# 2. Registre as funções unificadas como "Ferramentas".
# O FastMCP é inteligente o suficiente para entender `Union[str, List[str]]`
# e criar um esquema que aceita um único item ou uma lista.

@mcp.tool()
def regex_words(palavra_ou_palavras: Union[str, List[str]], textos: List[str]) -> Union[str, Dict[str, str]]:
    """Analisa uma ou mais palavras em múltiplos textos e retorna a(s) melhor(es) regex."""
    return services.regex_words(palavra_ou_palavras, textos)

@mcp.tool()
def regex_context(template_ou_templates: Union[str, List[str]], textos: List[str]) -> Union[str, Dict[str, str]]:
    """Analisa um ou mais templates em múltiplos textos e retorna a(s) melhor(es) regex."""
    return services.regex_context(template_ou_templates, textos)

@mcp.tool()
def regex_rank(item_ou_itens: Union[str, List[str]], textos: List[str]) -> Union[List[Dict[str, Any]], Dict[str, List[Dict[str, Any]]]]:
    """Analisa um ou mais itens (palavra ou template) e retorna o(s) ranking(s) de estratégias."""
    return services.regex_rank(item_ou_itens, textos)

# O objeto 'mcp' agora lida automaticamente com os endpoints '/' e '/execute'.
# Para iniciar o servidor, use o comando: uv run mcp dev server:mcp