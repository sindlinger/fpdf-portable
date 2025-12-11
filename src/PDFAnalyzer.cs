using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using FilterPDF.Strategies;
using iText.Kernel.Geom;
using iTextSharp.text.pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
// All text extraction now via iText7
using PdfReader7 = iText.Kernel.Pdf.PdfReader;
using PdfDocument7 = iText.Kernel.Pdf.PdfDocument;
using PdfTextExtractor7 = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor;
// Aliases to avoid ambiguity while migrating
using PdfDictionary5 = iTextSharp.text.pdf.PdfDictionary;
using PdfArray5 = iTextSharp.text.pdf.PdfArray;
using PdfName5 = iTextSharp.text.pdf.PdfName;
using PdfIndirectReference5 = iTextSharp.text.pdf.PdfIndirectReference;
using PRStream5 = iTextSharp.text.pdf.PRStream;
using PdfDictionary7 = iText.Kernel.Pdf.PdfDictionary;
using PdfArray7 = iText.Kernel.Pdf.PdfArray;
using PdfName7 = iText.Kernel.Pdf.PdfName;
using PdfStream7 = iText.Kernel.Pdf.PdfStream;
using PdfIndirectReference7 = iText.Kernel.Pdf.PdfIndirectReference;

namespace FilterPDF
{
    /// <summary>
    /// Analisador completo de PDFs - extrai todas as informações disponíveis
    /// Author: Eduardo Candeia Gonçalves (sindlinger@github.com)
    /// </summary>
    public class PDFAnalyzer
    {
        private PdfReader reader;          // iTextSharp (legacy paths)
        private string pdfPath;
        private bool ownsReader;
        private PdfDocument7? i7doc;       // iText7 document for new extraction
        private bool ownsDoc7;
        private readonly bool forceLegacyText;
        
        public PDFAnalyzer(string pdfPath)
        {
            this.pdfPath = pdfPath;
            this.forceLegacyText = Environment.GetEnvironmentVariable("FPDF_TEXT_LEGACY") == "1";
            Console.WriteLine($"    [PDFAnalyzer] Opening PDF: {Path.GetFileName(pdfPath)}");

            // Abrir iTextSharp reader (legado) e iText7 doc (novo)
            this.reader = PdfAccessManager.GetReader(pdfPath);
            this.ownsReader = false;
            this.i7doc = FilterPDF.Utils.PdfAccessManager7.GetDocument(pdfPath);
            this.ownsDoc7 = false; // cache gerencia ciclo de vida

            Console.WriteLine($"    [PDFAnalyzer] PDF opened successfully. Pages: {reader.NumberOfPages}");
        }
        
        /// <summary>
        /// Constructor that accepts an existing reader (for backward compatibility)
        /// </summary>
        public PDFAnalyzer(string pdfPath, PdfReader reader)
        {
            this.pdfPath = pdfPath;
            this.forceLegacyText = Environment.GetEnvironmentVariable("FPDF_TEXT_LEGACY") == "1";
            // Manter compat: se veio um reader externo (iText7), usamos doc a partir dele
            this.reader = reader;
            this.ownsReader = false;
            this.i7doc = FilterPDF.Utils.PdfAccessManager7.GetDocument(pdfPath);
            this.ownsDoc7 = false;
            Console.WriteLine($"    [PDFAnalyzer] Using existing reader. Pages: {reader.NumberOfPages}");
        }
        
        /// <summary>
        /// Análise completa do PDF
        /// </summary>
        public PDFAnalysisResult AnalyzeFull()
        {
            var result = new PDFAnalysisResult
            {
                FilePath = pdfPath,
                FileSize = new FileInfo(pdfPath).Length,
                AnalysisDate = DateTime.Now
            };
            
            try
            {
                // Metadados básicos
                result.Metadata = ExtractMetadata();
                
                // Metadados XMP
                result.XMPMetadata = ExtractXMPMetadata();
                
                // Informações do documento
                result.DocumentInfo = ExtractDocumentInfo();
                
                // Análise por página
                result.Pages = AnalyzePages();
                
                // Segurança
                result.Security = ExtractSecurityInfo();
                
                // Recursos
                result.Resources = ExtractResources();
                
                // Estatísticas
                result.Statistics = CalculateStatistics(result);
                
                // Novas funcionalidades avançadas
                result.Accessibility = ExtractAccessibilityInfo();
                result.Layers = ExtractOptionalContentGroups();
                result.Signatures = ExtractDigitalSignatures();
                result.ColorProfiles = ExtractColorProfiles();
                result.Bookmarks = ExtractBookmarkStructure();
                
                // Usar processadores avançados para funcionalidades das DLLs
                var advancedProcessor = new AdvancedPDFProcessor(reader);
                result.XMPMetadata = advancedProcessor.ExtractCompleteXMPMetadata();
                result.PDFACompliance = advancedProcessor.AnalyzePDFAConformance();
                result.Multimedia = advancedProcessor.DetectRichMedia();
                
                // Usar RichMediaProcessor para análise detalhada de Rich Media
                // var richMediaProcessor = new RichMediaProcessor(reader);
                // result.RichMediaAnalysis = richMediaProcessor.AnalyzeRichMedia();
                
                // Usar SpatialProcessor para dados espaciais
                // var spatialProcessor = new SpatialProcessor(reader);
                // result.SpatialData = spatialProcessor.AnalyzeSpatialData();
                
                // Usar PDFAProcessor para análise detalhada de PDF/A
                // var pdfaProcessor = new PDFAProcessor(reader);
                // result.PDFAValidation = pdfaProcessor.ValidatePDFA();
                // result.PDFACharacteristics = pdfaProcessor.AnalyzePDFACharacteristics();
            }
            finally
            {
                // Don't close the reader if we don't own it (it's managed by PdfAccessManager)
                if (ownsReader && reader != null)
                {
                    reader.Close();
                }
                if (ownsDoc7 && i7doc != null)
                {
                    i7doc.Close();
                }
            }
            
            return result;
        }
        
        private Metadata ExtractMetadata()
        {
            var metadata = new Metadata();

            // Tenta iText7 primeiro
            if (i7doc != null)
            {
                try
                {
                    var info = i7doc.GetDocumentInfo();
                    metadata.Title = info.GetTitle();
                    metadata.Author = info.GetAuthor();
                    metadata.Subject = info.GetSubject();
                    metadata.Keywords = info.GetKeywords();
                    metadata.Creator = info.GetCreator();
                    metadata.Producer = info.GetProducer();
                    metadata.CreationDate = ParsePDFDate(info.GetMoreInfo(PdfName7.CreationDate.GetValue()));
                    metadata.ModificationDate = ParsePDFDate(info.GetMoreInfo(PdfName7.ModDate.GetValue()));
                    metadata.PDFVersion = i7doc.GetPdfVersion().ToString();
                    metadata.IsTagged = i7doc.IsTagged();
                    return metadata;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] iText7 metadata failed: {ex.Message}, falling back to legacy");
                }
            }

            // Fallback iTextSharp
            var infoLegacy = reader.Info;
            if (infoLegacy.ContainsKey("Title")) metadata.Title = infoLegacy["Title"];
            if (infoLegacy.ContainsKey("Author")) metadata.Author = infoLegacy["Author"];
            if (infoLegacy.ContainsKey("Subject")) metadata.Subject = infoLegacy["Subject"];
            if (infoLegacy.ContainsKey("Keywords")) metadata.Keywords = infoLegacy["Keywords"];
            if (infoLegacy.ContainsKey("Creator")) metadata.Creator = infoLegacy["Creator"];
            if (infoLegacy.ContainsKey("Producer")) metadata.Producer = infoLegacy["Producer"];
            if (infoLegacy.ContainsKey("CreationDate")) metadata.CreationDate = ParsePDFDate(infoLegacy["CreationDate"]);
            if (infoLegacy.ContainsKey("ModDate")) metadata.ModificationDate = ParsePDFDate(infoLegacy["ModDate"]);

            metadata.PDFVersion = reader.PdfVersion.ToString();
            metadata.IsTagged = false; // iTextSharp não expõe
            return metadata;
        }
        
        private DocumentInfo ExtractDocumentInfo()
        {
            if (i7doc != null)
            {
                try
                {
                    var reader7 = i7doc.GetReader();
                    return new DocumentInfo
                    {
                        TotalPages = i7doc.GetNumberOfPages(),
                        IsEncrypted = reader7.IsEncrypted(),
                        IsLinearized = reader7.HasRebuiltXref() == false, // proxy: se não reconstruído, assume linearizado/ok
                        HasAcroForm = i7doc.GetCatalog()?.GetPdfObject()?.GetAsDictionary(PdfName7.AcroForm) != null,
                        HasXFA = false, // TODO: detectar XFA em iText7
                        FileStructure = reader7.HasRebuiltXref() ? "Rebuilt" : "Original"
                    };
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] iText7 doc info failed: {ex.Message}, fallback legacy");
                }
            }

            // Fallback iTextSharp
            return new DocumentInfo
            {
                TotalPages = reader.NumberOfPages,
                IsEncrypted = reader.IsEncrypted(),
                IsLinearized = false, // Not available in this version
                HasAcroForm = reader.AcroForm != null,
                HasXFA = reader.AcroFields.Xfa != null,
                FileStructure = reader.IsRebuilt() ? "Rebuilt" : "Original"
            };
        }
        
        private List<PageAnalysis> AnalyzePages()
        {
            var pages = new List<PageAnalysis>();
            
            for (int i = 1; i <= reader.NumberOfPages; i++)
            {
                var page = new PageAnalysis
                {
                    PageNumber = i,
                    Size = GetPageSize(i),
                    Rotation = reader.GetPageRotation(i),
                    TextInfo = AnalyzePageText(i),
                    Resources = AnalyzePageResources(i),
                    Annotations = ExtractAnnotations(i)
                };
                
                // Copiar FontInfo do TextInfo para o PageAnalysis
                page.FontInfo = page.TextInfo.Fonts;
                
                // Detectar cabeçalhos/rodapés e referências
                DetectHeadersFooters(page);
                DetectDocumentReferences(page);
                
                pages.Add(page);
            }
            
            return pages;
        }
        
        private PageSize GetPageSize(int pageNum)
        {
            var rect = reader.GetPageSize(pageNum);
            return new PageSize
            {
                Width = rect.Width,
                Height = rect.Height,
                WidthPoints = rect.Width,
                HeightPoints = rect.Height,
                WidthInches = rect.Width / 72f,
                HeightInches = rect.Height / 72f,
                WidthMM = rect.Width * 0.352778f,
                HeightMM = rect.Height * 0.352778f
            };
        }
        
        private TextInfo AnalyzePageText(int pageNum)
        {
            // Texto via iText7 apenas
            string text = string.Empty;
            if (i7doc != null && !forceLegacyText)
            {
                try
                {
                    text = PdfTextExtractor7.GetTextFromPage(i7doc.GetPage(pageNum));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] iText7 text extract failed on page {pageNum}: {ex.Message}");
                }
            }

            var textInfo = new TextInfo
            {
                CharacterCount = text.Length,
                WordCount = CountWords(text),
                LineCount = text.Split('\n').Length,
                Languages = DetectLanguages(text),
                // COMENTADO: dependia da strategy customizada
                // HasTables = strategy.HasTables(),
                // HasColumns = strategy.HasColumns(),
                HasTables = false, // TODO: detectar sem strategy customizada
                HasColumns = false, // TODO: detectar sem strategy customizada
                AverageLineLength = CalculateAverageLineLength(text),
                PageText = text // Texto original SEM MODIFICAÇÕES
            };
            
            // EXTRAÇÃO COMPLETA DE FONTES - TODAS as instâncias com tamanhos diferentes
            textInfo.Fonts = ExtractAllPageFontsWithSizes(pageNum);

            float pageWidth = 0;
            float pageHeight = 0;
            if (i7doc != null)
            {
                var rect = i7doc.GetPage(pageNum).GetPageSize();
                pageWidth = rect.GetWidth();
                pageHeight = rect.GetHeight();
            }

            // EXTRAÇÃO DE LINHAS COM FONTE/ESTILO/COORDENADAS (usando iText7)
            try
            {
                if (i7doc != null)
                {
                    var collector = new Strategies.IText7LineCollector();
                    var processor = new PdfCanvasProcessor(collector);
                    processor.ProcessPageContent(i7doc.GetPage(pageNum));
                    textInfo.Lines = collector.GetLines();
                    if (pageWidth > 0 && pageHeight > 0)
                    {
                        foreach (var l in textInfo.Lines)
                        {
                            l.NormX0 = l.X0 / pageWidth;
                            l.NormX1 = l.X1 / pageWidth;
                            l.NormY0 = l.Y0 / pageHeight;
                            l.NormY1 = l.Y1 / pageHeight;
                        }
                    }
                }
            }
            catch { }

            // EXTRAÇÃO DE PALAVRAS COM BBOX (para templates/campos) usando iText7
            try
            {
                if (i7doc != null)
                {
                    var wordCollector = new Strategies.IText7WordCollector();
                    var processor = new PdfCanvasProcessor(wordCollector);
                    processor.ProcessPageContent(i7doc.GetPage(pageNum));
                    textInfo.Words = wordCollector.GetWords();
                    if (textInfo.Words?.Count > 0)
                        textInfo.WordCount = textInfo.Words.Count;

                    if (pageWidth > 0 && pageHeight > 0)
                    {
                        foreach (var w in textInfo.Words)
                        {
                            w.NormX0 = w.X0 / pageWidth;
                            w.NormX1 = w.X1 / pageWidth;
                            w.NormY0 = w.Y0 / pageHeight;
                            w.NormY1 = w.Y1 / pageHeight;
                        }
                    }
                }
            }
            catch { }

            return textInfo;
        }
        
        /// <summary>
        /// EXTRAI TODAS AS FONTES COM TODOS OS TAMANHOS USADOS - COMO O CÓDIGO ROBUSTO ANTIGO
        /// </summary>
        private List<FontInfo> ExtractAllPageFontsWithSizes(int pageNum)
        {
            var fonts = new List<FontInfo>();
            var fontSizeMap = new Dictionary<string, HashSet<float>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (i7doc != null)
                {
                    var page = i7doc.GetPage(pageNum);

                    // Coletar tamanhos via listener iText7
                    var collector = new FilterPDF.Strategies.FontSizeCollector7(fontSizeMap);
                    var processor = new PdfCanvasProcessor(collector);
                    processor.ProcessPageContent(page);

                    var resources = page.GetResources();
                    var fontDict = resources?.GetResource(PdfName7.Font) as PdfDictionary7;

                    if (fontDict != null)
                    {
                        foreach (var fontKey in fontDict.KeySet())
                        {
                            var fontObj = fontDict.GetAsDictionary(fontKey);
                            if (fontObj != null)
                            {
                                var baseFont = fontObj.GetAsName(PdfName7.BaseFont) ?? fontObj.GetAsName(PdfName7.FontName);
                                string fontName = baseFont?.ToString() ?? fontKey.ToString();
                                fontName = FontNameFixer.Fix(fontName);

                                bool isEmbedded = false; // iText7 font dictionary doesn't expose embed flag directly here
                                string style = GetStyleFromName(fontName);

                                var sizes = new HashSet<float>();

                                string fontKeyStr = fontKey.ToString();
                                if (fontSizeMap.ContainsKey(fontKeyStr))
                                    sizes.UnionWith(fontSizeMap[fontKeyStr]);

                                foreach (var kvp in fontSizeMap)
                                {
                                    if (fontName.Contains(kvp.Key) || kvp.Key.Contains(fontName))
                                    {
                                        sizes.UnionWith(kvp.Value);
                                    }
                                }

                                if (sizes.Count == 0)
                                {
                                    sizes.Add(12.0f); // default fallback
                                }

                                string fontType = "Type1";
                                var subtype = fontObj.GetAsName(PdfName7.Subtype);
                                if (subtype != null)
                                {
                                    string subtypeStr = subtype.ToString();
                                    if (subtypeStr.Contains("TrueType")) fontType = "TrueType";
                                    else if (subtypeStr.Contains("Type0")) fontType = "Type0";
                                    else if (subtypeStr.Contains("CIDFont")) fontType = "CIDFont";
                                    else if (subtypeStr.Contains("Type3")) fontType = "Type3";
                                }

                                bool isBold = fontName.Contains("Bold") || fontName.Contains("bold") ||
                                              fontName.Contains("Heavy") || fontName.Contains("Black");
                                bool isItalic = fontName.Contains("Italic") || fontName.Contains("italic") ||
                                                fontName.Contains("Oblique") || fontName.Contains("Slant");
                                bool isMonospace = fontName.Contains("Courier") || fontName.Contains("Mono") ||
                                                   fontName.Contains("Consolas") || fontName.Contains("Fixed");
                                bool isSerif = fontName.Contains("Times") || fontName.Contains("Serif") ||
                                               fontName.Contains("Georgia") || fontName.Contains("Palatino");
                                bool isSansSerif = fontName.Contains("Arial") || fontName.Contains("Helvetica") ||
                                                   fontName.Contains("Sans") || fontName.Contains("Verdana");

                                fonts.Add(new FontInfo
                                {
                                    Name = fontName,
                                    BaseFont = fontName,
                                    FontType = fontType,
                                    Size = sizes.First(),
                                    Style = style,
                                    IsEmbedded = isEmbedded,
                                    FontSizes = sizes.ToList(),
                                    IsBold = isBold,
                                    IsItalic = isItalic,
                                    IsMonospace = isMonospace,
                                    IsSerif = isSerif,
                                    IsSansSerif = isSansSerif
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            return fonts;
        }
        
        private bool IsFontEmbedded(PdfDictionary fontDict)
        {
            try
            {
                var fontDescriptor = fontDict.GetAsDict(PdfName.FONTDESCRIPTOR);
                if (fontDescriptor != null)
                {
                    return fontDescriptor.Contains(PdfName.FONTFILE) || 
                           fontDescriptor.Contains(PdfName.FONTFILE2) || 
                           fontDescriptor.Contains(PdfName.FONTFILE3);
                }
            }
            catch { }
            return false;
        }
        
        private string? ExtractFontStyle(PdfDictionary fontDict)
        {
            try
            {
                var fontName = fontDict.GetAsName(PdfName.BASEFONT)?.ToString() ?? "";
                return GetStyleFromName(fontName);
            }
            catch
            {
                return null;
            }
        }

        private string GetStyleFromName(string fontName)
        {
            if (fontName.Contains("Bold") && fontName.Contains("Italic"))
                return "BoldItalic";
            else if (fontName.Contains("Bold"))
                return "Bold";
            else if (fontName.Contains("Italic"))
                return "Italic";
            else
                return "Regular";
        }
        
        private List<float> ExtractFontSizesFromContent(int pageNum, string fontName)
        {
            var sizes = new List<float>();
            
            try
            {
                // Implementar análise do stream de conteúdo para extrair tamanhos
                var pageDict = reader.GetPageN(pageNum);
                var contentBytes = reader.GetPageContent(pageNum);
                
                // Verificar recursos da página para fontes inline
                var resources = pageDict.GetAsDict(PdfName.RESOURCES);
                if (resources != null)
                {
                    var fonts = resources.GetAsDict(PdfName.FONT);
                    if (fonts != null)
                    {
                        // Analisar fontes definidas nos recursos da página
                        foreach (var fontKey in fonts.Keys)
                        {
                            var fontDict = fonts.GetAsDict(fontKey);
                            if (fontDict != null)
                            {
                                // Extrair tamanho padrão se disponível
                                var descriptor = fontDict.GetAsDict(PdfName.FONTDESCRIPTOR);
                                if (descriptor != null)
                                {
                                    var fontBBox = descriptor.GetAsArray(PdfName.FONTBBOX);
                                    if (fontBBox != null && fontBBox.Size >= 4)
                                    {
                                        // Estimar tamanho baseado na BBox
                                        var height = fontBBox.GetAsNumber(3).FloatValue - fontBBox.GetAsNumber(1).FloatValue;
                                        if (height > 0 && !sizes.Contains(height / 1000f * 12f))
                                        {
                                            sizes.Add(height / 1000f * 12f); // Normalizar para pontos
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                if (contentBytes != null)
                {
                    string content = System.Text.Encoding.ASCII.GetString(contentBytes);
                    
                    // Procurar por comandos Tf (set font and size)
                    var tfMatches = System.Text.RegularExpressions.Regex.Matches(content, @"(\d+\.?\d*)\s+Tf");
                    foreach (System.Text.RegularExpressions.Match match in tfMatches)
                    {
                        if (float.TryParse(match.Groups[1].Value, out float size))
                        {
                            if (!sizes.Contains(size))
                            {
                                sizes.Add(size);
                            }
                        }
                    }
                }
                
                if (sizes.Count == 0)
                {
                    sizes.Add(12.0f); // Default size
                }
            }
            catch
            {
                sizes.Add(12.0f);
            }
            
            return sizes;
        }
        
        private PageResources AnalyzePageResources(int pageNum)
        {
            if (i7doc != null)
            {
                try { return AnalyzePageResources7(pageNum); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] iText7 resources failed on page {pageNum}: {ex.Message}, falling back to legacy");
                }
            }
            return AnalyzePageResourcesLegacy(pageNum);
        }

        // Novo caminho com iText7
        private PageResources AnalyzePageResources7(int pageNum)
        {
            var result = new PageResources();
            var page = i7doc!.GetPage(pageNum);
            var resources = page.GetResources();
            if (resources == null) return result;

            // Imagens (XObject)
            var xobjects = resources.GetResource(PdfName7.XObject) as PdfDictionary7;
            if (xobjects != null)
            {
                foreach (var key in xobjects.KeySet())
                {
                    var obj = xobjects.Get(key);
                    if (obj == null) continue;
                    if (obj.IsIndirectReference())
                        obj = ((PdfIndirectReference7)obj).GetRefersTo();
                    if (obj is PdfStream7 stream)
                    {
                        var subtype = stream.GetAsName(PdfName7.Subtype);
                        if (PdfName7.Image.Equals(subtype))
                        {
                            result.Images.Add(ExtractImageInfo(stream, key.ToString()));
                        }
                    }
                }
            }

            // Fontes
            var fonts = resources.GetResource(PdfName7.Font) as PdfDictionary7;
            if (fonts != null)
            {
                result.FontCount = fonts.KeySet().Count;
            }

            // Forms (AcroForm fields on page) — reaproveita lógica existente
            result.FormFields = ExtractPageFormFields(pageNum);

            return result;
        }

        // Caminho legado (iTextSharp)
        private PageResources AnalyzePageResourcesLegacy(int pageNum)
        {
            var dict = reader.GetPageN(pageNum);
            var resources = dict.GetAsDict(PdfName.RESOURCES);
            var result = new PageResources();
            
            if (resources != null)
            {
                // Imagens
                var xobject = resources.GetAsDict(PdfName.XOBJECT);
                if (xobject != null)
                {
                    foreach (var key in xobject.Keys)
                    {
                        var obj = xobject.GetAsIndirectObject(key);
                        if (obj != null)
                        {
                            var stream = reader.GetPdfObject(obj.Number) as PRStream;
                            if (stream != null)
                            {
                                var subtype = stream.GetAsName(PdfName.SUBTYPE);
                                if (PdfName.IMAGE.Equals(subtype))
                                {
                                    // USAR DetailedImageExtractor para extrair informações completas
                                    var detailedImages = DetailedImageExtractor.ExtractCompleteImageDetails(reader, pageNum);
                                    result.Images.AddRange(detailedImages);
                                    return result; // Evitar duplicatas
                                }
                            }
                        }
                    }
                }
                
                // Fontes
                var fonts = resources.GetAsDict(PdfName.FONT);
                if (fonts != null)
                {
                    foreach (var key in fonts.Keys)
                    {
                        result.FontCount++;
                    }
                }
            }
            
            // Campos de formulário na página
            result.FormFields = ExtractPageFormFields(pageNum);
            
            return result;
        }
        
        private List<Annotation> ExtractAnnotations(int pageNum)
        {
            var annotations = new List<Annotation>();
            // iText7 path
            if (i7doc != null)
            {
                try
                {
                    var page = i7doc.GetPage(pageNum);
                    foreach (var annot in page.GetAnnotations())
                    {
                        var rectArray = annot.GetRectangle();
                        Rectangle rect;
                        if (rectArray != null && rectArray.Size() >= 4)
                        {
                            rect = new Rectangle(
                                rectArray.GetAsNumber(0)?.FloatValue() ?? 0,
                                rectArray.GetAsNumber(1)?.FloatValue() ?? 0,
                                rectArray.GetAsNumber(2)?.FloatValue() ?? 0,
                                rectArray.GetAsNumber(3)?.FloatValue() ?? 0);
                        }
                        else
                        {
                            rect = new Rectangle(0, 0, 0, 0);
                        }
                        var obj = annot.GetPdfObject();
                        annotations.Add(new Annotation
                        {
                            Type = annot.GetSubtype()?.ToString() ?? string.Empty,
                            Contents = annot.GetContents()?.ToString() ?? string.Empty,
                            Author = annot.GetTitle()?.ToString() ?? string.Empty,
                            Subject = obj?.GetAsString(PdfName7.Subj)?.ToString() ?? string.Empty,
                            ModificationDate = ParsePDFDate(obj?.GetAsString(PdfName7.M)?.ToString() ?? string.Empty),
                            X = rect.GetX(),
                            Y = rect.GetY()
                        });
                    }
                    return annotations;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] iText7 annotations failed on page {pageNum}: {ex.Message}, fallback legacy");
                }
            }

            // Legacy iTextSharp fallback
            var pageDict = reader.GetPageN(pageNum);
            var annots = pageDict.GetAsArray(PdfName5.ANNOTS);
            
            if (annots != null)
            {
                for (int i = 0; i < annots.Size; i++)
                {
                    var annotDict = annots.GetAsDict(i);
                    if (annotDict != null)
                    {
                        annotations.Add(new Annotation
                        {
                            Type = annotDict.GetAsName(PdfName5.SUBTYPE)?.ToString() ?? string.Empty,
                            Contents = annotDict.GetAsString(PdfName5.CONTENTS)?.ToString() ?? string.Empty,
                            Author = annotDict.GetAsString(PdfName5.T)?.ToString() ?? string.Empty,
                            Subject = annotDict.GetAsString(new PdfName5("Subj"))?.ToString() ?? string.Empty,
                            ModificationDate = ParsePDFDate(annotDict.GetAsString(PdfName5.M)?.ToString() ?? string.Empty)
                        });
                    }
                }
            }
            
            return annotations;
        }
        
        private void DetectHeadersFooters(PageAnalysis page)
        {
            // Obter altura da página (iText7 ou legado)
            float pageHeight;
            if (i7doc != null)
            {
                pageHeight = i7doc.GetPage(page.PageNumber).GetPageSize().GetHeight();
            }
            else
            {
                pageHeight = reader.GetPageSize(page.PageNumber).Height;
            }
            
            // Prefer iText7 listener; fallback to legacy if something goes wrong
            if (i7doc != null)
            {
                try
                {
                    var headerStrategy7 = new FilterPDF.Strategies.AdvancedHeaderFooterStrategy7(true, pageHeight);
                    var procHeader = new PdfCanvasProcessor(headerStrategy7);
                    procHeader.ProcessPageContent(i7doc.GetPage(page.PageNumber));
                    page.Headers = ParseHeaderFooterText(headerStrategy7.GetResultantText());

                    var footerStrategy7 = new FilterPDF.Strategies.AdvancedHeaderFooterStrategy7(false, pageHeight);
                    var procFooter = new PdfCanvasProcessor(footerStrategy7);
                    procFooter.ProcessPageContent(i7doc.GetPage(page.PageNumber));
                    page.Footers = ParseHeaderFooterText(footerStrategy7.GetResultantText());
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] iText7 header/footer failed on page {page.PageNumber}: {ex.Message}, fallback legacy");
                }
            }

        }
        
        private List<string> ParseHeaderFooterText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();
                
            return text.Split('\n')
                      .Where(line => !string.IsNullOrWhiteSpace(line))
                      .Select(line => line.Trim())
                      .ToList();
        }
        
        private void DetectDocumentReferences(PageAnalysis page)
        {
            var references = new List<string>();
            // Prefer texto já extraído via iText7 (mais fiel e já disponível)
            string text = page.TextInfo.PageText ?? string.Empty;

            // Padrões do código antigo - EXATAMENTE como solicitado
            var patterns = new[] {
                @"SEI\s+\d{6}-\d{2}\.\d{4}\.\d\.\d{2}(?:\s*/\s*pg\.\s*\d+)?",  // SEI 011622-24.2025.8.15 / pg. 1
                @"Processo\s+n[º°]\s*[\d.-]+",                                    // Processo nº 011622-24.2025.8.15
                @"Ofício\s+\d+\s+(?:\(\d+\))?",                                   // Ofício 473 Ofício (0201043)
                @"Anexo\s+\(\d+\)"                                                  // Anexo (0201081)
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(text, pattern);
                foreach (Match match in matches)
                    references.Add(match.Value);
            }

            page.DocumentReferences = references.Distinct().ToList();
        }
        
        private SecurityInfo ExtractSecurityInfo()
        {
            var security = new SecurityInfo
            {
                IsEncrypted = reader.IsEncrypted(),
                PermissionFlags = (int)reader.Permissions,
                EncryptionType = reader.GetCryptoMode()
            };
            
            if (security.IsEncrypted)
            {
                security.CanPrint = reader.IsOpenedWithFullPermissions;
                security.CanModify = (reader.Permissions & PdfWriter.ALLOW_MODIFY_CONTENTS) != 0;
                security.CanCopy = (reader.Permissions & PdfWriter.ALLOW_COPY) != 0;
                security.CanAnnotate = (reader.Permissions & PdfWriter.ALLOW_MODIFY_ANNOTATIONS) != 0;
            }
            
            return security;
        }
        
        private ResourcesSummary ExtractResources()
        {
            var resources = new ResourcesSummary();
            
            // Contar tipos de objetos
            for (int i = 0; i < reader.XrefSize; i++)
            {
                var obj = reader.GetPdfObject(i);
                if (obj != null && !obj.IsNull())
                {
                    if (obj.IsStream())
                    {
                        var stream = (PRStream)obj;
                        var subtype = stream.GetAsName(PdfName.SUBTYPE);
                        
                        if (PdfName.IMAGE.Equals(subtype))
                            resources.TotalImages++;
                        else if (PdfName.FORM.Equals(subtype))
                            resources.Forms++;
                    }
                }
            }
            
            // JavaScript detection not available in this version
            resources.HasJavaScript = false;
            resources.JavaScriptCount = 0;
            
            // Anexos
            var catalog = reader.Catalog;
            var names = catalog.GetAsDict(PdfName.NAMES);
            if (names != null)
            {
                var embeddedFiles = names.GetAsDict(PdfName.EMBEDDEDFILES);
                resources.HasAttachments = embeddedFiles != null;
            }
            
            return resources;
        }
        
        private Statistics CalculateStatistics(PDFAnalysisResult result)
        {
            var stats = new Statistics();
            
            // Estatísticas de texto
            stats.TotalCharacters = result.Pages.Sum(p => p.TextInfo.CharacterCount);
            stats.TotalWords = result.Pages.Sum(p => p.TextInfo.WordCount);
            stats.TotalLines = result.Pages.Sum(p => p.TextInfo.LineCount);
            stats.AverageWordsPerPage = result.Pages.Count > 0 ? stats.TotalWords / result.Pages.Count : 0;
            
            // Estatísticas de recursos
            stats.TotalImages = result.Pages.Sum(p => p.Resources.Images.Count);
            stats.TotalAnnotations = result.Pages.Sum(p => p.Annotations.Count);
            
            // Fontes únicas
            var allFonts = new HashSet<string>();
            foreach (var page in result.Pages)
            {
                foreach (var font in page.TextInfo.Fonts)
                {
                    allFonts.Add(font.Name);
                }
            }
            stats.UniqueFonts = allFonts.Count;
            
            // Páginas com características especiais
            stats.PagesWithImages = result.Pages.Count(p => p.Resources.Images.Count > 0);
            stats.PagesWithTables = result.Pages.Count(p => p.TextInfo.HasTables);
            stats.PagesWithColumns = result.Pages.Count(p => p.TextInfo.HasColumns);
            
            return stats;
        }
        
        private ImageInfo ExtractImageInfo(PdfStream7 stream, string name)
        {
            var info = new ImageInfo { Name = name };

            var width = stream.GetAsNumber(PdfName7.Width);
            var height = stream.GetAsNumber(PdfName7.Height);
            var bitsPerComponent = stream.GetAsNumber(PdfName7.BitsPerComponent);
            var filter = stream.GetAsName(PdfName7.Filter);

            if (width != null) info.Width = width.IntValue();
            if (height != null) info.Height = height.IntValue();
            if (bitsPerComponent != null) info.BitsPerComponent = bitsPerComponent.IntValue();
            if (filter != null) info.CompressionType = filter.ToString();

            var cs = stream.GetAsName(PdfName7.ColorSpace);
            info.ColorSpace = cs?.ToString() ?? "Unknown";

            return info;
        }

        private ImageInfo ExtractImageInfo(PRStream stream, string name)
        {
            var info = new ImageInfo { Name = name };
            
            var width = stream.GetAsNumber(PdfName.WIDTH);
            var height = stream.GetAsNumber(PdfName.HEIGHT);
            var bitsPerComponent = stream.GetAsNumber(PdfName.BITSPERCOMPONENT);
            var filter = stream.GetAsName(PdfName.FILTER);
            
            if (width != null) info.Width = width.IntValue;
            if (height != null) info.Height = height.IntValue;
            if (bitsPerComponent != null) info.BitsPerComponent = bitsPerComponent.IntValue;
            if (filter != null) info.CompressionType = filter.ToString();
            
            info.ColorSpace = stream.GetAsName(PdfName.COLORSPACE)?.ToString() ?? "Unknown";
            
            return info;
        }
        
        private DateTime? ParsePDFDate(string pdfDate)
        {
            if (string.IsNullOrEmpty(pdfDate)) return null;
            
            try
            {
                // PDF date format: D:YYYYMMDDHHmmSSOHH'mm'
                pdfDate = pdfDate.Replace("D:", "").Replace("'", "");
                if (pdfDate.Length >= 14)
                {
                    int year = int.Parse(pdfDate.Substring(0, 4));
                    int month = int.Parse(pdfDate.Substring(4, 2));
                    int day = int.Parse(pdfDate.Substring(6, 2));
                    int hour = int.Parse(pdfDate.Substring(8, 2));
                    int minute = int.Parse(pdfDate.Substring(10, 2));
                    int second = int.Parse(pdfDate.Substring(12, 2));
                    
                    return new DateTime(year, month, day, hour, minute, second);
                }
            }
            catch { }
            
            return null;
        }
        
        private int CountWords(string text)
        {
            return Regex.Matches(text, @"\b\w+\b").Count;
        }
        
        private List<string> DetectLanguages(string text)
        {
            var languages = new List<string>();
            
            // Detectar por padrões comuns
            if (Regex.IsMatch(text, @"\b(the|and|of|to|in|is|are)\b", RegexOptions.IgnoreCase))
                languages.Add("English");
            
            if (Regex.IsMatch(text, @"\b(de|da|do|que|para|com|em|por)\b", RegexOptions.IgnoreCase))
                languages.Add("Português");
            
            if (Regex.IsMatch(text, @"\b(el|la|de|que|en|es|un|una)\b", RegexOptions.IgnoreCase))
                languages.Add("Español");
            
            return languages.Distinct().ToList();
        }
        
        private double CalculateAverageLineLength(string text)
        {
            var lines = text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            return lines.Length > 0 ? lines.Average(l => l.Length) : 0;
        }
        
        // ========== NOVOS MÉTODOS AVANÇADOS ==========
        
        private XMPMetadata ExtractXMPMetadata()
        {
            // Usar o AdvancedPDFProcessor para extração COMPLETA de XMP
            // var processor = new AdvancedPDFProcessor(reader);
            // return processor.ExtractCompleteXMPMetadata();
            return new XMPMetadata(); // Temporary fallback
        }
        
        private AccessibilityInfo ExtractAccessibilityInfo()
        {
            var accessibility = new AccessibilityInfo();

            try
            {
                var catalog = reader.Catalog;

                // Verificar TODAS as tags de estrutura
                var structTree = catalog.GetAsDict(PdfName.STRUCTTREEROOT);
                if (structTree != null)
                {
                    accessibility.HasStructureTags = true;
                    accessibility.IsTaggedPDF = true;
                    
                    // EXTRAIR roles, elementos, hierarquia completa
                    var roleMap = structTree.GetAsDict(PdfName.ROLEMAP);
                    if (roleMap != null)
                    {
                        foreach (var key in roleMap.Keys)
                        {
                            var mappedRole = roleMap.Get(key);
                            if (mappedRole != null)
                            {
                                // Adicionar mapeamento de roles personalizados
                                accessibility.CustomRoles[key.ToString()] = mappedRole.ToString();
                            }
                        }
                    }
                    
                    // Extrair árvore de estrutura completa
                    var kids = structTree.GetAsArray(PdfName.K);
                    if (kids != null)
                    {
                        accessibility.StructureTree = ParseStructureTree(kids, 0);
                        CountStructureElements(accessibility.StructureTree, accessibility);
                    }
                    
                    // Verificar ParentTree para mapeamento de conteúdo
                    var parentTree = structTree.Get(PdfName.PARENTTREE);
                    if (parentTree != null)
                    {
                        accessibility.HasParentTree = true;
                    }
                    
                    // Verificar IDTree
                    var idTree = structTree.Get(new PdfName("IDTree"));
                    if (idTree != null)
                    {
                        accessibility.HasIDTree = true;
                    }
                }
                else
                {
                    accessibility.HasStructureTags = false;
                    accessibility.IsTaggedPDF = false;
                }
                
                // Detectar linguagem principal e alternativas
                var lang = catalog.GetAsString(PdfName.LANG);
                if (lang != null)
                {
                    accessibility.Language = lang.ToString();
                }
                
                // Verificar ViewerPreferences para acessibilidade
                var viewerPrefs = catalog.GetAsDict(PdfName.VIEWERPREFERENCES);
                if (viewerPrefs != null)
                {
                    var displayDocTitle = viewerPrefs.GetAsBoolean(new PdfName("DisplayDocTitle"));
                    if (displayDocTitle != null && displayDocTitle.BooleanValue)
                    {
                        accessibility.DisplayDocTitle = true;
                    }
                }
                
                // Verificar se tem texto alternativo em imagens
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    var page = reader.GetPageN(i);
                    CheckPageAccessibility(page, accessibility);
                }
            }
            catch { }

            return accessibility;
        }
        
        private void CheckPageAccessibility(PdfDictionary page, AccessibilityInfo accessibility)
        {
            try
            {
                var resources = page.GetAsDict(PdfName.RESOURCES);
                if (resources != null)
                {
                    var xobject = resources.GetAsDict(PdfName.XOBJECT);
                    if (xobject != null)
                    {
                        foreach (var key in xobject.Keys)
                        {
                            var obj = xobject.GetAsIndirectObject(key);
                            if (obj != null)
                            {
                                var stream = reader.GetPdfObject(obj.Number) as PRStream;
                                if (stream != null && PdfName.IMAGE.Equals(stream.GetAsName(PdfName.SUBTYPE)))
                                {
                                    // Verificar se tem texto alternativo
                                    var alt = stream.Get(PdfName.ALT);
                                    if (alt != null)
                                    {
                                        accessibility.HasAlternativeText = true;
                                        accessibility.ImagesWithAltText++;
                                    }
                                    accessibility.TotalImages++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
        
        private List<OptionalContentGroup> ExtractOptionalContentGroups()
        {
            var layers = new List<OptionalContentGroup>();
            
            try
            {
                var catalog = reader.Catalog;
                var ocProperties = catalog.GetAsDict(PdfName.OCPROPERTIES);
                
                if (ocProperties != null)
                {
                    var ocgs = ocProperties.GetAsArray(PdfName.OCGS);
                    if (ocgs != null)
                    {
                        for (int i = 0; i < ocgs.Size; i++)
                        {
                            var ocgDict = ocgs.GetAsDict(i);
                            if (ocgDict != null)
                            {
                                var layer = new OptionalContentGroup
                                {
                                    Name = ocgDict.GetAsString(PdfName.NAME)?.ToString() ?? "Unnamed Layer",
                                    Intent = ocgDict.GetAsName(PdfName.INTENT)?.ToString() ?? "View",
                                    IsVisible = true, // Padrão
                                    CanToggle = true
                                };
                                
                                var usage = ocgDict.GetAsDict(PdfName.USAGE);
                                if (usage != null)
                                {
                                    foreach (var key in usage.Keys)
                                    {
                                        layer.Usage = key.ToString();
                                    }
                                }
                                
                                layers.Add(layer);
                            }
                        }
                    }
                }
            }
            catch { }
            
            return layers;
        }
        
        private List<DigitalSignature> ExtractDigitalSignatures()
        {
            var signatures = new List<DigitalSignature>();
            
            try
            {
                var acroForm = reader.AcroForm;
                if (acroForm != null)
                {
                    var fields = acroForm.GetAsArray(PdfName.FIELDS);
                    if (fields != null)
                    {
                        for (int i = 0; i < fields.Size; i++)
                        {
                            var field = fields.GetAsDict(i);
                            if (field != null)
                            {
                                var ft = field.GetAsName(PdfName.FT);
                                if (PdfName.SIG.Equals(ft))
                                {
                                    var signature = new DigitalSignature
                                    {
                                        Name = field.GetAsString(PdfName.T)?.ToString() ?? "Signature",
                                        SignatureType = "Digital Signature"
                                    };
                                    
                                    var v = field.GetAsDict(PdfName.V);
                                    if (v != null)
                                    {
                                        signature.ContactInfo = v.GetAsString(PdfName.CONTACTINFO)?.ToString() ?? string.Empty;
                                        signature.Location = v.GetAsString(PdfName.LOCATION)?.ToString() ?? string.Empty;
                                        signature.Reason = v.GetAsString(PdfName.REASON)?.ToString() ?? string.Empty;
                                        
                                        var m = v.GetAsString(PdfName.M);
                                        if (m != null)
                                        {
                                            signature.SignDate = ParsePDFDate(m.ToString());
                                        }
                                    }
                                    
                                    signatures.Add(signature);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            
            return signatures;
        }
        
        private List<ColorProfile> ExtractColorProfiles()
        {
            var profiles = new List<ColorProfile>();

            try
            {
                // PERCORRER TODAS AS PÁGINAS
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    var page = reader.GetPageN(i);
                    var resources = page.GetAsDict(PdfName.RESOURCES);

                    if (resources != null)
                    {
                        // PROCURAR EM COLORSPACE
                        var colorSpaces = resources.GetAsDict(PdfName.COLORSPACE);
                        if (colorSpaces != null)
                        {
                            foreach (var key in colorSpaces.Keys)
                            {
                                var cs = colorSpaces.Get(key);
                                
                                // EXTRAIR informações ICC Profile
                                if (cs != null && cs.ToString().Contains("ICCBased"))
                                {
                                    var profile = new ColorProfile
                                    {
                                        Name = key.ToString(),
                                        ColorSpace = "ICC"
                                    };
                                    
                                    // Tentar extrair mais detalhes do stream ICC
                                    if (cs is PdfArray csArray && csArray.Size > 1)
                                    {
                                        var iccStream = csArray.GetAsStream(1);
                                        if (iccStream != null)
                                        {
                                            profile.ProfileSize = iccStream.Length;
                                            
                                            // Extrair número de componentes
                                            var n = iccStream.GetAsNumber(PdfName.N);
                                            if (n != null)
                                            {
                                                profile.NumberOfComponents = n.IntValue;
                                                profile.Description = $"{n.IntValue} component ICC profile";
                                            }
                                            
                                            // Tentar extrair metadata do ICC
                                            var alternate = iccStream.GetAsName(PdfName.ALTERNATE);
                                            if (alternate != null)
                                            {
                                                profile.AlternateColorSpace = alternate.ToString();
                                            }
                                        }
                                    }
                                    
                                    profiles.Add(profile);
                                }
                                // Verificar também outros tipos de colorspace
                                else if (cs is PdfName)
                                {
                                    var csName = cs.ToString();
                                    if (csName.Contains("RGB") || csName.Contains("CMYK") || csName.Contains("Gray"))
                                    {
                                        profiles.Add(new ColorProfile
                                        {
                                            Name = key.ToString(),
                                            ColorSpace = csName,
                                            Description = $"Device {csName}"
                                        });
                                    }
                                }
                            }
                        }
                        
                        // Verificar também em XObject para imagens com perfis embutidos
                        var xobjects = resources.GetAsDict(PdfName.XOBJECT);
                        if (xobjects != null)
                        {
                            foreach (var xkey in xobjects.Keys)
                            {
                                var xobj = xobjects.GetAsIndirectObject(xkey);
                                if (xobj != null)
                                {
                                    var stream = reader.GetPdfObject(xobj.Number) as PRStream;
                                    if (stream != null && PdfName.IMAGE.Equals(stream.GetAsName(PdfName.SUBTYPE)))
                                    {
                                        var imgColorSpace = stream.Get(PdfName.COLORSPACE);
                                        if (imgColorSpace != null && imgColorSpace.ToString().Contains("ICCBased"))
                                        {
                                            profiles.Add(new ColorProfile
                                            {
                                                Name = $"Image_{xkey}",
                                                ColorSpace = "ICC (Embedded in image)",
                                                Description = "ICC profile embedded in image"
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return profiles.Distinct().ToList();
        }
        
        private BookmarkStructure ExtractBookmarkStructure()
        {
            var bookmarks = new BookmarkStructure();
            
            try
            {
                var catalog = reader.Catalog;
                var outlines = catalog.GetAsDict(PdfName.OUTLINES);
                
                if (outlines != null)
                {
                    var first = outlines.GetAsDict(PdfName.FIRST);
                    if (first != null)
                    {
                        bookmarks.RootItems = ParseBookmarkItems(first, 0);
                        bookmarks.TotalCount = CountBookmarks(bookmarks.RootItems);
                        bookmarks.MaxDepth = CalculateMaxDepth(bookmarks.RootItems);
                    }
                }
            }
            catch { }
            
            return bookmarks;
        }
        
        // Métodos auxiliares
        private string? ExtractXMPValue(string xmpString, string tagName)
        {
            try
            {
                var pattern = $@"<{tagName}[^>]*>([^<]*)</{tagName}>";
                var match = Regex.Match(xmpString, pattern);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }
        
        private List<string> ExtractXMPArray(string xmpString, string tagName)
        {
            var results = new List<string>();
            try
            {
                var pattern = $@"<{tagName}[^>]*>.*?<rdf:li[^>]*>([^<]*)</rdf:li>.*?</{tagName}>";
                var matches = Regex.Matches(xmpString, pattern, RegexOptions.Singleline);
                foreach (Match match in matches)
                {
                    results.Add(match.Groups[1].Value);
                }
            }
            catch { }
            return results;
        }
        
        private List<StructureElement> ParseStructureTree(PdfArray kids, int level)
        {
            var elements = new List<StructureElement>();
            
            try
            {
                for (int i = 0; i < kids.Size; i++)
                {
                    var kid = kids.GetAsDict(i);
                    if (kid != null)
                    {
                        var element = new StructureElement
                        {
                            Type = kid.GetAsName(PdfName.S)?.ToString() ?? string.Empty,
                            Title = kid.GetAsString(PdfName.T)?.ToString() ?? string.Empty,
                            AlternativeText = kid.GetAsString(PdfName.ALT)?.ToString() ?? string.Empty,
                            ActualText = kid.GetAsString(PdfName.ACTUALTEXT)?.ToString() ?? string.Empty,
                            Level = level
                        };
                        
                        var childKids = kid.GetAsArray(PdfName.K);
                        if (childKids != null)
                        {
                            element.Children = ParseStructureTree(childKids, level + 1);
                        }
                        
                        elements.Add(element);
                    }
                }
            }
            catch { }
            
            return elements;
        }
        
        private void CountStructureElements(List<StructureElement> elements, AccessibilityInfo accessibility)
        {
            foreach (var element in elements)
            {
                switch (element.Type?.ToUpper())
                {
                    case "H":
                    case "H1":
                    case "H2":
                    case "H3":
                    case "H4":
                    case "H5":
                    case "H6":
                        accessibility.HeadingLevels++;
                        break;
                    case "L":
                    case "LI":
                        accessibility.ListElements++;
                        break;
                    case "TABLE":
                    case "TR":
                    case "TD":
                    case "TH":
                        accessibility.TableElements++;
                        break;
                    case "FIGURE":
                    case "IMG":
                        accessibility.FigureElements++;
                        break;
                }
                
                if (!string.IsNullOrEmpty(element.AlternativeText))
                {
                    accessibility.HasAlternativeText = true;
                }
                
                CountStructureElements(element.Children, accessibility);
            }
        }
        
        private List<BookmarkItem> ParseBookmarkItems(PdfDictionary first, int level)
        {
            var items = new List<BookmarkItem>();
            var current = first;
            
            try
            {
                while (current != null)
                {
                    var item = new BookmarkItem
                    {
                        Title = current.GetAsString(PdfName.TITLE)?.ToString() ?? string.Empty,
                        Level = level,
                        IsOpen = current.GetAsNumber(PdfName.COUNT)?.IntValue > 0
                    };
                    
                    // Extrair destino
                    var dest = current.GetAsArray(PdfName.DEST);
                    if (dest != null && dest.Size > 0)
                    {
                        item.Destination = new BookmarkDestination();
                        var pageRef = dest.GetAsIndirectObject(0);
                        if (pageRef != null)
                        {
                            item.Destination.PageNumber = GetPageNumberFromRef(pageRef);
                        }
                        
                        if (dest.Size > 1)
                        {
                            item.Destination.Type = dest.GetAsName(1)?.ToString() ?? string.Empty;
                        }
                    }
                    
                    // Extrair ação
                    var action = current.GetAsDict(PdfName.A);
                    if (action != null)
                    {
                        item.Action = new BookmarkAction
                        {
                            Type = action.GetAsName(PdfName.S)?.ToString() ?? string.Empty,
                            URI = action.GetAsString(PdfName.URI)?.ToString() ?? string.Empty
                        };
                    }
                    
                    // Processar filhos
                    var kidFirst = current.GetAsDict(PdfName.FIRST);
                    if (kidFirst != null)
                    {
                        item.Children = ParseBookmarkItems(kidFirst, level + 1);
                    }
                    
                    items.Add(item);
                    current = current.GetAsDict(PdfName.NEXT);
                }
            }
            catch { }
            
            return items;
        }
        
        private int CountBookmarks(List<BookmarkItem> items)
        {
            int count = items.Count;
            foreach (var item in items)
            {
                count += CountBookmarks(item.Children);
            }
            return count;
        }
        
        private int CalculateMaxDepth(List<BookmarkItem> items)
        {
            if (items.Count == 0) return 0;
            
            int maxDepth = 1;
            foreach (var item in items)
            {
                int childDepth = CalculateMaxDepth(item.Children);
                maxDepth = Math.Max(maxDepth, 1 + childDepth);
            }
            return maxDepth;
        }
        
        private List<FormField> ExtractPageFormFields(int pageNum)
        {
            var formFields = new List<FormField>();

            // Prefer iText7
            if (i7doc != null)
            {
                try
                {
                    var acro7 = iText.Forms.PdfAcroForm.GetAcroForm(i7doc, false);
                    if (acro7 != null)
                    {
                        foreach (var kv in acro7.GetFormFields())
                        {
                            var field = kv.Value;
                            var widgets = field.GetWidgets();
                            if (widgets == null) continue;

                            foreach (var widget in widgets)
                            {
                                var page = widget.GetPage();
                                // PdfPage doesn't expose GetPageNumber; use PdfDocument helper
                                if (page != null && i7doc.GetPageNumber(page) == pageNum)
                                {
                                    var rect = widget.GetRectangle();
                                    var formField = new FormField
                                    {
                                        Name = kv.Key,
                                        Type = field.GetFormType()?.ToString() ?? "Unknown",
                                        Value = field.GetValueAsString(),
                                        DefaultValue = field.GetDefaultValue() != null ? field.GetDefaultValue().ToString() : string.Empty,
                                        IsReadOnly = field.IsReadOnly(),
                                        IsRequired = field.IsRequired(),
                                        X = rect.GetAsNumber(0)?.FloatValue() ?? 0,
                                        Y = rect.GetAsNumber(1)?.FloatValue() ?? 0,
                                        Width = (rect.GetAsNumber(2)?.FloatValue() ?? 0) - (rect.GetAsNumber(0)?.FloatValue() ?? 0),
                                        Height = (rect.GetAsNumber(3)?.FloatValue() ?? 0) - (rect.GetAsNumber(1)?.FloatValue() ?? 0)
                                    };

                                    var opt = field.GetPdfObject().GetAsArray(PdfName7.Opt);
                                    if (opt != null)
                                    {
                                        for (int j = 0; j < opt.Size(); j++)
                                        {
                                            var option = opt.GetAsString(j);
                                            if (option != null) formField.Options.Add(option.ToString());
                                        }
                                    }

                                    formFields.Add(formField);
                                }
                            }
                        }
                        return formFields;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] iText7 form fields failed on page {pageNum}: {ex.Message}, fallback legacy");
                }
            }

            // Legacy iTextSharp fallback
            try
            {
                var acroForm = reader.AcroForm;
                if (acroForm != null)
                {
                    var fields = acroForm.GetAsArray(PdfName.FIELDS);
                    if (fields != null)
                    {
                        for (int i = 0; i < fields.Size; i++)
                        {
                            var field = fields.GetAsDict(i);
                            if (field != null && IsFieldOnPage(field, pageNum))
                            {
                                var formField = new FormField
                                {
                                    Name = field.GetAsString(PdfName.T)?.ToString() ?? string.Empty,
                                    Type = GetFieldType(field.GetAsName(PdfName.FT)),
                                    Value = field.GetAsString(PdfName.V)?.ToString() ?? string.Empty,
                                    DefaultValue = field.GetAsString(PdfName.DV)?.ToString() ?? string.Empty,
                                    IsRequired = (field.GetAsNumber(PdfName.FF)?.IntValue & 2) != 0,
                                    IsReadOnly = (field.GetAsNumber(PdfName.FF)?.IntValue & 1) != 0
                                };
                                
                                var opt = field.GetAsArray(PdfName.OPT);
                                if (opt != null)
                                {
                                    for (int j = 0; j < opt.Size; j++)
                                    {
                                        var option = opt.GetAsString(j);
                                        if (option != null) formField.Options.Add(option.ToString());
                                    }
                                }
                                
                                var rect = field.GetAsArray(PdfName.RECT);
                                if (rect != null && rect.Size >= 4)
                                {
                                    formField.X = rect.GetAsNumber(0).FloatValue;
                                    formField.Y = rect.GetAsNumber(1).FloatValue;
                                    formField.Width = rect.GetAsNumber(2).FloatValue - formField.X;
                                    formField.Height = rect.GetAsNumber(3).FloatValue - formField.Y;
                                }
                                
                                formFields.Add(formField);
                            }
                        }
                    }
                }
            }
            catch { }
            
            return formFields;
        }
        
        private bool IsFieldOnPage(PdfDictionary field, int pageNum)
        {
            try
            {
                var page = field.GetAsIndirectObject(PdfName.P);
                if (page != null)
                {
                    return GetPageNumberFromRef(page) == pageNum;
                }
                
                // Se não tem referência direta à página, verificar anotações
                var kids = field.GetAsArray(PdfName.KIDS);
                if (kids != null)
                {
                    for (int i = 0; i < kids.Size; i++)
                    {
                        var kid = kids.GetAsDict(i);
                        if (kid != null && IsFieldOnPage(kid, pageNum))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            
            return false;
        }
        
        private string GetFieldType(PdfName fieldType)
        {
            if (fieldType == null) return "Unknown";
            
            if (PdfName.TX.Equals(fieldType)) return "Text";
            if (PdfName.CH.Equals(fieldType)) return "Choice";
            if (PdfName.BTN.Equals(fieldType)) return "Button";
            if (PdfName.SIG.Equals(fieldType)) return "Signature";
            
            return fieldType.ToString();
        }
        
        private int GetPageNumberFromRef(PdfIndirectReference pageRef)
        {
            try
            {
                // Percorrer todas as páginas para encontrar a referência
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    var pageDict = reader.GetPageN(i);
                    if (pageDict != null && pageDict.IndRef != null && pageDict.IndRef.Equals(pageRef))
                    {
                        return i;
                    }
                }
            }
            catch { }
            return 1; // Fallback para página 1
        }
    }
    
    // REMOVIDO: TextAnalysisStrategy que reconstruía texto
    // Agora usamos extração direta com PdfTextExtractor.GetTextFromPage
    // para preservar o texto original sem modificações
    
    // REMOVIDO: MainContentExtractionStrategy porque reconstruía texto
    // REGRA: NUNCA alterar o texto original do PDF
}
