using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FilterPDF.Services;
using Newtonsoft.Json;

namespace FilterPDF
{
    /// <summary>
    /// Command to identify specific document types within PDFs
    /// </summary>
    public class FpdfDocTypesCommand
    {
        private readonly Dictionary<string, string> outputOptions;
        private readonly Dictionary<string, string> filterOptions;
        private PDFAnalysisResult analysisResult;
        private readonly int cacheIndex;
        private readonly CommandExecutor commandExecutor;

        public FpdfDocTypesCommand(
            PDFAnalysisResult result, 
            Dictionary<string, string> outputOpts, 
            Dictionary<string, string> filterOpts,
            int index,
            CommandExecutor executor)
        {
            analysisResult = result;
            outputOptions = outputOpts ?? new Dictionary<string, string>();
            filterOptions = filterOpts ?? new Dictionary<string, string>();
            cacheIndex = index;
            commandExecutor = executor;
        }

        public void Execute()
        {
            var fileName = System.IO.Path.GetFileName(analysisResult.FilePath) ?? "cache file";
            var format = outputOptions.ContainsKey("-F") ? outputOptions["-F"].ToString() : "txt";
            
            // Só mostrar mensagem de análise se não for CSV
            if (format.ToLower() != "csv")
            {
                Console.WriteLine($"Analyzing DOCUMENT TYPES in: {fileName}");
            }
            
            var docType = filterOptions.ContainsKey("--type") ? filterOptions["--type"].ToLower() : "despacho2025";

            switch (docType)
            {
                case "despacho2025":
                    IdentifyDespachoHonorarios();
                    break;
                case "peticao":
                    IdentifyPeticao();
                    break;
                case "sentenca":
                    IdentifySentenca();
                    break;
                case "laudo":
                    IdentifyLaudoPericial();
                    break;
                default:
                    Console.WriteLine($"Unknown document type: {docType}");
                    ShowAvailableTypes();
                    break;
            }
        }

        private void ShowAvailableTypes()
        {
            Console.WriteLine("\nAvailable document types:");
            Console.WriteLine("  despacho2025  - Despachos de autorização de pagamento de honorários periciais (TJPB 2025)");
            Console.WriteLine("  peticao       - Petições iniciais e intermediárias");
            Console.WriteLine("  sentenca      - Sentenças judiciais");
            Console.WriteLine("  laudo         - Laudos periciais");
            Console.WriteLine("\nUsage: fpdf [cache] doctypes --type despacho2025");
        }

        private void IdentifyDespachoHonorarios()
        {
            var matches = new List<DocumentTypeMatch>();
            
            foreach (var page in analysisResult.Pages)
            {
                var score = 0.0;
                var reasons = new List<string>();
                
                // CRITÉRIO 1: Estrutura básica (30% peso)
                if (page.TextInfo.WordCount >= 400 && page.TextInfo.WordCount <= 600)
                {
                    score += 0.10;
                    reasons.Add($"Word count in range: {page.TextInfo.WordCount}");
                }
                
                if (page.Resources?.Images?.Count == 1)
                {
                    var img = page.Resources.Images.First();
                    if (img.Width >= 60 && img.Width <= 100 && img.Height >= 60 && img.Height <= 100)
                    {
                        score += 0.10;
                        reasons.Add($"Logo image found: {img.Width}x{img.Height}");
                    }
                }
                
                if (page.Annotations?.Count == 0)
                {
                    score += 0.05;
                    reasons.Add("No annotations (clean document)");
                }
                
                if (page.TextInfo?.Fonts?.Any(f => f.Name.Contains("TimesNewRoman")) == true)
                {
                    score += 0.05;
                    reasons.Add("Official font (TimesNewRoman)");
                }
                
                // CRITÉRIO 2: Padrões textuais específicos (70% peso)
                var text = page.TextInfo?.PageText ?? "";
                
                // Cabeçalho institucional (obrigatório)
                if (Regex.IsMatch(text, @"PODER JUDICIÁRIO.*TRIBUNAL DE JUSTIÇA", RegexOptions.IgnoreCase))
                {
                    score += 0.15;
                    reasons.Add("Official header: TJPB");
                }
                
                // Identificador único do despacho DIESP
                if (Regex.IsMatch(text, @"Despacho DIESP n[ºo°]", RegexOptions.IgnoreCase))
                {
                    score += 0.20;
                    reasons.Add("DIESP dispatch identifier");
                }
                
                // Termos essenciais para pagamento de honorários
                if (text.Contains("requisição de pagamento de honorários", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("pagamento de honorários", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.15;
                    reasons.Add("Payment requisition text");
                }
                
                // Menção ao perito
                if (Regex.IsMatch(text, @"arbitrados em favor do Perito|favor do.*Perito", RegexOptions.IgnoreCase))
                {
                    score += 0.10;
                    reasons.Add("Expert payment reference");
                }
                
                // Sistema SIGHOP (muito específico)
                if (text.Contains("SIGHOP", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.15;
                    reasons.Add("SIGHOP system mentioned");
                }
                
                // Resolução específica
                if (text.Contains("Resolução 09/2017", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Resolução 232", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.10;
                    reasons.Add("Legal resolution reference");
                }
                
                // Referência SEI no rodapé
                if (Regex.IsMatch(text, @"SEI \d{6}-\d{2}\.\d{4}\.\d\.\d{2} / pg\. \d+"))
                {
                    score += 0.05;
                    reasons.Add("SEI reference in footer");
                }
                
                // Se score >= 0.70, é muito provável ser um despacho de honorários
                if (score >= 0.70)
                {
                    matches.Add(new DocumentTypeMatch
                    {
                        PageNumber = page.PageNumber,
                        DocumentType = "Despacho DIESP - Autorização de Pagamento de Honorários",
                        ConfidenceScore = score,
                        MatchReasons = reasons,
                        ExtractedData = ExtractDespachoData(page)
                    });
                }
            }
            
            OutputResults(matches);
        }

        // Regex compilados e cacheados para performance máxima
        private static readonly Regex CleanTextRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex SeiRegex = new Regex(@"SEI[:\s]*(\d{6}-\d{2}\.\d{4}\.\d\.\d{2})", RegexOptions.Compiled);
        private static readonly Regex DespachoRegex = new Regex(@"Despacho DIESP n[ºo°]\s*(\d+/\d{4})", RegexOptions.Compiled);
        private static readonly Regex ProcessoRegex = new Regex(@"(\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4})", RegexOptions.Compiled);
        private static readonly Regex ValorRegex = new Regex(@"valor de R\$\s*([\d.,]+)|R\$\s*([\d.,]+)\s*\([^)]*honorários", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CPFRegex = new Regex(@"CPF[:\s]*([\d]{3}\.[\d]{3}\.[\d]{3}-[\d]{2}|[\d]{11})", RegexOptions.Compiled);
        private static readonly Regex PISRegex = new Regex(@"PIS/PASEP[:\s]*([\d]+)", RegexOptions.Compiled);
        private static readonly Regex NascimentoRegex = new Regex(@"nascid[oa]\s+em\s+(\d{2}/\d{2}/\d{4})", RegexOptions.Compiled);
        private static readonly Regex CNPJRegex = new Regex(@"CNPJ[:\s]*([\d]{2}\.[\d]{3}\.[\d]{3}/[\d]{4}-[\d]{2}|[\d]{14})", RegexOptions.Compiled);
        
        // Regex combinado para perito (muito mais eficiente que loops)
        private static readonly Regex PeritoCombinadoRegex = new Regex(
            @"(?:(?:Perito|PERITO)[^,\n]*,?\s*([A-ZÀ-Ú][a-zà-ú]+(?: [A-ZÀ-Ú][a-zà-ú]+)+)(?:,?\s*CPF)|" +
            @"arbitrados em favor d[eo]\s*(?:Perito\s*)?([A-ZÀ-Ú][a-zà-ú]+(?: [A-ZÀ-Ú][a-zà-ú]+)+)|" +
            @"(?:Engenheiro|Médico|Perito)[^,\n]*,?\s*([A-ZÀ-Ú][a-zà-ú]+(?: [A-ZÀ-Ú][a-zà-ú]+)+)(?:,?\s*(?:CPF|nascid))|" +
            @"PERITO:\s*([A-ZÀ-Ú][a-zà-ú]+(?: [A-ZÀ-Ú][a-zà-ú]+)+)|" +
            @"Perito Judicial\s*([A-ZÀ-Ú][a-zà-ú]+(?: [A-ZÀ-Ú][a-zà-ú]+)+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
        // Regex combinado para parte autora
        private static readonly Regex AutorCombinadoRegex = new Regex(
            @"(?:movida por\s*([A-ZÀ-Ú][A-ZÀ-Ú\s]+(?:\s+[A-ZÀ-Ú]+)*)|" +
            @"AUTOR[:\s]*([A-ZÀ-Ú][A-ZÀ-Ú\s]+(?:\s+[A-ZÀ-Ú]+)*)|" +
            @"Ação n[ºo°][^,]+,\s*movida por\s*([^,]+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
        // Regex combinado para parte ré
        private static readonly Regex ReuCombinadoRegex = new Regex(
            @"(?:em face d[eo]\s*([A-ZÀ-Ú][^,\n]+)|" +
            @"RÉU[:\s]*([A-ZÀ-Ú][^,\n]+)|" +
            @"MUNICIPIO DE\s*([A-ZÀ-Ú]+(?:\s+[A-ZÀ-Ú]+)*))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
        // Regex combinado para comarca
        private static readonly Regex ComarcaCombinadoRegex = new Regex(
            @"(?:(?:Comarca|COMARCA) d[ea]\s*([A-ZÀ-Ú][a-zà-ú]+(?:\s+[A-ZÀ-Ú][a-zà-ú]+)*)|" +
            @"Comarca de\s+([A-ZÀ-Ú][a-zà-ú]+(?:\s+[A-ZÀ-Ú][a-zà-ú]+)*)|" +
            @"COMARCA:\s*([A-ZÀ-Ú][a-zà-ú]+(?:\s+[A-ZÀ-Ú][a-zà-ú]+)*))"   ,
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
        // Regex combinado para juízo/vara
        private static readonly Regex JuizoCombinadoRegex = new Regex(
            @"(?:(\d+[ªº]?\s*Vara[^,\n]*)|" +
            @"(Vara\s+[^,\n]+)|" +
            @"JUÍZO:\s*([^,\n]+)|" +
            @"perante o\s*([^,\n]*Vara[^,\n]*)|" +
            @"Juízo da\s*([^,\n]*Vara[^,\n]*))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
        // Regex combinado para especialidade
        private static readonly Regex EspecialidadeCombinadoRegex = new Regex(
            @"(?:(?:especialidad[ea]|ESPECIALIDADE)[:\s]*([^,\n]+)|" +
            @"Perito\s+(?:em\s+)?([A-Za-z]+(?:\s*[/-]\s*[A-Za-z]+)*)|" +
            @"(?:Engenheiro|Médico|Contador|Advogado|Economista)\s+([A-Za-z]+)|" +
            @"(GRAFOSCOPIA|MEDICINA|ODONTOLOGIA|ENGENHARIA|CONTABILIDADE|ECONOMIA))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
        // Regex combinado para tipo de perícia
        private static readonly Regex TipoPericiaCombinadoRegex = new Regex(
            @"(?:(?:ESPÉCIE DE PERÍCIA|tipo de perícia)[:\s]*([^,\n]+)|" +
            @"perícia\s+([a-záêçõ]+)|" +
            @"PERÍCIA\s+([A-ZÁÊÇÕ]+)|" +
            @"(grafoscópica|médica|contábil|técnica|judicial))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
        // Regex combinado para data de autorização
        private static readonly Regex DataAutorizacaoCombinadoRegex = new Regex(
            @"(?:Data da Autorização[:\s]*(\d{2}/\d{2}/\d{4})|" +
            @"autorizado em\s*(\d{2}/\d{2}/\d{4})|" +
            @"João Pessoa,\s*(\d{2} de \w+ de \d{4})|" +
            @"Em\s*(\d{2}/\d{2}/\d{4}))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private Dictionary<string, string> ExtractDespachoData(PageAnalysis page)
        {
            var data = new Dictionary<string, string>();
            var text = page.TextInfo?.PageText ?? "";
            
            // Remover quebras de linha extras para facilitar matching
            var cleanText = CleanTextRegex.Replace(text, " ");
            
            // Extrair número do processo administrativo SEI (MUITO IMPORTANTE)
            var seiMatch = SeiRegex.Match(cleanText);
            if (seiMatch.Success)
                data["ProcessoAdministrativo"] = seiMatch.Groups[1].Value;
            
            // Extrair número do despacho
            var despachoMatch = DespachoRegex.Match(cleanText);
            if (despachoMatch.Success)
                data["NumeroDespacho"] = despachoMatch.Groups[1].Value;
            
            // Extrair número do processo judicial (formato completo)
            var processoMatch = ProcessoRegex.Match(cleanText);
            if (processoMatch.Success)
                data["NumeroProcesso"] = processoMatch.Groups[1].Value;
            
            // Extrair valor dos honorários (melhorado para pegar o valor correto)
            var valorMatch = ValorRegex.Match(cleanText);
            if (valorMatch.Success)
            {
                data["ValorHonorarios"] = valorMatch.Groups[1].Success ? valorMatch.Groups[1].Value : valorMatch.Groups[2].Value;
            }
            
            // Extrair nome do perito usando regex combinado (muito mais rápido)
            var peritoMatch = PeritoCombinadoRegex.Match(cleanText);
            if (peritoMatch.Success)
            {
                // Pegar o primeiro grupo que teve match
                for (int i = 1; i < peritoMatch.Groups.Count; i++)
                {
                    if (peritoMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(peritoMatch.Groups[i].Value))
                    {
                        data["NomePerito"] = peritoMatch.Groups[i].Value.Trim();
                        break;
                    }
                }
            }
            
            // Extrair CPF do perito (formato completo)
            var cpfMatch = CPFRegex.Match(cleanText);
            if (cpfMatch.Success)
            {
                var cpf = cpfMatch.Groups[1].Value;
                // Formatar CPF se necessário
                if (!cpf.Contains(".") && cpf.Length == 11)
                {
                    cpf = $"{cpf.Substring(0,3)}.{cpf.Substring(3,3)}.{cpf.Substring(6,3)}-{cpf.Substring(9,2)}";
                }
                data["CPFPerito"] = cpf;
            }
            
            // Extrair PIS/PASEP
            var pisMatch = PISRegex.Match(cleanText);
            if (pisMatch.Success)
                data["PIS_PASEP"] = pisMatch.Groups[1].Value;
            
            // Extrair data de nascimento
            var nascimentoMatch = NascimentoRegex.Match(cleanText);
            if (nascimentoMatch.Success)
                data["DataNascimento"] = nascimentoMatch.Groups[1].Value;
            
            // Extrair nome da parte autora usando regex combinado
            var autorMatch = AutorCombinadoRegex.Match(cleanText);
            if (autorMatch.Success)
            {
                for (int i = 1; i < autorMatch.Groups.Count; i++)
                {
                    if (autorMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(autorMatch.Groups[i].Value))
                    {
                        var autor = CleanTextRegex.Replace(autorMatch.Groups[i].Value, " ").Trim();
                        if (autor.Length > 0 && !autor.Contains("CPF") && !autor.Contains("CNPJ"))
                        {
                            data["ParteAutora"] = autor;
                            break;
                        }
                    }
                }
            }
            
            // CPF da parte autora removido por performance - usar apenas CPFPerito
            
            // Extrair parte ré usando regex combinado
            var reuMatch = ReuCombinadoRegex.Match(cleanText);
            if (reuMatch.Success)
            {
                for (int i = 1; i < reuMatch.Groups.Count; i++)
                {
                    if (reuMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(reuMatch.Groups[i].Value))
                    {
                        var reu = CleanTextRegex.Replace(reuMatch.Groups[i].Value, " ").Trim();
                        if (reu.Length > 0 && !reu.Contains("perante"))
                        {
                            data["ParteRe"] = reu;
                            break;
                        }
                    }
                }
            }
            
            // Extrair CNPJ da parte ré
            var cnpjMatch = CNPJRegex.Match(cleanText);
            if (cnpjMatch.Success)
            {
                data["CNPJReu"] = cnpjMatch.Groups[1].Value;
            }
            
            // Extrair comarca usando regex combinado
            var comarcaMatch = ComarcaCombinadoRegex.Match(cleanText);
            if (comarcaMatch.Success)
            {
                for (int i = 1; i < comarcaMatch.Groups.Count; i++)
                {
                    if (comarcaMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(comarcaMatch.Groups[i].Value))
                    {
                        data["Comarca"] = comarcaMatch.Groups[i].Value;
                        break;
                    }
                }
            }
            
            // Extrair juízo/vara usando regex combinado
            var juizoMatch = JuizoCombinadoRegex.Match(cleanText);
            if (juizoMatch.Success)
            {
                for (int i = 1; i < juizoMatch.Groups.Count; i++)
                {
                    if (juizoMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(juizoMatch.Groups[i].Value))
                    {
                        data["Juizo"] = juizoMatch.Groups[i].Value.Trim();
                        break;
                    }
                }
            }
            
            // Extrair especialidade usando regex combinado
            var espMatch = EspecialidadeCombinadoRegex.Match(cleanText);
            if (espMatch.Success)
            {
                for (int i = 1; i < espMatch.Groups.Count; i++)
                {
                    if (espMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(espMatch.Groups[i].Value))
                    {
                        data["Especialidade"] = espMatch.Groups[i].Value.Trim();
                        break;
                    }
                }
                if (!data.ContainsKey("Especialidade") && espMatch.Groups[0].Success)
                    data["Especialidade"] = espMatch.Groups[0].Value.Trim();
            }
            
            // Extrair tipo/espécie de perícia usando regex combinado
            var tipoMatch = TipoPericiaCombinadoRegex.Match(cleanText);
            if (tipoMatch.Success)
            {
                for (int i = 1; i < tipoMatch.Groups.Count; i++)
                {
                    if (tipoMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(tipoMatch.Groups[i].Value))
                    {
                        data["EspeciePericia"] = tipoMatch.Groups[i].Value.Trim();
                        break;
                    }
                }
                if (!data.ContainsKey("EspeciePericia") && tipoMatch.Groups[0].Success)
                    data["EspeciePericia"] = tipoMatch.Groups[0].Value.Trim();
            }
            
            // Extrair data de autorização usando regex combinado
            var dataMatch = DataAutorizacaoCombinadoRegex.Match(cleanText);
            if (dataMatch.Success)
            {
                for (int i = 1; i < dataMatch.Groups.Count; i++)
                {
                    if (dataMatch.Groups[i].Success && !string.IsNullOrWhiteSpace(dataMatch.Groups[i].Value))
                    {
                        data["DataAutorizacao"] = dataMatch.Groups[i].Value;
                        break;
                    }
                }
            }
            
            return data;
        }

        private void IdentifyPeticao()
        {
            Console.WriteLine("Petição identification not yet implemented");
        }

        private void IdentifySentenca()
        {
            Console.WriteLine("Sentença identification not yet implemented");
        }

        private void IdentifyLaudoPericial()
        {
            Console.WriteLine("Laudo pericial identification not yet implemented");
        }

        private void OutputResults(List<DocumentTypeMatch> matches)
        {
            var format = outputOptions.ContainsKey("-F") ? outputOptions["-F"].ToString() : "txt";
            
            // Para CSV e JSON, só mostrar quando há resultados
            if (matches.Count == 0)
            {
                if (format.ToLower() != "csv" && format.ToLower() != "json")
                {
                    Console.WriteLine("\nNo matching documents found for the specified type.");
                }
                return;
            }
            
            switch (format.ToLower())
            {
                case "json":
                    Console.WriteLine(JsonConvert.SerializeObject(matches, Formatting.Indented));
                    break;
                    
                case "csv":
                    OutputAsCsv(matches);
                    break;
                    
                default:
                    Console.WriteLine($"\nFound {matches.Count} matching document(s):\n");
                    foreach (var match in matches)
                    {
                        Console.WriteLine($"PAGE {match.PageNumber}:");
                        Console.WriteLine($"  Type: {match.DocumentType}");
                        Console.WriteLine($"  Confidence: {match.ConfidenceScore:P0}");
                        Console.WriteLine($"  Match reasons:");
                        foreach (var reason in match.MatchReasons)
                        {
                            Console.WriteLine($"    - {reason}");
                        }
                        if (match.ExtractedData.Count > 0)
                        {
                            Console.WriteLine($"  Extracted data:");
                            foreach (var kvp in match.ExtractedData)
                            {
                                Console.WriteLine($"    {kvp.Key}: {kvp.Value}");
                            }
                        }
                        Console.WriteLine();
                    }
                    break;
            }
        }

        private void OutputAsCsv(List<DocumentTypeMatch> matches)
        {
            // Cabeçalho completo com todos os campos importantes
            Console.WriteLine("ProcessoAdmin,Juizo,Comarca,NumeroProcesso,Promovente,Promovido,NomePerito,CPF_CNPJ,Especialidade,EspeciePericia,ValorHonorarios,DataAutorizacao,Page");
            
            foreach (var match in matches)
            {
                var data = match.ExtractedData;
                
                // Montar linha CSV com todos os campos essenciais
                Console.WriteLine(
                    $"{data.GetValueOrDefault("ProcessoAdministrativo", "")}," +
                    $"\"{data.GetValueOrDefault("Juizo", "")}\"," +
                    $"\"{data.GetValueOrDefault("Comarca", "")}\"," +
                    $"{data.GetValueOrDefault("NumeroProcesso", "")}," +
                    $"\"{data.GetValueOrDefault("ParteAutora", data.GetValueOrDefault("Promovente", ""))}\"," +
                    $"\"{data.GetValueOrDefault("ParteRe", data.GetValueOrDefault("Promovido", ""))}\"," +
                    $"\"{data.GetValueOrDefault("NomePerito", "")}\"," +
                    $"{data.GetValueOrDefault("CPFPerito", "")}," +
                    $"\"{data.GetValueOrDefault("Especialidade", "")}\"," +
                    $"\"{data.GetValueOrDefault("EspeciePericia", "")}\"," +
                    $"{data.GetValueOrDefault("ValorHonorarios", "")}," +
                    $"{data.GetValueOrDefault("DataAutorizacao", "")}," +
                    $"{match.PageNumber}"
                );
            }
        }
    }

    public class DocumentTypeMatch
    {
        public int PageNumber { get; set; }
        public string DocumentType { get; set; }
        public double ConfidenceScore { get; set; }
        public List<string> MatchReasons { get; set; } = new List<string>();
        public Dictionary<string, string> ExtractedData { get; set; } = new Dictionary<string, string>();
    }
}