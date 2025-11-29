#!/usr/bin/env python3
"""
Detector de Notas de Empenho em PDFs
Analisa PDFs em cache para identificar páginas que são notas de empenho
"""

import json
import sys
import os
import re
from pathlib import Path
from typing import List, Dict, Tuple

class EmpenhoDetector:
    """Detecta notas de empenho em PDFs processados pelo fpdf"""
    
    # Palavras-chave típicas de notas de empenho
    KEYWORDS = [
        'empenho', 'nota de empenho', 'ne', 'n.e.',
        'dotação', 'orçamentária', 'credor', 'beneficiário',
        'elemento de despesa', 'programa', 'ação',
        'fonte de recursos', 'valor empenhado',
        'autorização', 'ordenador', 'saldo'
    ]
    
    # Padrões regex para identificar números de empenho
    PATTERNS = [
        r'\d{4}NE\d{6}',  # 2024NE000001
        r'NE\s*[:]\s*\d+',  # NE: 123456
        r'Nota\s+de\s+Empenho\s*[:]\s*\d+',  # Nota de Empenho: 123
        r'Empenho\s+n[º°]\s*\d+',  # Empenho nº 123
    ]
    
    def __init__(self, cache_dir=".cache"):
        self.cache_dir = Path(cache_dir)
        
    def analyze_cache_file(self, cache_file: str) -> Dict:
        """Analisa um arquivo de cache para detectar notas de empenho"""
        
        with open(cache_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        results = {
            'file': cache_file,
            'total_pages': len(data.get('Pages', [])),
            'empenho_pages': [],
            'confidence_scores': {}
        }
        
        for page in data.get('Pages', []):
            score, reasons = self.analyze_page(page)
            if score > 0.3:  # Threshold de confiança
                page_num = page.get('PageNumber', 0)
                results['empenho_pages'].append(page_num)
                results['confidence_scores'][page_num] = {
                    'score': score,
                    'reasons': reasons
                }
        
        return results
    
    def analyze_page(self, page: Dict) -> Tuple[float, List[str]]:
        """
        Analisa uma página e retorna score de confiança (0-1) 
        e razões para considerar como nota de empenho
        """
        score = 0.0
        reasons = []
        
        # 1. Verificar se é página escaneada
        is_scanned = self.is_scanned_page(page)
        if is_scanned:
            score += 0.3
            reasons.append("Página escaneada (imagem)")
        
        # 2. Verificar texto extraível
        text = page.get('TextInfo', {}).get('PageText', '').lower()
        text_length = len(text)
        
        # 3. Buscar palavras-chave
        keyword_count = sum(1 for kw in self.KEYWORDS if kw in text)
        if keyword_count > 0:
            score += min(0.4, keyword_count * 0.1)
            reasons.append(f"Encontradas {keyword_count} palavras-chave")
        
        # 4. Buscar padrões de número de empenho
        for pattern in self.PATTERNS:
            if re.search(pattern, text, re.IGNORECASE):
                score += 0.3
                reasons.append(f"Padrão de número de empenho detectado")
                break
        
        # 5. Verificar proporção imagem/texto
        image_count = len(page.get('Resources', {}).get('Images', []))
        if image_count > 0 and text_length < 200:
            score += 0.2
            reasons.append(f"Alta proporção imagem/texto ({image_count} imgs, {text_length} chars)")
        
        # 6. Verificar se tem tabelas (comum em notas de empenho)
        if 'Tables' in page.get('LayoutInfo', {}):
            score += 0.1
            reasons.append("Contém tabelas")
        
        # 7. Verificar metadata da imagem se for escaneada
        for img in page.get('Resources', {}).get('Images', []):
            if img.get('IsFullPage', False):
                score += 0.1
                reasons.append("Imagem ocupa página inteira")
                break
        
        return min(1.0, score), reasons
    
    def is_scanned_page(self, page: Dict) -> bool:
        """Verifica se a página é escaneada (imagem)"""
        
        # Verificar flag direto
        for img in page.get('Resources', {}).get('Images', []):
            if img.get('IsScannedPage', False):
                return True
        
        # Verificar por heurística
        text_length = len(page.get('TextInfo', {}).get('PageText', ''))
        image_count = len(page.get('Resources', {}).get('Images', []))
        
        # Página com imagem e pouco/nenhum texto é provavelmente escaneada
        return image_count > 0 and text_length < 100
    
    def find_empenho_in_cache(self, pattern: str = "*_cache.json") -> List[Dict]:
        """Busca notas de empenho em todos os arquivos de cache"""
        
        results = []
        cache_files = list(self.cache_dir.glob(pattern))
        
        print(f"🔍 Analisando {len(cache_files)} arquivos em cache...")
        
        for cache_file in cache_files:
            try:
                result = self.analyze_cache_file(cache_file)
                if result['empenho_pages']:
                    results.append(result)
                    print(f"✓ {cache_file.name}: {len(result['empenho_pages'])} páginas encontradas")
            except Exception as e:
                print(f"✗ Erro ao analisar {cache_file.name}: {e}")
        
        return results

def main():
    """Função principal"""
    
    if len(sys.argv) < 2:
        print("Uso:")
        print("  python detect_empenho.py <cache_file.json>  # Analisar arquivo específico")
        print("  python detect_empenho.py --all              # Analisar todo o cache")
        print("  python detect_empenho.py --search <termo>   # Buscar termo específico")
        sys.exit(1)
    
    detector = EmpenhoDetector()
    
    if sys.argv[1] == "--all":
        # Analisar todos os arquivos em cache
        results = detector.find_empenho_in_cache()
        
        print("\n" + "="*50)
        print("📊 RESUMO DA ANÁLISE")
        print("="*50)
        
        total_empenhos = 0
        for result in results:
            file_name = Path(result['file']).name
            pages = result['empenho_pages']
            total_empenhos += len(pages)
            
            print(f"\n📄 {file_name}")
            print(f"   Total de páginas: {result['total_pages']}")
            print(f"   Páginas com nota de empenho: {pages}")
            
            for page_num, info in result['confidence_scores'].items():
                print(f"   • Página {page_num} (confiança: {info['score']:.1%})")
                for reason in info['reasons']:
                    print(f"     - {reason}")
        
        print(f"\n✅ Total: {total_empenhos} notas de empenho encontradas em {len(results)} PDFs")
        
    elif sys.argv[1] == "--search" and len(sys.argv) > 2:
        # Buscar termo específico
        search_term = sys.argv[2]
        print(f"🔍 Buscando por '{search_term}'...")
        # Implementar busca específica
        
    else:
        # Analisar arquivo específico
        cache_file = sys.argv[1]
        if not os.path.exists(cache_file):
            print(f"❌ Arquivo não encontrado: {cache_file}")
            sys.exit(1)
        
        result = detector.analyze_cache_file(cache_file)
        
        print(f"\n📄 Análise de: {cache_file}")
        print(f"Total de páginas: {result['total_pages']}")
        
        if result['empenho_pages']:
            print(f"✅ Páginas com nota de empenho: {result['empenho_pages']}")
            
            for page_num, info in result['confidence_scores'].items():
                print(f"\n📌 Página {page_num}")
                print(f"   Confiança: {info['score']:.1%}")
                print("   Razões:")
                for reason in info['reasons']:
                    print(f"   • {reason}")
                    
            print(f"\n💡 Para extrair as imagens das notas de empenho:")
            print(f"   fpdf <cache_index> images")
            print(f"   fpdf <cache_index> ocr  # Para extrair texto")
        else:
            print("❌ Nenhuma nota de empenho detectada")

if __name__ == "__main__":
    main()