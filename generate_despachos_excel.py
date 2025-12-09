#!/usr/bin/env python3
"""
Script para gerar arquivo Excel a partir dos resultados JSON dos despachos
"""
import json
import pandas as pd
from datetime import datetime
import re
import sys
import os

def extract_key_info(content):
    """Extract key information from despacho content"""
    info = {
        'processo_numero': '',
        'perito_nome': '',
        'cpf_perito': '',
        'valor_honorarios': '',
        'natureza_acao': '',
        'unidade_judiciaria': ''
    }
    
    # Extract process number
    processo_match = re.search(r'(?:Processo|processo).*?(?:nº|N°|Nº)[\s]*([0-9\-\.]+)', content, re.IGNORECASE)
    if processo_match:
        info['processo_numero'] = processo_match.group(1).strip()
    
    # Extract perito name
    perito_matches = [
        r'Senhor\(a\)\s+([A-Z\s]+),\s+aceitou',
        r'nomeado:\s*([A-Z\s]+)\s*(?:–|-).*?CPF',
        r'Interessado:\s*([A-Z\s]+)\s*(?:–|-).*?Perito'
    ]
    for pattern in perito_matches:
        match = re.search(pattern, content, re.IGNORECASE)
        if match:
            info['perito_nome'] = match.group(1).strip()
            break
    
    # Extract CPF
    cpf_match = re.search(r'CPF[\s]*([0-9\.\-/]+)', content)
    if cpf_match:
        info['cpf_perito'] = cpf_match.group(1).strip()
    
    # Extract honorários value
    valor_matches = [
        r'R\$\s*([0-9\.,]+)',
        r'arbitrado.*?R\$\s*([0-9\.,]+)',
        r'Valor:\s*R\$\s*([0-9\.,]+)'
    ]
    for pattern in valor_matches:
        match = re.search(pattern, content)
        if match:
            info['valor_honorarios'] = match.group(1).strip()
            break
    
    # Extract natureza da ação
    natureza_match = re.search(r'Natureza da ação:\s*([^0-9\n]+)', content, re.IGNORECASE)
    if natureza_match:
        info['natureza_acao'] = natureza_match.group(1).strip()
    
    # Extract unidade judiciária
    unidade_matches = [
        r'Unidade judiciária.*?:\s*([^0-9\n]+)',
        r'perante o\s*([^,\n]+)',
        r'JUÍZO.*?([^,\n]+)'
    ]
    for pattern in unidade_matches:
        match = re.search(pattern, content, re.IGNORECASE)
        if match:
            info['unidade_judiciaria'] = match.group(1).strip()
            break
    
    return info

def load_despachos_json(filename):
    """Load despachos from JSON file"""
    try:
        with open(filename, 'r', encoding='utf-8') as f:
            data = json.load(f)
        return data['despachos']
    except Exception as e:
        print(f"Erro ao carregar arquivo JSON: {e}")
        return []

def generate_excel(despachos_list, output_filename):
    """Generate Excel file from despachos list"""
    
    # Prepare data for DataFrame
    excel_data = []
    
    for idx, despacho in enumerate(despachos_list, 1):
        # Extract key information
        key_info = extract_key_info(despacho['conteudo'])
        
        # Prepare row data
        row = {
            'ID': idx,
            'Arquivo': despacho['arquivo'],
            'Página': despacho['pagina'],
            'Processo Número': key_info['processo_numero'],
            'Nome do Perito': key_info['perito_nome'],
            'CPF Perito': key_info['cpf_perito'],
            'Valor Honorários': key_info['valor_honorarios'],
            'Natureza da Ação': key_info['natureza_acao'],
            'Unidade Judiciária': key_info['unidade_judiciaria'],
            'Conteúdo Resumo': despacho['conteudo'][:200] + '...' if len(despacho['conteudo']) > 200 else despacho['conteudo'],
            'Palavra Encontrada': despacho['palavra_encontrada']
        }
        
        excel_data.append(row)
    
    # Create DataFrame
    df = pd.DataFrame(excel_data)
    
    # Create Excel file with formatting
    with pd.ExcelWriter(output_filename, engine='openpyxl') as writer:
        # Write main sheet
        df.to_excel(writer, sheet_name='Despachos', index=False)
        
        # Get the workbook and worksheet
        workbook = writer.book
        worksheet = writer.sheets['Despachos']
        
        # Auto-adjust column widths
        for column in worksheet.columns:
            max_length = 0
            column_letter = column[0].column_letter
            for cell in column:
                try:
                    if len(str(cell.value)) > max_length:
                        max_length = len(str(cell.value))
                except:
                    pass
            adjusted_width = min(max_length + 2, 50)  # Cap at 50 characters
            worksheet.column_dimensions[column_letter].width = adjusted_width
        
        # Create summary sheet
        summary_data = {
            'Métrica': [
                'Total de Despachos',
                'Total de Arquivos', 
                'Arquivos Únicos',
                'Média Despachos por Arquivo',
                'Data de Geração'
            ],
            'Valor': [
                len(despachos_list),
                len(set(d['arquivo'] for d in despachos_list)),
                len(set(d['arquivo'] for d in despachos_list)),
                round(len(despachos_list) / len(set(d['arquivo'] for d in despachos_list)), 2),
                datetime.now().strftime('%Y-%m-%d %H:%M:%S')
            ]
        }
        
        summary_df = pd.DataFrame(summary_data)
        summary_df.to_excel(writer, sheet_name='Resumo', index=False)
        
        # Auto-adjust summary columns
        summary_ws = writer.sheets['Resumo']
        for column in summary_ws.columns:
            max_length = 0
            column_letter = column[0].column_letter
            for cell in column:
                try:
                    if len(str(cell.value)) > max_length:
                        max_length = len(str(cell.value))
                except:
                    pass
            adjusted_width = max_length + 2
            summary_ws.column_dimensions[column_letter].width = adjusted_width

def main():
    """Main function"""
    print("=== Geração de Excel - Despachos 2024 ===")
    
    # Find most recent JSON file
    json_files = [f for f in os.listdir('.') if f.startswith('despachos_') and f.endswith('.json')]
    
    if not json_files:
        print("❌ Nenhum arquivo JSON de despachos encontrado!")
        return
    
    # Use most recent file
    latest_json = sorted(json_files)[-1]
    print(f"📄 Usando arquivo: {latest_json}")
    
    # Load despachos
    despachos = load_despachos_json(latest_json)
    
    if not despachos:
        print("❌ Nenhum despacho encontrado no arquivo JSON!")
        return
    
    print(f"📊 Carregados {len(despachos)} despachos de {len(set(d['arquivo'] for d in despachos))} arquivos")
    
    # Generate Excel filename
    timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
    excel_filename = f"despachos_2024_{timestamp}.xlsx"
    
    try:
        # Generate Excel file
        generate_excel(despachos, excel_filename)
        print(f"✅ Arquivo Excel gerado: {excel_filename}")
        
        # Print summary
        print(f"\n=== RESUMO ===")
        print(f"Total de despachos: {len(despachos)}")
        print(f"Arquivos únicos: {len(set(d['arquivo'] for d in despachos))}")
        print(f"Média por arquivo: {len(despachos) / len(set(d['arquivo'] for d in despachos)):.2f}")
        print(f"Arquivo Excel: {excel_filename}")
        
    except Exception as e:
        print(f"❌ Erro ao gerar Excel: {e}")
        print("💡 Certifique-se de que o pandas e openpyxl estão instalados:")
        print("   pip install pandas openpyxl")

if __name__ == "__main__":
    main()