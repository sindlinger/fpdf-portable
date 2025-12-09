#!/usr/bin/env python3
import json
import time
import sys

start = time.time()

# Carregar o cache diretamente
with open('.cache/0001210._cache.json', 'r') as f:
    data = json.load(f)

# Processar despachos
despachos = []
for page in data.get('pages', []):
    content = page.get('text', '')
    if 'DESPACHO' in content:
        despachos.append(content[:100])

print(f"Encontrados {len(despachos)} despachos")
print(f"Tempo: {time.time() - start:.3f} segundos")