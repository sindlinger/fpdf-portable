#!/usr/bin/env python3
"""
Script para consolidar TODOS os despachos extraídos com os 3 padrões diferentes
"""

import pandas as pd
import glob
import os
from datetime import datetime

def consolidate_all_despachos():
    print("🔍 CONSOLIDANDO TODOS OS DESPACHOS EXTRAÍDOS")
    print("=" * 60)
    
    # Encontrar todos os arquivos XLSX de despachos
    xlsx_files = glob.glob("despachos_extraction_*.xlsx")
    
    if not xlsx_files:
        print("❌ Nenhum arquivo XLSX encontrado!")
        return
    
    print(f"📊 Encontrados {len(xlsx_files)} arquivos XLSX para consolidar")
    
    all_data = []
    total_despachos = 0
    
    # Processar cada arquivo
    for i, file in enumerate(xlsx_files, 1):
        try:
            df = pd.read_excel(file)
            if len(df) > 0:
                # Adicionar coluna indicando o tipo de busca usado
                if 'Confidence' in df.columns:
                    # Inferir tipo de busca baseado no timestamp do arquivo
                    if '213331' <= file.split('_')[2] <= '213400':
                        df['TipoBusca'] = 'despacho2024'
                    elif '213352' <= file.split('_')[2] <= '213430':
                        df['TipoBusca'] = 'despacho2023'
                    else:
                        df['TipoBusca'] = 'despacho2025'
                
                all_data.append(df)
                total_despachos += len(df)
                
            if i % 100 == 0:
                print(f"  ✅ Processados {i}/{len(xlsx_files)} arquivos...")
                
        except Exception as e:
            print(f"  ❌ Erro ao processar {file}: {e}")
    
    if not all_data:
        print("❌ Nenhum dado válido encontrado!")
        return
    
    # Consolidar todos os dados
    print("\n📋 Consolidando dados...")
    final_df = pd.concat(all_data, ignore_index=True)
    
    # Remover duplicatas (mesmo processo pode aparecer em múltiplas buscas)
    print("🔄 Removendo duplicatas...")
    initial_count = len(final_df)
    
    # Remover duplicatas baseado em ProcessoAdmin + NomePerito + ValorHonorarios
    final_df = final_df.drop_duplicates(
        subset=['ProcessoAdmin', 'NomePerito', 'ValorHonorarios'], 
        keep='first'
    )
    
    duplicates_removed = initial_count - len(final_df)
    print(f"  ✅ Removidas {duplicates_removed} duplicatas")
    
    # Calcular estatísticas
    print("\n📊 ESTATÍSTICAS FINAIS:")
    print("-" * 40)
    
    # Por tipo de busca
    if 'TipoBusca' in final_df.columns:
        busca_stats = final_df['TipoBusca'].value_counts()
        for busca, count in busca_stats.items():
            print(f"  • {busca}: {count} despachos únicos")
    
    # Por ano do processo
    processos_2022 = len(final_df[final_df['ProcessoAdmin'].str.contains('2022', na=False)])
    processos_2023 = len(final_df[final_df['ProcessoAdmin'].str.contains('2023', na=False)])
    processos_2024 = len(final_df[final_df['ProcessoAdmin'].str.contains('2024', na=False)])
    processos_2025 = len(final_df[final_df['ProcessoAdmin'].str.contains('2025', na=False)])
    
    print(f"\n📅 POR ANO DO PROCESSO:")
    print(f"  • 2022: {processos_2022} despachos")
    print(f"  • 2023: {processos_2023} despachos")
    print(f"  • 2024: {processos_2024} despachos")
    print(f"  • 2025: {processos_2025} despachos")
    
    # Calcular valores
    try:
        valores_limpos = final_df['ValorHonorarios'].replace('[R\$ ]', '', regex=True).replace(',', '.', regex=True)
        valores_num = pd.to_numeric(valores_limpos, errors='coerce').fillna(0)
        valor_total = valores_num.sum()
        
        print(f"\n💰 VALOR TOTAL: R$ {valor_total:,.2f}")
    except Exception as e:
        print(f"❌ Erro ao calcular valores: {e}")
    
    # Salvar arquivo consolidado final
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_file = f"despachos_consolidado_COMPLETO_{timestamp}.xlsx"
    
    try:
        final_df.to_excel(output_file, index=False)
        file_size = os.path.getsize(output_file) / 1024  # KB
        
        print(f"\n✅ ARQUIVO FINAL CRIADO:")
        print(f"  📁 Nome: {output_file}")
        print(f"  📊 Tamanho: {file_size:.1f} KB")
        print(f"  📋 Total de despachos únicos: {len(final_df)}")
        
        return output_file
        
    except Exception as e:
        print(f"❌ Erro ao salvar arquivo final: {e}")
        return None

if __name__ == "__main__":
    result = consolidate_all_despachos()
    if result:
        print(f"\n🎉 CONSOLIDAÇÃO COMPLETA!")
        print(f"Arquivo: {result}")
    else:
        print("\n❌ Falha na consolidação!")