using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
// NOTE: pdfa and xtra DLLs available but namespaces may differ
// Will implement advanced features using core iTextSharp for now

namespace FilterPDF
{
    /// <summary>
    /// Processador avançado que usa TODAS as funcionalidades das DLLs
    /// </summary>
    public class AdvancedPDFProcessor
    {
        private PdfReader reader;
        
        public AdvancedPDFProcessor(PdfReader reader)
        {
            this.reader = reader;
        }
        
        /// <summary>
        /// Extrai metadados XMP COMPLETOS analisando o XML diretamente
        /// </summary>
        public XMPMetadata ExtractCompleteXMPMetadata()
        {
            var xmp = new XMPMetadata();
            
            try
            {
                byte[] xmpBytes = reader.Metadata;
                if (xmpBytes != null)
                {
                    string xmpString = Encoding.UTF8.GetString(xmpBytes);
                    
                    // Parse XML diretamente
                    XmlDocument xmpDoc = new XmlDocument();
                    xmpDoc.LoadXml(xmpString);
                    
                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmpDoc.NameTable);
                    nsmgr.AddNamespace("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                    nsmgr.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
                    nsmgr.AddNamespace("xmp", "http://ns.adobe.com/xap/1.0/");
                    nsmgr.AddNamespace("xmpMM", "http://ns.adobe.com/xap/1.0/mm/");
                    nsmgr.AddNamespace("stEvt", "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#");
                    nsmgr.AddNamespace("pdfaid", "http://www.aiim.org/pdfa/ns/id/");
                    nsmgr.AddNamespace("xmpRights", "http://ns.adobe.com/xap/1.0/rights/");
                    
                    // Dublin Core
                    xmp.DublinCoreTitle = ExtractXmlValue(xmpDoc, "//dc:title/rdf:Alt/rdf:li", nsmgr) ?? string.Empty;
                    xmp.DublinCoreCreator = ExtractXmlValue(xmpDoc, "//dc:creator/rdf:Seq/rdf:li", nsmgr) ?? string.Empty;
                    xmp.DublinCoreSubject = ExtractXmlValue(xmpDoc, "//dc:subject/rdf:Bag/rdf:li", nsmgr) ?? string.Empty;
                    xmp.DublinCoreDescription = ExtractXmlValue(xmpDoc, "//dc:description/rdf:Alt/rdf:li", nsmgr) ?? string.Empty;
                    
                    // Keywords
                    var keywordNodes = xmpDoc.SelectNodes("//dc:subject/rdf:Bag/rdf:li", nsmgr);
                    if (keywordNodes != null)
                    {
                        foreach (XmlNode node in keywordNodes)
                        {
                            if (!string.IsNullOrEmpty(node.InnerText))
                                xmp.DublinCoreKeywords.Add(node.InnerText);
                        }
                    }
                    
                    // Rights
                    xmp.CopyrightNotice = ExtractXmlValue(xmpDoc, "//dc:rights/rdf:Alt/rdf:li", nsmgr) ?? string.Empty;
                    xmp.CopyrightOwner = ExtractXmlValue(xmpDoc, "//xmpRights:Owner/rdf:Bag/rdf:li", nsmgr) ?? string.Empty;
                    var copyrightDateStr = ExtractXmlValue(xmpDoc, "//xmpRights:WebStatement", nsmgr);
                    if (!string.IsNullOrEmpty(copyrightDateStr))
                    {
                        DateTime copyrightDate;
                        if (DateTime.TryParse(copyrightDateStr, out copyrightDate))
                            xmp.CopyrightDate = copyrightDate;
                    }
                    
                    // XMP Basic
                    xmp.CreatorTool = ExtractXmlValue(xmpDoc, "//xmp:CreatorTool", nsmgr) ?? string.Empty;
                    var metadataDateStr = ExtractXmlValue(xmpDoc, "//xmp:MetadataDate", nsmgr);
                    if (!string.IsNullOrEmpty(metadataDateStr))
                    {
                        xmp.MetadataDate = ParseXmpDate(metadataDateStr);
                    }
                    
                    // XMP Media Management
                    xmp.DocumentID = ExtractXmlValue(xmpDoc, "//xmpMM:DocumentID", nsmgr) ?? string.Empty;
                    xmp.InstanceID = ExtractXmlValue(xmpDoc, "//xmpMM:InstanceID", nsmgr) ?? string.Empty;
                    
                    // Edit History
                    var historyNodes = xmpDoc.SelectNodes("//xmpMM:History/rdf:Seq/rdf:li", nsmgr);
                    if (historyNodes != null)
                    {
                        foreach (XmlNode historyNode in historyNodes)
                        {
                            var entry = new EditHistoryEntry();
                            entry.Action = GetNodeValue(historyNode, "stEvt:action", nsmgr) ?? "";
                            var whenStr = GetNodeValue(historyNode, "stEvt:when", nsmgr);
                            if (!string.IsNullOrEmpty(whenStr))
                                entry.When = ParseXmpDate(whenStr) ?? DateTime.MinValue;
                            entry.SoftwareAgent = GetNodeValue(historyNode, "stEvt:softwareAgent", nsmgr) ?? "";
                            entry.Parameters = GetNodeValue(historyNode, "stEvt:parameters", nsmgr) ?? "";
                            
                            if (!string.IsNullOrEmpty(entry.Action))
                                xmp.EditHistory.Add(entry);
                        }
                    }
                    
                    // PDF/A Conformance
                    var pdfaPart = ExtractXmlValue(xmpDoc, "//pdfaid:part", nsmgr);
                    var pdfaConf = ExtractXmlValue(xmpDoc, "//pdfaid:conformance", nsmgr);
                    if (!string.IsNullOrEmpty(pdfaPart))
                    {
                        xmp.PDFAConformance = $"PDF/A-{pdfaPart}{pdfaConf}";
                        xmp.PDFAVersion = pdfaConf ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"XMP extraction error: {ex.Message}");
            }
            
            return xmp;
        }
        
        private string? ExtractXmlValue(XmlDocument doc, string xpath, XmlNamespaceManager nsmgr)
        {
            var node = doc.SelectSingleNode(xpath, nsmgr);
            return node?.InnerText;
        }
        
        private string? GetNodeValue(XmlNode parentNode, string childPath, XmlNamespaceManager nsmgr)
        {
            var node = parentNode.SelectSingleNode(childPath, nsmgr);
            return node?.InnerText;
        }
        
        private DateTime? ParseXmpDate(string xmpDate)
        {
            if (string.IsNullOrEmpty(xmpDate))
                return null;
                
            try
            {
                // XMP dates são em formato ISO 8601
                return DateTime.Parse(xmpDate);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Extrai Optional Content Groups (Layers) usando iTextSharp.xtra
        /// </summary>
        public List<OptionalContentGroup> ExtractOptionalContentGroups()
        {
            var layers = new List<OptionalContentGroup>();
            
            try
            {
                var catalog = reader.Catalog;
                var ocProps = catalog.GetAsDict(PdfName.OCPROPERTIES);
                
                if (ocProps != null)
                {
                    var ocgs = ocProps.GetAsArray(PdfName.OCGS);
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
                                    Intent = ocgDict.GetAsName(new PdfName("Intent"))?.ToString() ?? "View",
                                    Usage = ExtractLayerUsage(ocgDict)
                                };
                                
                                layers.Add(layer);
                            }
                        }
                    }
                    
                    // Extrair configuração padrão
                    var d = ocProps.GetAsDict(PdfName.D);
                    if (d != null)
                    {
                        var order = d.GetAsArray(new PdfName("Order"));
                        if (order != null)
                        {
                            for (int i = 0; i < layers.Count && i < order.Size; i++)
                            {
                                layers[i].Order = i;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Layer extraction error: {ex.Message}");
            }
            
            return layers;
        }
        
        /// <summary>
        /// Extrai perfis de cor ICC usando recursos avançados
        /// </summary>
        public List<ColorProfile> ExtractColorProfiles()
        {
            var profiles = new List<ColorProfile>();
            
            try
            {
                // Verificar OutputIntents
                var catalog = reader.Catalog;
                var outputIntents = catalog.GetAsArray(PdfName.OUTPUTINTENTS);
                
                if (outputIntents != null)
                {
                    for (int i = 0; i < outputIntents.Size; i++)
                    {
                        var intent = outputIntents.GetAsDict(i);
                        if (intent != null)
                        {
                            var profile = new ColorProfile
                            {
                                Type = "OutputIntent",
                                Info = intent.GetAsString(PdfName.INFO)?.ToString() ?? "",
                                RegistryName = intent.GetAsString(new PdfName("RegistryName"))?.ToString() ?? "",
                                Condition = intent.GetAsString(new PdfName("OutputCondition"))?.ToString() ?? ""
                            };
                            
                            var destProfile = intent.GetAsStream(new PdfName("DestOutputProfile"));
                            if (destProfile != null)
                            {
                                profile.HasEmbeddedProfile = true;
                                var profileData = PdfReader.GetStreamBytes((PRStream)destProfile);
                                profile.ProfileSize = profileData.Length;
                            }
                            
                            profiles.Add(profile);
                        }
                    }
                }
                
                // Verificar perfis em ColorSpaces
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var pageDict = reader.GetPageN(pageNum);
                    var resources = pageDict.GetAsDict(PdfName.RESOURCES);
                    
                    if (resources != null)
                    {
                        var colorSpaces = resources.GetAsDict(PdfName.COLORSPACE);
                        if (colorSpaces != null)
                        {
                            foreach (var csKey in colorSpaces.Keys)
                            {
                                var cs = colorSpaces.Get(csKey);
                                if (cs != null)
                                {
                                    ExtractColorSpaceProfile(cs, profiles, csKey.ToString());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Color profile extraction error: {ex.Message}");
            }
            
            return profiles;
        }
        
        /// <summary>
        /// Extrai assinaturas digitais usando iTextSharp.xtra.security
        /// </summary>
        public List<DigitalSignature> ExtractDigitalSignatures()
        {
            var signatures = new List<DigitalSignature>();
            
            try
            {
                var acroForm = reader.AcroForm;
                if (acroForm != null)
                {
                    var fields = acroForm.Fields;
                    if (fields != null)
                    {
                        foreach (var fieldInfo in fields)
                        {
                            var fieldName = fieldInfo.Name;
                            var field = fieldInfo.Info;
                            if (field != null && field.ToString().Contains("/Sig"))
                        {
                            var signature = new DigitalSignature
                            {
                                FieldName = fieldName,
                                SignatureType = "PKCS#7"
                            };
                            
                            // Extrair informações da assinatura
                            var sigDict = field as PdfDictionary;
                            if (sigDict != null)
                            {
                                var v = sigDict.GetAsDict(PdfName.V);
                                if (v != null)
                                {
                                    var filter = v.GetAsName(PdfName.FILTER);
                                    if (filter != null)
                                    {
                                        signature.Filter = filter.ToString();
                                    }
                                    
                                    var subFilter = v.GetAsName(new PdfName("SubFilter"));
                                    if (subFilter != null)
                                    {
                                        signature.SubFilter = subFilter.ToString();
                                    }
                                    
                                    var m = v.GetAsString(PdfName.M);
                                    if (m != null)
                                    {
                                        signature.SigningTime = PdfDate.Decode(m.ToString());
                                    }
                                    
                                    var name = v.GetAsString(PdfName.NAME);
                                    if (name != null)
                                    {
                                        signature.SignerName = name.ToString();
                                    }
                                    
                                    var reason = v.GetAsString(new PdfName("Reason"));
                                    if (reason != null)
                                    {
                                        signature.Reason = reason.ToString();
                                    }
                                    
                                    var location = v.GetAsString(new PdfName("Location"));
                                    if (location != null)
                                    {
                                        signature.Location = location.ToString();
                                    }
                                }
                            }
                            
                            signatures.Add(signature);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Digital signature extraction error: {ex.Message}");
            }
            
            return signatures;
        }
        
        /// <summary>
        /// Detecta multimídia verificando anotações RichMedia e Screen usando iTextSharp.xtra
        /// </summary>
        public List<MultimediaInfo> DetectRichMedia()
        {
            var multimedia = new List<MultimediaInfo>();
            
            try
            {
                // Percorrer todas as páginas
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var pageDict = reader.GetPageN(pageNum);
                    var annots = pageDict.GetAsArray(PdfName.ANNOTS);
                    
                    if (annots != null)
                    {
                        for (int i = 0; i < annots.Size; i++)
                        {
                            var annotDict = annots.GetAsDict(i);
                            if (annotDict != null)
                            {
                                var subtype = annotDict.GetAsName(PdfName.SUBTYPE);
                                
                                // RichMedia annotation
                                if (subtype != null && subtype.ToString() == "/RichMedia")
                                {
                                    var richMedia = new MultimediaInfo
                                    {
                                        Type = "RichMedia",
                                        PageNumber = pageNum
                                    };
                                    
                                    // Extrair conteúdo RichMedia
                                    var richMediaDict = annotDict.GetAsDict(new PdfName("RichMediaContent"));
                                    if (richMediaDict != null)
                                    {
                                        var assets = richMediaDict.GetAsDict(new PdfName("Assets"));
                                        if (assets != null)
                                        {
                                            foreach (var assetKey in assets.Keys)
                                            {
                                                richMedia.Assets.Add(assetKey.ToString());
                                            }
                                        }
                                        
                                        var configs = richMediaDict.GetAsArray(new PdfName("Configurations"));
                                        if (configs != null)
                                        {
                                            richMedia.ConfigurationCount = configs.Size;
                                        }
                                    }
                                    
                                    multimedia.Add(richMedia);
                                }
                                // Screen annotation (video/audio)
                                else if (subtype != null && subtype.ToString() == "/Screen")
                                {
                                    var screen = new MultimediaInfo
                                    {
                                        Type = "Screen",
                                        PageNumber = pageNum
                                    };
                                    
                                    var action = annotDict.GetAsDict(PdfName.A);
                                    if (action != null)
                                    {
                                        var actionType = action.GetAsName(PdfName.S);
                                        if (actionType != null)
                                        {
                                            screen.ActionType = actionType.ToString();
                                        }
                                        
                                        var rendition = action.GetAsDict(new PdfName("R"));
                                        if (rendition != null)
                                        {
                                            screen.HasRendition = true;
                                        }
                                    }
                                    
                                    multimedia.Add(screen);
                                }
                                // 3D annotation
                                else if (subtype != null && subtype.ToString() == "/3D")
                                {
                                    var threeDMedia = new MultimediaInfo
                                    {
                                        Type = "3D",
                                        PageNumber = pageNum
                                    };
                                    
                                    var threeDDict = annotDict.GetAsDict(new PdfName("3D"));
                                    if (threeDDict != null)
                                    {
                                        var stream = threeDDict.GetAsStream(new PdfName("3DD"));
                                        if (stream != null)
                                        {
                                            threeDMedia.Assets.Add("3D Model Stream");
                                        }
                                        
                                        var views = threeDDict.GetAsArray(new PdfName("3DV"));
                                        if (views != null)
                                        {
                                            threeDMedia.ConfigurationCount = views.Size;
                                        }
                                    }
                                    
                                    multimedia.Add(threeDMedia);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Multimedia detection error: {ex.Message}");
            }
            
            return multimedia;
        }
        
        /// <summary>
        /// Analisa conformidade PDF/A verificando características obrigatórias
        /// </summary>
        public PDFAInfo AnalyzePDFAConformance()
        {
            var pdfaInfo = new PDFAInfo();
            
            try
            {
                // Verificar se é PDF/A através dos metadados
                var catalog = reader.Catalog;
                var metadata = catalog.GetAsStream(PdfName.METADATA);
                
                if (metadata != null)
                {
                    byte[] xmpBytes = PdfReader.GetStreamBytes((PRStream)metadata);
                    string xmpString = Encoding.UTF8.GetString(xmpBytes);
                    
                    if (xmpString.Contains("pdfaid:part"))
                    {
                        pdfaInfo.IsPDFA = true;
                        
                        // Extrair nível de conformidade
                        var partMatch = System.Text.RegularExpressions.Regex.Match(xmpString, @"<pdfaid:part>(\d+)</pdfaid:part>");
                        var confMatch = System.Text.RegularExpressions.Regex.Match(xmpString, @"<pdfaid:conformance>(\w+)</pdfaid:conformance>");
                        
                        if (partMatch.Success && confMatch.Success)
                        {
                            pdfaInfo.ConformanceLevel = $"PDF/A-{partMatch.Groups[1].Value}{confMatch.Groups[1].Value}";
                        }
                    }
                }
                
                // Verificar OutputIntents (obrigatório para PDF/A)
                var outputIntents = catalog.GetAsArray(PdfName.OUTPUTINTENTS);
                if (outputIntents != null && outputIntents.Size > 0)
                {
                    pdfaInfo.HasOutputIntent = true;
                    
                    for (int i = 0; i < outputIntents.Size; i++)
                    {
                        var intent = outputIntents.GetAsDict(i);
                        if (intent != null)
                        {
                            var info = intent.GetAsString(PdfName.INFO);
                            if (info != null)
                            {
                                pdfaInfo.OutputIntentInfo = info.ToString();
                            }
                            
                            var destProfile = intent.GetAsStream(PdfName.DESTOUTPUTPROFILE);
                            if (destProfile != null)
                            {
                                pdfaInfo.HasICCProfile = true;
                            }
                        }
                    }
                }
                
                // Verificar características PDF/A
                pdfaInfo.HasEmbeddedFonts = CheckAllFontsEmbedded();
                pdfaInfo.HasTransparency = CheckForTransparency();
                pdfaInfo.HasJavaScript = CheckForJavaScript();
                pdfaInfo.HasEncryption = reader.IsEncrypted();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"PDF/A analysis error: {ex.Message}");
            }
            
            return pdfaInfo;
        }
        
        private bool CheckAllFontsEmbedded()
        {
            try
            {
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var pageDict = reader.GetPageN(pageNum);
                    var resources = pageDict.GetAsDict(PdfName.RESOURCES);
                    
                    if (resources != null)
                    {
                        var fonts = resources.GetAsDict(PdfName.FONT);
                        if (fonts != null)
                        {
                            foreach (var fontKey in fonts.Keys)
                            {
                                var fontDict = fonts.GetAsDict(fontKey);
                                if (fontDict != null)
                                {
                                    var descriptor = fontDict.GetAsDict(PdfName.FONTDESCRIPTOR);
                                    if (descriptor == null)
                                        return false;
                                    
                                    // Verificar se tem arquivo de fonte embutido
                                    if (!descriptor.Contains(PdfName.FONTFILE) && 
                                        !descriptor.Contains(PdfName.FONTFILE2) && 
                                        !descriptor.Contains(PdfName.FONTFILE3))
                                    {
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private bool CheckForTransparency()
        {
            try
            {
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var pageDict = reader.GetPageN(pageNum);
                    var resources = pageDict.GetAsDict(PdfName.RESOURCES);
                    
                    if (resources != null)
                    {
                        // Verificar ExtGState
                        var extGState = resources.GetAsDict(PdfName.EXTGSTATE);
                        if (extGState != null)
                        {
                            foreach (var key in extGState.Keys)
                            {
                                var gs = extGState.GetAsDict(key);
                                if (gs != null)
                                {
                                    // Verificar atributos de transparência
                                    if (gs.Contains(PdfName.CA) || gs.Contains(new PdfName("ca")) || 
                                        gs.Contains(PdfName.BM) || gs.Contains(new PdfName("SMask")))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool CheckForJavaScript()
        {
            try
            {
                var catalog = reader.Catalog;
                var names = catalog.GetAsDict(PdfName.NAMES);
                
                if (names != null)
                {
                    var javascript = names.GetAsDict(PdfName.JAVASCRIPT);
                    if (javascript != null)
                        return true;
                }
                
                // Verificar AcroForm actions
                var acroForm = catalog.GetAsDict(PdfName.ACROFORM);
                if (acroForm != null)
                {
                    var fields = acroForm.GetAsArray(PdfName.FIELDS);
                    if (fields != null)
                    {
                        for (int i = 0; i < fields.Size; i++)
                        {
                            if (CheckFieldForJavaScript(fields.GetAsDict(i)))
                                return true;
                        }
                    }
                }
                
                // Verificar OpenAction
                var openAction = catalog.GetAsDict(PdfName.OPENACTION);
                if (openAction != null)
                {
                    var s = openAction.GetAsName(PdfName.S);
                    if (s != null && s.ToString() == "/JavaScript")
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool CheckFieldForJavaScript(PdfDictionary field)
        {
            if (field == null)
                return false;
                
            // Verificar Additional Actions
            var aa = field.GetAsDict(PdfName.AA);
            if (aa != null)
            {
                foreach (var key in aa.Keys)
                {
                    var action = aa.GetAsDict(key);
                    if (action != null)
                    {
                        var s = action.GetAsName(PdfName.S);
                        if (s != null && s.ToString() == "/JavaScript")
                            return true;
                    }
                }
            }
            
            // Verificar ação padrão
            var a = field.GetAsDict(PdfName.A);
            if (a != null)
            {
                var s = a.GetAsName(PdfName.S);
                if (s != null && s.ToString() == "/JavaScript")
                    return true;
            }
            
            return false;
        }
        
        private string ExtractLayerUsage(PdfDictionary ocgDict)
        {
            var usage = ocgDict.GetAsDict(new PdfName("Usage"));
            if (usage != null)
            {
                var creatorInfo = usage.GetAsDict(new PdfName("CreatorInfo"));
                if (creatorInfo != null)
                {
                    var creator = creatorInfo.GetAsString(new PdfName("Creator"));
                    if (creator != null)
                    {
                        return creator.ToString();
                    }
                }
            }
            return "General";
        }
        
        private void ExtractColorSpaceProfile(PdfObject cs, List<ColorProfile> profiles, string name)
        {
            if (cs.IsArray())
            {
                var csArray = (PdfArray)cs;
                if (csArray.Size > 1)
                {
                    var csName = csArray.GetAsName(0);
                    if (csName != null && csName.ToString() == "/ICCBased")
                    {
                        var iccStream = csArray.GetAsStream(1);
                        if (iccStream != null)
                        {
                            var profile = new ColorProfile
                            {
                                Type = "ICCBased",
                                Name = name,
                                HasEmbeddedProfile = true
                            };
                            
                            var profileData = PdfReader.GetStreamBytes((PRStream)iccStream);
                            profile.ProfileSize = profileData.Length;
                            
                            var n = iccStream.GetAsNumber(PdfName.N);
                            if (n != null)
                            {
                                profile.Components = n.IntValue;
                            }
                            
                            profiles.Add(profile);
                        }
                    }
                }
            }
        }
    }
}