#!/usr/bin/env python3
"""
Consolidar todos os arquivos CSV de despachos em um único Excel
"""

import pandas as pd
import glob
import os
from datetime import datetime

def main():
    # Encontrar todos os arquivos CSV de despachos
    csv_files = glob.glob("despachos_extraction_*.csv")
    
    print(f"📊 Encontrados {len(csv_files)} arquivos CSV para consolidar")
    
    if not csv_files:
        print("❌ Nenhum arquivo CSV encontrado!")
        return
    
    # Consolidar todos os DataFrames
    all_data = []
    
    for csv_file in csv_files:
        try:
            # Ler CSV com encoding UTF-8-sig para lidar com BOM
            df = pd.read_csv(csv_file, encoding='utf-8-sig')
            all_data.append(df)
            print(f"✅ Processado: {csv_file} ({len(df)} registros)")
        except Exception as e:
            print(f"⚠️ Erro ao ler {csv_file}: {e}")
    
    if not all_data:
        print("❌ Nenhum dado foi carregado!")
        return
    
    # Combinar todos os DataFrames
    combined_df = pd.concat(all_data, ignore_index=True)
    
    # Remover duplicatas baseado em ProcessoAdmin
    combined_df = combined_df.drop_duplicates(subset=['ProcessoAdmin'], keep='first')
    
    # Converter valores monetários
    if 'ValorHonorarios' in combined_df.columns:
        combined_df['ValorHonorarios'] = pd.to_numeric(combined_df['ValorHonorarios'], errors='coerce')
    
    # Ordenar por ProcessoAdmin
    combined_df = combined_df.sort_values('ProcessoAdmin', na_position='last')
    
    # Gerar nome do arquivo Excel com timestamp
    excel_filename = f"despachos_consolidados_{datetime.now().strftime('%Y%m%d_%H%M%S')}.xlsx"
    
    # Salvar em Excel com formatação
    with pd.ExcelWriter(excel_filename, engine='openpyxl') as writer:
        combined_df.to_excel(writer, sheet_name='Despachos', index=False)
        
        # Ajustar largura das colunas
        worksheet = writer.sheets['Despachos']
        for column in combined_df:
            column_width = max(combined_df[column].astype(str).map(len).max(), len(column))
            col_idx = combined_df.columns.get_loc(column)
            worksheet.column_dimensions[chr(65 + col_idx)].width = min(column_width + 2, 50)
    
    print(f"\n📊 CONSOLIDAÇÃO COMPLETA!")
    print(f"✅ Total de despachos únicos: {len(combined_df)}")
    print(f"💰 Valor total de honorários: R$ {combined_df['ValorHonorarios'].sum():,.2f}")
    print(f"📁 Arquivo Excel gerado: {excel_filename}")
    
    # Estatísticas por comarca
    if 'Comarca' in combined_df.columns:
        print("\n📍 Despachos por Comarca:")
        comarca_stats = combined_df.groupby('Comarca').agg({
            'ProcessoAdmin': 'count',
            'ValorHonorarios': 'sum'
        }).rename(columns={'ProcessoAdmin': 'Quantidade'})
        print(comarca_stats.to_string())

if __name__ == "__main__":
    main()