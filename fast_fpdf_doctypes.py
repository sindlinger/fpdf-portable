#!/usr/bin/env python3
"""
Script ULTRA-RÁPIDO para processar doctypes sem overhead do fpdf
Lê direto do arquivo de cache específico
"""
import json
import sys
import re
import time
from pathlib import Path

def process_doctypes(cache_index):
    """Processa doctypes direto do cache"""
    start = time.time()
    
    # Encontrar arquivo de cache diretamente
    cache_dir = Path('.cache')
    cache_files = sorted(cache_dir.glob('*_cache.json'))
    
    if cache_index > len(cache_files):
        print(f"Erro: índice {cache_index} não existe. Total: {len(cache_files)}")
        return
    
    cache_file = cache_files[cache_index - 1]
    print(f"Processando: {cache_file.name}")
    
    # Carregar cache
    with open(cache_file, 'r', encoding='utf-8') as f:
        data = json.load(f)
    
    # Regex compilados (como no C# otimizado)
    sei_regex = re.compile(r'SEI[:\s]*(\d{6}-\d{2}\.\d{4}\.\d\.\d{2})')
    processo_regex = re.compile(r'(\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4})')
    valor_regex = re.compile(r'valor de R\$\s*([\d.,]+)|R\$\s*([\d.,]+)\s*\([^)]*honorários', re.IGNORECASE)
    
    results = []
    
    for page in data.get('Pages', []):
        text = page.get('TextInfo', {}).get('PageText', '')
        
        # Verificação rápida
        if 'despacho' in text.lower() and 'diesp' in text.lower():
            result = {
                'page': page.get('PageNumber', 0),
                'tipo': 'Despacho DIESP'
            }
            
            # Extrações rápidas com regex compilados
            match = sei_regex.search(text)
            if match:
                result['processo_admin'] = match.group(1)
            
            match = processo_regex.search(text)
            if match:
                result['processo'] = match.group(1)
            
            match = valor_regex.search(text)
            if match:
                result['valor'] = match.group(1) if match.group(1) else match.group(2)
            
            results.append(result)
    
    # Output
    print(f"\n✅ Encontrados {len(results)} despachos em {time.time() - start:.2f} segundos")
    
    for r in results:
        print(f"\nPágina {r['page']}:")
        print(f"  Tipo: {r.get('tipo', '')}")
        print(f"  Processo Admin: {r.get('processo_admin', 'N/A')}")
        print(f"  Processo: {r.get('processo', 'N/A')}")
        print(f"  Valor: R$ {r.get('valor', 'N/A')}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Uso: fast_fpdf_doctypes.py <cache-index>")
        sys.exit(1)
    
    cache_index = int(sys.argv[1])
    process_doctypes(cache_index)