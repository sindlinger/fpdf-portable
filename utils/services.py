import re
from collections import Counter
from typing import List, Dict, Any, Tuple, Union

# --- Service Class for Word-Based Regex Analysis ---

class RegexAnalyzerService:
    """
    Encapsulates the logic for analyzing a target word in various text contexts
    to generate and rank different regex strategies.
    """
    def __init__(self, palavra_alvo: str, textos_de_exemplo: List[str]):
        if not palavra_alvo or not textos_de_exemplo:
            raise ValueError("Palavra-alvo e textos de exemplo não podem ser vazios.")
        self.palavra_alvo = palavra_alvo
        self.textos_de_exemplo = textos_de_exemplo
        self.total_textos = len(textos_de_exemplo)
        self._ranked_results = None # Cache for results

    def _get_ranked_results(self) -> List[Dict[str, Any]]:
        """Caches and returns the ranked analysis results."""
        if self._ranked_results is None:
            self._ranked_results = self.analyze_and_rank()
        return self._ranked_results

    def _find_longest_common_prefix(self, strings: List[str]) -> str:
        """Finds the longest common prefix from a list of strings."""
        if not strings:
            return ""
        shortest_str = min(strings, key=len)
        for i, char in enumerate(shortest_str):
            for other_str in strings:
                if other_str[i] != char:
                    return shortest_str[:i]
        return shortest_str

    def _find_longest_common_suffix(self, strings: List[str]) -> str:
        """Finds the longest common suffix from a list of strings."""
        if not strings:
            return ""
        reversed_strings = [s[::-1] for s in strings]
        lcp = self._find_longest_common_prefix(reversed_strings)
        return lcp[::-1]

    def _find_all_contexts(self, context_size: int = 30) -> List[Tuple[str, str]]:
        """Finds all occurrences of the keyword and extracts their context."""
        contexts = []
        finder = re.compile(re.escape(self.palavra_alvo), re.IGNORECASE)
        for text in self.textos_de_exemplo:
            for match in finder.finditer(text):
                start, end = match.span()
                prefix = text[max(0, start - context_size):start]
                suffix = text[end:min(len(text), end + context_size)]
                contexts.append((prefix, suffix))
        return contexts

    def _generate_candidate_strategies(self) -> List[Dict[str, str]]:
        """Internal method to generate regex candidates based on context."""
        palavra_escapada = re.escape(self.palavra_alvo)
        candidatas = []

        # Strategy 1: Simple Word Boundary
        candidatas.append({
            "estrategia": "Borda de Palavra Simples (ignora maiúsculas/minúsculas)",
            "regex": r'\b' + palavra_escapada + r'\b',
            "flags": re.IGNORECASE
        })

        # --- NEW: Add more generic strategies ---
        candidatas.append({
            "estrategia": "Correspondência Exata (sensível a maiúsculas/minúsculas)",
            "regex": r'\b' + palavra_escapada + r'\b',
            "flags": 0
        })

        prefixos, sufixos = self._find_neighboring_chars()

        # Strategy 2: Most Common Prefix
        if prefixos:
            prefixo_comum = Counter(prefixos).most_common(1)[0][0]
            candidatas.append({
                "estrategia": f"Precedido pelo caractere mais comum ('{prefixo_comum}')",
                "regex": re.escape(prefixo_comum) + r'\s*' + palavra_escapada + r'\b',
                "flags": re.IGNORECASE
            })

        # Strategy 3: Most Common Suffix
        if sufixos:
            sufixo_comum = Counter(sufixos).most_common(1)[0][0]
            candidatas.append({
                "estrategia": f"Seguido pelo caractere mais comum ('{sufixo_comum}')",
                "regex": r'\b' + palavra_escapada + r'\s*' + re.escape(sufixo_comum),
                "flags": re.IGNORECASE
            })

        # Strategy 4: Surrounded by Most Common Chars
        if prefixos and sufixos:
            prefixo_comum = Counter(prefixos).most_common(1)[0][0]
            sufixo_comum = Counter(sufixos).most_common(1)[0][0]
            candidatas.append({
                "estrategia": f"Cercado pelos caracteres mais comuns ('{prefixo_comum}' e '{sufixo_comum}')",
                "regex": re.escape(prefixo_comum) + r'\s*' + palavra_escapada + r'\s*' + re.escape(sufixo_comum),
                "flags": re.IGNORECASE
            })

        # --- NEW: Add the intelligent, context-aware strategy ---
        contexts = self._find_all_contexts()
        if contexts:
            prefixes = [ctx[0] for ctx in contexts]
            suffixes = [ctx[1] for ctx in contexts]
            common_prefix = self._find_longest_common_prefix(prefixes).strip()
            common_suffix = self._find_longest_common_suffix(suffixes).strip()

            if common_prefix or common_suffix:
                parts = [re.escape(p) for p in [common_prefix, self.palavra_alvo, common_suffix] if p]
                optimal_regex = r'\s*'.join(parts)
                candidatas.append({
                    "estrategia": "Contexto Otimizado (aprendido dos exemplos)",
                    "regex": optimal_regex,
                    "flags": re.IGNORECASE
                })
            
        return candidatas

    def _find_neighboring_chars(self) -> tuple[List[str], List[str]]:
        """Internal method to find characters immediately surrounding the target word."""
        prefixos, sufixos = [], []
        # Captures: (1: prefix), (2: word), (3: suffix)
        regex_contexto = re.compile(r'(\S)?\s*(' + re.escape(self.palavra_alvo) + r')\s*(\S)?', re.IGNORECASE)
        for texto in self.textos_de_exemplo:
            for match in regex_contexto.finditer(texto):
                if match.group(2).lower() == self.palavra_alvo.lower():
                    if match.group(1): prefixos.append(match.group(1))
                    if match.group(3): sufixos.append(match.group(3))
        return prefixos, sufixos

    def analyze_and_rank(self) -> List[Dict[str, Any]]:
        """Executes the full analysis and returns a ranked list of regex strategies."""
        regex_candidatas = self._generate_candidate_strategies()
        
        resultados_avaliados = []
        for candidata in regex_candidatas:
            flags = candidata.get("flags", re.IGNORECASE)
            try:
                sucessos = sum(1 for texto in self.textos_de_exemplo if re.search(candidata["regex"], texto, flags))
                rating = (sucessos / self.total_textos) * 100
                resultados_avaliados.append({
                    "regex": candidata["regex"],
                    "estrategia": candidata["estrategia"],
                    "rating_sucesso": f"{rating:.2f}%",
                    "matches": f"{sucessos}/{self.total_textos}"
                })
            except re.error:
                continue

        resultados_avaliados.sort(key=lambda x: float(x["rating_sucesso"][:-1]), reverse=True)
        return resultados_avaliados

    def get_best_regex(self) -> str:
        """Selects the best regex from the ranked results."""
        ranked_results = self._get_ranked_results()
        if not ranked_results:
            return r'\b' + re.escape(self.palavra_alvo) + r'\b'
        # The list is already sorted by rating. The first one is the best.
        return ranked_results[0]['regex']

# --- Service Class for Template-Based Regex Analysis ---

class TemplateAnalyzerService:
    """
    Encapsulates the logic for analyzing a template to generate and rank
    different regex strategies for context-based extraction.
    """
    def __init__(self, template_string: str, textos_de_exemplo: List[str]):
        if not template_string or not textos_de_exemplo:
            raise ValueError("Template e textos de exemplo não podem ser vazios.")
        self.template_string = template_string
        self.textos_de_exemplo = textos_de_exemplo
        self.total_textos = len(textos_de_exemplo)
        self._ranked_results = None # Cache for results

    def _get_ranked_results(self) -> List[Dict[str, Any]]:
        """Caches and returns the ranked analysis results."""
        if self._ranked_results is None:
            self._ranked_results = self.analyze_and_rank()
        return self._ranked_results

    def _build_regex_strategies(self) -> List[Dict[str, str]]:
        """Internal method to build different regex strategies from the template."""
        parts = re.split(r'(\[.*?\])', self.template_string)
        if len(parts) < 2 or not any(p.startswith('[') for p in parts):
            raise ValueError("Template inválido. Deve conter pelo menos um marcador como [nome].")

        strategies = []
        full_regex_parts = []
        for part in parts:
            if not part: continue
            full_regex_parts.append('(.+?)' if part.startswith('[') else re.escape(part.strip()))

        # Strategy 1: Full Context
        strategies.append({"estrategia": "Contexto Completo", "regex": r'\s*'.join(filter(None, full_regex_parts))})
        # Strategy 2: Start-Anchored
        if not parts[-1].startswith('[') and len(parts) > 2:
            strategies.append({"estrategia": "Ancorado no Início", "regex": r'\s*'.join(filter(None, full_regex_parts[:-1]))})
        # Strategy 3: End-Anchored
        if not parts[0].startswith('[') and len(parts) > 2:
            strategies.append({"estrategia": "Ancorado no Fim", "regex": r'\s*'.join(filter(None, full_regex_parts[1:]))})

        # Strategy 4: Minimal Context (between first and last placeholder)
        first_cap_idx = next((i for i, s in enumerate(parts) if s.startswith('[')), -1)
        last_cap_idx = len(parts) - 1 - next((i for i, s in enumerate(reversed(parts)) if s.startswith('[')), -1)

        if first_cap_idx != -1 and last_cap_idx > first_cap_idx:
            minimal_parts_sliced = parts[first_cap_idx : last_cap_idx + 1]
            minimal_regex_parts = []
            has_literal_context = False
            for part in minimal_parts_sliced:
                if part.startswith('['):
                    minimal_regex_parts.append('(.+?)')
                elif part.strip():
                    minimal_regex_parts.append(re.escape(part.strip()))
                    has_literal_context = True
            
            # Only add this strategy if there is some literal text between the placeholders
            if has_literal_context:
                strategies.append({"estrategia": "Contexto Mínimo (entre marcadores)", "regex": r'\s*'.join(minimal_regex_parts)})
        
        unique_strategies = {s['regex']: s for s in strategies if s['regex']}
        return list(unique_strategies.values())

    def analyze_and_rank(self) -> List[Dict[str, Any]]:
        """Executes the full analysis and returns a ranked list of regex strategies."""
        regex_candidatas = self._build_regex_strategies()
        resultados_avaliados = []
        for candidata in regex_candidatas:
            sucessos = sum(1 for texto in self.textos_de_exemplo if re.search(candidata["regex"], texto, re.IGNORECASE | re.DOTALL))
            rating = (sucessos / self.total_textos) * 100
            resultados_avaliados.append({
                "regex": candidata["regex"],
                "estrategia": candidata["estrategia"],
                "rating_sucesso": f"{rating:.2f}%",
                "matches": f"{sucessos}/{self.total_textos}"
            })
        resultados_avaliados.sort(key=lambda x: float(x["rating_sucesso"][:-1]), reverse=True)
        return resultados_avaliados

    def get_best_regex(self) -> str:
        """Selects the best regex from the ranked results."""
        ranked_results = self._get_ranked_results()
        if not ranked_results:
            return ""
        return ranked_results[0]['regex']

# --- Public Facade Functions (API for the Server) ---

def regex_words(palavra_ou_palavras: Union[str, List[str]], textos: List[str]) -> Union[str, Dict[str, str]]:
    """
    Analisa uma ou mais palavras em múltiplos textos e retorna a(s) melhor(es) regex.
    """
    if isinstance(palavra_ou_palavras, str):
        try:
            service = RegexAnalyzerService(palavra_ou_palavras, textos)
            return service.get_best_regex()
        except ValueError:
            return r'\b' + re.escape(palavra_ou_palavras) + r'\b'
    elif isinstance(palavra_ou_palavras, list):
        if not textos:
            return {p: r'\b' + re.escape(p) + r'\b' for p in palavra_ou_palavras}
        results = {}
        for palavra in palavra_ou_palavras:
            # Re-usa a lógica da chamada única
            results[palavra] = regex_words(palavra, textos)
        return results
    raise TypeError("Input para 'palavra_ou_palavras' deve ser uma string ou uma lista de strings.")

def regex_context(template_ou_templates: Union[str, List[str]], textos: List[str]) -> Union[str, Dict[str, str]]:
    """
    Analisa um ou mais templates em múltiplos textos e retorna a(s) melhor(es) regex.
    """
    if isinstance(template_ou_templates, str):
        try:
            service = TemplateAnalyzerService(template_ou_templates, textos)
            return service.get_best_regex()
        except ValueError:
            return ""
    elif isinstance(template_ou_templates, list):
        if not textos:
            return {t: "" for t in template_ou_templates}
        results = {}
        for template in template_ou_templates:
            results[template] = regex_context(template, textos)
        return results
    raise TypeError("Input para 'template_ou_templates' deve ser uma string ou uma lista de strings.")

def regex_rank(item_ou_itens: Union[str, List[str]], textos: List[str]) -> Union[List[Dict[str, Any]], Dict[str, List[Dict[str, Any]]]]:
    """
    Analisa um ou mais itens (palavra ou template) e retorna o(s) ranking(s) de estratégias.
    """
    def _get_rank_for_single_item(item: str) -> List[Dict[str, Any]]:
        try:
            if '[' in item and ']' in item:
                service = TemplateAnalyzerService(item, textos)
            else:
                service = RegexAnalyzerService(item, textos)
            return service.analyze_and_rank()
        except ValueError as e:
            return [{"error": str(e)}]

    if isinstance(item_ou_itens, str):
        return _get_rank_for_single_item(item_ou_itens)
    elif isinstance(item_ou_itens, list):
        if not textos:
            return {item: [] for item in item_ou_itens}
        results = {}
        for item in item_ou_itens:
            results[item] = _get_rank_for_single_item(item)
        return results
    raise TypeError("Input para 'item_ou_itens' deve ser uma string ou uma lista de strings.")