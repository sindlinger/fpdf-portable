#!/usr/bin/env python3
"""
Script RÁPIDO para extrair despachos sem usar o fpdf lento
Lê direto dos arquivos de cache JSON
"""
import json
import os
import sys
import re
import glob
from datetime import datetime

def process_cache_file(cache_file):
    """Processa um arquivo de cache JSON diretamente"""
    try:
        with open(cache_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        results = []
        
        # Procura páginas com palavra "despacho"
        for page in data.get('Pages', []):
            text = page.get('TextInfo', {}).get('PageText', '')
            
            # Verificação rápida se tem despacho
            if 'despacho' in text.lower():
                # Extração básica de dados
                result = {
                    'arquivo': os.path.basename(cache_file).replace('_cache.json', '.pdf'),
                    'pagina': page.get('PageNumber', 0),
                    'tem_despacho': True
                }
                
                # Tenta extrair alguns dados básicos (sem regex complexo)
                if 'Robson de Lima Cananéa' in text:
                    result['tipo'] = 'Despacho DIESP'
                if 'R$' in text:
                    # Extrai valor simples
                    match = re.search(r'R\$\s*([\d.,]+)', text)
                    if match:
                        result['valor'] = match.group(1)
                
                # Extrai processo simples
                match = re.search(r'(\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4})', text)
                if match:
                    result['processo'] = match.group(1)
                
                results.append(result)
        
        return results
    
    except Exception as e:
        print(f"Erro processando {cache_file}: {e}", file=sys.stderr)
        return []

def main():
    if len(sys.argv) < 2:
        print("Uso: fast_doctypes.py <start-end ou all>")
        print("Exemplo: fast_doctypes.py 1-100")
        print("Exemplo: fast_doctypes.py all")
        sys.exit(1)
    
    range_arg = sys.argv[1]
    
    # Lista todos os arquivos de cache
    cache_files = sorted(glob.glob('*_cache.json'))
    
    if range_arg == 'all':
        files_to_process = cache_files
    else:
        # Parse do range
        if '-' in range_arg:
            start, end = map(int, range_arg.split('-'))
            # Ajusta para índice 0-based
            start = max(0, start - 1)
            end = min(len(cache_files), end)
            files_to_process = cache_files[start:end]
        else:
            idx = int(range_arg) - 1
            files_to_process = [cache_files[idx]] if idx < len(cache_files) else []
    
    print(f"Processando {len(files_to_process)} arquivos...")
    
    all_results = []
    for cache_file in files_to_process:
        results = process_cache_file(cache_file)
        all_results.extend(results)
    
    # Salva resultados
    output_file = f"despachos_fast_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(all_results, f, ensure_ascii=False, indent=2)
    
    print(f"\n✅ Processados {len(files_to_process)} arquivos")
    print(f"📄 Encontrados {len(all_results)} despachos")
    print(f"💾 Resultados salvos em: {output_file}")

if __name__ == "__main__":
    main()