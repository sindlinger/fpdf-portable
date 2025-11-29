#!/usr/bin/env python3
"""
Script de OCR melhorado para processar imagem já extraída
"""

import sys
import os
import json
import re
from pathlib import Path

try:
    import easyocr
    import cv2
    import numpy as np
    from PIL import Image, ImageEnhance, ImageFilter
except ImportError as e:
    print(f"❌ Erro: {e}")
    print("📦 Instale as dependências: pip install easyocr opencv-python pillow")
    sys.exit(1)

def enhance_image_for_ocr(image_path):
    """Melhora a qualidade da imagem para OCR"""
    try:
        # Abrir imagem
        img = Image.open(image_path)
        
        # Converter para escala de cinza se necessário
        if img.mode != 'L':
            img = img.convert('L')
        
        # Melhorar contraste
        enhancer = ImageEnhance.Contrast(img)
        img = enhancer.enhance(1.3)
        
        # Melhorar nitidez
        enhancer = ImageEnhance.Sharpness(img)
        img = enhancer.enhance(1.5)
        
        # Aplicar filtro para reduzir ruído
        img = img.filter(ImageFilter.MedianFilter(size=3))
        
        # Salvar imagem melhorada
        enhanced_path = image_path.replace('.png', '_enhanced.png')
        img.save(enhanced_path, 'PNG', quality=100, optimize=True)
        
        return enhanced_path
    except Exception as e:
        print(f"⚠️ Erro ao melhorar imagem: {e}")
        return image_path

def process_text_spacing(text):
    """Melhora o espaçamento do texto OCR"""
    if not text:
        return text
    
    # Corrigir espaçamentos em palavras comuns brasileiras
    corrections = {
        r'\bR\s*\$\s*': 'R$ ',
        r'\bC\s*P\s*F\s*': 'CPF ',
        r'\bC\s*N\s*P\s*J\s*': 'CNPJ ',
        r'\bC\s*E\s*P\s*': 'CEP ',
        r'\bR\s*U\s*A\s*': 'RUA ',
        r'\bA\s*V\s*E\s*N\s*I\s*D\s*A\s*': 'AVENIDA ',
        r'\bB\s*R\s*A\s*S\s*I\s*L\s*': 'BRASIL ',
        r'\bG\s*O\s*V\s*E\s*R\s*N\s*O\s*': 'GOVERNO ',
        r'\bE\s*S\s*T\s*A\s*D\s*O\s*': 'ESTADO ',
        r'\bP\s*A\s*R\s*A\s*Í\s*B\s*A\s*': 'PARAÍBA ',
        r'\bP\s*A\s*R\s*A\s*I\s*B\s*A\s*': 'PARAÍBA ',
        r'\bJ\s*U\s*D\s*I\s*C\s*I\s*Á\s*R\s*I\s*O\s*': 'JUDICIÁRIO ',
        r'\bF\s*U\s*N\s*D\s*O\s*': 'FUNDO ',
        r'\bE\s*S\s*P\s*E\s*C\s*I\s*A\s*L\s*': 'ESPECIAL ',
        r'\bP\s*O\s*D\s*E\s*R\s*': 'PODER ',
        r'\bB\s*A\s*N\s*C\s*O\s*': 'BANCO ',
        r'\bP\s*R\s*O\s*C\s*E\s*S\s*S\s*O\s*': 'PROCESSO ',
        r'\bN\s*Ú\s*M\s*E\s*R\s*O\s*': 'NÚMERO ',
        r'\bD\s*O\s*C\s*U\s*M\s*E\s*N\s*T\s*O\s*': 'DOCUMENTO ',
        r'\bE\s*M\s*P\s*E\s*N\s*H\s*O\s*': 'EMPENHO ',
        r'\bN\s*O\s*T\s*A\s*': 'NOTA ',
        r'\bJ\s*O\s*Ã\s*O\s*': 'JOÃO ',
        r'\bP\s*E\s*S\s*S\s*O\s*A\s*': 'PESSOA ',
        r'\bC\s*E\s*N\s*T\s*R\s*O\s*': 'CENTRO ',
        r'\bF\s*E\s*V\s*E\s*R\s*E\s*I\s*R\s*O\s*': 'FEVEREIRO ',
        r'\bM\s*A\s*R\s*Ç\s*O\s*': 'MARÇO ',
        r'\bM\s*A\s*I\s*O\s*': 'MAIO ',
        r'\bJ\s*U\s*L\s*H\s*O\s*': 'JULHO ',
        r'\bO\s*U\s*T\s*U\s*B\s*R\s*O\s*': 'OUTUBRO ',
        r'\bD\s*E\s*Z\s*E\s*M\s*B\s*R\s*O\s*': 'DEZEMBRO ',
        r'\bS\s*E\s*R\s*V\s*I\s*Ç\s*O\s*': 'SERVIÇO ',
        r'\bO\s*R\s*D\s*E\s*N\s*A\s*D\s*O\s*R\s*': 'ORDENADOR ',
        r'\bC\s*Ó\s*D\s*I\s*G\s*O\s*': 'CÓDIGO ',
        r'\bA\s*U\s*T\s*O\s*R\s*I\s*D\s*A\s*D\s*E\s*': 'AUTORIDADE ',
        r'\bD\s*E\s*S\s*P\s*E\s*S\s*A\s*': 'DESPESA ',
        r'\bT\s*O\s*T\s*A\s*L\s*': 'TOTAL ',
        r'\bI\s*M\s*P\s*O\s*R\s*T\s*Â\s*N\s*C\s*I\s*A\s*': 'IMPORTÂNCIA ',
        r'\bE\s*M\s*P\s*E\s*N\s*H\s*A\s*D\s*A\s*': 'EMPENHADA ',
        r'\bF\s*A\s*V\s*O\s*R\s*': 'FAVOR ',
        r'\bP\s*E\s*R\s*I\s*T\s*O\s*': 'PERITO ',
        r'\bM\s*É\s*D\s*I\s*C\s*O\s*': 'MÉDICO ',
        r'\bD\s*E\s*T\s*E\s*R\s*M\s*I\s*N\s*A\s*D\s*A\s*': 'DETERMINADA ',
        r'\bH\s*O\s*N\s*O\s*R\s*Á\s*R\s*I\s*O\s*S\s*': 'HONORÁRIOS ',
    }
    
    # Aplicar correções
    for pattern, replacement in corrections.items():
        text = re.sub(pattern, replacement, text, flags=re.IGNORECASE)
    
    # Corrigir espaços múltiplos
    text = re.sub(r'\s+', ' ', text)
    
    # Corrigir espaços antes de pontuação
    text = re.sub(r'\s+([,.;:!?])', r'\1', text)
    
    # Corrigir espaços após parênteses de abertura e antes de fechamento
    text = re.sub(r'\(\s+', '(', text)
    text = re.sub(r'\s+\)', ')', text)
    
    # Melhorar formatação de números
    text = re.sub(r'(\d)\s+(\d)', r'\1\2', text)  # Juntar dígitos separados
    text = re.sub(r'(\d)\s*\.\s*(\d)', r'\1.\2', text)  # Corrigir pontos decimais
    text = re.sub(r'(\d)\s*,\s*(\d)', r'\1,\2', text)  # Corrigir vírgulas decimais
    
    return text.strip()

def extract_brazilian_patterns(text):
    """Extrai padrões brasileiros do texto"""
    patterns = {}
    
    # CPF - mais flexível
    cpf_pattern = r'\b\d{3}\.?\s*\d{3}\.?\s*\d{3}\s*-?\s*\d{2}\b'
    cpfs = re.findall(cpf_pattern, text)
    if cpfs:
        # Limpar CPFs
        clean_cpfs = []
        for cpf in cpfs:
            clean_cpf = re.sub(r'[^\d]', '', cpf)
            if len(clean_cpf) == 11:
                formatted_cpf = f"{clean_cpf[:3]}.{clean_cpf[3:6]}.{clean_cpf[6:9]}-{clean_cpf[9:]}"
                clean_cpfs.append(formatted_cpf)
        if clean_cpfs:
            patterns['cpf'] = list(set(clean_cpfs))
    
    # CNPJ - mais flexível
    cnpj_pattern = r'\b\d{2}\.?\s*\d{3}\.?\s*\d{3}\s*/?\s*\d{4}\s*-?\s*\d{2}\b'
    cnpjs = re.findall(cnpj_pattern, text)
    if cnpjs:
        # Limpar CNPJs
        clean_cnpjs = []
        for cnpj in cnpjs:
            clean_cnpj = re.sub(r'[^\d]', '', cnpj)
            if len(clean_cnpj) == 14:
                formatted_cnpj = f"{clean_cnpj[:2]}.{clean_cnpj[2:5]}.{clean_cnpj[5:8]}/{clean_cnpj[8:12]}-{clean_cnpj[12:]}"
                clean_cnpjs.append(formatted_cnpj)
        if clean_cnpjs:
            patterns['cnpj'] = list(set(clean_cnpjs))
    
    # CEP
    cep_pattern = r'\b\d{5}\s*-?\s*\d{3}\b'
    ceps = re.findall(cep_pattern, text)
    if ceps:
        clean_ceps = []
        for cep in ceps:
            clean_cep = re.sub(r'[^\d]', '', cep)
            if len(clean_cep) == 8:
                formatted_cep = f"{clean_cep[:5]}-{clean_cep[5:]}"
                clean_ceps.append(formatted_cep)
        if clean_ceps:
            patterns['cep'] = list(set(clean_ceps))
    
    # Valores monetários
    money_pattern = r'R\$?\s*\d{1,3}(?:[.,]\d{3})*[.,]\d{2}'
    values = re.findall(money_pattern, text, re.IGNORECASE)
    if values:
        patterns['valores'] = list(set(values))
    
    # Datas
    date_pattern = r'\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b'
    dates = re.findall(date_pattern, text)
    if dates:
        patterns['datas'] = list(set(dates))
    
    # Números de processo
    process_pattern = r'\b\d{7}[-.]?\d{2}\.?\d{4}\.?\d{1}\.?\d{2}\.?\d{4}\b'
    processes = re.findall(process_pattern, text)
    if processes:
        patterns['processos'] = list(set(processes))
    
    return patterns

def perform_ocr_on_image(image_path):
    """Realiza OCR na imagem com configurações otimizadas para português"""
    try:
        print(f"🔍 Inicializando EasyOCR (português + inglês)...")
        # Inicializar EasyOCR com português e inglês
        reader = easyocr.Reader(['pt', 'en'], gpu=False, verbose=False)
        
        # Melhorar imagem
        print(f"🎨 Melhorando qualidade da imagem...")
        enhanced_image = enhance_image_for_ocr(image_path)
        
        print(f"🔍 Realizando OCR...")
        # Realizar OCR com configurações otimizadas
        results = reader.readtext(
            enhanced_image,
            detail=1,  # Retornar coordenadas e confiança
            paragraph=True,  # Agrupar em parágrafos
            width_ths=0.8,  # Limiar para largura do texto
            height_ths=0.8,  # Limiar para altura do texto
            decoder='beamsearch',  # Melhor decodificador
            beamWidth=5,  # Largura do beam search
            batch_size=1,  # Tamanho do batch
            workers=0,  # Usar todos os workers disponíveis
            allowlist=None,  # Permitir todos os caracteres
            blocklist=None  # Não bloquear caracteres
        )
        
        # Processar resultados
        all_texts = []
        word_confidences = []
        
        print(f"📝 Processando {len(results)} blocos de texto...")
        for i, (bbox, text, confidence) in enumerate(results):
            if confidence > 0.2:  # Filtrar por confiança mínima baixa
                processed_text = process_text_spacing(text.strip())
                if processed_text:  # Só adicionar se não estiver vazio
                    all_texts.append(processed_text)
                    word_confidences.append(confidence)
                    print(f"   Bloco {i+1}: '{processed_text[:50]}...' (confiança: {confidence:.2f})")
        
        # Limpar imagem melhorada temporária
        if enhanced_image != image_path and os.path.exists(enhanced_image):
            os.remove(enhanced_image)
        
        # Juntar todos os textos
        full_text = '\n'.join(all_texts)
        
        # Processar texto final
        full_text = process_text_spacing(full_text)
        
        # Extrair padrões brasileiros
        print(f"🇧🇷 Extraindo padrões brasileiros...")
        patterns = extract_brazilian_patterns(full_text)
        
        # Calcular estatísticas
        avg_confidence = sum(word_confidences) / len(word_confidences) if word_confidences else 0
        word_count = len(full_text.split()) if full_text else 0
        
        return {
            'success': True,
            'text': full_text,
            'word_count': word_count,
            'avg_confidence': avg_confidence,
            'patterns': patterns,
            'engine': 'EasyOCR v1.7+',
            'blocks_processed': len(results),
            'blocks_accepted': len(all_texts)
        }
        
    except Exception as e:
        return {
            'success': False,
            'error': str(e),
            'text': '',
            'word_count': 0,
            'avg_confidence': 0,
            'patterns': {},
            'engine': 'EasyOCR',
            'blocks_processed': 0,
            'blocks_accepted': 0
        }

def main():
    if len(sys.argv) != 3:
        print("Uso: python ocr_image.py <image_path> <output_dir>")
        sys.exit(1)
    
    image_path = sys.argv[1]
    output_dir = sys.argv[2]
    
    # Criar diretório de saída se não existir
    os.makedirs(output_dir, exist_ok=True)
    
    if not os.path.exists(image_path):
        print(f"❌ Imagem não encontrada: {image_path}")
        sys.exit(1)
    
    print(f"🔍 Processando imagem: {os.path.basename(image_path)}")
    print(f"📁 Saída: {output_dir}")
    print()
    
    # Realizar OCR
    print("🔍 Realizando OCR otimizado para português...")
    ocr_result = perform_ocr_on_image(image_path)
    
    if not ocr_result['success']:
        print(f"❌ Falha no OCR: {ocr_result['error']}")
        sys.exit(1)
    
    # Mostrar resultados
    print(f"✅ OCR realizado com sucesso!")
    print(f"📊 Blocos processados: {ocr_result['blocks_processed']}")
    print(f"📊 Blocos aceitos: {ocr_result['blocks_accepted']}")
    print(f"📊 Palavras encontradas: {ocr_result['word_count']}")
    print(f"🎯 Confiança média: {ocr_result['avg_confidence']:.2f}")
    
    if ocr_result['patterns']:
        print("🇧🇷 Padrões brasileiros encontrados:")
        for pattern_type, values in ocr_result['patterns'].items():
            print(f"   📍 {pattern_type.upper()}: {', '.join(values)}")
    
    # Salvar resultado
    base_name = os.path.splitext(os.path.basename(image_path))[0]
    text_file = os.path.join(output_dir, f"{base_name}_OCR_MELHORADO.txt")
    json_file = os.path.join(output_dir, f"{base_name}_OCR_MELHORADO.json")
    
    # Salvar texto
    with open(text_file, 'w', encoding='utf-8') as f:
        f.write(f"PÁGINA OCR MELHORADO - {base_name.upper()}\n")
        f.write("=" * 60 + "\n\n")
        f.write(f"📊 ESTATÍSTICAS:\n")
        f.write(f"   Engine: {ocr_result['engine']}\n")
        f.write(f"   Blocos processados: {ocr_result['blocks_processed']}\n")
        f.write(f"   Blocos aceitos: {ocr_result['blocks_accepted']}\n")
        f.write(f"   Palavras: {ocr_result['word_count']}\n")
        f.write(f"   Confiança média: {ocr_result['avg_confidence']:.2f}\n\n")
        
        if ocr_result['patterns']:
            f.write("🇧🇷 PADRÕES BRASILEIROS ENCONTRADOS:\n")
            f.write("-" * 40 + "\n")
            for pattern_type, values in ocr_result['patterns'].items():
                f.write(f"📍 {pattern_type.upper()}:\n")
                for value in values:
                    f.write(f"   • {value}\n")
                f.write("\n")
        
        f.write("📄 TEXTO EXTRAÍDO:\n")
        f.write("=" * 60 + "\n")
        f.write(ocr_result['text'])
    
    # Salvar JSON completo
    with open(json_file, 'w', encoding='utf-8') as f:
        json.dump(ocr_result, f, ensure_ascii=False, indent=2)
    
    print(f"\n💾 Resultados salvos:")
    print(f"   📄 Texto: {os.path.basename(text_file)}")
    print(f"   📊 JSON: {os.path.basename(json_file)}")

if __name__ == "__main__":
    main()