using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FilterPDF.Utils;
using System.Text;
using System.Security.Cryptography;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Executa a etapa FPDF do pipeline-tjpb lendo as análises já salvas em raw_processes
    /// (camada hard/soft) e grava o bucket pipeline_processes_tjpb no Postgres.
    ///
    /// Uso:
    ///   fpdf pipeline-tjpb [--only-despachos] [--signer-contains <texto>] [--process <num>]
    ///
    /// Campos gerados por documento:
    /// - process, pdf_path
    /// - doc_label, start_page, end_page, doc_pages, total_pages
    /// - text (concatenação das páginas do documento)
    /// - fonts (nomes), images (quantidade), page_size, has_signature_image (heurística)
    /// </summary>
    public partial class PipelineTjpbCommand : Command
    {
        public override string Name => "pipeline-tjpb";
        public override string Description => "Etapa FPDF do pipeline-tjpb: consolida documentos em JSON";

        private LaudoHashDb? _hashDb;
        private string _hashDbPath = "";

        public override void Execute(string[] args)
        {
            try
            {
                bool onlyDespachos = false;
                string? signerContains = null;
                string? processFilter = null; // opcional: processar só um processo
                string? hashDbArg = null;
                // Sempre salva no Postgres padrão
                string pgUri = PgDocStore.DefaultPgUri;
                var analysesByProcess = new Dictionary<string, PDFAnalysisResult>();
                string noBookmarksDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "no_bookmarks");
                Directory.CreateDirectory(noBookmarksDir);
                double despachoThreshold = DespachoScorer.GetThreshold();
                // Localiza configs/fields e docid/layout_hashes.csv respeitando cwd ou pasta do binário.
                string cwd = Directory.GetCurrentDirectory();
                string exeBase = AppContext.BaseDirectory;
                string[] fieldsCandidates =
                {
                    Path.Combine(cwd, "src/PipelineTjpb/configs/fields"),
                    Path.Combine(cwd, "../src/PipelineTjpb/configs/fields"),
                    Path.Combine(cwd, "../../src/PipelineTjpb/configs/fields"),
                    Path.Combine(cwd, "configs/fields"), // fallback
                    Path.Combine(cwd, "../configs/fields"), // fallback
                    Path.Combine(cwd, "../../configs/fields"), // fallback
                    Path.GetFullPath(Path.Combine(exeBase, "../../../../src/PipelineTjpb/configs/fields")),
                    Path.GetFullPath(Path.Combine(exeBase, "../../../../configs/fields"))
                };
                var fieldScriptsPath = fieldsCandidates.FirstOrDefault(Directory.Exists);
                var fieldScripts = fieldScriptsPath != null
                    ? FieldScripts.LoadScripts(fieldScriptsPath)
                    : new List<FieldScript>();

                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--only-despachos") onlyDespachos = true;
                    if (args[i] == "--signer-contains" && i + 1 < args.Length) signerContains = args[i + 1];
                    if (args[i] == "--process" && i + 1 < args.Length) processFilter = args[i + 1];
                    if (args[i] == "--hash-db" && i + 1 < args.Length) hashDbArg = args[i + 1];
                }

                // Carrega hash DB (opcional)
                var hashCandidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(hashDbArg)) hashCandidates.Add(hashDbArg);
                hashCandidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "laudo_hashes.json"));
                hashCandidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "data", "reference", "laudo_hashes.json"));
                hashCandidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "data", "reference", "laudo_hashes.jsonl"));
                foreach (var hc in hashCandidates)
                {
                    if (File.Exists(hc))
                    {
                        _hashDb = LaudoHashDb.Load(hc);
                        if (_hashDb != null) { _hashDbPath = hc; break; }
                    }
                }

                // ---------- Lê análises do Postgres (raw_processes) ----------
                var allDocs = new List<Dictionary<string, object>>();
                var allDocsWords = new List<List<Dictionary<string, object>>>();

                var rows = PgAnalysisLoader.ListRawProcesses(pgUri);
                if (!string.IsNullOrWhiteSpace(processFilter))
                    rows = rows.Where(r => string.Equals(r.ProcessNumber, processFilter, StringComparison.OrdinalIgnoreCase)).ToList();

                if (rows.Count == 0)
                {
                    Console.WriteLine("[pipeline-tjpb] Nenhum processo encontrado em raw_processes.");
                    return;
                }

                foreach (var row in rows)
                {
                    try
                    {
                        var analysis = PgAnalysisLoader.Deserialize(row.Json);
                        if (analysis == null)
                        {
                            Console.Error.WriteLine($"[pipeline-tjpb] WARN {row.ProcessNumber}: raw_json inválido");
                            continue;
                        }

                        if (analysis.Pages == null || analysis.Pages.Count == 0)
                        {
                            Console.Error.WriteLine($"[pipeline-tjpb] WARN {row.ProcessNumber}: sem páginas no raw_json");
                            continue;
                        }

                        analysesByProcess[row.ProcessNumber] = analysis;
                        // Segmentação por bookmark desativada: usar o range completo já presente na camada raw
                        var docs = new List<DocumentBoundary>
                        {
                            new DocumentBoundary
                            {
                                StartPage = 1,
                                EndPage = analysis.DocumentInfo.TotalPages,
                                Title = SanitizeBookmarkTitle(Path.GetFileNameWithoutExtension(analysis.FilePath)),
                                RawTitle = Path.GetFileNameWithoutExtension(analysis.FilePath),
                                DetectedType = "full",
                                FirstPageText = analysis.Pages[0].TextInfo.PageText,
                                LastPageText = analysis.Pages[analysis.DocumentInfo.TotalPages - 1].TextInfo.PageText,
                                FullText = string.Join("\n", analysis.Pages.Select(p => p.TextInfo.PageText)),
                                Fonts = new HashSet<string>(analysis.Pages.SelectMany(p => p.TextInfo.Fonts.Select(f => f.Name)), StringComparer.OrdinalIgnoreCase),
                                PageSize = analysis.Pages.First().Size.GetPaperSize(),
                                HasSignatureImage = analysis.Pages.Any(p => p.Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false),
                                TotalWords = analysis.Pages.Sum(p => p.TextInfo.WordCount)
                            }
                        };

                        foreach (var d in docs)
                        {
                            var obj = BuildDocObject(d, analysis, row.Source, fieldScripts, despachoThreshold);
                            allDocs.Add(obj);
                            if (obj.ContainsKey("words"))
                            {
                                var w = obj["words"] as List<Dictionary<string, object>>;
                                if (w != null) allDocsWords.Add(w);
                            }

                            if (d.DetectedType == "anexo")
                            {
                                var anexosChildren = SplitAnexos(d, analysis, row.Source, fieldScripts, despachoThreshold);
                                allDocs.AddRange(anexosChildren);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[pipeline-tjpb] WARN {row.ProcessNumber}: {ex.Message}");
                    }
                }

            // Camada soft (sempre completa): agrupar todos os docs por processo
            var groupedAll = allDocs
                          .GroupBy(d => d.TryGetValue("process", out var p) ? p?.ToString() ?? "sem_processo" : "sem_processo")
                          .Select(g => new { process = g.Key, documents = g.ToList() })
                          .ToList();

            // Se algum processo ficou sem documentos (ex: PDF sem bookmarks), copia o PDF para logs/no_bookmarks e registra
            foreach (var kv in analysesByProcess)
            {
                bool hasDocs = groupedAll.Any(g => g.process == kv.Key && g.documents.Count > 0);
                if (!hasDocs)
                {
                    try
                    {
                        var src = kv.Value.FilePath;
                        var destPdf = Path.Combine(noBookmarksDir, Path.GetFileName(src));
                        File.Copy(src, destPdf, true);
                        var logPath = destPdf + ".txt";
                        File.WriteAllText(logPath, "Sem documentos gerados: não foram encontrados bookmarks válidos.");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[pipeline-tjpb] WARN ao registrar no_bookmarks: {ex.Message}");
                    }
                }
            }

            // Dedupe: só 1 laudo por processo (keep o maior em páginas), demais viram anexo
            foreach (var grp in groupedAll)
            {
                var laudos = grp.documents
                    .Where(d => string.Equals(d.TryGetValue("doc_type", out var t) ? t?.ToString() : "", "laudo", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (laudos.Count <= 1) continue;
                var keep = laudos
                    .OrderByDescending(d => Convert.ToInt32(d.TryGetValue("doc_pages", out var pg) ? pg : 0))
                    .ThenBy(d => Convert.ToInt32(d.TryGetValue("start_page", out var sp) ? sp : 0))
                    .First();
                foreach (var d in laudos)
                {
                    if (ReferenceEquals(d, keep)) continue;
                    d["doc_type"] = "anexo";
                    d["docid_reason"] = "laudo_dedup";
                }
            }

            SaveSoftLayer(analysesByProcess, groupedAll.Cast<dynamic>().ToList(), pgUri);

            // Bucket de despachos
            SaveBucketLayer(allDocs, pgUri, onlyDespachos, signerContains);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[pipeline-tjpb] FATAL: {ex}");
        }
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf pipeline-tjpb [--only-despachos] [--signer-contains <texto>] [--process <num>]");
            Console.WriteLine("Lê análises do Postgres (raw_processes) e grava documentos em pipeline_processes_tjpb.");
            Console.WriteLine("--only-despachos: filtra apenas documentos cujo doc_label/doc_type contenha 'Despacho'.");
            Console.WriteLine("--signer-contains: filtra documentos cujo signer contenha o texto informado (case-insensitive).");
            Console.WriteLine("--process <num>: processa apenas o processo indicado (opcional).");
        }

        private string DeriveProcessName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var m = Regex.Match(name, @"\d+");
            if (m.Success) return m.Value;
            return name;
        }

        private Dictionary<string, object> BuildDocObject(DocumentBoundary d, PDFAnalysisResult analysis, string pdfPath, List<FieldScript> scripts, double despachoThreshold)
        {
            var docText = string.Join("\n", Enumerable.Range(d.StartPage, d.PageCount)
                                                      .Select(p => analysis.Pages[p - 1].TextInfo.PageText ?? ""));
            var docPagesText = Enumerable.Range(d.StartPage, d.PageCount)
                                         .Select(p => analysis.Pages[p - 1].TextInfo.PageText ?? "")
                                         .ToList();
            var lastPageText = analysis.Pages[Math.Max(0, Math.Min(analysis.Pages.Count - 1, d.EndPage - 1))].TextInfo.PageText ?? "";
            var lastTwoText = lastPageText;
            if (d.PageCount >= 2)
            {
                var prev = analysis.Pages[Math.Max(0, d.EndPage - 2)].TextInfo.PageText ?? "";
                lastTwoText = $"{prev}\n{lastPageText}";
            }

            var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int p = d.StartPage; p <= d.EndPage; p++)
                foreach (var f in analysis.Pages[p - 1].TextInfo.Fonts)
                    fonts.Add(f.Name);

            // Capturar rodapé predominante do intervalo
            var footerLines = new List<string>();
            int images = 0;
            bool hasSignature = false;
            int wordCount = 0;
            int charCount = 0;
            double wordsArea = 0;
            double pageAreaAcc = 0;
            var wordsWithCoords = new List<Dictionary<string, object>>();
            var docBookmarks = ExtractBookmarksForRange(analysis, d.StartPage, d.EndPage);
            var unpackedAttachments = new List<Dictionary<string, object>>();
            string header = string.Empty;
            string footer = string.Empty;

            for (int p = d.StartPage; p <= d.EndPage; p++)
            {
                images += analysis.Pages[p - 1].Resources.Images?.Count ?? 0;
                // Heurística simples: imagem com largura/altura > 100 pode ser assinatura
                hasSignature |= (analysis.Pages[p - 1].Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false);

                var page = analysis.Pages[p - 1];
                wordCount += page.TextInfo.WordCount;
                charCount += page.TextInfo.CharacterCount;

                var size = page.Size;
                var pageArea = Math.Max(1, size.Width * size.Height);
                pageAreaAcc += pageArea;

                foreach (var w in page.TextInfo.Words)
                {
                    double wArea = Math.Max(0, (w.X1 - w.X0) * (w.Y1 - w.Y0));
                    wordsArea += wArea;
                    wordsWithCoords.Add(new Dictionary<string, object>
                    {
                        ["text"] = w.Text,
                        ["page"] = p,
                        ["font"] = w.Font,
                        ["size"] = w.Size,
                        ["bold"] = w.Bold,
                        ["italic"] = w.Italic,
                        ["underline"] = w.Underline,
                        ["render_mode"] = w.RenderMode,
                        ["char_spacing"] = w.CharSpacing,
                        ["word_spacing"] = w.WordSpacing,
                        ["horiz_scaling"] = w.HorizontalScaling,
                        ["rise"] = w.Rise,
                        ["x0"] = w.X0,
                        ["y0"] = w.Y0,
                        ["x1"] = w.X1,
                        ["y1"] = w.Y1,
                        ["nx0"] = w.NormX0,
                        ["ny0"] = w.NormY0,
                        ["nx1"] = w.NormX1,
                        ["ny1"] = w.NormY1
                    });
                }

                if (string.IsNullOrEmpty(header) && page.TextInfo.Headers.Any()) header = page.TextInfo.Headers.First();
                if (string.IsNullOrEmpty(footer) && page.TextInfo.Footers.Any()) footer = page.TextInfo.Footers.First();
                if (page.TextInfo.Footers != null && page.TextInfo.Footers.Count > 0)
                    footerLines.AddRange(page.TextInfo.Footers);
            }

            string footerLabel = footerLines
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "";

            var pageSize = analysis.Pages.First().Size.GetPaperSize();

            // Heurística: bookmark intitulado "Anexo" ou "Anexos" pode conter vários documentos fundidos.
            // Vamos registrar todos os bookmarks de nível 1/2 com esse título dentro do range,
            // para permitir pós-processamento (split fino) sem quebrar a segmentação principal.
            var anexos = docBookmarks
                .Where(b => Regex.IsMatch(b["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase))
                .ToList();
            double textDensity = pageAreaAcc > 0 ? wordsArea / pageAreaAcc : 0;
            double blankRatio = 1 - textDensity;

            var originalLabel = !string.IsNullOrWhiteSpace(d.RawTitle) ? d.RawTitle :
                                (!string.IsNullOrWhiteSpace(d.Title) ? d.Title : ExtractDocumentName(d));
            var sanitizedLabel = SanitizeBookmarkTitle(originalLabel);
            var docLabel = sanitizedLabel;

            // Rodapé preferencial: compacta e usa só a parte antes de "/ pg."
            if (!string.IsNullOrWhiteSpace(footerLabel) && footerLabel.Contains("SEI"))
            {
                var compact = CompactFooter(footerLabel);
                var cut = compact.Split("/ pg", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(cut))
                {
                    docLabel = cut.Trim();
                }
            }

            var docSummary = BuildDocSummary(d, pdfPath, docText, lastPageText, lastTwoText, header, footer, docBookmarks, analysis.Signatures, docLabel, wordsWithCoords);
            var forensics = BuildForensics(d, analysis, docText, wordsWithCoords);
            string originMain = docSummary.TryGetValue("origin_main", out var om) ? om?.ToString() ?? "" : "";
            string originSub = docSummary.TryGetValue("origin_sub", out var os) ? os?.ToString() ?? "" : "";
            string originExtra = docSummary.TryGetValue("origin_extra", out var oe) ? oe?.ToString() ?? "" : "";
            string signer = docSummary.TryGetValue("signer", out var si) ? si?.ToString() ?? "" : "";
            bool isDiretoriaEspecial = IsDiretoriaEspecial(originMain, originSub, originExtra);
            bool isRobson = IsRobsonSigner(signer);
            bool pagesDensityOk = d.PageCount >= 2 || textDensity > 0.20;
            var id = DocumentIdentifier.Identify(docLabel, header, docText, footer, isDiretoriaEspecial, isRobson, pagesDensityOk, docPagesText);

            string docType = NormalizeDocType(id.DocType, id.Reason);

            // Match por hash no banco de laudos (espécie/perito)
            LaudoHashDbEntry? hashHit = null;
            if (_hashDb != null && !string.IsNullOrWhiteSpace(id.LaudoHash))
            {
                _hashDb.TryGet(id.LaudoHash, out hashHit);
            }

            // Se houve hash hit, propaga espécie/perito para o summary
            if (hashHit != null)
            {
                docSummary["hash_species"] = hashHit.Especie;
                docSummary["hash_perito"] = hashHit.Perito;
                docSummary["hash_cpf"] = hashHit.Cpf;
                docSummary["hash_especialidade"] = hashHit.Especialidade;
                docSummary["hash_db_path"] = _hashDbPath;
            }

            var finalScore = DespachoScorer.Score(docLabel, docType ?? "", docSummary, header, footer, textDensity, d.PageCount, docBookmarks);
            double despachoScore = finalScore.score;
            var despachoSignals = finalScore.signals;

            bool isDespachoBucket = id.IsDespachoBucket;
            // Extratores rodam nos tipos principais + laudo pericial
            List<Dictionary<string, object>> extractedFields;
            List<Dictionary<string, object>> bandFields;
            bool runExtractors =
                docType.IndexOf("despacho", StringComparison.OrdinalIgnoreCase) >= 0 ||
                docType.IndexOf("requisicao_pagamento_honorarios", StringComparison.OrdinalIgnoreCase) >= 0 ||
                docType.IndexOf("certidao_cm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                docType.IndexOf("laudo", StringComparison.OrdinalIgnoreCase) >= 0;

            if (runExtractors)
            {
                extractedFields = ExtractFields(docText, wordsWithCoords, d, pdfPath, scripts, docType);
                bandFields = ExtractBandFields(docText, wordsWithCoords, d.StartPage);
                extractedFields = MergeFields(extractedFields, bandFields);
            }
            else
            {
                extractedFields = new List<Dictionary<string, object>>();
                bandFields = new List<Dictionary<string, object>>();
            }

            if (docType.IndexOf("laudo", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !extractedFields.Any(f => string.Equals(f.GetValueOrDefault("name")?.ToString(), "DOC_TYPE", StringComparison.OrdinalIgnoreCase)))
            {
                extractedFields.Add(new Dictionary<string, object>
                {
                    ["name"] = "DOC_TYPE",
                    ["value"] = docType,
                    ["page"] = 0,
                    ["pattern"] = "detector_laudo_perito_quesitos",
                    ["weight"] = 0.9
                });
            }

            // Campo de origem por hash-db (não cai na filtragem de ESPÉCIE)
            if (hashHit != null)
            {
                extractedFields.Add(new Dictionary<string, object>
                {
                    ["name"] = "HASH_DB_SPECIES",
                    ["value"] = hashHit.Especie,
                    ["page"] = 0,
                    ["pattern"] = "hash_db",
                    ["weight"] = 1.0
                });
                if (!string.IsNullOrWhiteSpace(hashHit.Perito))
                {
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "HASH_DB_PERITO",
                        ["value"] = hashHit.Perito,
                        ["page"] = 0,
                        ["pattern"] = "hash_db",
                        ["weight"] = 1.0
                    });
                }
                if (!string.IsNullOrWhiteSpace(hashHit.Cpf))
                {
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "HASH_DB_CPF",
                        ["value"] = hashHit.Cpf,
                        ["page"] = 0,
                        ["pattern"] = "hash_db",
                        ["weight"] = 1.0
                    });
                }
                if (!string.IsNullOrWhiteSpace(hashHit.Especialidade))
                {
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "HASH_DB_ESPECIALIDADE",
                        ["value"] = hashHit.Especialidade,
                        ["page"] = 0,
                        ["pattern"] = "hash_db",
                        ["weight"] = 1.0
                    });
                }
            }

            // Manter sinalização geral, mas bucket passa a ser definido apenas pelo critério acima.
            bool isDespacho = isDespachoBucket
                              || (!string.IsNullOrWhiteSpace(docType) && docType.IndexOf("despacho", StringComparison.OrdinalIgnoreCase) >= 0)
                              || (!string.IsNullOrWhiteSpace(docLabel) && docLabel.IndexOf("despacho", StringComparison.OrdinalIgnoreCase) >= 0);
            bool isTemplateCandidate = isDespachoBucket && d.PageCount >= 2 && textDensity >= 0.20;

            // Datas do rodapé (despacho/certidão) com normalização
            var dateFooter = docSummary.TryGetValue("date_footer", out var dfObj) ? dfObj?.ToString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(dateFooter))
                dateFooter = ExtractDateFromFooter(lastTwoText, footer, header);
            var signedAtSummary = docSummary.TryGetValue("signed_at", out var saObj) ? saObj?.ToString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(signedAtSummary))
                dateFooter = signedAtSummary;

            if (!string.IsNullOrWhiteSpace(dateFooter))
            {
                if (docType.IndexOf("certidao", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    docSummary["certidao_date"] = dateFooter;
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "DATA DA CERTIDAO",
                        ["value"] = dateFooter,
                        ["page"] = d.EndPage,
                        ["pattern"] = "footer_date",
                        ["weight"] = 1.0
                    });
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "DATA DA AUTORIZACAO DA DESPESA",
                        ["value"] = dateFooter,
                        ["page"] = d.EndPage,
                        ["pattern"] = "footer_date",
                        ["weight"] = 1.0
                    });
                }
                if (docType.IndexOf("despacho", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    docSummary["despacho_date"] = dateFooter;
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "DATA DO DESPACHO",
                        ["value"] = dateFooter,
                        ["page"] = d.EndPage,
                        ["pattern"] = "footer_date",
                        ["weight"] = 1.0
                    });
                }
            }

            // Valores direcionados: juiz (página 1, topo), Diretor Especial (última página, rodapé), Conselho (certidão CM)
            extractedFields = MergeFields(extractedFields, ExtractDirectedValues(analysis, d, docType, docText));

            // Normaliza todos os campos apenas depois de completar os extras
            extractedFields = NormalizeAndValidateFields(extractedFields);

            // Certidão CM: se não veio valor, pegar o primeiro monetário do texto e atribuir a VALOR ARBITRADO - CM
            if (docType.IndexOf("certidao_cm", StringComparison.OrdinalIgnoreCase) >= 0
                && !extractedFields.Any(f => string.Equals(f.GetValueOrDefault("name")?.ToString(), "VALOR ARBITRADO - CM", StringComparison.OrdinalIgnoreCase)))
            {
                var moneyMatches = MoneyRegex.Matches(docText ?? "");
                string bestRaw = "";
                double bestVal = -1;
                int bestScore = -1;
                foreach (Match m in moneyMatches)
                {
                    var valStr = CleanStandard("VALOR ARBITRADO - CM", m.Value);
                    if (!double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) continue;
                    // Prefer valores perto de “honor” ou “pagamento” e, em empate, o maior
                    int score = 0;
                    int pos = docText.IndexOf(m.Value, StringComparison.OrdinalIgnoreCase);
                    if (pos >= 0)
                    {
                        int start = Math.Max(0, pos - 60);
                        int len = Math.Min(docText.Length - start, 120);
                        var ctx = docText.Substring(start, len).ToLowerInvariant();
                        if (ctx.Contains("honor")) score += 2;
                        if (ctx.Contains("pagament")) score += 1;
                    }
                    if (score > bestScore || (score == bestScore && v > bestVal))
                    {
                        bestScore = score;
                        bestVal = v;
                        bestRaw = valStr;
                    }
                }
                if (bestVal >= 0 && ValidateStandard("VALOR ARBITRADO - CM", bestRaw))
                {
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "VALOR ARBITRADO - CM",
                        ["value"] = bestRaw,
                        ["page"] = d.EndPage,
                        ["pattern"] = "certidao_money_fallback",
                        ["weight"] = 0.8
                    });
                }
            }

            // Fallback: valor DE (diretor) quando há assinatura Robson + Diretoria Especial e nenhum valor DE
            if (!extractedFields.Any(f => string.Equals(f.GetValueOrDefault("name")?.ToString(), "VALOR ARBITRADO - DE", StringComparison.OrdinalIgnoreCase))
                && isDiretoriaEspecial && isRobson)
            {
                var lastPageTxt = SafePageText(analysis, d.EndPage);
                var moneyMatches = MoneyRegex.Matches(lastPageTxt ?? "");
                double bestVal = -1;
                string bestRaw = "";
                foreach (Match m in moneyMatches)
                {
                    var valStr = CleanStandard("VALOR ARBITRADO - DE", m.Value);
                    if (!double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) continue;
                    if (v > bestVal)
                    {
                        bestVal = v;
                        bestRaw = valStr;
                    }
                }
                if (bestVal >= 0)
                {
                    extractedFields.Add(new Dictionary<string, object>
                    {
                        ["name"] = "VALOR ARBITRADO - DE",
                        ["value"] = bestRaw,
                        ["page"] = d.EndPage,
                        ["pattern"] = "direct_de_lastpage_fallback",
                        ["weight"] = 0.55
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["doc_id"] = docSummary["doc_id"],
                ["process"] = DeriveProcessName(pdfPath),
                ["pdf_path"] = pdfPath,
                ["doc_label"] = docLabel,
                ["doc_label_original"] = originalLabel,
                ["doc_type"] = docType,
                ["docid_confidence"] = 0.0,
                ["docid_reason"] = id.Reason,
                ["hash_db_match"] = hashHit != null,
                ["hash_db_path"] = _hashDbPath,
                ["laudo_score"] = id.LaudoScore,
                ["laudo_reason"] = id.LaudoReason,
                ["laudo_hash"] = id.LaudoHash,
                ["laudo_perito_hint"] = id.LaudoPerito,
                ["laudo_especialidade_hint"] = id.LaudoEspecialidade,
                ["laudo_especie_hint"] = id.LaudoEspecie,
                ["despacho_score"] = despachoScore,
                ["despacho_threshold"] = despachoThreshold,
                ["despacho_signals"] = despachoSignals,
                ["is_despacho"] = isDespacho,
                ["is_despacho_bucket"] = isDespachoBucket,
                ["is_despacho_autorizacao"] = isDespacho && docLabel.IndexOf("autoriz", StringComparison.OrdinalIgnoreCase) >= 0,
                ["is_despacho_encaminhamento"] = isDespacho && docLabel.IndexOf("encaminh", StringComparison.OrdinalIgnoreCase) >= 0,
                ["modification_dates"] = analysis.ModificationDates,
                ["start_page"] = d.StartPage,
                ["end_page"] = d.EndPage,
                ["doc_pages"] = d.PageCount,
                ["total_pages"] = analysis.DocumentInfo.TotalPages,
                ["text"] = docText,
                ["fonts"] = fonts.ToArray(),
                ["images"] = images,
                ["page_size"] = pageSize,
                ["has_signature_image"] = hasSignature,
                ["is_anexo"] = false,
                ["word_count"] = wordCount,
                ["char_count"] = charCount,
                ["text_density"] = textDensity,
                ["blank_ratio"] = blankRatio,
                ["words"] = wordsWithCoords,
                ["header"] = header,
                ["footer"] = footer,
                ["bookmarks"] = docBookmarks,
                ["anexos_bookmarks"] = anexos,
                ["doc_summary"] = docSummary,
                ["is_despacho_candidate"] = isDespachoBucket,
                ["fields"] = extractedFields,
                ["band_fields"] = bandFields,
                ["forensics"] = forensics
            };
        }

        private string ComputeLaudoHash(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            // normaliza: remove diacríticos, baixa caixa, colapsa espaços
            var norm = RemoveDiacritics(text).ToLowerInvariant();
            norm = Regex.Replace(norm, @"\s+", " ").Trim();
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(norm));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // Doc type classification removida: usamos apenas o nome do bookmark como rótulo.
        private string NormalizeDocType(string docType, string reason)
        {
            if (string.IsNullOrWhiteSpace(docType)) return docType;
            var lower = docType.ToLowerInvariant();

            if (reason == "fallback")
            {
                // tenta inferir pelo rótulo
                if (lower.Contains("certidão") || lower.Contains("certidao")) return "certidao";
                if (lower.Contains("nota de empenho") || lower.Contains("empenho")) return "nota_empenho";
                if (lower.Contains("laudo")) return "laudo";
                if (lower.Contains("sighop") || lower.Contains("sig hop")) return "sig_hop";
                if (lower.Contains("anexo")) return "anexo";
            }

            // remove códigos/SEI do doc_type se ele for um label longo
            if (docType.Length > 80 && (reason == "fallback" || reason.StartsWith("anchor")))
            {
                if (lower.Contains("certidão") || lower.Contains("certidao")) return "certidao";
                if (lower.Contains("nota de empenho") || lower.Contains("empenho")) return "nota_empenho";
                if (lower.Contains("laudo")) return "laudo";
                if (lower.Contains("sighop") || lower.Contains("sig hop")) return "sig_hop";
                if (lower.Contains("anexo")) return "anexo";
            }

            return docType;
        }

        private Dictionary<string, object> BuildDocSummary(DocumentBoundary d, string pdfPath, string fullText, string lastPageText, string lastTwoText, string header, string footer, List<Dictionary<string, object>> bookmarks, List<DigitalSignature> signatures, string docLabel, List<Dictionary<string, object>> words)
        {
            string docId = $"{Path.GetFileNameWithoutExtension(pdfPath)}_{d.StartPage}-{d.EndPage}";
            string originMain = ExtractOrigin(header, bookmarks, fullText, excludeGeneric: true);
            string originSub = ExtractSubOrigin(header, bookmarks, fullText, originMain, excludeGeneric: true);
            string originExtra = ExtractExtraOrigin(header, bookmarks, fullText, originMain, originSub);
            var sei = ExtractSeiMetadata(fullText, lastTwoText, footer, docLabel);
            string signer = sei.Signer ?? ExtractSigner(lastTwoText, footer, signatures);
            string signedAt = sei.SignedAt ?? ExtractSignedAt(lastTwoText, footer);
            string dateFooter = ExtractDateFromFooter(lastTwoText, footer, header);
            string headerHash = HashText(header);
            string template = docLabel; // não classificar; manter o nome do bookmark
            string title = ExtractTitle(header, bookmarks, fullText, originMain, originSub);
            var paras = BuildParagraphsFromWords(words);
            var party = ExtractPartyInfo(fullText);
            var partyBBoxes = ExtractPartyBBoxes(paras, d.StartPage, d.EndPage);
            var procInfo = ExtractProcessInfo(paras, sei.Process);

            return new Dictionary<string, object>
            {
                ["doc_id"] = docId,
                ["origin_main"] = originMain,
                ["origin_sub"] = originSub,
                ["origin_extra"] = originExtra,
                ["signer"] = signer,
                ["signed_at"] = signedAt,
                ["header_hash"] = headerHash,
                ["title"] = title,
                ["template"] = template,
                ["sei_process"] = string.IsNullOrWhiteSpace(sei.Process) ? procInfo.ProcessNumber : sei.Process,
                ["sei_doc"] = sei.DocNumber,
                ["sei_crc"] = sei.CRC,
                ["sei_verifier"] = sei.Verifier,
                ["auth_url"] = sei.AuthUrl,
                ["date_footer"] = dateFooter,
                ["process_line"] = procInfo.ProcessLine,
                ["process_bbox"] = procInfo.ProcessBBox,
                ["interested_line"] = party.InterestedLine,
                ["interested_name"] = party.InterestedName,
                ["interested_profession"] = party.InterestedProfession,
                ["interested_email"] = party.InterestedEmail,
                ["juizo_line"] = party.JuizoLine,
                ["juizo_vara"] = party.JuizoVara,
                ["comarca"] = party.Comarca,
                ["interested_bbox"] = partyBBoxes.InterestedBBox,
                ["juizo_bbox"] = partyBBoxes.JuizoBBox
            };
        }

        private (string InterestedLine, string InterestedName, string InterestedProfession, string InterestedEmail, string JuizoLine, string JuizoVara, string Comarca) ExtractPartyInfo(string fullText)
        {
            string interestedLine = "";
            string interestedName = "";
            string interestedProf = "";
            string interestedEmail = "";
            string juizoLine = "";
            string juizoVara = "";
            string comarca = "";

            var lines = (fullText ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            // Interessado / Requerente
            foreach (var line in lines)
            {
                var m = Regex.Match(line, @"^(interessad[oa])\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    interestedLine = line;
                    var rest = m.Groups[2].Value.Trim();
                    // e-mail
                    var em = Regex.Match(rest, @"([\\w.+-]+@[\\w.-]+)");
                    if (em.Success) interestedEmail = em.Groups[1].Value;
                    // Split por " - " ou " – "
                    var parts = Regex.Split(rest, @"\s[-–]\s");
                    if (parts.Length > 0) interestedName = parts[0].Trim();
                    if (parts.Length > 1) interestedProf = parts[1].Trim();
                    break;
                }
            }

            // Juízo / Comarca
            foreach (var line in lines)
            {
                var m = Regex.Match(line, @"^(ju[ií]zo|vara).*", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    juizoLine = line;
                    // Vara
                    var vm = Regex.Match(line, @"(ju[ií]zo\s+da\s+|ju[ií]zo\s+do\s+|vara\s+)([^,]+)", RegexOptions.IgnoreCase);
                    if (vm.Success) juizoVara = vm.Groups[2].Value.Trim();
                    // Comarca
                    var cm = Regex.Match(line, @"comarca\s+de\s+([^,\-]+)", RegexOptions.IgnoreCase);
                    if (cm.Success) comarca = cm.Groups[1].Value.Trim();
                    break;
                }
            }

            return (interestedLine, interestedName, interestedProf, interestedEmail, juizoLine, juizoVara, comarca);
        }

        private (Dictionary<string, double> InterestedBBox, Dictionary<string, double> JuizoBBox) ExtractPartyBBoxes(ParagraphObj[] paras, int startPage, int endPage)
        {
            Dictionary<string, double> interested = null;
            Dictionary<string, double> juizo = null;

            foreach (var p in paras)
            {
                if (p.Page < startPage || p.Page > endPage) continue;
                var text = p.Text ?? "";
                if (interested == null && Regex.IsMatch(text, @"^(interessad[oa])\s*:", RegexOptions.IgnoreCase))
                {
                    interested = new Dictionary<string, double> { ["nx0"] = p.NX0, ["ny0"] = p.Ny0, ["nx1"] = p.NX1, ["ny1"] = p.Ny1 };
                }
                if (juizo == null && Regex.IsMatch(text, @"^(ju[ií]zo|vara)", RegexOptions.IgnoreCase))
                {
                    juizo = new Dictionary<string, double> { ["nx0"] = p.NX0, ["ny0"] = p.Ny0, ["nx1"] = p.NX1, ["ny1"] = p.Ny1 };
                }
                if (interested != null && juizo != null) break;
            }

            return (interested, juizo);
        }

        private (string ProcessLine, string ProcessNumber, Dictionary<string, double> ProcessBBox) ExtractProcessInfo(ParagraphObj[] paras, string fallbackProcess)
        {
            foreach (var p in paras)
            {
                var text = p.Text ?? "";
                var m = Regex.Match(text, @"processo\s*n[º°]?\s*:?\s*([\d\.\-\/]+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var bbox = new Dictionary<string, double> { ["nx0"] = p.NX0, ["ny0"] = p.Ny0, ["nx1"] = p.NX1, ["ny1"] = p.Ny1 };
                    return (text, m.Groups[1].Value.Trim(), bbox);
                }
            }
            // fallback
            return ("", fallbackProcess ?? "", null);
        }

        private class SeiMeta
        {
            public string Process { get; set; }
            public string DocNumber { get; set; }
            public string CRC { get; set; }
            public string Verifier { get; set; }
            public string SignedAt { get; set; }
            public string Signer { get; set; }
            public string AuthUrl { get; set; }
        }

        private SeiMeta ExtractSeiMetadata(string fullText, string lastTwoText, string footer, string docLabel)
        {
            var meta = new SeiMeta();
            // Usar apenas o texto das duas últimas páginas + footer para evitar capturar datas irrelevantes (ex.: nascimento)
            string hay = $"{lastTwoText}\n{footer}";

            // Processo SEI (formato com hífens/pontos)
            var mProc = Regex.Match(hay, @"Processo\s+n[º°]?\s*([\d]{6}-\d{2}\.\d{4}\.\d\.\d{2})", RegexOptions.IgnoreCase);
            if (!mProc.Success)
                mProc = Regex.Match(hay, @"SEI\s+([\d]{6}-\d{2}\.\d{4}\.\d\.\d{2})");
            if (mProc.Success) meta.Process = mProc.Groups[1].Value.Trim();

            // Número da peça SEI (doc)
            var mDoc = Regex.Match(hay, @"SEI\s*n[º°]?\s*([0-9]{4,})", RegexOptions.IgnoreCase);
            if (!mDoc.Success)
                mDoc = Regex.Match(docLabel ?? "", @"\((\d{4,})\)");
            if (mDoc.Success) meta.DocNumber = mDoc.Groups[1].Value.Trim();

            var mCRC = Regex.Match(hay, @"CRC\s+([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (mCRC.Success) meta.CRC = mCRC.Groups[1].Value.Trim();

            var mVer = Regex.Match(hay, @"verificador\s+([0-9]{4,})", RegexOptions.IgnoreCase);
            if (mVer.Success) meta.Verifier = mVer.Groups[1].Value.Trim();

            if (hay.Contains("assinado eletronicamente", StringComparison.OrdinalIgnoreCase))
            {
                var mSigner = Regex.Match(hay, @"Documento assinado eletronicamente por\s+(.+?),\s*(.+?),\s*em", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (mSigner.Success)
                    meta.Signer = $"{mSigner.Groups[1].Value.Trim()} - {mSigner.Groups[2].Value.Trim()}";
                else
                {
                    var line = lastTwoText.Split('\n').Select(l => l.Trim()).Reverse().FirstOrDefault(l => l.Contains("–"));
                    if (!string.IsNullOrWhiteSpace(line)) meta.Signer = line;
                }

                // Data/hora logo após a frase de assinatura
                var mDate = Regex.Match(hay, @"assinado eletronicamente.*?em\s*([0-9]{2}/[0-9]{2}/[0-9]{4}).*?([0-9]{2}:[0-9]{2})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (mDate.Success)
                {
                    var s = $"{mDate.Groups[1].Value} {mDate.Groups[2].Value}";
                    if (DateTime.TryParse(s, new System.Globalization.CultureInfo("pt-BR"), DateTimeStyles.AssumeLocal, out var dt))
                        meta.SignedAt = dt.ToString("yyyy-MM-dd HH:mm");
                }
                else
                {
                    // Data por extenso (ex.: 22 de julho de 2024)
                    var ext = Regex.Match(hay, @"(\d{1,2})\s+de\s+(janeiro|fevereiro|març|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s+de\s+(\d{4})", RegexOptions.IgnoreCase);
                    if (ext.Success)
                    {
                        var dia = ext.Groups[1].Value.PadLeft(2, '0');
                        var mes = ext.Groups[2].Value.ToLower()
                            .Replace("març", "marco");
                        var ano = ext.Groups[3].Value;
                        var meses = new Dictionary<string, string>
                        {
                            ["janeiro"]="01",["fevereiro"]="02",["marco"]="03",["abril"]="04",["maio"]="05",["junho"]="06",
                            ["julho"]="07",["agosto"]="08",["setembro"]="09",["outubro"]="10",["novembro"]="11",["dezembro"]="12"
                        };
                        if (meses.TryGetValue(mes, out var mnum))
                            meta.SignedAt = $"{ano}-{mnum}-{dia}";
                    }
                }
            }

            var mUrl = Regex.Match(hay, @"https?://\S+autentica\S*", RegexOptions.IgnoreCase);
            if (mUrl.Success) meta.AuthUrl = mUrl.Value.TrimEnd('.', ',');

            return meta;
        }

        // FORÊNSE
        private Dictionary<string, object> BuildForensics(DocumentBoundary d, PDFAnalysisResult analysis, string docText, List<Dictionary<string, object>> words)
        {
            var result = new Dictionary<string, object>();

            // clusterizar linhas (y) por página
            var lineObjs = BuildLines(words);
            // parágrafos (agora cada bloco será tratado como “bodyN” de fato)
            var paragraphs = BuildParagraphsFromWords(words);

            // fontes/tamanhos dominantes
            var fontNames = words.Select(w => w["font"]?.ToString() ?? "").Where(f => f != "").ToList();
            var fontSizes = words.Select(w => Convert.ToDouble(w["size"])).Where(s => s > 0).ToList();
            string dominantFont = Mode(fontNames);
            double dominantSize = Median(fontSizes);

            // outliers: linha cujo tamanho médio difere >20% ou fonte não dominante
            var outliers = lineObjs.Where(l =>
            {
                double sz = l.FontSizeAvg;
                bool sizeOut = dominantSize > 0 && Math.Abs(sz - dominantSize) / dominantSize > 0.2;
                bool fontOut = !string.IsNullOrEmpty(dominantFont) && !string.Equals(l.Font, dominantFont, StringComparison.OrdinalIgnoreCase);
                return sizeOut || fontOut;
            }).Take(10).ToList();

            // linhas repetidas (hash texto)
            var repeats = lineObjs.GroupBy(l => l.TextNorm)
                                  .Where(g => g.Count() > 1)
                                  .Select(g => new { text = g.Key, count = g.Count() })
                                  .OrderByDescending(x => x.count)
                                  .Take(10)
                                  .ToList();

            // anchors: assinatura, verificador, crc, rodapé SEI
            var anchors = DetectAnchors(lineObjs);

            // bandas header/subheader/body1/body2/footer
            var bands = LayoutBands.SummarizeBands(lineObjs);

            // anotações agregadas
            var ann = SummarizeAnnotations(analysis);

            result["font_dominant"] = dominantFont;
            result["size_dominant"] = dominantSize;
            result["outlier_lines"] = outliers.Select(l => l.ToDict()).ToList();
            result["repeat_lines"] = repeats;
            result["anchors"] = anchors;
            result["bands"] = bands;
            result["paragraphs"] = paragraphs.Select(p => new
            {
                page = p.Page,
                nx0 = p.NX0,
                ny0 = p.Ny0,
                nx1 = p.NX1,
                ny1 = p.Ny1,
                text = p.Text,
                tokens = p.Tokens
            }).ToList();
            result["annotations"] = ann;

            return result;
        }

        private List<LineObj> BuildLines(List<Dictionary<string, object>> words)
        {
            var lines = new List<LineObj>();
            var byPage = words.GroupBy(w => Convert.ToInt32(w["page"]));
            foreach (var pg in byPage)
            {
                var clusters = ClusterLines(pg.ToList());
                foreach (var c in clusters)
                {
                    var ordered = c.OrderBy(w => Convert.ToDouble(w["x0"])).ToList();
                    string text = string.Join(" ", ordered.Select(w => w["text"].ToString()));
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    double nx0 = ordered.Min(w => Convert.ToDouble(w["nx0"]));
                    double ny0 = ordered.Min(w => Convert.ToDouble(w["ny0"]));
                    double nx1 = ordered.Max(w => Convert.ToDouble(w["nx1"]));
                    double ny1 = ordered.Max(w => Convert.ToDouble(w["ny1"]));
                    var fonts = ordered.Select(w => w["font"]?.ToString() ?? "").Where(f => f != "").ToList();
                    var sizes = ordered.Select(w => Convert.ToDouble(w["size"])).Where(s => s > 0).ToList();

                    lines.Add(new LineObj
                    {
                        Page = pg.Key,
                        Text = text.Trim(),
                        TextNorm = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim(),
                        NX0 = nx0, NX1 = nx1, NY0 = ny0, NY1 = ny1,
                        Font = Mode(fonts),
                        FontSizeAvg = sizes.Count > 0 ? sizes.Average() : 0
                    });
                }
            }
            return lines;
        }

        private List<List<Dictionary<string, object>>> ClusterLines(List<Dictionary<string, object>> words, double tol = 1.5)
        {
            var groups = new List<List<Dictionary<string, object>>>();
            foreach (var w in words.OrderByDescending(w => Convert.ToDouble(w["y0"])))
            {
                double y = Convert.ToDouble(w["y0"]);
                bool placed = false;
                foreach (var g in groups)
                {
                    double gy = g.Average(x => Convert.ToDouble(x["y0"]));
                    if (Math.Abs(gy - y) <= tol)
                    {
                        g.Add(w);
                        placed = true;
                        break;
                    }
                }
                if (!placed) groups.Add(new List<Dictionary<string, object>> { w });
            }
            return groups;
        }

        private Dictionary<string, object> DetectAnchors(List<LineObj> lines)
        {
            var anchors = new Dictionary<string, object>();
            anchors["signature"] = FindAnchor(lines, "documento assinado eletronicamente");
            anchors["verifier"] = FindAnchor(lines, "código verificador");
            anchors["crc"] = FindAnchor(lines, "crc");
            anchors["sei_footer"] = FindAnchor(lines, " / pg");
            return anchors;
        }

        // -------- Paragraph stats across all docs --------

        private List<object> BuildParagraphStats(List<List<Dictionary<string, object>>> allDocsWords)
        {
            if (allDocsWords == null || allDocsWords.Count == 0)
                return new List<object>();

            var paragraphsPerDoc = allDocsWords.Select(BuildParagraphsFromWords).ToList();
            if (paragraphsPerDoc.Count == 0 || paragraphsPerDoc.All(p => p.Length == 0))
                return new List<object>();

            int maxPars = paragraphsPerDoc.Max(p => p.Length);
            var stats = new List<object>();

            for (int idx = 0; idx < maxPars; idx++)
            {
                int docn = 0;
                var df2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var tf2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var df3 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var tf3 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var pars in paragraphsPerDoc)
                {
                    if (idx >= pars.Length) continue;
                    docn++;
                    var tokens = pars[idx].Tokens;
                    var seen2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var seen3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var bg in Ngrams(tokens, 2))
                    {
                        tf2[bg] = tf2.TryGetValue(bg, out var v) ? v + 1 : 1;
                        seen2.Add(bg);
                    }
                    foreach (var tr in Ngrams(tokens, 3))
                    {
                        tf3[tr] = tf3.TryGetValue(tr, out var v) ? v + 1 : 1;
                        seen3.Add(tr);
                    }
                    foreach (var bg in seen2)
                        df2[bg] = df2.TryGetValue(bg, out var v) ? v + 1 : 1;
                    foreach (var tr in seen3)
                        df3[tr] = df3.TryGetValue(tr, out var v) ? v + 1 : 1;
                }

                int thresholdStable = (int)Math.Ceiling(docn * 0.6);
                int thresholdVariable = (int)Math.Floor(docn * 0.2);

                var stable2 = df2.Where(kv => kv.Value >= thresholdStable)
                                 .OrderByDescending(kv => kv.Value)
                                 .ThenByDescending(kv => tf2[kv.Key])
                                 .ThenBy(kv => kv.Key)
                                 .Take(20)
                                 .Select(kv => new { ngram = kv.Key, docfreq = kv.Value, tf = tf2[kv.Key] });

                var stable3 = df3.Where(kv => kv.Value >= thresholdStable)
                                 .OrderByDescending(kv => kv.Value)
                                 .ThenByDescending(kv => tf3[kv.Key])
                                 .ThenBy(kv => kv.Key)
                                 .Take(20)
                                 .Select(kv => new { ngram = kv.Key, docfreq = kv.Value, tf = tf3[kv.Key] });

                var variable2 = df2.Where(kv => kv.Value <= thresholdVariable)
                                   .OrderBy(kv => kv.Value)
                                   .ThenByDescending(kv => tf2[kv.Key])
                                   .ThenBy(kv => kv.Key)
                                   .Take(20)
                                   .Select(kv => new { ngram = kv.Key, docfreq = kv.Value, tf = tf2[kv.Key] });

                var variable3 = df3.Where(kv => kv.Value <= thresholdVariable)
                                   .OrderBy(kv => kv.Value)
                                   .ThenByDescending(kv => tf3[kv.Key])
                                   .ThenBy(kv => kv.Key)
                                   .Take(20)
                                   .Select(kv => new { ngram = kv.Key, docfreq = kv.Value, tf = tf3[kv.Key] });

                stats.Add(new
                {
                    paragraph = idx + 1,
                    docs_with_par = docn,
                    stable_bigrams = stable2,
                    stable_trigrams = stable3,
                    variable_bigrams = variable2,
                    variable_trigrams = variable3
                });
            }

            return stats;
        }

        private class ParagraphObj
        {
            public int Page { get; set; }
            public double Ny0 { get; set; }
            public double Ny1 { get; set; }
            public double NX0 { get; set; }
            public double NX1 { get; set; }
            public string Text { get; set; } = "";
            public List<string> Tokens { get; set; } = new List<string>();
        }

        private ParagraphObj[] BuildParagraphsFromWords(List<Dictionary<string, object>> words)
        {
            var stopWords = new HashSet<string>(new[] { "", "-", "/", "pg", "se", "em", "de", "da", "do", "das", "dos", "a", "o", "e", "que", "para", "com", "no", "na", "as", "os", "ao", "à", "até", "por", "uma", "um", "§", "art", "artigo" });
            var pages = new Dictionary<int, List<Dictionary<string, object>>>();
            foreach (var w in words)
            {
                int p = Convert.ToInt32(w["page"]);
                if (!pages.ContainsKey(p)) pages[p] = new List<Dictionary<string, object>>();
                pages[p].Add(w);
            }

            var paras = new List<ParagraphObj>();
            foreach (var kv in pages.OrderBy(k => k.Key))
            {
                var clusters = new List<List<Dictionary<string, object>>>();
                foreach (var w in kv.Value.OrderByDescending(w => Convert.ToDouble(w["y0"])))
                {
                    double y = Convert.ToDouble(w["y0"]);
                    bool placed = false;
                    foreach (var c in clusters)
                    {
                        double gy = c.Average(x => Convert.ToDouble(x["y0"]));
                        if (Math.Abs(gy - y) <= 1.5)
                        {
                            c.Add(w); placed = true; break;
                        }
                    }
                    if (!placed) clusters.Add(new List<Dictionary<string, object>> { w });
                }

                foreach (var cl in clusters)
                {
                    var line = cl.OrderBy(w => Convert.ToDouble(w["x0"])).ToList();
                    string text = RebuildLine(line);
                    text = DespaceIfNeeded(text);
                    var tokens = text.Split(' ')
                                     .Select(t => Regex.Replace(t, @"[^\w\d]+", "", RegexOptions.None).ToLowerInvariant())
                                     .Where(t => t.Length > 0 && !stopWords.Contains(t))
                                     .ToList();
                    paras.Add(new ParagraphObj
                    {
                        Page = kv.Key,
                        Ny0 = cl.Min(w => Convert.ToDouble(w["ny0"])),
                        Ny1 = cl.Max(w => Convert.ToDouble(w["ny1"])),
                        NX0 = cl.Min(w => Convert.ToDouble(w["nx0"])),
                        NX1 = cl.Max(w => Convert.ToDouble(w["nx1"])),
                        Text = text,
                        Tokens = tokens
                    });
                }
            }

            return paras.OrderByDescending(p => p.Page)
                        .ThenByDescending(p => p.Ny0)
                        .ToArray();
        }

        private string RebuildLine(List<Dictionary<string, object>> ws, double spaceFactor = 0.6)
        {
            if (ws == null || ws.Count == 0) return "";
            var sorted = ws.OrderBy(w => Convert.ToDouble(w["x0"])).ToList();
            string result = sorted[0]["text"].ToString();
            double avgW = sorted.Average(w => Convert.ToDouble(w["x1"]) - Convert.ToDouble(w["x0"]));
            for (int i = 1; i < sorted.Count; i++)
            {
                double gap = Convert.ToDouble(sorted[i]["x0"]) - Convert.ToDouble(sorted[i - 1]["x1"]);
                int spaces = (gap > avgW * 0.2) ? Math.Max(1, (int)(gap / (avgW * spaceFactor))) : 0;
                result += new string(' ', spaces) + sorted[i]["text"];
            }
            return result;
        }

        private string DespaceIfNeeded(string text)
        {
            var tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return text;
            int single = tokens.Count(t => t.Length == 1);
            if ((double)single / tokens.Length > 0.5)
                return Regex.Replace(text, @"\s+", "");
            return text;
        }

        private IEnumerable<string> Ngrams(List<string> tokens, int n)
        {
            if (tokens == null || tokens.Count < n) yield break;
            for (int i = 0; i <= tokens.Count - n; i++)
                yield return string.Join(" ", tokens.GetRange(i, n));
        }

        private object FindAnchor(List<LineObj> lines, string needle)
        {
            var hits = lines.Where(l => l.TextNorm.Contains(needle.ToLowerInvariant())).ToList();
            if (!hits.Any()) return new { found = false };
            return new
            {
                found = true,
                count = hits.Count,
                bbox = new
                {
                    nx0 = Median(hits.Select(h => h.NX0).ToList()),
                    ny0 = Median(hits.Select(h => h.NY0).ToList()),
                    nx1 = Median(hits.Select(h => h.NX1).ToList()),
                    ny1 = Median(hits.Select(h => h.NY1).ToList())
                },
                samples = hits.Take(3).Select(h => h.Text).ToList()
            };
        }

        private object SummarizeAnnotations(PDFAnalysisResult analysis)
        {
            var anns = analysis.Pages.SelectMany(p => p.Annotations ?? new List<Annotation>()).ToList();
            var byType = anns.GroupBy(a => a.Type ?? "").ToDictionary(g => g.Key, g => g.Count());
            DateTime? min = anns.Where(a => a.ModificationDate.HasValue).Select(a => a.ModificationDate).DefaultIfEmpty(null).Min();
            DateTime? max = anns.Where(a => a.ModificationDate.HasValue).Select(a => a.ModificationDate).DefaultIfEmpty(null).Max();
            return new
            {
                count = anns.Count,
                by_type = byType,
                date_min = min,
                date_max = max
            };
        }

        private string Mode(List<string> items)
        {
            var clean = items?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
            if (clean.Count == 0) return "";
            return clean.GroupBy(x => x.ToLowerInvariant())
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault();
        }

        private double Median(List<double> items)
        {
            if (items == null || items.Count == 0) return 0;
            items = items.OrderBy(x => x).ToList();
            int n = items.Count;
            if (n % 2 == 1) return items[n / 2];
            return (items[n / 2 - 1] + items[n / 2]) / 2.0;
        }

        private string CompactFooter(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            // remove espaçamento intercalado (ex.: "D e s p a c h o" -> "Despacho")
            var noSpaces = Regex.Replace(text, @"\s{1}", " ");
            var collapsed = Regex.Replace(noSpaces, @"\s+", " ").Trim();
            var chars = collapsed.ToCharArray();
            var sb = new StringBuilder(chars.Length);
            int consecutiveSingles = 0;
            foreach (var c in chars)
            {
                if (c == ' ')
                {
                    consecutiveSingles++;
                    if (consecutiveSingles > 1) continue;
                }
                else
                {
                    consecutiveSingles = 0;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private readonly string[] GenericOrigins = new[]
        {
            "PODER JUDICIÁRIO",
            "PODER JUDICIARIO",
            "TRIBUNAL DE JUSTIÇA",
            "TRIBUNAL DE JUSTICA",
            "MINISTÉRIO PÚBLICO",
            "MINISTERIO PUBLICO",
            "DEFENSORIA PÚBLICA",
            "DEFENSORIA PUBLICA",
            "PROCURADORIA",
            "ESTADO DA PARAÍBA",
            "ESTADO DA PARAIBA",
            "GOVERNO DO ESTADO"
        };

        private bool IsGeneric(string text)
        {
            var t = (text ?? "").Trim();
            return GenericOrigins.Any(g => string.Equals(g, t, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsCandidateTitle(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var lower = line.ToLowerInvariant();
            string[] kws = { "despacho", "certidão", "certidao", "sentença", "sentenca", "decisão", "decisao", "ofício", "oficio", "laudo", "nota de empenho", "autorização", "autorizacao", "requisição", "requisicao" };
            return kws.Any(k => lower.Contains(k));
        }

        private string ExtractOrigin(string header, List<Dictionary<string, object>> bookmarks, string text, bool excludeGeneric)
        {
            // 1) header em maiúsculas é o melhor candidato
            if (!string.IsNullOrWhiteSpace(header))
            {
                var lines = header.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 3).ToList();
                var upper = lines.FirstOrDefault(l => l.All(c => !char.IsLetter(c) || char.IsUpper(c)));
                if (!string.IsNullOrWhiteSpace(upper) && (!excludeGeneric || !IsGeneric(upper))) return upper;
                var first = lines.FirstOrDefault(l => !excludeGeneric || !IsGeneric(l));
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }

            // 2) bookmark de nível 1
            var bm = bookmarks.FirstOrDefault(b =>
            {
                if (b.TryGetValue("level", out var lvlObj) && int.TryParse(lvlObj?.ToString(), out var lvl))
                    return lvl <= 1;
                return false;
            });
            if (bm != null && bm.TryGetValue("title", out var t) && t != null)
            {
                var val = t.ToString() ?? "";
                if (!excludeGeneric || !IsGeneric(val)) return val;
            }

            // 3) primeira linha do texto
            var firstLine = (text ?? "").Split('\n').FirstOrDefault() ?? "";
            if (excludeGeneric && IsGeneric(firstLine)) return "";
            return firstLine;
        }

        private string ExtractSubOrigin(string header, List<Dictionary<string, object>> bookmarks, string text, string originMain, bool excludeGeneric)
        {
            if (!string.IsNullOrWhiteSpace(header))
            {
                var lines = header.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 3).ToList();
                if (lines.Count > 1)
                {
                    var second = lines.Skip(1).FirstOrDefault(l => !string.Equals(l, originMain, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(second) && (!excludeGeneric || !IsGeneric(second))) return second;
                }
            }

            var bmSub = bookmarks.Skip(1).FirstOrDefault();
            if (bmSub != null && bmSub.TryGetValue("title", out var t) && t != null)
            {
                var val = t.ToString() ?? "";
                if (!excludeGeneric || !IsGeneric(val)) return val;
            }

            // fallback: segunda linha do texto
            var secondLine = (text ?? "").Split('\n').Skip(1).FirstOrDefault() ?? "";
            if (excludeGeneric && IsGeneric(secondLine)) return "";
            return secondLine;
        }

        private string ExtractExtraOrigin(string header, List<Dictionary<string, object>> bookmarks, string text, string originMain, string originSub)
        {
            // Procura uma terceira linha de header que não seja genérica nem igual às anteriores
            if (!string.IsNullOrWhiteSpace(header))
            {
                var lines = header.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 3).ToList();
                var extra = lines.Skip(2).FirstOrDefault(l =>
                    !string.Equals(l, originMain, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(l, originSub, StringComparison.OrdinalIgnoreCase) &&
                    !IsGeneric(l));
                if (!string.IsNullOrWhiteSpace(extra)) return extra;
            }

            // Bookmark de nível 2/3 que não seja genérico
            var bm = bookmarks.FirstOrDefault(b =>
            {
                if (b.TryGetValue("level", out var lvlObj) && int.TryParse(lvlObj?.ToString(), out var lvl))
                    return lvl >= 2;
                return false;
            });
            if (bm != null && bm.TryGetValue("title", out var t) && t != null)
            {
                var val = t.ToString() ?? "";
                if (!IsGeneric(val)) return val;
            }

            // fallback: linha do texto com palavras-chave de setor/órgão (ex.: "Diretoria", "Secretaria", etc.)
            var firstNonGeneric = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l =>
                !string.IsNullOrWhiteSpace(l) &&
                !IsGeneric(l) &&
                !string.Equals(l, originMain, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(l, originSub, StringComparison.OrdinalIgnoreCase));
            return firstNonGeneric ?? "";
        }

        private string ExtractTitle(string header, List<Dictionary<string, object>> bookmarks, string text, string originMain, string originSub)
        {
            // Se o header tiver 3+ linhas, terceira linha costuma ser o título
            if (!string.IsNullOrWhiteSpace(header))
            {
                var lines = header.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 3).ToList();
                var titleFromHeader = lines.Skip(2).FirstOrDefault(l =>
                    !string.Equals(l, originMain, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(l, originSub, StringComparison.OrdinalIgnoreCase) &&
                    !IsGeneric(l) &&
                    IsCandidateTitle(l));
                if (!string.IsNullOrWhiteSpace(titleFromHeader)) return titleFromHeader;
            }

            // Bookmark de nível 2 ou 3 pode conter o título específico
            var bm = bookmarks.FirstOrDefault(b =>
            {
                if (b.TryGetValue("level", out var lvlObj) && int.TryParse(lvlObj?.ToString(), out var lvl))
                    return lvl >= 2;
                return false;
            });
            if (bm != null && bm.TryGetValue("title", out var t) && t != null)
            {
                var val = t.ToString() ?? "";
                if (IsCandidateTitle(val) && !IsGeneric(val)) return val;
            }

            // fallback: primeira linha não vazia do texto (evita repetir origem)
            var firstLine = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l =>
                !string.IsNullOrWhiteSpace(l) &&
                !string.Equals(l, originMain, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(l, originSub, StringComparison.OrdinalIgnoreCase) &&
                !IsGeneric(l) &&
                IsCandidateTitle(l));
            return firstLine ?? "";
        }

        private string ExtractSigner(string lastPageText, string footer, List<DigitalSignature> signatures)
        {
            string[] sources =
            {
                $"{lastPageText}\n{footer}",
                ReverseText($"{lastPageText}\n{footer}")
            };

            foreach (var source in sources)
            {
                // Formato mais completo do SEI: "Documento assinado eletronicamente por NOME, <cargo>, em 12/03/2024"
                var docSigned = Regex.Match(source, @"documento\s+assinado\s+eletronicamente\s+por\s+([\p{L} .'’-]+?)(?:,|\sem\s|\n|$)", RegexOptions.IgnoreCase);
                if (docSigned.Success) return docSigned.Groups[1].Value.Trim();

                var match = Regex.Match(source, @"assinado(?:\s+digitalmente|\s+eletronicamente)?\s+por\s+([\p{L} .'’-]+)", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value.Trim();

                // Digital signature block (X.509)
                var sigMatch = Regex.Match(source, @"Assinatura(?:\s+)?:(.+)", RegexOptions.IgnoreCase);
                if (sigMatch.Success) return sigMatch.Groups[1].Value.Trim();
            }

            // Info vinda do objeto de assinatura digital (se existir)
            if (signatures != null && signatures.Count > 0)
            {
                var sigName = signatures.Select(s => s.SignerName).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                if (!string.IsNullOrWhiteSpace(sigName)) return sigName.Trim();
                var sigField = signatures.Select(s => s.Name).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                if (!string.IsNullOrWhiteSpace(sigField)) return sigField.Trim();
            }

            // Heurística: linha final com nome/cargo em maiúsculas
            var lines = lastPageText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var cargoKeywords = new[] { "diretor", "diretora", "presidente", "juiz", "juíza", "desembargador", "desembargadora", "secretário", "secretaria", "chefe", "coordenador", "coordenadora", "gerente", "perito", "analista", "assessor", "assessora", "procurador", "procuradora" };
            var namePattern = new Regex(@"^[A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-Za-zÁÉÍÓÚÂÊÔÃÕÇçãõâêîôûäëïöüàèìòùÿ'`\-]+(\s+[A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-Za-zÁÉÍÓÚÂÊÔÃÕÇçãõâêîôûäëïöüàèìòùÿ'`\-]+){1,4}(\s*[,–-]\s*.+)?$", RegexOptions.Compiled);
            foreach (var line in lines.AsEnumerable().Reverse())
            {
                if (line.Length < 8 || line.Length > 120) continue;
                if (Regex.IsMatch(line, @"\d{2}[\\/]\d{2}[\\/]\d{2,4}")) continue; // data
                if (line.IndexOf("SEI", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (line.IndexOf("pg.", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("página", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (IsGeneric(line)) continue;
                if (line.ToLowerInvariant() == line) continue; // toda minúscula
                // evita repetir origens ou título
                if (line.Equals(lastPageText, StringComparison.OrdinalIgnoreCase)) continue;
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2) continue;
                var lower = line.ToLowerInvariant();
                if (cargoKeywords.Any(k => lower.Contains(k)) || namePattern.IsMatch(line))
                    return line;
            }
            return "";
        }

        private string ReverseText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var arr = text.ToCharArray();
            Array.Reverse(arr);
            return new string(arr);
        }

        private string ExtractSignedAt(string lastPagesText, string footer)
        {
            var source = $"{lastPagesText}\n{footer}";

            // Prefer datas próximas a termos de assinatura
            var windowMatch = Regex.Match(source, @"assinado[^\n]{0,120}?(\d{1,2}[\/-]?\d{1,2}[\/-]?\d{2,4})", RegexOptions.IgnoreCase);
            if (windowMatch.Success)
            {
                var val = NormalizeDate(windowMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(val)) return val;
            }

            // Datas por extenso: 25 de agosto de 2024
            var extensoMatch = Regex.Match(source, @"\b(\d{1,2})\s+de\s+(janeiro|fevereiro|març|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s+de\s+(\d{4})\b", RegexOptions.IgnoreCase);
            if (extensoMatch.Success)
            {
                var val = NormalizeDateExtenso(extensoMatch.Groups[1].Value, extensoMatch.Groups[2].Value, extensoMatch.Groups[3].Value);
                if (!string.IsNullOrEmpty(val)) return val;
            }

            // Fallback: primeira data plausível
            var match = Regex.Match(source, @"\b(\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4})\b");
            if (match.Success)
            {
                var val = NormalizeDate(match.Groups[1].Value);
                if (!string.IsNullOrEmpty(val)) return val;
            }

            return "";
        }

        private string ExtractDateFromFooter(string lastPagesText, string footer, string header = "")
        {
            var source = $"{footer}\n{lastPagesText}\n{header}";

            var dates = new List<DateTime>();

            var signedAt = ExtractSignedAt(lastPagesText, footer);
            if (!string.IsNullOrWhiteSpace(signedAt) && DateTime.TryParse(signedAt, out var dtSigned))
                dates.Add(dtSigned);

            var extensoMatches = Regex.Matches(source, @"\b(\d{1,2})\s+de\s+(janeiro|fevereiro|març|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s+de\s+(\d{4})\b", RegexOptions.IgnoreCase);
            foreach (Match m in extensoMatches)
            {
                var val = NormalizeDateExtenso(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                if (DateTime.TryParse(val, out var dt)) dates.Add(dt);
            }

            var numericMatches = Regex.Matches(source, @"\b(\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4})\b");
            foreach (Match m in numericMatches)
            {
                var val = NormalizeDate(m.Groups[1].Value);
                if (DateTime.TryParse(val, out var dt)) dates.Add(dt);
            }

            if (dates.Count == 0) return "";
            // Preferir a data de assinatura se presente; caso contrário, a mais recente no rodapé
            if (!string.IsNullOrWhiteSpace(signedAt) && DateTime.TryParse(signedAt, out var dtSignedFirst))
                return dtSignedFirst.ToString("yyyy-MM-dd");

            var latest = dates.OrderByDescending(d => d).First();
            return latest.ToString("yyyy-MM-dd");
        }

        private string NormalizeDate(string raw)
        {
            string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy", "yyyy-MM-dd", "yyyy-M-d" };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                // Filtrar datas implausíveis (anos muito antigos)
                int year = dt.Year;
                int currentYear = DateTime.UtcNow.Year;
                if (year < 1990 || year > currentYear + 1)
                    return "";
                return dt.ToString("yyyy-MM-dd");
            }
            return "";
        }

        private string NormalizeDateFlexible(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var iso = Regex.Match(raw, @"(\d{4}-\d{2}-\d{2})");
            if (iso.Success)
            {
                var valIso = NormalizeDate(iso.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(valIso)) return valIso;
            }

            var ext = Regex.Match(raw, @"(\d{1,2})\s+de\s+(janeiro|fevereiro|març|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s+de\s+(\d{4})", RegexOptions.IgnoreCase);
            if (ext.Success)
            {
                var val = NormalizeDateExtenso(ext.Groups[1].Value, ext.Groups[2].Value, ext.Groups[3].Value);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            var num = Regex.Match(raw, @"(\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4})");
            if (num.Success)
            {
                var val = NormalizeDate(num.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            return NormalizeDate(raw);
        }

        private bool ContainsDecisionPhrase(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            return Regex.IsMatch(t, @"autorizad[ao].{0,80}pagamento.{0,40}honor", RegexOptions.Singleline)
                   || Regex.IsMatch(t, @"autoriza.{0,40}despesa", RegexOptions.Singleline)
                   || Regex.IsMatch(t, @"pagamento\s+dos\s+honor[áa]rios\s+periciais", RegexOptions.Singleline);
        }

        private string NormalizeDateExtenso(string dayStr, string monthStr, string yearStr)
        {
            int day, year;
            if (!int.TryParse(dayStr, out day)) return "";
            if (!int.TryParse(yearStr, out year)) return "";
            var month = MonthFromPortuguese(monthStr);
            if (month == 0) return "";
            try
            {
                var dt = new DateTime(year, month, day);
                int currentYear = DateTime.UtcNow.Year;
                if (year < 1990 || year > currentYear + 1) return "";
                return dt.ToString("yyyy-MM-dd");
            }
            catch { return ""; }
        }

        private int MonthFromPortuguese(string month)
        {
            var m = month.ToLowerInvariant();
            switch (m)
            {
                case "janeiro": return 1;
                case "fevereiro": return 2;
                case "março":
                case "marco": return 3;
                case "abril": return 4;
                case "maio": return 5;
                case "junho": return 6;
                case "julho": return 7;
                case "agosto": return 8;
                case "setembro": return 9;
                case "outubro": return 10;
                case "novembro": return 11;
                case "dezembro": return 12;
                default: return 0;
            }
        }

        // ------------------ Simple field extraction (first module) ------------------

        private static readonly Regex CnjRegex = new Regex(@"\b\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}\b", RegexOptions.Compiled);
        private static readonly Regex SeiLikeRegex = new Regex(@"\b\d{6,}\b", RegexOptions.Compiled);
        private static readonly Regex CpfRegex = new Regex(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", RegexOptions.Compiled);
        private static readonly Regex MoneyRegex = new Regex(@"R\$ ?\d{1,3}(\.\d{3})*,\d{2}", RegexOptions.Compiled);
        private static readonly Regex DateNumRegex = new Regex(@"\b[0-3]?\d/[01]?\d/\d{2,4}\b", RegexOptions.Compiled);

        private List<Dictionary<string, object>> ExtractFields(string fullText, List<Dictionary<string, object>> words, DocumentBoundary d, string pdfPath, List<FieldScript> scripts, string docType)
        {
            var name = ExtractDocumentName(d);
            var bucket = ClassifyBucket(name, fullText, docType);
            var namePdf = name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";

            var fields = new List<Dictionary<string, object>>();
            // YAML oficial
            fields.AddRange(FieldScripts.RunScripts(scripts, namePdf, fullText ?? string.Empty, words, d.StartPage, bucket));
            // Extrator legado para compatibilidade total (inclui processo admin/ADME)
            fields.AddRange(CoreFieldExtractor.Extract(fullText ?? string.Empty, words, d.StartPage));
            return fields;
        }

        private string ClassifyBucket(string name, string text, string? docType = null)
        {
            var n = (name ?? string.Empty).ToLowerInvariant();
            var t = (text ?? string.Empty).ToLowerInvariant();
            var snippet = t.Length > 2000 ? t.Substring(0, 2000) : t;
            if (!string.IsNullOrWhiteSpace(docType))
            {
                if (docType.IndexOf("laudo", StringComparison.OrdinalIgnoreCase) >= 0) return "laudo";
                if (docType.IndexOf("despacho", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    docType.IndexOf("certidao", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    docType.IndexOf("certidão", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    docType.IndexOf("requisicao", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    docType.IndexOf("requisição", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "principal";
            }
            if (IsLaudo(n, snippet)) return "laudo";
            if (IsPrincipal(n, snippet)) return "principal";
            if (IsApoio(n, snippet)) return "apoio";
            return "outro";
        }

        private bool IsLaudo(string name, string text)
        {
            string[] kws = { "laudo", "quesito", "perícia", "pericial", "esclarecimento", "parecer" };
            return kws.Any(k => name.Contains(k) || text.Contains(k));
        }

        private bool IsPrincipal(string name, string text)
        {
            string[] kws = { "despacho", "decisão", "decisao", "senten", "certidao", "certidão", "oficio", "ofício", "nota de empenho", "autorizacao", "autorização", "requisição", "requisicao" };
            return kws.Any(k => name.Contains(k) || text.Contains(k));
        }

        private bool IsApoio(string name, string text)
        {
            string[] kws = { "anexo", "relatório", "relatorio", "planilha" };
            return kws.Any(k => name.Contains(k) || text.Contains(k));
        }
private int FindPageForText(List<Dictionary<string, object>> words, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.ToLowerInvariant()).ToList();
            if (tokens.Count == 0) return 0;

            for (int i = 0; i < words.Count; i++)
            {
                if (words[i]["text"].ToString()!.ToLowerInvariant() != tokens[0]) continue;
                bool ok = true;
                for (int k = 1; k < tokens.Count; k++)
                {
                    if (i + k >= words.Count || words[i + k]["text"].ToString()!.ToLowerInvariant() != tokens[k])
                    {
                        ok = false; break;
                    }
                }
                if (ok)
                {
                    return Convert.ToInt32(words[i]["page"]);
                }
            }
            // fallback: no page found
            return 0;
        }

        private string NormalizeDigits(string raw) => new string(raw.Where(char.IsDigit).ToArray());

        private string MapProfissaoToHonorariosArea(string prof)
        {
            if (string.IsNullOrWhiteSpace(prof)) return "";
            var norm = RemoveDiacritics(prof).ToLowerInvariant();
            if (norm.Contains("psiqu")) return "MEDICINA PSIQUIATRIA";
            if (norm.Contains("neuro")) return "MEDICINA NEUROLOGIA";
            if (norm.Contains("trabalho") && norm.Contains("medic")) return "MEDICINA DO TRABALHO";
            if (norm.Contains("odont")) return "MEDICINA / ODONTOLOGIA";
            if (norm.Contains("medic")) return "MEDICINA";
            if (norm.Contains("psicolog")) return "PSICOLOGIA";
            if (norm.Contains("engenhe") || norm.Contains("arquitet")) return "ENGENHARIA E ARQUITETURA";
            if (norm.Contains("grafot")) return "GRAFOTECNIA";
            if (norm.Contains("assistente social") || norm.Contains("servico social") || norm.Contains("serviço social"))
                return "SERVIÇO SOCIAL";
            if (norm.Contains("contab") || norm.Contains("contador")) return "CIÊNCIAS CONTÁBEIS";

            // fallback: normaliza para título curto
            return PeritoCatalog.NormalizeShortSpecialty(prof);
        }

        private string SanitizeBookmarkTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var t = raw.Replace('_', ' ');
            t = Regex.Replace(t, @"\s+", " ").Trim();
            // Remove códigos SEI ou hashes entre parênteses
            t = Regex.Replace(t, @"\([\dA-Za-z]{4,}\)$", "").Trim();
            // Remove prefixos/sufixos triviais
            t = Regex.Replace(t, @"^\d+\s*-\s*", "");
            t = Regex.Replace(t, @"\s*-\s*SEI.*$", "", RegexOptions.IgnoreCase);
            return t.Trim();
        }

        private bool ContainsDespacho(string title)
        {
            var t = title?.ToLowerInvariant() ?? "";
            return t.Contains("despacho");
        }

        private bool IsDiretoriaEspecial(string originMain, string originSub, string originExtra)
        {
            string Normalize(string s) => RemoveDiacritics(s ?? "").ToLowerInvariant();
            var mains = new[] { Normalize(originMain), Normalize(originSub), Normalize(originExtra) };
            return mains.Any(o => o.Contains("diretoria especial") || o.Contains("deisp") || o.Contains("diesp"));
        }

        private bool IsRobsonSigner(string signer)
        {
            var s = RemoveDiacritics(signer ?? "").ToLowerInvariant();
            return s.Contains("robson");
        }

        private List<string> ExtractPeritoNames(PDFAnalysisResult analysis, int startPage, int endPage)
        {
            var names = new List<string>();
            var regex = new Regex(@"perit[oa][\s:–-]*([A-ZÁÂÃÉÊÍÓÔÕÚÀÇ][A-Za-zÀ-ÿ'`´^~çÇ\.\s]{3,80})", RegexOptions.IgnoreCase);

            for (int p = startPage; p <= endPage; p++)
            {
                var pageTxt = analysis.Pages[p - 1].TextInfo.PageText ?? "";
                foreach (var line in pageTxt.Split('\n'))
                {
                    var m = regex.Match(line);
                    if (m.Success)
                    {
                        var name = m.Groups[1].Value.Trim().Trim('-', '–', ':');
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                }
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? "";
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private List<Dictionary<string, object>> MergeFields(List<Dictionary<string, object>> primary, List<Dictionary<string, object>> secondary)
        {
            var result = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRange(List<Dictionary<string, object>> src)
            {
                foreach (var item in src)
                {
                    var key = $"{item.GetValueOrDefault("name")}|{item.GetValueOrDefault("value")}";
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    result.Add(item);
                }
            }

            AddRange(primary ?? new List<Dictionary<string, object>>());
            AddRange(secondary ?? new List<Dictionary<string, object>>());
            return result;
        }

        /// <summary>
        /// Extrai valores direcionados por função: JZ (relato inicial), DE (autorização GEORC), CM (certidão CM).
        /// Não depende dos YAML; usa contexto de página/faixa para reduzir ruído.
        /// </summary>
        private List<Dictionary<string, object>> ExtractDirectedValues(PDFAnalysisResult analysis, DocumentBoundary d, string docType, string fullText)
        {
            var hits = new List<Dictionary<string, object>>();
            if (analysis?.Pages == null || analysis.Pages.Count == 0) return hits;

            int firstPage = d.StartPage;
            int lastPage = d.EndPage;
            string page1Text = SafePageText(analysis, firstPage);
            string lastPageText = SafePageText(analysis, lastPage);

            // ----- VALOR ARBITRADO - JZ (relato do juiz) na 1ª página (parte alta) -----
            // Contexto: honorários ... R$ ... em favor do perito ... (requisicao, relato)
            var mJz = Regex.Match(page1Text, @"honor[aá]rios[^\n]{0,120}?R\$\s*([0-9\.]{1,3}(?:\.[0-9]{3})*,\d{2})", RegexOptions.IgnoreCase);
            if (mJz.Success)
            {
                AddValueHit(hits, "VALOR ARBITRADO - JZ", mJz.Groups[1].Value, firstPage, "direct_jz_page1");
            }

            // ----- VALOR ARBITRADO - DE (autorização diretor) na última página (rodapé/faixa baixa) -----
            // Contexto: autorizo a despesa / reserva orçamentária / encaminhem-se à GEORC ... valor de R$
            var mDe = Regex.Match(lastPageText, @"(?:(?:autorizo a despesa)|(?:reserva or[cç]ament[áa]ria)|(?:encaminh?em-se[^\n]{0,80}?GEORC)|(?:proceder à reserva or[cç]ament[áa]ria))[^\n]{0,200}?R\$\s*([0-9\.]{1,3}(?:\.[0-9]{3})*,\d{2})", RegexOptions.IgnoreCase);
            if (mDe.Success)
            {
                AddValueHit(hits, "VALOR ARBITRADO - DE", mDe.Groups[1].Value, lastPage, "direct_de_lastpage");
            }

            // ----- VALOR ARBITRADO - CM (certidão do Conselho) -----
            if (!string.IsNullOrWhiteSpace(docType) && docType.Contains("certidao_cm", StringComparison.OrdinalIgnoreCase))
            {
                var mCm = Regex.Match(fullText ?? "", @"(?:autoriza(?:d[oa])?.{0,80}pagamento|despesa)[^\n]{0,160}?R\$\s*([0-9\.]{1,3}(?:\.[0-9]{3})*,\d{2})", RegexOptions.IgnoreCase);
                if (mCm.Success)
                    AddValueHit(hits, "VALOR ARBITRADO - CM", mCm.Groups[1].Value, lastPage, "direct_cm");
            }

            return hits;
        }

        private string SafePageText(PDFAnalysisResult analysis, int page)
        {
            if (analysis == null || analysis.Pages == null) return "";
            if (page < 1 || page > analysis.Pages.Count) return "";
            return analysis.Pages[page - 1].TextInfo.PageText ?? "";
        }

        private void AddValueHit(List<Dictionary<string, object>> hits, string field, string raw, int page, string pattern)
        {
            raw = raw?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return;
            var cleaned = CleanStandard(field, raw);
            if (!ValidateStandard(field, cleaned)) return;
            hits.Add(new Dictionary<string, object>
            {
                ["name"] = field,
                ["value"] = cleaned,
                ["page"] = page,
                ["pattern"] = pattern,
                ["weight"] = 1.0
            });
        }

        private List<Dictionary<string, object>> NormalizeAndValidateFields(List<Dictionary<string, object>> fields)
        {
            if (fields == null) return new List<Dictionary<string, object>>();
            var cleaned = new List<Dictionary<string, object>>();
            HonorariosCatalog.Entry? honorEntry = null;
            double? valorHonor = null;
            string profissaoVal = "";
            string specialtyVal = "";
            string profPeritoLinked = "";
            int peritoPage = 0;

            // Primeiro aplica limpeza/validação por campo
            foreach (var f in fields)
            {
                var name = (f.GetValueOrDefault("name") ?? "").ToString();
                var val = (f.GetValueOrDefault("value") ?? "").ToString();
                var band = f.GetValueOrDefault("band")?.ToString();

                val = TrimAffixes(name, val);

                // ESPÉCIE DE PERÍCIA: agora ignoramos hits textuais; espécie será resolvida via laudos
                if (string.Equals(name, "ESPÉCIE DE PERÍCIA", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(name, "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase))
                    specialtyVal = string.IsNullOrWhiteSpace(val) ? specialtyVal : val;
                if (string.Equals(name, "PROFISSÃO", StringComparison.OrdinalIgnoreCase))
                {
                    profissaoVal = val;
                    int pg = Convert.ToInt32(f.GetValueOrDefault("page") ?? 0);
                    if (peritoPage > 0 && pg == peritoPage)
                        profPeritoLinked = string.IsNullOrWhiteSpace(profPeritoLinked) ? val : profPeritoLinked;
                    else if (peritoPage == 0 && pg == 0)
                        profPeritoLinked = string.IsNullOrWhiteSpace(profPeritoLinked) ? val : profPeritoLinked;
                }
                if (string.Equals(name, "PERITO", StringComparison.OrdinalIgnoreCase))
                {
                    peritoPage = Convert.ToInt32(f.GetValueOrDefault("page") ?? peritoPage);
                }
                // valores (qualquer arbitração/honorário)
                if (name.StartsWith("VALOR", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var vMoney))
                        valorHonor ??= vMoney;
                }

                val = CleanStandard(name, val);
                if (!ValidateStandard(name, val)) continue;
                f["value"] = val;
                f["name"] = name.ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(band)) f["band"] = band;
                cleaned.Add(f);
            }

            // Filtra especialidades lixo (muito longas ou frase completa)
            cleaned = cleaned.Where(f =>
            {
                var n = f.GetValueOrDefault("name")?.ToString();
                if (!string.Equals(n, "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase)) return true;
                var v = f.GetValueOrDefault("value")?.ToString() ?? "";
                var words = v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                return v.Length <= 40 && words <= 6;
            }).ToList();

            double? PickMoney(string fieldName)
            {
                var item = cleaned.FirstOrDefault(f => string.Equals(f.GetValueOrDefault("name")?.ToString(), fieldName, StringComparison.OrdinalIgnoreCase));
                if (item == null) return null;
                var v = item.GetValueOrDefault("value")?.ToString() ?? "";
                if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
                return null;
            }

            // Reprioriza valor: juiz > diretor > conselho > honorários genéricos
            valorHonor = PickMoney("VALOR ARBITRADO - JZ")
                         ?? PickMoney("VALOR ARBITRADO - DE")
                         ?? PickMoney("VALOR ARBITRADO - CM")
                         ?? PickMoney("VALOR HONORARIOS")
                         ?? valorHonor;

            // Consolidar ESPECIALIDADE: derivar da profissão (prioriza a colada ao perito)
            string specialtyCandidate = "";
            if (!string.IsNullOrWhiteSpace(profPeritoLinked))
                specialtyCandidate = MapProfissaoToHonorariosArea(profPeritoLinked);
            if (string.IsNullOrWhiteSpace(specialtyCandidate) && !string.IsNullOrWhiteSpace(profissaoVal))
                specialtyCandidate = MapProfissaoToHonorariosArea(profissaoVal);

            if (!string.IsNullOrWhiteSpace(specialtyCandidate))
            {
                cleaned = cleaned.Where(x => !string.Equals(x.GetValueOrDefault("name")?.ToString(), "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase)).ToList();
                cleaned.Add(new Dictionary<string, object>
                {
                    ["name"] = "ESPECIALIDADE",
                    ["value"] = specialtyCandidate,
                    ["page"] = 0,
                    ["pattern"] = "profissao_map",
                    ["weight"] = 0.7
                });
            }

            // Resolver ESPÉCIE: somente Tabela de Honorários, mesma área; valor como critério primário
            if (!string.IsNullOrWhiteSpace(specialtyCandidate) && valorHonor.HasValue)
                honorEntry = HonorariosCatalog.MatchByAreaAndValue(specialtyCandidate, valorHonor.Value);

            if (honorEntry == null && !string.IsNullOrWhiteSpace(specialtyCandidate))
                honorEntry = HonorariosCatalog.MatchUniqueByArea(specialtyCandidate); // só se área tiver espécie única

            if (honorEntry != null)
            {
                cleaned.Add(new Dictionary<string, object>
                {
                    ["name"] = "ESPÉCIE DE PERÍCIA",
                    ["value"] = honorEntry.Descricao,
                    ["page"] = 0,
                    ["pattern"] = "species_from_honorarios",
                    ["weight"] = 0.7
                });
            }

            // Anexa referência da tabela de honorários se encontramos (sem sobrescrever especialidade/espécie)
            if (honorEntry != null)
            {
                if (!cleaned.Any(x => string.Equals(x.GetValueOrDefault("name")?.ToString(), "Fator", StringComparison.OrdinalIgnoreCase)))
                {
                    cleaned.Add(new Dictionary<string, object>
                    {
                        ["name"] = "Fator",
                        ["value"] = honorEntry.Id,
                        ["page"] = 0,
                        ["pattern"] = "honorarios_catalog_id",
                        ["weight"] = 0.9
                    });
                }

                cleaned.Add(new Dictionary<string, object>
                {
                    ["name"] = "HONORARIOS_TABELA_ID",
                    ["value"] = honorEntry.Id,
                    ["page"] = 0,
                    ["pattern"] = "honorarios_catalog",
                    ["weight"] = 0.7
                });
                cleaned.Add(new Dictionary<string, object>
                {
                    ["name"] = "HONORARIOS_TABELA_DESC",
                    ["value"] = honorEntry.Descricao,
                    ["page"] = 0,
                    ["pattern"] = "honorarios_catalog",
                    ["weight"] = 0.7
                });
                cleaned.Add(new Dictionary<string, object>
                {
                    ["name"] = "HONORARIOS_TABELA_VALOR",
                    ["value"] = honorEntry.Valor.ToString("0.00", CultureInfo.InvariantCulture),
                    ["page"] = 0,
                    ["pattern"] = "honorarios_catalog",
                    ["weight"] = 0.7
                });

                if (valorHonor.HasValue)
                {
                    var status = Math.Abs(valorHonor.Value - honorEntry.Valor) < 0.01 ? "info_ok" : "info_mismatch";
                    cleaned.Add(new Dictionary<string, object>
                    {
                        ["name"] = "HONORARIOS_VALIDACAO",
                        ["value"] = $"{status}|esperado:{honorEntry.Valor:0.00}|achado:{valorHonor.Value:0.00}",
                        ["page"] = 0,
                        ["pattern"] = "honorarios_catalog_compare",
                        ["weight"] = status == "ok" ? 1.0 : 0.2
                });
            }
            }

            // Consistência e validação contra catálogo de peritos
            string perito = cleaned.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "PERITO", StringComparison.OrdinalIgnoreCase))?.GetValueOrDefault("value")?.ToString();
            string cpf = cleaned.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "CPF/CNPJ", StringComparison.OrdinalIgnoreCase))?.GetValueOrDefault("value")?.ToString();
            string especialidade = cleaned.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase))?.GetValueOrDefault("value")?.ToString();
            string profissao = cleaned.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "PROFISSÃO", StringComparison.OrdinalIgnoreCase))?.GetValueOrDefault("value")?.ToString();

            var lookupByCpf = PeritoCatalog.Lookup(null, cpf);
            var lookup = lookupByCpf.found ? lookupByCpf : PeritoCatalog.Lookup(perito, cpf);
            if (lookup.found)
            {
                // remove variantes e reescreve com valores do catálogo
                cleaned = cleaned.Where(f =>
                {
                    var n = f["name"]?.ToString();
                    return !(string.Equals(n, "PERITO", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(n, "CPF/CNPJ", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(n, "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase));
                }).ToList();

                cleaned.Add(new Dictionary<string, object> { ["name"] = "PERITO", ["value"] = lookup.nome, ["page"] = 0, ["pattern"] = "perito_catalog", ["weight"] = 0.95 });
                if (!string.IsNullOrWhiteSpace(lookup.cpf))
                    cleaned.Add(new Dictionary<string, object> { ["name"] = "CPF DO PERITO", ["value"] = lookup.cpf, ["page"] = 0, ["pattern"] = "perito_catalog", ["weight"] = 0.95 });
                var espFinal = !string.IsNullOrWhiteSpace(lookup.especialidade) ? lookup.especialidade : especialidade;
                espFinal = PeritoCatalog.NormalizeShortSpecialty(espFinal ?? "");
                if (!string.IsNullOrWhiteSpace(espFinal))
                    cleaned.Add(new Dictionary<string, object> { ["name"] = "ESPECIALIDADE", ["value"] = espFinal, ["page"] = 0, ["pattern"] = "perito_catalog", ["weight"] = 0.90 });
            }
            else
            {
                // Sem catálogo: mantém nome/CPF e tenta mapear profissão -> especialidade da tabela de honorários.
                string promA = cleaned.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "PROMOVENTE", StringComparison.OrdinalIgnoreCase))?.GetValueOrDefault("value")?.ToString();
                string promB = cleaned.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "PROMOVIDO", StringComparison.OrdinalIgnoreCase))?.GetValueOrDefault("value")?.ToString();
                string normPerito = NormalizeName(perito ?? "");
                bool peritoCoincideParte = (!string.IsNullOrWhiteSpace(normPerito) &&
                                            (string.Equals(normPerito, NormalizeName(promA ?? ""), StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(normPerito, NormalizeName(promB ?? ""), StringComparison.OrdinalIgnoreCase)));

                bool hasCpfPerito = !string.IsNullOrWhiteSpace(cpf);

                // Só remove se for claramente igual a uma parte e sem CPF; caso contrário preserva.
                if (peritoCoincideParte && !hasCpfPerito)
                {
                    cleaned = cleaned.Where(f =>
                    {
                        var n = f["name"]?.ToString();
                        return !(string.Equals(n, "PERITO", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(n, "PROFISSÃO", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(n, "CPF/CNPJ", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(n, "CPF DO PERITO", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(n, "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase));
                    }).ToList();
                }
                else
                {
                    string espFromProf = !string.IsNullOrWhiteSpace(profissao)
                        ? MapProfissaoToHonorariosArea(profissao)
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(espFromProf) && !string.IsNullOrWhiteSpace(especialidade))
                        espFromProf = MapProfissaoToHonorariosArea(especialidade);

                    if (!string.IsNullOrWhiteSpace(espFromProf))
                    {
                        cleaned = cleaned.Where(f => !string.Equals(f["name"]?.ToString(), "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase)).ToList();
                        cleaned.Add(new Dictionary<string, object>
                        {
                            ["name"] = "ESPECIALIDADE",
                            ["value"] = espFromProf,
                            ["page"] = 0,
                            ["pattern"] = "profissao_map",
                            ["weight"] = 0.55
                        });
                    }
                }
            }

            // Dedup name|value
            var dedup = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in cleaned)
            {
                var key = $"{f.GetValueOrDefault("name")}|{f.GetValueOrDefault("value")}";
                if (seen.Contains(key)) continue;
                seen.Add(key);
                dedup.Add(f);
            }

            // Forçar ESPECIALIDADE coerente com PROFISSÃO (pós-dedup)
            var profCleaned = dedup.FirstOrDefault(x => string.Equals(x.GetValueOrDefault("name")?.ToString(), "PROFISSÃO", StringComparison.OrdinalIgnoreCase))
                                   ?.GetValueOrDefault("value")?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(profCleaned))
            {
                var mapped = MapProfissaoToHonorariosArea(profCleaned);
                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    dedup = dedup.Where(x => !string.Equals(x.GetValueOrDefault("name")?.ToString(), "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase)).ToList();
                    dedup.Add(new Dictionary<string, object>
                    {
                        ["name"] = "ESPECIALIDADE",
                        ["value"] = mapped,
                        ["page"] = 0,
                        ["pattern"] = "profissao_map_post",
                        ["weight"] = 0.7
                    });
                }
            }

            return dedup;
        }

        private string CleanStandard(string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? "";
            var f = field.ToUpperInvariant();
            switch (f)
            {
                case "VALOR HONORARIOS":
                case "VALOR ARBITRADO - JZ":
                case "VALOR ARBITRADO - DE":
                case "VALOR ARBITRADO - CM":
                case "ADIANTAMENTO":
                case "VALOR TABELADO ANEXO I - TABELA I":
                    var money = CleanMoney(value);
                    if (double.TryParse(money, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var vm))
                    {
                        if (vm > 1000) vm = vm / 100.0;
                        money = vm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    return money;
                case "CPF/CNPJ":
                    return FormatCpf(value);
                case "CPF DO PERITO":
                    return FormatCpf(value);
                case "PERITO":
                    value = StripSpecialtyFromPerito(value);
                    value = TrimAffixes("PERITO", value);
                    return NormalizeName(value);
                case "PROMOVENTE":
                case "PROMOVIDO":
                    return NormalizeName(value);
                case "PROFISSÃO":
                case "ESPECIALIDADE":
                case "ESPÉCIE DE PERÍCIA":
                    value = TrimAffixes(f, value);
                    value = PeritoCatalog.NormalizeShortSpecialty(value);
                    return value;
                case "COMARCA":
                    return NormalizeComarca(value);
                case "DATA DO DESPACHO":
                case "DATA DA CERTIDAO":
                case "DATA DA AUTORIZACAO DA DESPESA":
                    var dt = NormalizeDateFlexible(value);
                    return string.IsNullOrWhiteSpace(dt) ? Regex.Replace(value, @"\s+", " ").Trim() : dt;
                default:
                    return Regex.Replace(value, @"\s+", " ").Trim();
            }
        }

        private bool ValidateStandard(string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var f = field.ToUpperInvariant();
            return f switch
            {
                "VALOR HONORARIOS" or "VALOR ARBITRADO - JZ" or "VALOR ARBITRADO - DE" or "VALOR ARBITRADO - CM" or "ADIANTAMENTO" or "VALOR TABELADO ANEXO I - TABELA I"
                    => Regex.IsMatch(value, @"^\d+(?:\.\d{2})?$"),
                "CPF/CNPJ" => value.Length >= 11 && value.Length <= 18,
                "PERITO" => value.Length >= 5,
                "PROFISSÃO" or "ESPECIALIDADE" or "ESPÉCIE DE PERÍCIA" or "PROMOVENTE" or "PROMOVIDO" or "COMARCA" or "JUÍZO"
                    => value.Length >= 3,
                "PROCESSO JUDICIAL"
                    => Regex.IsMatch(value, @"^\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}$"),
                "DATA DO DESPACHO" or "DATA DA CERTIDAO" or "DATA DA AUTORIZACAO DA DESPESA"
                    => Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$"),
                _ => true
            };
        }

        private string CleanMoney(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var trimmed = raw.Trim();
            // Tenta primeiro formato pt-BR com vírgula
            var match = Regex.Match(trimmed, @"\d[\d\.]*,\d{2}");
            if (match.Success && double.TryParse(match.Value, System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("pt-BR"), out var vbr))
                return vbr.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            var digits = trimmed.Replace("R$", "", StringComparison.OrdinalIgnoreCase)
                                .Replace(" ", "");
            digits = digits.Replace(".", "");
            digits = digits.Replace(",", ".");
            if (double.TryParse(digits, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                // Se não havia separador, assume duas casas (ex.: 37000 -> 370.00)
                if (!trimmed.Contains(",") && !trimmed.Contains(".") && v > 1000) v = v / 100.0;
                // Se veio como 37000.00 mas deveria ser 370.00 (5 dígitos + .00)
                if (v > 1000 && Regex.IsMatch(trimmed, @"^\d{4,6}\.00$")) v = v / 100.0;
                return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            }
            // fallback: se só dígitos, tenta dividir por 100
            var onlyDigits = Regex.Replace(digits, @"[^\d]", "");
            if (onlyDigits.Length > 2 && double.TryParse(onlyDigits, out var v2))
                return (v2 / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return trimmed;
        }

        private string FormatCpf(string raw)
        {
            var digits = Regex.Replace(raw ?? "", @"\D", "");
            if (digits.Length == 11)
                return $"{digits.Substring(0, 3)}.{digits.Substring(3, 3)}.{digits.Substring(6, 3)}-{digits.Substring(9, 2)}";
            return digits;
        }

        private string NormalizeName(string val)
        {
            val = Regex.Replace(val ?? "", @"\s+", " ").Trim();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(val.ToLowerInvariant());
        }

        private string NormalizeTitle(string val)
        {
            val = Regex.Replace(val ?? "", @"\s+", " ").Trim();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(val.ToLowerInvariant());
        }

        private string NormalizeComarca(string val)
        {
            val = (val ?? "").Replace("Comarca de", "", StringComparison.OrdinalIgnoreCase);
            return NormalizeTitle(val);
        }

        private string StripSpecialtyFromPerito(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return val ?? "";
            var parts = Regex.Split(val, @"\s*[–-]\s*");
            if (parts.Length >= 2)
            {
                var left = parts[0].Trim();
                var right = string.Join(" - ", parts.Skip(1)).Trim();
                bool leftSpec = LooksLikeSpecialty(left);
                bool rightSpec = LooksLikeSpecialty(right);
                if (leftSpec && !rightSpec) return right;
                if (rightSpec && !leftSpec) return left;
            }
            return val;
        }

        // Remove títulos e sufixos que costumam vir grudados nos campos textuais
        private string TrimAffixes(string field, string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return val ?? "";
            var f = field.ToUpperInvariant();

            if (f == "PERITO" || f == "ESPECIALIDADE" || f == "PROFISSÃO")
            {
                // corta depois de identificadores de documento
                val = Regex.Split(val, @"(?i)(CPF|PIS|PASEP|INSS|CBO|NASCID|EMAIL|E-MAIL|FONE|TEL)").FirstOrDefault() ?? val;
                // remove títulos no início
                val = Regex.Replace(val, @"^(?i)(perit[oa]|dr\.?|dra\.?|doutor(?:a)?|sr\.?|sra\.?)\s+", "");
                // remove sufixos “- perito …”
                val = Regex.Replace(val, @"(?i)\s*[-–]\s*perit[oa].*$", "");
                val = Regex.Replace(val, @"\s+", " ").Trim();
            }

            if (f == "PROMOVENTE" || f == "PROMOVIDO" || f.StartsWith("JUÍZO"))
            {
                val = Regex.Replace(val, @"(?i)perante o ju[ií]zo\s+de\s+", "");
                val = Regex.Replace(val, @"(?i)nos autos\s+do\s+processo.*", "");
                val = Regex.Replace(val, @"\s+", " ").Trim();
            }

            return val;
        }

        private bool LooksLikeSpecialty(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            var keywords = new[] { "psiq", "psi", "medic", "engenheir", "odont", "grafot", "assistente social", "social", "contab", "perito", "perita" };
            return keywords.Any(k => t.Contains(k));
        }

        private class BandPattern
        {
            public BandPattern(string field, string pattern, RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Singleline)
            {
                Field = field;
                Pattern = pattern;
                Options = options;
            }
            public string Field { get; }
            public string Pattern { get; }
            public RegexOptions Options { get; }
        }

        private List<Dictionary<string, object>> ExtractBandFields(string fullText, List<Dictionary<string, object>> words, int fallbackPage)
        {
            var hits = new List<Dictionary<string, object>>();
            if (words == null || words.Count == 0) return hits;

            var paragraphs = BuildParagraphsFromWords(words);
            // Agrupa texto por banda usando mesmo corte do LayoutBands
            var bandTexts = new Dictionary<string, string>
            {
                ["header"] = "",
                ["subheader"] = "",
                ["body1"] = "",
                ["body2"] = "",
                ["body3"] = "",
                ["body4"] = "",
                ["footer"] = ""
            };

            foreach (var p in paragraphs)
            {
                string band = p.Ny0 >= 0.88 ? "header"
                              : p.Ny0 >= 0.80 ? "subheader"
                              : p.Ny0 >= 0.65 ? "body1"
                              : p.Ny0 >= 0.50 ? "body2"
                              : p.Ny0 >= 0.35 ? "body3"
                              : p.Ny0 >= 0.20 ? "body4"
                              : "footer";
                bandTexts[band] += p.Text + "\n";
            }

            var patterns = new List<BandPattern>
            {
                new BandPattern("PROCESSO ADMINISTRATIVO", @"Processo\s+n[ºo]\s+([\d\.\-\/]+)"),
                new BandPattern("REQUERENTE/VARA/COMARCA", @"Requerente:\s*([^\n]{1,120}?)\s+Interessado:", RegexOptions.IgnoreCase | RegexOptions.Singleline),
                new BandPattern("PERITO", @"Interessado:\s*([^–-]{3,80})[–-]\s*([^\n]{2,80})"),
                new BandPattern("ESPECIALIDADE", @"Interessado:\s*[^–-]{3,80}[–-]\s*([^\n]{2,80})"),
                new BandPattern("CPF/CNPJ", @"CPF\s+([\d\.\-]{11,18})"),
                new BandPattern("PROCESSO JUDICIAL", @"autos do processo nº\s+([\d\.\-\/]+)"),
                new BandPattern("PROMOVENTE", @"movid[oa]\s+por\s+(.+?)(?=,\s*(?:CPF|CNPJ))"),
                new BandPattern("PROMOVIDO", @"em face de\s+(.+?)(?=,\s*(?:CPF|CNPJ))"),
                new BandPattern("VALOR HONORARIOS", @"valor de R\$\s*([\d\.,]+)"),
                new BandPattern("DIRETOR ASSINATURA", @"Em razão do exposto, autorizo a despesa,[\s\S]{0,400}?([^\n]{1,80})$")
            };

            var filled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var band in new[] { "header", "subheader", "body1", "body2", "body3", "body4", "footer" })
            {
                var text = bandTexts[band];
                foreach (var pat in patterns)
                {
                    if (filled.Contains(pat.Field)) continue;
                    var rg = new Regex(pat.Pattern, pat.Options);
                    var m = rg.Match(text);
                    if (!m.Success) continue;

                    // Determine value by field
                    string value;
                    if (string.Equals(pat.Field, "PROMOVIDO", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(pat.Field, "PROMOVENTE", StringComparison.OrdinalIgnoreCase))
                    {
                        // pick group 1 or 2 depending on field
                        value = string.Equals(pat.Field, "PROMOVENTE", StringComparison.OrdinalIgnoreCase) ? m.Groups[1].Value : m.Groups[2].Value;
                    }
                    else if (string.Equals(pat.Field, "ESPECIALIDADE", StringComparison.OrdinalIgnoreCase) && m.Groups.Count > 2)
                    {
                        value = m.Groups[2].Value;
                    }
                    else
                    {
                        value = m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
                    }

                    var hit = MakeBandHit(pat.Field, value, pat.Pattern, words, fallbackPage, band);
                    if (hit.Count > 0)
                    {
                        hits.Add(hit);
                        filled.Add(pat.Field);
                    }
                }
            }

            return hits;
        }

        private Dictionary<string, object> MakeBandHit(string field, string value, string pattern, List<Dictionary<string, object>> words, int fallbackPage, string band)
        {
            value = value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value)) return new Dictionary<string, object>();
            var bbox = FindBBoxForBand(words, value);
            int page = bbox?.page ?? fallbackPage;
            value = CleanStandard(field, value);
            if (!ValidateStandard(field, value)) return new Dictionary<string, object>();

            var hit = new Dictionary<string, object>
            {
                ["name"] = field,
                ["value"] = value,
                ["page"] = page,
                ["pattern"] = pattern,
                ["weight"] = 0.6,
                ["band"] = band
            };
            if (bbox != null)
            {
                hit["bbox"] = new Dictionary<string, double>
                {
                    ["nx0"] = bbox.Value.nx0,
                    ["ny0"] = bbox.Value.ny0,
                    ["nx1"] = bbox.Value.nx1,
                    ["ny1"] = bbox.Value.ny1
                };
            }
            return hit;
        }

        private (int page, double nx0, double ny0, double nx1, double ny1)? FindBBoxForBand(List<Dictionary<string, object>> words, string raw)
        {
            if (words == null || words.Count == 0 || string.IsNullOrWhiteSpace(raw)) return null;
            var tokens = Regex.Split(raw, @"\s+")
                              .Select(t => Regex.Replace(t, @"[^\p{L}\p{N}]+", "").ToLowerInvariant())
                              .Where(t => t.Length > 0)
                              .ToList();
            if (tokens.Count == 0) return null;

            var wordTokens = words.Select(w => Regex.Replace(w.GetValueOrDefault("text")?.ToString() ?? "", @"[^\p{L}\p{N}]+", "").ToLowerInvariant()).ToList();
            for (int i = 0; i < wordTokens.Count; i++)
            {
                if (wordTokens[i] != tokens[0]) continue;
                int k = 0;
                int page = Convert.ToInt32(words[i]["page"]);
                while (i + k < wordTokens.Count && k < tokens.Count)
                {
                    int pcur = Convert.ToInt32(words[i + k]["page"]);
                    if (pcur != page) break;
                    if (wordTokens[i + k] != tokens[k]) break;
                    k++;
                }
                if (k == tokens.Count)
                {
                    var slice = words.Skip(i).Take(k);
                    double nx0 = slice.Min(w => Convert.ToDouble(w["nx0"]));
                    double ny0 = slice.Min(w => Convert.ToDouble(w["ny0"]));
                    double nx1 = slice.Max(w => Convert.ToDouble(w["nx1"]));
                    double ny1 = slice.Max(w => Convert.ToDouble(w["ny1"]));
                    return (page, nx0, ny0, nx1, ny1);
                }
            }
            return null;
        }

        private List<Dictionary<string, object>> ExtractBookmarksForRange(PDFAnalysisResult analysis, int startPage, int endPage)
        {
            var list = new List<Dictionary<string, object>>();
            if (analysis.Bookmarks?.RootItems == null) return list;

            void Walk(IEnumerable<BookmarkItem> items)
            {
                foreach (var b in items)
                {
                    int page = b.Destination?.PageNumber ?? 0;
                    if (page >= startPage && page <= endPage && page > 0)
                    {
                        list.Add(new Dictionary<string, object>
                        {
                            ["title"] = b.Title,
                            ["page"] = page,
                            ["level"] = b.Level
                        });
                    }

                    if (b.Children != null && b.Children.Count > 0)
                        Walk(b.Children);
                }
            }

            Walk(analysis.Bookmarks.RootItems);
            return list.OrderBy(b => (int)b["page"]).ToList();
        }

        // BuildBookmarkBoundaries removido (legado) – segmentação por bookmark é feita inline no loop principal.

        private PDFAnalysisResult CloneRange(PDFAnalysisResult analysis, int startPage, int endPage)
        {
            var clone = new PDFAnalysisResult
            {
                FilePath = analysis.FilePath,
                FileSize = analysis.FileSize,
                AnalysisDate = analysis.AnalysisDate,
                Metadata = analysis.Metadata,
                XMPMetadata = analysis.XMPMetadata,
                DocumentInfo = new DocumentInfo { TotalPages = endPage - startPage + 1 },
                Pages = analysis.Pages.Skip(startPage - 1).Take(endPage - startPage + 1)
                    .Select(p => p) // shallow copy is enough for segmentation heuristics
                    .ToList(),
                Security = analysis.Security,
                Resources = analysis.Resources,
                Statistics = analysis.Statistics,
                Accessibility = analysis.Accessibility,
                Layers = analysis.Layers,
                Signatures = analysis.Signatures,
                ColorProfiles = analysis.ColorProfiles,
                Bookmarks = new BookmarkStructure { RootItems = new List<BookmarkItem>(), MaxDepth = 0, TotalCount = 0 },
                PDFACompliance = analysis.PDFACompliance,
                Multimedia = analysis.Multimedia,
                PDFAValidation = analysis.PDFAValidation,
                SecurityInfo = analysis.SecurityInfo,
                AccessibilityInfo = analysis.AccessibilityInfo
            };
            return clone;
        }

        private string ExtractDocumentName(DocumentBoundary d)
        {
            // mesmo critério do FpdfDocumentsCommand: primeira linha do FullText/FirstPageText
            var text = d.FullText ?? d.FirstPageText ?? "";
            var firstLine = text.Split('\n').FirstOrDefault() ?? "";
            return firstLine.Length > 80 ? firstLine.Substring(0, 80) + "..." : firstLine;
        }

        private string HashText(string text)
        {
            text ??= "";
            var normalized = Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(normalized);
            var hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private List<Dictionary<string, object>> SplitAnexos(DocumentBoundary parent, PDFAnalysisResult analysis, string pdfPath, List<FieldScript> scripts, double despachoThreshold)
        {
            var list = new List<Dictionary<string, object>>();

            // Pega bookmarks "Anexo/Anexos" no range do documento
            var subBms = ExtractBookmarksForRange(analysis, parent.StartPage, parent.EndPage)
                .Where(b => Regex.IsMatch(b["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase))
                .OrderBy(b => (int)b["page"])
                .ToList();

            if (subBms.Count == 0) return list;

            // Cria subfaixas a partir dos bookmarks: cada anexo vai da página do bookmark até a página anterior ao próximo bookmark (ou fim do doc)
            for (int i = 0; i < subBms.Count; i++)
            {
                int start = (int)subBms[i]["page"];
                int end = (i + 1 < subBms.Count) ? ((int)subBms[i + 1]["page"]) - 1 : parent.EndPage;
                if (start < parent.StartPage || start > parent.EndPage) continue;
                if (end < start) end = start;

                var boundary = new DocumentBoundary
                {
                    StartPage = start,
                    EndPage = end,
                    DetectedType = "anexo",
                    FirstPageText = analysis.Pages[start - 1].TextInfo.PageText,
                    LastPageText = analysis.Pages[end - 1].TextInfo.PageText,
                    FullText = string.Join("\n", Enumerable.Range(start, end - start + 1).Select(p => analysis.Pages[p - 1].TextInfo.PageText)),
                    Fonts = parent.Fonts,
                    PageSize = parent.PageSize,
                    HasSignatureImage = parent.HasSignatureImage,
                    TotalWords = parent.TotalWords
                };

                var obj = BuildDocObject(boundary, analysis, pdfPath, scripts, despachoThreshold);
                obj["parent_doc_label"] = ExtractDocumentName(parent);
                obj["doc_label"] = subBms[i]["title"]?.ToString() ?? "Anexo";
                obj["inside_anexo"] = true;
                list.Add(obj);
            }

            return list;
        }
    }
}
