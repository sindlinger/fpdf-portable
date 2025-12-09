

import json

def identificar_falhas_final(input_path):
    with open(input_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    print("--- Análise de Falhas na Extração Final ---")
    falhas = False
    arquivos_com_falha = set()
    for i, item in enumerate(data):
        campos_nulos = []
        for campo, valor in item.items():
            if valor is None:
                campos_nulos.append(campo)
        
        if campos_nulos:
            falhas = True
            arquivo = item.get('arquivo_origem')
            if arquivo not in arquivos_com_falha:
                print(f"\nArquivo com falha: {arquivo}")
                arquivos_com_falha.add(arquivo)
            print(f"  - Registro {i}: Campos com falha: {', '.join(campos_nulos)}")

    if not falhas:
        print("\nNenhuma falha encontrada. Todos os campos foram extraídos com sucesso!")

if __name__ == "__main__":
    identificar_falhas_final("dados_incompletos_corrigidos.json")
