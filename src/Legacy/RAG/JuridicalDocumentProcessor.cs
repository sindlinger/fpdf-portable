using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Security.Cryptography;

namespace FilterPDF.RAG
{
    /// <summary>
    /// Processador especializado para documentos jurídicos
    /// Otimizado para máxima precisão em RAG systems
    /// </summary>
    public class JuridicalDocumentProcessor
    {
        /// <summary>
        /// Estrutura enriquecida para documentos jurídicos
        /// </summary>
        public class JuridicalDocument
        {
            // Identificadores únicos e validados
            public string DocumentId { get; set; } = "";
            public string ProcessoNumero { get; set; } = "";
            public string ProcessoNumeroNormalizado { get; set; } = "";
            public string ProcessoHash { get; set; } = "";
            
            // Metadados estruturados
            public ProcessMetadata Metadata { get; set; } = new ProcessMetadata();
            
            // Entidades extraídas e validadas
            public JuridicalEntities Entities { get; set; } = new JuridicalEntities();
            
            // Chunks inteligentes para embedding
            public List<IntelligentChunk> Chunks { get; set; } = new List<IntelligentChunk>();
            
            // Índices auxiliares para busca exata
            public SearchIndexes Indexes { get; set; } = new SearchIndexes();
        }

        public class ProcessMetadata
        {
            public string Tribunal { get; set; } = "";
            public string Vara { get; set; } = "";
            public string Comarca { get; set; } = "";
            public string TipoProcesso { get; set; } = "";
            public string ClasseProcessual { get; set; } = "";
            public string Assunto { get; set; } = "";
            public DateTime? DataDistribuicao { get; set; }
            public DateTime? DataUltimaMovimentacao { get; set; }
            public string Status { get; set; } = "";
            public decimal? ValorCausa { get; set; }
            public int NumeroPaginas { get; set; }
            public string DocumentType { get; set; } = ""; // Sentença, Petição, Despacho, etc.
        }

        public class JuridicalEntities
        {
            public List<Parte> Partes { get; set; } = new List<Parte>();
            public List<Advogado> Advogados { get; set; } = new List<Advogado>();
            public Juiz? Juiz { get; set; }
            public List<string> CPFs { get; set; } = new List<string>();
            public List<string> CNPJs { get; set; } = new List<string>();
            public List<DateTime> DatasRelevantes { get; set; } = new List<DateTime>();
            public List<ValorMonetario> Valores { get; set; } = new List<ValorMonetario>();
            public List<string> NumerosRelacionados { get; set; } = new List<string>(); // Outros processos citados
        }

        public class Parte
        {
            public string Nome { get; set; } = "";
            public string NomeNormalizado { get; set; } = "";
            public string Tipo { get; set; } = ""; // Autor, Réu, Interessado
            public string Documento { get; set; } = ""; // CPF/CNPJ
            public string DocumentoTipo { get; set; } = "";
            public Advogado? Representante { get; set; }
        }

        public class Advogado
        {
            public string Nome { get; set; } = "";
            public string OAB { get; set; } = "";
            public string OABNormalizado { get; set; } = "";
            public string Estado { get; set; } = "";
        }

        public class Juiz
        {
            public string Nome { get; set; } = "";
            public string Cargo { get; set; } = "";
        }

        public class ValorMonetario
        {
            public decimal Valor { get; set; }
            public string Contexto { get; set; } = ""; // Valor da causa, multa, honorários
            public int PaginaReferencia { get; set; }
        }

        public class IntelligentChunk
        {
            public string ChunkId { get; set; } = "";
            public string Content { get; set; } = "";
            public string ContentNormalizado { get; set; } = "";
            public int StartPage { get; set; }
            public int EndPage { get; set; }
            public string Section { get; set; } = ""; // Relatório, Fundamentação, Dispositivo
            public Dictionary<string, string> LocalContext { get; set; } = new Dictionary<string, string>();
            public List<string> ReferencedEntities { get; set; } = new List<string>();
            public double ImportanceScore { get; set; }
            public string ChunkType { get; set; } = ""; // narrative, legal_foundation, decision, evidence
        }

        public class SearchIndexes
        {
            // Índices exatos para busca precisa
            public Dictionary<string, List<int>> ExactTermIndex { get; set; } = new Dictionary<string, List<int>>();
            public Dictionary<string, string> NormalizedTerms { get; set; } = new Dictionary<string, string>();
            public List<string> UniqueIdentifiers { get; set; } = new List<string>();
            public string DocumentFingerprint { get; set; } = "";
        }

        /// <summary>
        /// Processa documento para máxima precisão em RAG
        /// </summary>
        public static JuridicalDocument ProcessForRAG(string fullText, string fileName, Dictionary<string, object>? metadata = null)
        {
            var doc = new JuridicalDocument
            {
                DocumentId = GenerateDocumentId(fullText, fileName)
            };

            // 1. Extração e validação do número do processo
            ExtractAndValidateProcessNumber(fullText, doc);

            // 2. Extração de metadados estruturados
            ExtractMetadata(fullText, doc, metadata);

            // 3. Extração de entidades nomeadas (NER)
            ExtractEntities(fullText, doc);

            // 4. Criação de chunks inteligentes
            CreateIntelligentChunks(fullText, doc);

            // 5. Criação de índices de busca exata
            BuildSearchIndexes(fullText, doc);

            // 6. Validação e normalização final
            ValidateAndNormalize(doc);

            return doc;
        }

        private static void ExtractAndValidateProcessNumber(string text, JuridicalDocument doc)
        {
            // Padrão CNJ: NNNNNNN-DD.AAAA.J.TT.OOOO
            var cnvPattern = @"\b(\d{7})-(\d{2})\.(\d{4})\.(\d)\.(\d{2})\.(\d{4})\b";
            var match = Regex.Match(text, cnvPattern);
            
            if (match.Success)
            {
                doc.ProcessoNumero = match.Value;
                doc.ProcessoNumeroNormalizado = NormalizeProcessNumber(match.Value);
                doc.ProcessoHash = GenerateHash(doc.ProcessoNumeroNormalizado);
                
                // Valida dígito verificador
                if (!ValidateCNJNumber(match.Value))
                {
                    doc.Metadata.Status = "Número de processo com dígito verificador inválido";
                }
            }
            else
            {
                // Tenta outros padrões
                var alternativePatterns = new[]
                {
                    @"Processo\s*n[°º]?\s*([\d\.\-\/]+)",
                    @"Autos\s*n[°º]?\s*([\d\.\-\/]+)",
                    @"N[°º]\s*([\d\.\-\/]+)"
                };
                
                foreach (var pattern in alternativePatterns)
                {
                    match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        doc.ProcessoNumero = match.Groups[1].Value;
                        doc.ProcessoNumeroNormalizado = NormalizeProcessNumber(match.Groups[1].Value);
                        doc.ProcessoHash = GenerateHash(doc.ProcessoNumeroNormalizado);
                        break;
                    }
                }
            }
        }

        private static void ExtractEntities(string text, JuridicalDocument doc)
        {
            // Extração de partes
            ExtractPartes(text, doc.Entities);
            
            // Extração de advogados
            ExtractAdvogados(text, doc.Entities);
            
            // Extração de juiz
            ExtractJuiz(text, doc.Entities);
            
            // Extração de documentos (CPF/CNPJ)
            ExtractDocumentos(text, doc.Entities);
            
            // Extração de valores
            ExtractValores(text, doc.Entities);
            
            // Extração de datas
            ExtractDatas(text, doc.Entities);
            
            // Extração de processos relacionados
            ExtractProcessosRelacionados(text, doc.Entities);
        }

        private static void CreateIntelligentChunks(string text, JuridicalDocument doc)
        {
            // Estratégia de chunking específica para documentos jurídicos
            var sections = IdentifyDocumentSections(text);
            
            foreach (var section in sections)
            {
                // Cria chunks com overlap e contexto
                var chunks = CreateContextualChunks(section.Content, section.Type);
                
                foreach (var chunk in chunks)
                {
                    var intelligentChunk = new IntelligentChunk
                    {
                        ChunkId = GenerateChunkId(doc.DocumentId, chunk),
                        Content = chunk.Text,
                        ContentNormalizado = NormalizeContent(chunk.Text),
                        Section = section.Type,
                        StartPage = chunk.StartPage,
                        EndPage = chunk.EndPage,
                        ChunkType = DetermineChunkType(chunk.Text),
                        ImportanceScore = CalculateImportance(chunk.Text, section.Type)
                    };
                    
                    // Adiciona contexto local
                    intelligentChunk.LocalContext["processo"] = doc.ProcessoNumero;
                    intelligentChunk.LocalContext["secao"] = section.Type;
                    intelligentChunk.LocalContext["documento_tipo"] = doc.Metadata.DocumentType;
                    
                    // Identifica entidades referenciadas
                    intelligentChunk.ReferencedEntities = ExtractReferencedEntities(chunk.Text, doc.Entities);
                    
                    doc.Chunks.Add(intelligentChunk);
                }
            }
        }

        private static void BuildSearchIndexes(string text, JuridicalDocument doc)
        {
            // Cria índice de termos exatos para busca precisa
            var terms = ExtractImportantTerms(text);
            
            foreach (var term in terms)
            {
                var normalized = NormalizeTerm(term);
                doc.Indexes.NormalizedTerms[term] = normalized;
                
                // Encontra todas as ocorrências
                var positions = FindAllOccurrences(text, term);
                doc.Indexes.ExactTermIndex[normalized] = positions;
            }
            
            // Adiciona identificadores únicos
            doc.Indexes.UniqueIdentifiers.Add(doc.ProcessoNumero);
            doc.Indexes.UniqueIdentifiers.AddRange(doc.Entities.CPFs);
            doc.Indexes.UniqueIdentifiers.AddRange(doc.Entities.CNPJs);
            doc.Indexes.UniqueIdentifiers.AddRange(doc.Entities.Advogados.Select(a => a.OAB));
            
            // Gera fingerprint do documento
            doc.Indexes.DocumentFingerprint = GenerateDocumentFingerprint(doc);
        }

        /// <summary>
        /// Prepara documento para ingestão no RAG com máxima precisão
        /// </summary>
        public static object PrepareForRAGIngestion(JuridicalDocument doc)
        {
            return new
            {
                // Identificação única e inequívoca
                id = doc.DocumentId,
                processo_numero = doc.ProcessoNumero,
                processo_numero_normalizado = doc.ProcessoNumeroNormalizado,
                processo_hash = doc.ProcessoHash,
                
                // Metadados estruturados para filtragem precisa
                metadata = new
                {
                    tribunal = doc.Metadata.Tribunal,
                    vara = doc.Metadata.Vara,
                    comarca = doc.Metadata.Comarca,
                    tipo_processo = doc.Metadata.TipoProcesso,
                    classe_processual = doc.Metadata.ClasseProcessual,
                    assunto = doc.Metadata.Assunto,
                    data_distribuicao = doc.Metadata.DataDistribuicao?.ToString("yyyy-MM-dd"),
                    data_ultima_movimentacao = doc.Metadata.DataUltimaMovimentacao?.ToString("yyyy-MM-dd"),
                    status = doc.Metadata.Status,
                    valor_causa = doc.Metadata.ValorCausa,
                    document_type = doc.Metadata.DocumentType,
                    
                    // Dados para deduplicação
                    fingerprint = doc.Indexes.DocumentFingerprint,
                    unique_identifiers = doc.Indexes.UniqueIdentifiers
                },
                
                // Entidades para busca exata
                entities = new
                {
                    partes = doc.Entities.Partes.Select(p => new
                    {
                        nome = p.Nome,
                        nome_normalizado = p.NomeNormalizado,
                        tipo = p.Tipo,
                        documento = p.Documento,
                        documento_tipo = p.DocumentoTipo
                    }),
                    advogados = doc.Entities.Advogados.Select(a => new
                    {
                        nome = a.Nome,
                        oab = a.OAB,
                        oab_normalizado = a.OABNormalizado,
                        estado = a.Estado
                    }),
                    juiz = doc.Entities.Juiz != null ? new
                    {
                        nome = doc.Entities.Juiz.Nome,
                        cargo = doc.Entities.Juiz.Cargo
                    } : null,
                    cpfs = doc.Entities.CPFs,
                    cnpjs = doc.Entities.CNPJs,
                    valores = doc.Entities.Valores.Select(v => new
                    {
                        valor = v.Valor,
                        contexto = v.Contexto,
                        pagina = v.PaginaReferencia
                    }),
                    processos_relacionados = doc.Entities.NumerosRelacionados
                },
                
                // Chunks otimizados para embedding
                chunks = doc.Chunks.Select(c => new
                {
                    chunk_id = c.ChunkId,
                    content = c.Content,
                    content_normalized = c.ContentNormalizado,
                    section = c.Section,
                    chunk_type = c.ChunkType,
                    importance_score = c.ImportanceScore,
                    context = c.LocalContext,
                    referenced_entities = c.ReferencedEntities,
                    pages = new { start = c.StartPage, end = c.EndPage }
                }),
                
                // Índices para busca híbrida
                search_indexes = new
                {
                    exact_terms = doc.Indexes.ExactTermIndex,
                    normalized_terms = doc.Indexes.NormalizedTerms
                }
            };
        }

        // Métodos auxiliares
        private static string NormalizeProcessNumber(string number)
        {
            // Remove todos os caracteres não numéricos
            return Regex.Replace(number, @"[^\d]", "");
        }

        private static bool ValidateCNJNumber(string number)
        {
            // Implementa validação do dígito verificador CNJ
            var clean = NormalizeProcessNumber(number);
            if (clean.Length != 20) return false;
            
            // Cálculo do dígito verificador CNJ
            var origem = clean.Substring(0, 7);
            var ano = clean.Substring(9, 4);
            var segmento = clean.Substring(13, 1);
            var tribunal = clean.Substring(14, 2);
            var numeroOrigem = clean.Substring(16, 4);
            
            var baseCalculo = origem + ano + segmento + tribunal + numeroOrigem;
            var resto = CalcularModulo97(baseCalculo);
            var digitoCalculado = 98 - resto;
            
            var digitoInformado = int.Parse(clean.Substring(7, 2));
            
            return digitoCalculado == digitoInformado;
        }

        private static int CalcularModulo97(string numero)
        {
            // Implementação do cálculo módulo 97 para CNJ
            var resultado = 0;
            foreach (var c in numero)
            {
                resultado = (resultado * 10 + (c - '0')) % 97;
            }
            return resultado;
        }

        private static string GenerateHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(bytes);
            }
        }

        private static string GenerateDocumentId(string text, string fileName)
        {
            var combined = fileName + text.GetHashCode().ToString();
            return GenerateHash(combined);
        }

        private static string GenerateChunkId(string docId, dynamic chunk)
        {
            return GenerateHash(docId + chunk.GetHashCode().ToString());
        }

        private static string NormalizeContent(string content)
        {
            // Remove acentos, normaliza espaços, lowercase
            var normalized = RemoveAccents(content.ToLower());
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized.Trim();
        }

        private static string RemoveAccents(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string NormalizeTerm(string term)
        {
            return RemoveAccents(term.ToLower().Trim());
        }

        private static List<int> FindAllOccurrences(string text, string term)
        {
            var positions = new List<int>();
            var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            
            while (index != -1)
            {
                positions.Add(index);
                index = text.IndexOf(term, index + 1, StringComparison.OrdinalIgnoreCase);
            }
            
            return positions;
        }

        private static string GenerateDocumentFingerprint(JuridicalDocument doc)
        {
            var fingerprint = doc.ProcessoNumero + 
                             string.Join(",", doc.Entities.CPFs) +
                             string.Join(",", doc.Entities.CNPJs) +
                             doc.Metadata.DataDistribuicao?.ToString("yyyyMMdd");
            
            return GenerateHash(fingerprint);
        }

        // Stubs para métodos complexos (implementar conforme necessário)
        private static void ExtractMetadata(string text, JuridicalDocument doc, Dictionary<string, object>? metadata) { }
        private static void ExtractPartes(string text, JuridicalEntities entities) { }
        private static void ExtractAdvogados(string text, JuridicalEntities entities) { }
        private static void ExtractJuiz(string text, JuridicalEntities entities) { }
        private static void ExtractDocumentos(string text, JuridicalEntities entities) { }
        private static void ExtractValores(string text, JuridicalEntities entities) { }
        private static void ExtractDatas(string text, JuridicalEntities entities) { }
        private static void ExtractProcessosRelacionados(string text, JuridicalEntities entities) { }
        private static List<dynamic> IdentifyDocumentSections(string text) { return new List<dynamic>(); }
        private static List<dynamic> CreateContextualChunks(string content, string type) { return new List<dynamic>(); }
        private static string DetermineChunkType(string text) { return "narrative"; }
        private static double CalculateImportance(string text, string sectionType) { return 0.5; }
        private static List<string> ExtractReferencedEntities(string text, JuridicalEntities entities) { return new List<string>(); }
        private static List<string> ExtractImportantTerms(string text) { return new List<string>(); }
        private static void ValidateAndNormalize(JuridicalDocument doc) { }
    }
}