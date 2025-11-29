#!/usr/bin/env python3
"""
Script para extrair todos os despachos de documentos de 2024 
usando fpdf com cache funcionando
"""
import subprocess
import json
import re
from datetime import datetime
import sys
import os

def execute_fpdf_command(start_idx, end_idx):
    """Execute fpdf command for a range of cache indices"""
    cmd = [
        "./bin/fpdf", 
        f"{start_idx}-{end_idx}", 
        "pages", 
        "--word", "despacho",
        "--format", "json"
    ]
    
    try:
        print(f"Executando: {' '.join(cmd)}")
        result = subprocess.run(
            cmd, 
            capture_output=True, 
            text=True, 
            timeout=300,  # 5 minutos timeout
            cwd="/mnt/b/dev-2/fpdf"
        )
        
        if result.returncode == 0:
            return result.stdout
        else:
            print(f"Erro no comando: {result.stderr}")
            return None
            
    except subprocess.TimeoutExpired:
        print(f"Timeout executando range {start_idx}-{end_idx}")
        return None
    except Exception as e:
        print(f"Erro executando comando: {e}")
        return None

def parse_fpdf_output(output_text):
    """Parse the fpdf JSON output to extract despacho information"""
    results = []
    
    # Split output by JSON objects (each starts with "{")
    json_objects = []
    current_json = ""
    brace_count = 0
    in_json = False
    
    for line in output_text.split('\n'):
        line = line.strip()
        if not line:
            continue
            
        # Skip info lines
        if line.startswith('[INFO]') or line.startswith('Finding PAGES') or line.startswith('Active filters') or line.startswith('🔍') or line.startswith('====='):
            continue
            
        # Check if this is start of JSON
        if line.startswith('{') and not in_json:
            in_json = True
            current_json = line
            brace_count = line.count('{') - line.count('}')
        elif in_json:
            current_json += '\n' + line
            brace_count += line.count('{') - line.count('}')
            
            if brace_count == 0:
                # Complete JSON object
                json_objects.append(current_json)
                current_json = ""
                in_json = False
    
    # Parse each JSON object
    for json_text in json_objects:
        try:
            data = json.loads(json_text)
            if 'arquivo' in data and 'paginas' in data:
                # Extract despacho information from each page
                for page in data['paginas']:
                    despacho_info = {
                        'arquivo': data['arquivo'],
                        'pagina': page['pageNumber'],
                        'conteudo': page['content'],
                        'palavra_encontrada': page.get('searchedWords', 'despacho')
                    }
                    results.append(despacho_info)
                    
        except json.JSONDecodeError as e:
            print(f"Erro decodificando JSON: {e}")
            print(f"JSON problemático: {json_text[:200]}...")
            continue
    
    return results

def main():
    """Main function to extract all despachos from 2024 files"""
    print("=== Extração de Despachos 2024 ===")
    print(f"Iniciado em: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    
    # Process in batches of 20 to avoid timeouts
    batch_size = 20
    start_idx = 2  # First 2024 file based on cache list
    end_idx = 654   # Last file based on cache list
    
    all_despachos = []
    total_batches = (end_idx - start_idx + batch_size) // batch_size
    current_batch = 0
    
    for batch_start in range(start_idx, end_idx + 1, batch_size):
        current_batch += 1
        batch_end = min(batch_start + batch_size - 1, end_idx)
        
        print(f"\n--- Lote {current_batch}/{total_batches}: IDs {batch_start}-{batch_end} ---")
        
        output = execute_fpdf_command(batch_start, batch_end)
        if output:
            batch_results = parse_fpdf_output(output)
            all_despachos.extend(batch_results)
            print(f"Encontrados {len(batch_results)} despachos neste lote")
        else:
            print(f"Falha no lote {current_batch}")
    
    # Save results
    timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
    results_file = f"despachos_2024_{timestamp}.json"
    
    final_results = {
        'timestamp': timestamp,
        'total_despachos': len(all_despachos),
        'total_arquivos_processados': len(set(d['arquivo'] for d in all_despachos)),
        'despachos': all_despachos
    }
    
    with open(results_file, 'w', encoding='utf-8') as f:
        json.dump(final_results, f, ensure_ascii=False, indent=2)
    
    print(f"\n=== RESUMO ===")
    print(f"Total de despachos encontrados: {len(all_despachos)}")
    print(f"Total de arquivos com despachos: {len(set(d['arquivo'] for d in all_despachos))}")
    print(f"Resultados salvos em: {results_file}")
    print(f"Concluído em: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")

if __name__ == "__main__":
    main()