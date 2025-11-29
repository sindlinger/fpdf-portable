#!/usr/bin/env python3
"""
Script otimizado para processar arquivos cache JSON restantes
Converte *_cache.json para *._cache.json no formato do fpdf
"""

import os
import json
import shutil
import time
from pathlib import Path

def main():
    source_dir = Path("/home/chanfle/DE_cache/qdrant_database")
    target_dir = Path("/mnt/b/dev-2/fpdf/.cache")
    
    # Garantir que diretório target existe
    target_dir.mkdir(exist_ok=True)
    
    # Listar arquivos fonte e processados
    source_files = list(source_dir.glob("*_cache.json"))
    processed_files = set(f.stem.replace("._cache", "") for f in target_dir.glob("*._cache.json"))
    
    print(f"📊 Total arquivos fonte: {len(source_files):,}")
    print(f"✅ Já processados: {len(processed_files):,}")
    
    # Filtrar apenas arquivos não processados
    remaining_files = []
    for source_file in source_files:
        # Extrair ID base removendo "_cache"
        base_id = source_file.stem.replace("_cache", "")
        if base_id not in processed_files:
            remaining_files.append(source_file)
    
    print(f"🔄 Restantes para processar: {len(remaining_files):,}")
    
    if not remaining_files:
        print("🎉 Todos os arquivos já foram processados!")
        return
    
    # Processar arquivos restantes com progress bar
    print("\n" + "="*70)
    print("🚀 INICIANDO PROCESSAMENTO OTIMIZADO")
    print("="*70)
    
    start_time = time.time()
    success_count = 0
    error_count = 0
    
    for i, source_file in enumerate(remaining_files, 1):
        try:
            # Converter nome do arquivo: *_cache.json -> *._cache.json
            base_id = source_file.stem.replace("_cache", "")
            target_file = target_dir / f"{base_id}._cache.json"
            
            # Copiar arquivo diretamente (muito mais rápido que reprocessar)
            shutil.copy2(source_file, target_file)
            success_count += 1
            
            # Progress display otimizado (a cada 50 arquivos ou a cada 2 segundos)
            if i % 50 == 0 or (time.time() - start_time) > 2:
                elapsed = time.time() - start_time
                rate = i / max(0.1, elapsed)
                remaining_time = (len(remaining_files) - i) / max(0.1, rate)
                
                percentage = (i * 100) / len(remaining_files)
                bar_width = 50
                filled = int(bar_width * percentage / 100)
                bar = '█' * filled + '░' * (bar_width - filled)
                
                print(f"\r[{bar}] {percentage:5.1f}% ({i:,}/{len(remaining_files):,}) | "
                      f"{rate:6.1f} files/s | ETA: {remaining_time/60:4.1f}m", end='', flush=True)
        
        except Exception as e:
            error_count += 1
            print(f"\n❌ Erro processando {source_file.name}: {e}")
    
    total_time = time.time() - start_time
    
    print("\n\n" + "="*70)
    print("✅ PROCESSAMENTO CONCLUÍDO")
    print("="*70)
    print(f"📈 Arquivos processados: {success_count:,}")
    print(f"❌ Erros: {error_count:,}")
    print(f"⏱️  Tempo total: {total_time/60:.1f} minutos")
    print(f"🚀 Velocidade média: {success_count/max(0.1, total_time):.1f} arquivos/segundo")
    
    # Verificar total final
    final_count = len(list(target_dir.glob("*._cache.json")))
    print(f"📊 Total final no cache fpdf: {final_count:,} arquivos")

if __name__ == "__main__":
    main()