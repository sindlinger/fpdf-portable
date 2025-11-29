#!/usr/bin/env python3
"""
Consolidate all despachos CSV files into a single Excel file
"""

import pandas as pd
import glob
import os
from datetime import datetime
import sys

def consolidate_csvs_to_excel():
    # Find all CSV files
    csv_files = glob.glob("despachos_extraction_*.csv")
    
    if not csv_files:
        print("❌ No CSV files found")
        return
    
    print(f"📊 Found {len(csv_files)} CSV files to consolidate")
    
    # Read and combine all CSVs
    all_data = []
    files_with_data = 0
    total_rows = 0
    
    for csv_file in sorted(csv_files):
        try:
            df = pd.read_csv(csv_file)
            if len(df) > 0:
                all_data.append(df)
                files_with_data += 1
                total_rows += len(df)
                print(f"✅ Loaded {csv_file}: {len(df)} rows")
        except Exception as e:
            print(f"⚠️ Error reading {csv_file}: {e}")
    
    if not all_data:
        print("❌ No data found in CSV files")
        return
    
    # Combine all dataframes
    combined_df = pd.concat(all_data, ignore_index=True)
    
    # Remove duplicates based on key columns
    key_columns = ['NumeroProcesso', 'NomePerito', 'CPF_CNPJ', 'ValorHonorarios', 'DataAutorizacao', 'Page', 'Arquivo']
    if all(col in combined_df.columns for col in key_columns):
        before_dedup = len(combined_df)
        combined_df = combined_df.drop_duplicates(subset=key_columns, keep='first')
        after_dedup = len(combined_df)
        if before_dedup > after_dedup:
            print(f"🔄 Removed {before_dedup - after_dedup} duplicate rows")
    
    # Sort by arquivo and page
    if 'Arquivo' in combined_df.columns and 'Page' in combined_df.columns:
        combined_df = combined_df.sort_values(['Arquivo', 'Page'])
    
    # Generate output filename
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_file = f"despachos_consolidado_{timestamp}.xlsx"
    
    # Write to Excel with formatting
    with pd.ExcelWriter(output_file, engine='openpyxl') as writer:
        combined_df.to_excel(writer, sheet_name='Despachos', index=False)
        
        # Get the workbook and worksheet
        workbook = writer.book
        worksheet = writer.sheets['Despachos']
        
        # Auto-adjust column widths
        for column in combined_df.columns:
            column_width = max(combined_df[column].astype(str).map(len).max(), len(column)) + 2
            column_width = min(column_width, 50)  # Max width of 50
            col_idx = combined_df.columns.get_loc(column) + 1
            worksheet.column_dimensions[chr(65 + col_idx - 1)].width = column_width
    
    print(f"\n✅ Excel file created: {output_file}")
    print(f"📊 Total files processed: {files_with_data}")
    print(f"📊 Total rows: {len(combined_df)}")
    
    # Show summary statistics
    if 'ValorHonorarios' in combined_df.columns:
        total_value = combined_df['ValorHonorarios'].sum()
        print(f"💰 Total value: R$ {total_value:,.2f}")
    
    if 'Confidence' in combined_df.columns:
        avg_confidence = combined_df['Confidence'].mean()
        print(f"🎯 Average confidence: {avg_confidence:.2%}")
    
    # Clean up CSV files if requested
    if len(sys.argv) > 1 and sys.argv[1] == '--clean':
        print("\n🧹 Cleaning up CSV files...")
        for csv_file in csv_files:
            try:
                os.remove(csv_file)
            except:
                pass
        print(f"✅ Removed {len(csv_files)} CSV files")

if __name__ == "__main__":
    consolidate_csvs_to_excel()