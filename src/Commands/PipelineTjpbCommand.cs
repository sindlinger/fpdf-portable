using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Executa a etapa FPDF do pipeline-tjpb: processa todos os PDFs de um diretório
    /// e gera um JSON consolidado compatível com tmp/pipeline/step2/fpdf.json.
    ///
    /// Uso:
    ///   fpdf pipeline-tjpb --input-dir tmp/preprocessor_stage --output tmp/pipeline/step2/fpdf.json
    ///
    /// Campos gerados por documento:
    /// - process, pdf_path
    /// - doc_label, start_page, end_page, doc_pages, total_pages
    /// - text (concatenação das páginas do documento)
    /// - fonts (nomes), images (quantidade), page_size, has_signature_image (heurística)
    /// </summary>
    public class PipelineTjpbCommand : Command
    {
        public override string Name => "pipeline-tjpb";
        public override string Description => "Etapa FPDF do pipeline-tjpb: consolida documentos em JSON";

        public override void Execute(string[] args)
        {
            string inputDir = ".";
            string output = "fpdf.json";
            bool splitAnexos = false; // backward compat (anexos now covered by bookmark docs)
            int maxBookmarkPages = 30; // agora interno, sem flag na CLI
            string? pgUri = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--input-dir" && i + 1 < args.Length) inputDir = args[i + 1];
                if (args[i] == "--output" && i + 1 < args.Length) output = args[i + 1];
                if (args[i] == "--split-anexos") splitAnexos = true;
                if (args[i] == "--pg-uri" && i + 1 < args.Length) pgUri = args[i + 1];
            }

            var dir = new DirectoryInfo(inputDir);
            if (!dir.Exists)
            {
                Console.WriteLine($"Diretório não encontrado: {inputDir}");
                return;
            }

            var pdfs = dir.GetFiles("*.pdf").OrderBy(f => f.Name).ToList();
            var allDocs = new List<Dictionary<string, object>>();

            foreach (var pdf in pdfs)
            {
                try
                {
                    var analysis = new PDFAnalyzer(pdf.FullName).AnalyzeFull();

                    // Opcional: persiste análise completa no Postgres
                    if (!string.IsNullOrWhiteSpace(pgUri))
                    {
                        try
                        {
                            var classifier = new BookmarkClassifier();
                            PgDocStore.UpsertProcess(pgUri, pdf.FullName, analysis, classifier, storeJson: false);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[pipeline-tjpb] WARN PG {pdf.Name}: {ex.Message}");
                        }
                    }
                    var bookmarkDocs = BuildBookmarkBoundaries(analysis, maxBookmarkPages);
                    var segmenter = new DocumentSegmenter(new DocumentSegmentationConfig());
                    var docs = bookmarkDocs.Count > 0 ? bookmarkDocs : segmenter.FindDocuments(analysis);

                    foreach (var d in docs)
                    {
                        var obj = BuildDocObject(d, analysis, pdf.FullName);
                        allDocs.Add(obj);

                        // Legacy split-anexos now redundant; kept for compatibility
                        if (splitAnexos && d.DetectedType == "anexo")
                        {
                            var anexosChildren = SplitAnexos(d, analysis, pdf.FullName);
                            allDocs.AddRange(anexosChildren);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[pipeline-tjpb] WARN {pdf.Name}: {ex.Message}");
                }
            }

            var result = new { documents = allDocs };
            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
            File.WriteAllText(output, json);
            Console.WriteLine($"[pipeline-tjpb] {allDocs.Count} documentos -> {output}");
        }

        public override void ShowHelp()
        {
            Console.WriteLine("fpdf pipeline-tjpb --input-dir <dir> --output fpdf.json [--split-anexos]");
            Console.WriteLine("Gera JSON compatível com tmp/pipeline/step2/fpdf.json para o CI Dashboard.");
            Console.WriteLine("--split-anexos: cria subdocumentos a partir de bookmarks 'Anexo/Anexos' dentro de cada documento.");
        }

        private Dictionary<string, object> BuildDocObject(DocumentBoundary d, PDFAnalysisResult analysis, string pdfPath)
        {
            var docText = string.Join("\n", Enumerable.Range(d.StartPage, d.PageCount)
                                                      .Select(p => analysis.Pages[p - 1].TextInfo.PageText ?? ""));
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
            }

            var pageSize = analysis.Pages.First().Size.GetPaperSize();

            // Heurística: bookmark intitulado "Anexo" ou "Anexos" pode conter vários documentos fundidos.
            // Vamos registrar todos os bookmarks de nível 1/2 com esse título dentro do range,
            // para permitir pós-processamento (split fino) sem quebrar a segmentação principal.
            var anexos = docBookmarks
                .Where(b => Regex.IsMatch(b["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase))
                .ToList();
            double textDensity = pageAreaAcc > 0 ? wordsArea / pageAreaAcc : 0;
            double blankRatio = 1 - textDensity;

            var docType = string.IsNullOrEmpty(d.DetectedType) ? "heuristic" : d.DetectedType;
            var docSummary = BuildDocSummary(d, pdfPath, docText, lastPageText, lastTwoText, header, footer, docBookmarks, analysis.Signatures);
            var extractedFields = ExtractFields(docText, wordsWithCoords);

            return new Dictionary<string, object>
            {
                ["process"] = Path.GetFileNameWithoutExtension(pdfPath),
                ["pdf_path"] = pdfPath,
                ["doc_label"] = ExtractDocumentName(d),
                ["doc_type"] = docType,
                ["start_page"] = d.StartPage,
                ["end_page"] = d.EndPage,
                ["doc_pages"] = d.PageCount,
                ["total_pages"] = analysis.DocumentInfo.TotalPages,
                ["text"] = docText,
                ["fonts"] = fonts.ToArray(),
                ["images"] = images,
                ["page_size"] = pageSize,
                ["has_signature_image"] = hasSignature,
                ["is_attachment"] = false,
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
                ["fields"] = extractedFields
            };
        }

        private Dictionary<string, object> BuildDocSummary(DocumentBoundary d, string pdfPath, string fullText, string lastPageText, string lastTwoText, string header, string footer, List<Dictionary<string, object>> bookmarks, List<DigitalSignature> signatures)
        {
            string docId = $"{Path.GetFileNameWithoutExtension(pdfPath)}_{d.StartPage}-{d.EndPage}";
            string originMain = ExtractOrigin(header, bookmarks, fullText, excludeGeneric: true);
            string originSub = ExtractSubOrigin(header, bookmarks, fullText, originMain, excludeGeneric: true);
            string originExtra = ExtractExtraOrigin(header, bookmarks, fullText, originMain, originSub);
            string signer = ExtractSigner(lastTwoText, footer, signatures);
            string signedAt = ExtractSignedAt(lastTwoText, footer);
            string template = string.IsNullOrWhiteSpace(d.DetectedType) ? "unknown" : d.DetectedType;
            string title = ExtractTitle(header, bookmarks, fullText, originMain, originSub);

            return new Dictionary<string, object>
            {
                ["doc_id"] = docId,
                ["origin_main"] = originMain,
                ["origin_sub"] = originSub,
                ["origin_extra"] = originExtra,
                ["signer"] = signer,
                ["signed_at"] = signedAt,
                ["title"] = title,
                ["template"] = template
            };
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
                var docSigned = Regex.Match(source, @"documento\s+assinado\s+eletronicamente\s+por\s+([\\p{L} .'’-]+?)(?:,|\sem\s|\n|$)", RegexOptions.IgnoreCase);
                if (docSigned.Success) return docSigned.Groups[1].Value.Trim();

                var match = Regex.Match(source, @"assinado(?:\s+digitalmente|\s+eletronicamente)?\s+por\s+([\\p{L} .'’-]+)", RegexOptions.IgnoreCase);
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
            var windowMatch = Regex.Match(source, @"assinado[^\\n]{0,120}?(\\d{1,2}[\\/]-?\\d{1,2}[\\/]-?\\d{2,4})", RegexOptions.IgnoreCase);
            if (windowMatch.Success)
            {
                var val = NormalizeDate(windowMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(val)) return val;
            }

            // Datas por extenso: 25 de agosto de 2024
            var extensoMatch = Regex.Match(source, @"\\b(\\d{1,2})\\s+de\\s+(janeiro|fevereiro|març|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\\s+de\\s+(\\d{4})\\b", RegexOptions.IgnoreCase);
            if (extensoMatch.Success)
            {
                var val = NormalizeDateExtenso(extensoMatch.Groups[1].Value, extensoMatch.Groups[2].Value, extensoMatch.Groups[3].Value);
                if (!string.IsNullOrEmpty(val)) return val;
            }

            // Fallback: primeira data plausível
            var match = Regex.Match(source, @"\\b(\\d{1,2}[\\/-]\\d{1,2}[\\/-]\\d{2,4})\\b");
            if (match.Success)
            {
                var val = NormalizeDate(match.Groups[1].Value);
                if (!string.IsNullOrEmpty(val)) return val;
            }

            return "";
        }

        private string NormalizeDate(string raw)
        {
            string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy" };
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

        private List<Dictionary<string, object>> ExtractFields(string fullText, List<Dictionary<string, object>> words)
        {
            var list = new List<Dictionary<string, object>>();

            // PARTES (Promovente / Promovido)
            var reqReq = Regex.Match(fullText, @"requerente\s*[:\-]?\s*(.+?)\s+requerid[oa]\s*[:\-]?\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (reqReq.Success)
                AddTwoFields(list, "PROMOVENTE", reqReq.Groups[1].Value, "PROMOVIDO", reqReq.Groups[2].Value, "requerente_requerido");

            var autorReu = Regex.Match(fullText, @"autor\s*[:\-]?\s*(.+?)\s+r[eé]u\s*[:\-]?\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (autorReu.Success)
                AddTwoFields(list, "PROMOVENTE", autorReu.Groups[1].Value, "PROMOVIDO", autorReu.Groups[2].Value, "autor_reu");

            var movidoPor = Regex.Match(fullText, @"movid[oa]\s+por\s+(.+?)\s+em\s+face\s+de\s+(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (movidoPor.Success)
                AddTwoFields(list, "PROMOVENTE", movidoPor.Groups[1].Value, "PROMOVIDO", movidoPor.Groups[2].Value, "movido_por");

            // PERITO / CPF / PROFISSÃO
            var peritoLinha = Regex.Match(fullText, @"perit[oa]\s*[:\-]?\s*([A-ZÁÂÃÀÉÊÍÓÔÕÚÇ][A-Za-zÁÂÃÀÉÊÍÓÔÕÚÇ\s\.\-]{5,})", RegexOptions.IgnoreCase);
            if (peritoLinha.Success)
                list.Add(MakeField("PERITO", peritoLinha.Groups[1].Value, "perito_linha"));

            var interessado = Regex.Match(fullText, @"interessad[oa]\s*[:\-]?\s*([A-ZÁÂÃÀÉÊÍÓÔÕÚÇ][A-Za-zÁÂÃÀÉÊÍÓÔÕÚÇ\s\.\-]{5,})", RegexOptions.IgnoreCase);
            if (interessado.Success)
                list.Add(MakeField("PERITO", interessado.Groups[1].Value, "interessado"));

            var cpfMatch = CpfRegex.Match(fullText);
            if (cpfMatch.Success)
                list.Add(MakeField("CPF/CNPJ", cpfMatch.Value, "cpf"));

            var prof = Regex.Match(fullText, @"perit[oa][^\n]{0,40}?(m[eé]dic[oa]|psic[oó]log[oa]|engenheir[oa](?:\s+civil)?|odont[oó]log[oa]|contabil|grafot[eé]cnico|assistente\s+social)", RegexOptions.IgnoreCase);
            if (prof.Success)
                list.Add(MakeField("PROFISSÃO", prof.Groups[1].Value, "profissao_no_despacho"));
            else
            {
                var profIso = Regex.Match(fullText, @"(m[eé]dic[oa]|psic[oó]log[oa]|engenheir[oa](?:\s+civil)?|odont[oó]log[oa]|contabil|grafot[eé]cnico|assistente\s+social)", RegexOptions.IgnoreCase);
                if (profIso.Success)
                    list.Add(MakeField("PROFISSÃO", profIso.Groups[1].Value, "profissao_isolada"));
            }

            // VALOR TABELADO / FATOR (honorários)
            var fator = Regex.Match(fullText, @"fator\s*[:\-]?\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (fator.Success)
                list.Add(MakeField("Fator", fator.Groups[1].Value, "fator_inline"));

            var valorTab = MoneyRegex.Match(fullText);
            if (valorTab.Success)
                list.Add(MakeField("Valor Tabelado Anexo I - Tabela I", valorTab.Value, "valor_tabelado"));

            // VALORES ARBITRADOS / DATA AUTORIZAÇÃO / ADIANTAMENTO
            var valArb = Regex.Match(fullText, @"valor\s+dos\s+honor[aá]rios\s+(?:finais|arbitrados)[^\d]*(R\\$ ?\\d{1,3}(?:\\.\\d{3})*,\\d{2})", RegexOptions.IgnoreCase);
            if (valArb.Success)
                list.Add(MakeField("VALOR ARBITRADO - JZ", valArb.Groups[1].Value, "valor_arbitrado"));

            var dataAut = Regex.Match(fullText, @"autoriz[aç][aã]o\s+da\s+despesa[^\d]*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase);
            if (dataAut.Success)
                list.Add(MakeField("DATA DA AUTORIZACAO DA DESPESA", dataAut.Groups[1].Value, "data_autorizacao"));

            var adiant = Regex.Match(fullText, @"adiantamento[^\d]*(R\\$ ?\\d{1,3}(?:\\.\\d{3})*,\\d{2})", RegexOptions.IgnoreCase);
            if (adiant.Success)
                list.Add(MakeField("ADIANTAMENTO", adiant.Groups[1].Value, "adiantamento"));

            // JUÍZO / COMARCA
            var juizoLinha = Regex.Match(fullText, @"ju[ií]zo\s*[:\-]?\s*([^\n]+)", RegexOptions.IgnoreCase);
            if (juizoLinha.Success)
                list.Add(MakeField("JUÍZO", juizoLinha.Groups[1].Value, "juizo_linha"));

            var orgaoJulg = Regex.Match(fullText, @"[óo]rg[aã]o julgador\s*[:\-]?\s*([^\n]+)", RegexOptions.IgnoreCase);
            if (orgaoJulg.Success)
                list.Add(MakeField("JUÍZO", orgaoJulg.Groups[1].Value, "orgao_julgador"));

            var varaComarca = Regex.Match(fullText, @"(\d+\s*a?\s*vara[^\n]{0,80}?)\s+da\s+comarca\s+de\s+([\w\sçãáéíóúãõç]+)", RegexOptions.IgnoreCase);
            if (varaComarca.Success)
            {
                list.Add(MakeField("JUÍZO", varaComarca.Groups[1].Value, "vara_comarca_dupla"));
                list.Add(MakeField("COMARCA", varaComarca.Groups[2].Value, "vara_comarca_dupla"));
            }

            if (Regex.IsMatch(fullText, @"comarca da capital", RegexOptions.IgnoreCase))
                list.Add(MakeField("COMARCA", "João Pessoa", "comarca_capital"));

            var comarcaDe = Regex.Match(fullText, @"comarca\s+de\s+([\w\sçãáéíóúãõç]+)", RegexOptions.IgnoreCase);
            if (comarcaDe.Success)
                list.Add(MakeField("COMARCA", comarcaDe.Groups[1].Value, "comarca_de"));

            // ESPECIALIDADE / ESPÉCIE (simplificada, com keywords)
            var especMatch = Regex.Match(fullText, @"esp[eé]cie\s+de\s+per[ií]cia\s*[:\-]?\s*([^\n]+)", RegexOptions.IgnoreCase);
            if (especMatch.Success)
                list.Add(MakeField("ESPÉCIE DE PERÍCIA", especMatch.Groups[1].Value, "especie_inline"));
            if (Regex.IsMatch(fullText, @"\\blaudo\\b", RegexOptions.IgnoreCase))
                list.Add(MakeField("ESPÉCIE DE PERÍCIA", "LAUDO", "laudo_kw"));
            if (Regex.IsMatch(fullText, @"grafo[té]cnic", RegexOptions.IgnoreCase))
                list.Add(MakeField("ESPECIALIDADE", "GRAFOTÉCNICO", "especialidade_kw"));
            if (Regex.IsMatch(fullText, @"m[eé]dic", RegexOptions.IgnoreCase))
                list.Add(MakeField("ESPECIALIDADE", "MÉDICA", "especialidade_kw"));

            // PROCESSO JUDICIAL (CNJ)
            var cnjMatch = CnjRegex.Match(fullText);
            if (cnjMatch.Success)
            {
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = "PROCESSO JUDICIAL",
                    ["value"] = cnjMatch.Value,
                    ["page"] = FindPageForText(words, cnjMatch.Value),
                    ["pattern"] = "cnj_regex"
                });
            }

            // PROCESSO ADMINISTRATIVO / SEI
            // Aceita número SEI ou administrativo longo (>=14 dígitos) que não seja CNJ
            var seiMatch = SeiLikeRegex.Matches(fullText)
                .Cast<Match>()
                .FirstOrDefault(m => m.Value.Length >= 14 && !CnjRegex.IsMatch(m.Value));
            if (seiMatch != null)
            {
                var norm = NormalizeDigits(seiMatch.Value);
                if (!string.IsNullOrEmpty(norm))
                {
                    list.Add(new Dictionary<string, object>
                    {
                        ["name"] = "PROCESSO ADMINISTRATIVO",
                        ["value"] = norm,
                        ["page"] = FindPageForText(words, seiMatch.Value),
                        ["pattern"] = "sei_numeric"
                    });
                }
            }

            return list;
        }

        private void AddTwoFields(List<Dictionary<string, object>> list, string f1, string v1, string f2, string v2, string pattern)
        {
            if (!string.IsNullOrWhiteSpace(v1))
                list.Add(MakeField(f1, v1, pattern));
            if (!string.IsNullOrWhiteSpace(v2))
                list.Add(MakeField(f2, v2, pattern));
        }

        private Dictionary<string, object> MakeField(string name, string value, string pattern)
        {
            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["value"] = value.Trim(),
                ["page"] = 0,
                ["pattern"] = pattern
            };
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

        /// <summary>
        /// Converte bookmarks em limites de documentos. Bookmarks passam a ser "documentos".
        /// Se não houver bookmarks, retorna lista vazia (segmenter padrão será usado).
        /// </summary>
        private List<DocumentBoundary> BuildBookmarkBoundaries(PDFAnalysisResult analysis, int maxBookmarkPages)
        {
            var result = new List<DocumentBoundary>();
            var bms = ExtractBookmarksForRange(analysis, 1, analysis.DocumentInfo.TotalPages);
            if (bms.Count == 0) return result;

            for (int i = 0; i < bms.Count; i++)
            {
                int start = (int)bms[i]["page"];
                int end = (i + 1 < bms.Count) ? ((int)bms[i + 1]["page"]) - 1 : analysis.DocumentInfo.TotalPages;
                if (end < start) end = start;

                bool isAnexo = Regex.IsMatch(bms[i]["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase);
                bool needsSegment = (end - start + 1) > maxBookmarkPages;

                // Se o bookmark for muito grande, resegmenta internamente com o segmenter heurístico
                if (needsSegment)
                {
                    var segmenter = new DocumentSegmenter(new DocumentSegmentationConfig());
                    var subAnalysis = CloneRange(analysis, start, end);
                    var subDocs = segmenter.FindDocuments(subAnalysis);

                    foreach (var sd in subDocs)
                    {
                        sd.StartPage += (start - 1); // ajustar para páginas originais
                        sd.EndPage += (start - 1);
                        sd.DetectedType = isAnexo ? "anexo+segment" : "bookmark+segment";
                        result.Add(sd);
                    }

                    // Se não encontrou subdocs (caso extremo), registra o próprio bookmark
                    if (subDocs.Count == 0)
                    {
                        var fallback = new DocumentBoundary
                        {
                            StartPage = start,
                            EndPage = end,
                            DetectedType = isAnexo ? "anexo" : "bookmark",
                            FirstPageText = analysis.Pages[start - 1].TextInfo.PageText,
                            LastPageText = analysis.Pages[end - 1].TextInfo.PageText,
                            FullText = string.Join("\n", Enumerable.Range(start, end - start + 1).Select(p => analysis.Pages[p - 1].TextInfo.PageText)),
                            Fonts = new HashSet<string>(analysis.Pages.Skip(start - 1).Take(end - start + 1).SelectMany(p => p.TextInfo.Fonts.Select(f => f.Name)), StringComparer.OrdinalIgnoreCase),
                            PageSize = analysis.Pages.First().Size.GetPaperSize(),
                            HasSignatureImage = analysis.Pages.Skip(start - 1).Take(end - start + 1).Any(p => p.Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false),
                            TotalWords = analysis.Pages.Skip(start - 1).Take(end - start + 1).Sum(p => p.TextInfo.WordCount)
                        };
                        result.Add(fallback);
                    }
                }
                else
                {
                    var boundary = new DocumentBoundary
                    {
                        StartPage = start,
                        EndPage = end,
                        DetectedType = isAnexo ? "anexo" : "bookmark",
                        FirstPageText = analysis.Pages[start - 1].TextInfo.PageText,
                        LastPageText = analysis.Pages[end - 1].TextInfo.PageText,
                        FullText = string.Join("\n", Enumerable.Range(start, end - start + 1).Select(p => analysis.Pages[p - 1].TextInfo.PageText)),
                        Fonts = new HashSet<string>(analysis.Pages.Skip(start - 1).Take(end - start + 1).SelectMany(p => p.TextInfo.Fonts.Select(f => f.Name)), StringComparer.OrdinalIgnoreCase),
                        PageSize = analysis.Pages.First().Size.GetPaperSize(),
                        HasSignatureImage = analysis.Pages.Skip(start - 1).Take(end - start + 1).Any(p => p.Resources.Images?.Any(img => img.Width > 100 && img.Height > 30) ?? false),
                        TotalWords = analysis.Pages.Skip(start - 1).Take(end - start + 1).Sum(p => p.TextInfo.WordCount)
                    };
                    result.Add(boundary);
                }
            }

            return result;
        }

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

        private List<Dictionary<string, object>> SplitAnexos(DocumentBoundary parent, PDFAnalysisResult analysis, string pdfPath)
        {
            var list = new List<Dictionary<string, object>>();

            // Pega bookmarks "Anexo/Anexos" no range do documento
            var anexos = ExtractBookmarksForRange(analysis, parent.StartPage, parent.EndPage)
                .Where(b => Regex.IsMatch(b["title"].ToString() ?? "", "^anexos?$", RegexOptions.IgnoreCase))
                .OrderBy(b => (int)b["page"])
                .ToList();

            if (anexos.Count == 0) return list;

            // Cria subfaixas a partir dos bookmarks: cada anexo vai da página do bookmark até a página anterior ao próximo bookmark (ou fim do doc)
            for (int i = 0; i < anexos.Count; i++)
            {
                int start = (int)anexos[i]["page"];
                int end = (i + 1 < anexos.Count) ? ((int)anexos[i + 1]["page"]) - 1 : parent.EndPage;
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

                var obj = BuildDocObject(boundary, analysis, pdfPath);
                obj["parent_doc_label"] = ExtractDocumentName(parent);
                obj["doc_label"] = anexos[i]["title"]?.ToString() ?? "Anexo";
                obj["doc_type"] = "anexo_split";
                list.Add(obj);
            }

            return list;
        }
    }
}
