#!/usr/bin/env python3
"""
Script para executar doctypes corretamente sem o bug do --type obrigatório
"""
import subprocess
import sys
import json

def execute_doctypes(start_idx, end_idx, doc_type="despacho2025", format_type="json"):
    """Executa o comando doctypes para um range de arquivos"""
    
    # Para cada arquivo no range, executar individualmente com --type
    all_results = []
    
    for idx in range(start_idx, end_idx + 1):
        cmd = [
            "./publish/fpdf",
            str(idx),
            "doctypes",
            "--type", doc_type,
            "--format", format_type
        ]
        
        try:
            print(f"Processando arquivo {idx}...", file=sys.stderr)
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                timeout=30,
                cwd="/mnt/b/dev-2/fpdf"
            )
            
            if result.returncode == 0:
                # Filtrar apenas as linhas relevantes (não o lixo de INFO e DEBUG)
                lines = result.stdout.split('\n')
                for line in lines:
                    # Pular linhas de info/debug
                    if line.startswith('[INFO]') or line.startswith('🔧 DEBUG:'):
                        continue
                    # Pular linhas vazias
                    if not line.strip():
                        continue
                    # Imprimir linhas relevantes
                    if 'Type:' in line or 'Score:' in line or 'PAGE' in line or 'Confidence:' in line:
                        print(line)
                    elif 'ProcessoAdministrativo:' in line or 'NumeroProcesso:' in line:
                        print(line)
                    elif 'NomePerito:' in line or 'CPFPerito:' in line:
                        print(line)
                    elif 'ValorHonorarios:' in line or 'Comarca:' in line:
                        print(line)
                    elif '✅' in line or '⚠️' in line:
                        print(line)
                    elif 'arquivo' in line or 'Found' in line:
                        print(line)
            else:
                print(f"Erro processando arquivo {idx}: {result.stderr}", file=sys.stderr)
                
        except subprocess.TimeoutExpired:
            print(f"Timeout processando arquivo {idx}", file=sys.stderr)
        except Exception as e:
            print(f"Erro: {e}", file=sys.stderr)
    
    print(f"\n✅ Processados {end_idx - start_idx + 1} arquivos")

def main():
    if len(sys.argv) < 2:
        print("Uso: fix_doctypes.py <range> [tipo] [formato]")
        print("Exemplo: fix_doctypes.py 1-100")
        print("Exemplo: fix_doctypes.py 1-100 despacho2025 json")
        sys.exit(1)
    
    # Parse do range
    range_str = sys.argv[1]
    if '-' in range_str:
        start, end = map(int, range_str.split('-'))
    else:
        start = end = int(range_str)
    
    # Tipo de documento (padrão: despacho2025)
    doc_type = sys.argv[2] if len(sys.argv) > 2 else "despacho2025"
    
    # Formato de saída (padrão: json)
    format_type = sys.argv[3] if len(sys.argv) > 3 else "json"
    
    print(f"Processando arquivos {start} até {end}...")
    print(f"Tipo: {doc_type}, Formato: {format_type}")
    print("=" * 70)
    
    execute_doctypes(start, end, doc_type, format_type)

if __name__ == "__main__":
    main()