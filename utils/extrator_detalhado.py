import json
import re
import pandas as pd

def extrair_informacoes(texto):
    info = {
        "processo_administrativo": None,
        "requerente_vara_comarca": None,
        "perito_nome": None,
        "perito_especialidade": None,
        "perito_cpf": None,
        "processo_judicial": None,
        "autor_judicial": None,
        "reu_judicial": None,
        "valor_honorarios": None,
        "data_documento": None,
        "diretor_assinatura": None,
    }

    # Processo Administrativo
    match = re.search(r"Processo\s+nº\s+([\d.-]+)", texto)
    if match:
        info["processo_administrativo"] = match.group(1).strip()

    # Requerente (Vara/Comarca)
    match = re.search(r"Requerente:\s*(.*?)\s+Interessado:", texto, re.DOTALL)
    if match:
        info["requerente_vara_comarca"] = match.group(1).replace('\n', ' ').strip()

    # Interessado (Perito)
    match = re.search(r"Interessado:\s*([^–-]+–\s*[^-\n]+)", texto)
    if match:
        perito_info = match.group(1).strip()
        parts = [p.strip() for p in perito_info.split('–') if p.strip()]
        if len(parts) > 0:
            info["perito_nome"] = parts[0]
        if len(parts) > 1:
            info["perito_especialidade"] = parts[1]

    # CPF do Perito
    match = re.search(r"CPF\s+([\d.-]+)", texto)
    if match:
        info["perito_cpf"] = match.group(1).strip()

    # Processo Judicial
    match = re.search(r"autos do processo nº\s+([\d.-]+)", texto)
    if match:
        info["processo_judicial"] = match.group(1).strip()

    # Autor e Réu
    match = re.search(r"movido por\s+(.*?),\s+CPF\s+[\d.-]+,\s+em face de\s+(.*?),\s+CPF", texto, re.DOTALL)
    if match:
        info["autor_judicial"] = match.group(1).replace('\n', ' ').strip()
        info["reu_judicial"] = match.group(2).replace('\n', ' ').strip()
    else:
        match = re.search(r"movida por\s+(.*?),\s+CPF\s+[\d.-]+,\s+em face de\s+(.*?),\s+CNPJ", texto, re.DOTALL)
        if match:
            info["autor_judicial"] = match.group(1).replace('\n', ' ').strip()
            info["reu_judicial"] = match.group(2).replace('\n', ' ').strip()


    # Valor dos Honorários
    match = re.search(r"valor de R\$\s+([\d.,]+)\s+\((.*?)\)", texto)
    if match:
        info["valor_honorarios"] = match.group(1).strip()

    # Data do Documento (a partir do nome do despacho)
    match = re.search(r"Despacho DIESP nº\s+\d+\/(\d{4})", texto)
    if match:
        info["data_documento"] = match.group(1).strip()

    # Diretor (Exemplo, pode precisar de ajuste)
    match = re.search(r"Em razão do exposto, autorizo a despesa,[\s\S]*?([^\n]+?)$", texto)
    if match:
        # Esta é uma suposição e pode não capturar o nome correto.
        # A lógica pode precisar ser mais robusta.
        assinatura = match.group(1).strip()
        if len(assinatura) < 50: # Evita pegar parágrafos longos
             info["diretor_assinatura"] = assinatura


    return info

def analisar_e_extrair(input_path, output_path):
    with open(input_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    resultados_extraidos = []
    for resultado in data.get("resultados", []):
        for doc in resultado.get("documentos", []):
            info_extraida = extrair_informacoes(doc.get("content", ""))
            info_extraida["arquivo_origem"] = resultado.get("arquivo")
            info_extraida["documento_nome"] = doc.get("documentName")
            resultados_extraidos.append(info_extraida)

    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(resultados_extraidos, f, indent=2, ensure_ascii=False)

    print(f"Dados extraídos e salvos em: {output_path}")
    # Imprime os primeiros 3 resultados para visualização
    print(json.dumps(resultados_extraidos[:3], indent=2, ensure_ascii=False))


if __name__ == "__main__":
    analisar_e_extrair("modelo_diesp.json", "dados_extraidos.json")
