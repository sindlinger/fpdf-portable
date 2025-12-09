
import re
from typing import List, Dict, Any, Optional

def _parse_template(template: str) -> Optional[Dict[str, Any]]:
    """
    Função interna para decompor o template em partes literais e de captura.
    Exemplo: "A [b] C" -> {'original_parts': ['A ', '[b]', ' C']}
    """
    parts = re.split(r'(\[.*?\])', template)
    # Um template válido deve ter pelo menos um literal e um marcador, resultando em 3 partes.
    if len(parts) < 2 or not any(p.startswith('[') for p in parts):
        return None

    return {"original_parts": parts}

def _build_regex_strategies(parsed_template: Dict[str, Any]) -> List[Dict[str, str]]:
    """
    Função interna para construir diferentes estratégias de regex a partir do template decomposto.
    """
    strategies = []
    parts = parsed_template["original_parts"]
    
    # --- Estratégia 1: Contexto Completo (a mais estrita) ---
    full_regex_parts = []
    for i, part in enumerate(parts):
        if not part: continue
        if part.startswith('[') and part.endswith(']'):
            full_regex_parts.append('(.+?)')
        else:
            full_regex_parts.append(re.escape(part.strip()))
    
    strategies.append({
        "estrategia": "Contexto Completo (usa todo o template)",
        "regex": r'\s*'.join(full_regex_parts)
    })

    # --- Estratégia 2: Ancorado no Início (ignora o último trecho literal) ---
    if not parts[-1].startswith('[') and len(parts) > 2:
        start_anchored_parts = [p for p in full_regex_parts[:-1] if p]
        strategies.append({
            "estrategia": "Ancorado no Início (ignora o final do template)",
            "regex": r'\s*'.join(start_anchored_parts)
        })

    # --- Estratégia 3: Ancorado no Fim (ignora o primeiro trecho literal) ---
    if not parts[0].startswith('[') and len(parts) > 2:
        end_anchored_parts = [p for p in full_regex_parts[1:] if p]
        strategies.append({
            "estrategia": "Ancorado no Fim (ignora o início do template)",
            "regex": r'\s*'.join(end_anchored_parts)
        })

    # --- Estratégia 4: Contexto Mínimo (apenas entre os marcadores) ---
    first_cap_idx = next((i for i, s in enumerate(parts) if s.startswith('[')), -1)
    last_cap_idx = len(parts) - 1 - next((i for i, s in enumerate(reversed(parts)) if s.startswith('[')), -1)

    if first_cap_idx != -1 and last_cap_idx > first_cap_idx:
        # Pega somente as partes entre o primeiro e o último marcador
        minimal_parts_sliced = parts[first_cap_idx : last_cap_idx + 1]
        minimal_regex_parts = []
        for part in minimal_parts_sliced:
            if part.startswith('['):
                minimal_regex_parts.append('(.+?)')
            elif part.strip():
                minimal_regex_parts.append(re.escape(part.strip()))
        
        # Só adiciona se houver algum contexto literal entre os marcadores
        if any(re.escape(p.strip()) in minimal_regex_parts for p in parts if not p.startswith('[')):
            strategies.append({
                "estrategia": "Contexto Mínimo (apenas entre os marcadores)",
                "regex": r'\s*'.join(minimal_regex_parts)
            })

    # Remove estratégias que geraram regex duplicadas
    unique_strategies = []
    seen_regexes = set()
    for s in strategies:
        if s['regex'] and s['regex'] not in seen_regexes:
            unique_strategies.append(s)
            seen_regexes.add(s['regex'])

    return unique_strategies


def analisar_template_e_rankear_regex(template_string: str, textos_de_exemplo: List[str]) -> List[Dict[str, Any]]:
    """
    Recebe um template com marcadores, gera múltiplas estratégias de regex,
    testa-as e retorna uma lista rankeada com os resultados.
    """
    if not textos_de_exemplo:
        return []

    parsed_template = _parse_template(template_string)
    if not parsed_template:
        print("ERRO: Template inválido. Deve conter pelo menos um marcador como [nome].")
        return []

    regex_candidatas = _build_regex_strategies(parsed_template)
    
    resultados_avaliados = []
    total_textos = len(textos_de_exemplo)

    for candidata in regex_candidatas:
        sucessos = 0
        for texto in textos_de_exemplo:
            if re.search(candidata["regex"], texto, re.IGNORECASE | re.DOTALL):
                sucessos += 1
        
        rating = (sucessos / total_textos) * 100
        
        resultados_avaliados.append({
            "regex": candidata["regex"],
            "estrategia": candidata["estrategia"],
            "rating_sucesso": f"{rating:.2f}%",
            "matches": f"{sucessos}/{total_textos}"
        })

    resultados_avaliados.sort(key=lambda x: float(x["rating_sucesso"][:-1]), reverse=True)
    
    return resultados_avaliados