using System;
using System.Collections.Generic;
using System.Linq;

namespace FilterPDF
{
    /// <summary>
    /// Parser de cache ranges similar ao PageRangeParser
    /// Suporta:
    /// - Índices individuais: 1,3,5
    /// - Intervalos: 1-10, 10-1 (reverso)
    /// - Último cache: z ou r1
    /// - Contagem reversa: r2 (penúltimo), r3 (antepenúltimo)
    /// - Exclusões: 1-10,x3-4 (1 a 10 exceto 3 e 4)
    /// - Filtros: 1-10:even, 1-10:odd
    /// </summary>
    public static class CacheRangeParser
    {
        public static List<int> Parse(string rangeSpec, int totalCaches)
        {
            if (string.IsNullOrWhiteSpace(rangeSpec))
                return Enumerable.Range(1, totalCaches).ToList(); // Todos os caches se não especificado
                
            var result = new List<int>();
            var excludeCaches = new HashSet<int>();
            
            // Dividir por vírgulas
            var parts = rangeSpec.Split(',');
            
            foreach (var part in parts)
            {
                var trimmedPart = part.Trim();
                if (string.IsNullOrEmpty(trimmedPart))
                    continue;
                    
                // Verificar se é exclusão (começa com x)
                bool isExclusion = trimmedPart.StartsWith("x");
                if (isExclusion)
                    trimmedPart = trimmedPart.Substring(1);
                
                // Verificar se tem filtro :odd ou :even
                string? filter = null;
                if (trimmedPart.Contains(":"))
                {
                    var filterParts = trimmedPart.Split(':');
                    trimmedPart = filterParts[0];
                    filter = filterParts[1].ToLower();
                }
                
                // Processar o range
                var caches = ProcessRange(trimmedPart, totalCaches);
                
                // Aplicar filtro se houver
                if (filter != null)
                {
                    caches = ApplyFilter(caches, filter);
                }
                
                // Adicionar ou excluir caches
                if (isExclusion)
                {
                    foreach (var cache in caches)
                        excludeCaches.Add(cache);
                }
                else
                {
                    result.AddRange(caches);
                }
            }
            
            // Remover caches excluídos
            if (excludeCaches.Count > 0)
            {
                result = result.Where(c => !excludeCaches.Contains(c)).ToList();
            }
            
            // Remover duplicados e ordenar
            result = result.Distinct().OrderBy(c => c).ToList();
            
            return result;
        }
        
        private static List<int> ProcessRange(string range, int totalCaches)
        {
            var result = new List<int>();
            
            // Substituir z pelo último cache
            range = range.Replace("z", totalCaches.ToString());
            
            // Verificar se é um intervalo (contém -)
            if (range.Contains("-"))
            {
                var rangeParts = range.Split('-');
                if (rangeParts.Length == 2)
                {
                    int start = ParseCacheNumber(rangeParts[0], totalCaches);
                    int end = ParseCacheNumber(rangeParts[1], totalCaches);
                    
                    if (start <= end)
                    {
                        // Intervalo crescente
                        for (int i = start; i <= end; i++)
                        {
                            if (i >= 1 && i <= totalCaches)
                                result.Add(i);
                        }
                    }
                    else
                    {
                        // Intervalo decrescente
                        for (int i = start; i >= end; i--)
                        {
                            if (i >= 1 && i <= totalCaches)
                                result.Add(i);
                        }
                    }
                }
            }
            else
            {
                // Cache único
                int cache = ParseCacheNumber(range, totalCaches);
                if (cache >= 1 && cache <= totalCaches)
                    result.Add(cache);
            }
            
            return result;
        }
        
        private static int ParseCacheNumber(string cacheSpec, int totalCaches)
        {
            cacheSpec = cacheSpec.Trim();
            
            // Verificar se é contagem reversa (r1, r2, etc.)
            if (cacheSpec.StartsWith("r"))
            {
                string numberPart = cacheSpec.Substring(1);
                if (int.TryParse(numberPart, out int reverseIndex))
                {
                    return totalCaches - reverseIndex + 1;
                }
            }
            
            // Tentar parse normal
            if (int.TryParse(cacheSpec, out int cacheNumber))
            {
                return cacheNumber;
            }
            
            return 0; // Cache inválido
        }
        
        private static List<int> ApplyFilter(List<int> caches, string filter)
        {
            var result = new List<int>();
            
            if (filter == "odd")
            {
                // Pegar caches em posições ímpares (1º, 3º, 5º, etc.)
                for (int i = 0; i < caches.Count; i++)
                {
                    if ((i + 1) % 2 == 1) // Posição ímpar (1-based)
                        result.Add(caches[i]);
                }
            }
            else if (filter == "even")
            {
                // Pegar caches em posições pares (2º, 4º, 6º, etc.)
                for (int i = 0; i < caches.Count; i++)
                {
                    if ((i + 1) % 2 == 0) // Posição par (1-based)
                        result.Add(caches[i]);
                }
            }
            else
            {
                // Filtro desconhecido, retornar todos
                return caches;
            }
            
            return result;
        }
        
        /// <summary>
        /// Valida se uma especificação de cache range é válida
        /// </summary>
        public static bool IsValid(string rangeSpec, int totalCaches, out string? error)
        {
            error = null;
            
            try
            {
                var caches = Parse(rangeSpec, totalCaches);
                
                if (caches.Count == 0)
                {
                    error = "No valid caches in range";
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        
        /// <summary>
        /// Retorna uma descrição amigável do range
        /// </summary>
        public static string Describe(string rangeSpec, int totalCaches)
        {
            try
            {
                var caches = Parse(rangeSpec, totalCaches);
                
                if (caches.Count == 0)
                    return "No caches";
                else if (caches.Count == 1)
                    return $"Cache {caches[0]}";
                else if (caches.Count <= 10)
                    return $"Caches {string.Join(", ", caches)}";
                else
                    return $"{caches.Count} caches: {string.Join(", ", caches.Take(5))}... and {caches.Count - 5} more";
            }
            catch
            {
                return "Invalid range";
            }
        }
    }
}