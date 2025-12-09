#!/usr/bin/env python3
"""
Script melhorado de OCR para documentos brasileiros
Extrai páginas como imagens de alta qualidade e aplica OCR otimizado
"""

import sys
import os
import subprocess
import tempfile
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
        
        # Aumentar resolução (2x)
        width, height = img.size
        img = img.resize((width * 2, height * 2), Image.LANCZOS)
        
        # Melhorar contraste
        enhancer = ImageEnhance.Contrast(img)
        img = enhancer.enhance(1.5)
        
        # Melhorar nitidez
        enhancer = ImageEnhance.Sharpness(img)
        img = enhancer.enhance(2.0)
        
        # Aplicar filtro para reduzir ruído
        img = img.filter(ImageFilter.MedianFilter(size=3))
        
        # Salvar imagem melhorada
        enhanced_path = image_path.replace('.png', '_enhanced.png')
        img.save(enhanced_path, 'PNG', quality=100, optimize=True)
        
        return enhanced_path
    except Exception as e:
        print(f"⚠️ Erro ao melhorar imagem: {e}")
        return image_path

def extract_pdf_page_as_image(pdf_path, page_number, output_dir):
    """Extrai página do PDF como imagem de alta qualidade"""
    try:
        output_path = os.path.join(output_dir, f"page_{page_number}.png")
        
        # Usar pdftoppm para extrair com alta qualidade
        cmd = [
            'pdftoppm',
            '-png',
            '-r', '400',  # 400 DPI para alta qualidade
            '-f', str(page_number),
            '-l', str(page_number),
            '-singlefile',
            pdf_path,
            output_path.replace('.png', '')
        ]
        
        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode != 0:
            print(f"❌ Erro ao extrair página: {result.stderr}")
            return None
            
        return output_path
    except Exception as e:
        print(f"❌ Erro ao extrair página: {e}")
        return None

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
    
    return text.strip()

def extract_brazilian_patterns(text):
    """Extrai padrões brasileiros do texto"""
    patterns = {}
    
    # CPF
    cpf_pattern = r'\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b'
    cpfs = re.findall(cpf_pattern, text)
    if cpfs:
        patterns['cpf'] = list(set(cpfs))
    
    # CNPJ  
    cnpj_pattern = r'\b\d{2}\.?\d{3}\.?\d{3}/?0001-?\d{2}\b'
    cnpjs = re.findall(cnpj_pattern, text)
    if cnpjs:
        patterns['cnpj'] = list(set(cnpjs))
    
    # CEP
    cep_pattern = r'\b\d{5}-?\d{3}\b'
    ceps = re.findall(cep_pattern, text)
    if ceps:
        patterns['cep'] = list(set(ceps))
    
    # Valores monetários
    money_pattern = r'R\$\s*\d{1,3}(?:[.,]\d{3})*[.,]\d{2}'
    values = re.findall(money_pattern, text)
    if values:
        patterns['valores'] = list(set(values))
    
    # Datas
    date_pattern = r'\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b'
    dates = re.findall(date_pattern, text)
    if dates:
        patterns['datas'] = list(set(dates))
    
    return patterns

def perform_ocr_on_image(image_path):
    """Realiza OCR na imagem com configurações otimizadas para português"""
    try:
        # Inicializar EasyOCR com português e inglês
        reader = easyocr.Reader(['pt', 'en'], gpu=True)
        
        # Melhorar imagem
        enhanced_image = enhance_image_for_ocr(image_path)
        
        # Realizar OCR com configurações otimizadas
        results = reader.readtext(
            enhanced_image,
            detail=1,  # Retornar coordenadas e confiança
            paragraph=True,  # Agrupar em parágrafos
            width_ths=0.7,  # Limiar para largura do texto
            height_ths=0.7,  # Limiar para altura do texto
            decoder='beamsearch',  # Melhor decodificador
            beamWidth=5,  # Largura do beam search
            batch_size=1,  # Tamanho do batch
            workers=0,  # Usar todos os workers disponíveis
            allowlist=None,  # Permitir todos os caracteres
            blocklist=None  # Não bloquear caracteres
        )
        
        # Processar resultados
        full_text = ""
        word_confidences = []
        
        for (bbox, text, confidence) in results:
            if confidence > 0.3:  # Filtrar por confiança mínima
                processed_text = process_text_spacing(text)
                full_text += processed_text + " "
                word_confidences.append(confidence)
        
        # Limpar imagem melhorada temporária
        if enhanced_image != image_path and os.path.exists(enhanced_image):
            os.remove(enhanced_image)
        
        # Processar texto final
        full_text = process_text_spacing(full_text)
        
        # Extrair padrões brasileiros
        patterns = extract_brazilian_patterns(full_text)
        
        # Calcular estatísticas
        avg_confidence = sum(word_confidences) / len(word_confidences) if word_confidences else 0
        word_count = len(full_text.split())
        
        return {
            'success': True,
            'text': full_text,
            'word_count': word_count,
            'avg_confidence': avg_confidence,
            'patterns': patterns,
            'engine': 'EasyOCR'
        }
        
    except Exception as e:
        return {
            'success': False,
            'error': str(e),
            'text': '',
            'word_count': 0,
            'avg_confidence': 0,
            'patterns': {},
            'engine': 'EasyOCR'
        }

def main():
    if len(sys.argv) != 4:
        print("Uso: python improved_ocr.py <pdf_path> <page_number> <output_dir>")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    page_number = int(sys.argv[2])
    output_dir = sys.argv[3]
    
    # Criar diretório de saída se não existir
    os.makedirs(output_dir, exist_ok=True)
    
    print(f"🔍 Processando PDF: {os.path.basename(pdf_path)}")
    print(f"📄 Página: {page_number}")
    print(f"📁 Saída: {output_dir}")
    print()
    
    # Extrair página como imagem
    print("🖼️ Extraindo página como imagem de alta qualidade...")
    image_path = extract_pdf_page_as_image(pdf_path, page_number, output_dir)
    
    if not image_path or not os.path.exists(image_path):
        print("❌ Falha ao extrair página como imagem")
        sys.exit(1)
    
    print(f"✅ Imagem extraída: {os.path.basename(image_path)}")
    
    # Realizar OCR
    print("🔍 Realizando OCR otimizado para português...")
    ocr_result = perform_ocr_on_image(image_path)
    
    if not ocr_result['success']:
        print(f"❌ Falha no OCR: {ocr_result['error']}")
        sys.exit(1)
    
    # Mostrar resultados
    print(f"✅ OCR realizado com sucesso!")
    print(f"📊 Palavras encontradas: {ocr_result['word_count']}")
    print(f"🎯 Confiança média: {ocr_result['avg_confidence']:.2f}")
    
    if ocr_result['patterns']:
        print("🇧🇷 Padrões brasileiros encontrados:")
        for pattern_type, values in ocr_result['patterns'].items():
            print(f"   {pattern_type.upper()}: {', '.join(values)}")
    
    # Salvar resultado
    base_name = f"page_{page_number}_ocr"
    text_file = os.path.join(output_dir, f"{base_name}.txt")
    json_file = os.path.join(output_dir, f"{base_name}.json")
    
    # Salvar texto
    with open(text_file, 'w', encoding='utf-8') as f:
        f.write(f"PÁGINA {page_number} - OCR MELHORADO\n")
        f.write("=" * 50 + "\n\n")
        f.write(f"📊 Estatísticas:\n")
        f.write(f"   Palavras: {ocr_result['word_count']}\n")
        f.write(f"   Confiança: {ocr_result['avg_confidence']:.2f}\n")
        f.write(f"   Engine: {ocr_result['engine']}\n\n")
        
        if ocr_result['patterns']:
            f.write("🇧🇷 Padrões brasileiros:\n")
            for pattern_type, values in ocr_result['patterns'].items():
                f.write(f"   {pattern_type.upper()}: {', '.join(values)}\n")
            f.write("\n")
        
        f.write("📄 TEXTO EXTRAÍDO:\n")
        f.write("=" * 50 + "\n")
        f.write(ocr_result['text'])
    
    # Salvar JSON completo
    with open(json_file, 'w', encoding='utf-8') as f:
        json.dump(ocr_result, f, ensure_ascii=False, indent=2)
    
    print(f"💾 Resultados salvos:")
    print(f"   📄 Texto: {os.path.basename(text_file)}")
    print(f"   📊 JSON: {os.path.basename(json_file)}")
    print(f"   🖼️ Imagem: {os.path.basename(image_path)}")

if __name__ == "__main__":
    main()