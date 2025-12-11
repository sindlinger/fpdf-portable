using System;
using System.Collections.Generic;
using System.Text;
using iTextSharp.text.pdf;

namespace FilterPDF
{
    /// <summary>
    /// Classe utilitária para validações comuns de PDF
    /// Elimina duplicação de código entre processadores
    /// </summary>
    public static class PDFValidationHelper
    {
        /// <summary>
        /// Verifica se todas as fontes estão embutidas no PDF
        /// </summary>
        public static bool CheckAllFontsEmbedded(PdfReader reader)
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
        
        /// <summary>
        /// Verifica se o PDF contém transparência
        /// </summary>
        public static bool CheckForTransparency(PdfReader reader)
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
        
        /// <summary>
        /// Verifica se o PDF contém JavaScript
        /// </summary>
        public static bool CheckForJavaScript(PdfReader reader)
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
                
                // Verificar páginas
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    if (CheckObjectForJavaScript(page))
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Verifica se um campo de formulário contém JavaScript
        /// </summary>
        public static bool CheckFieldForJavaScript(PdfDictionary field)
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
        
        /// <summary>
        /// Verifica se um objeto PDF contém JavaScript
        /// </summary>
        public static bool CheckObjectForJavaScript(PdfDictionary obj)
        {
            if (obj == null)
                return false;
                
            // Verificar ações no objeto
            var a = obj.GetAsDict(PdfName.A);
            if (a != null)
            {
                var s = a.GetAsName(PdfName.S);
                if (s != null && s.ToString() == "/JavaScript")
                    return true;
            }
            
            // Verificar Additional Actions
            var aa = obj.GetAsDict(PdfName.AA);
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
            
            // Verificar anotações
            var annots = obj.GetAsArray(PdfName.ANNOTS);
            if (annots != null)
            {
                for (int i = 0; i < annots.Size; i++)
                {
                    var annotDict = annots.GetAsDict(i);
                    if (annotDict != null && CheckObjectForJavaScript(annotDict))
                        return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Verifica se o PDF contém anotações proibidas para PDF/A
        /// </summary>
        public static bool CheckForProhibitedAnnotations(PdfReader reader)
        {
            try
            {
                var prohibitedTypes = new HashSet<string>
                {
                    "/Sound", "/Movie", "/Screen", "/3D", "/RichMedia", "/FileAttachment"
                };
                
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    var annots = page.GetAsArray(PdfName.ANNOTS);
                    
                    if (annots != null)
                    {
                        for (int i = 0; i < annots.Size; i++)
                        {
                            var annotDict = annots.GetAsDict(i);
                            if (annotDict != null)
                            {
                                var subtype = annotDict.GetAsName(PdfName.SUBTYPE);
                                if (subtype != null && prohibitedTypes.Contains(subtype.ToString()))
                                {
                                    return true;
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
        
        /// <summary>
        /// Verifica se o PDF contém ações proibidas para PDF/A
        /// </summary>
        public static bool CheckForProhibitedActions(PdfReader reader)
        {
            try
            {
                var prohibitedTypes = new HashSet<string>
                {
                    "/JavaScript", "/Launch", "/Sound", "/Movie", "/ResetForm",
                    "/ImportData", "/Hide", "/SetOCGState", "/Rendition", "/Trans"
                };
                
                var catalog = reader.Catalog;
                if (CheckObjectForProhibitedActions(catalog, prohibitedTypes))
                    return true;
                
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    if (CheckObjectForProhibitedActions(page, prohibitedTypes))
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private static bool CheckObjectForProhibitedActions(PdfDictionary dict, HashSet<string> prohibitedTypes)
        {
            if (dict == null)
                return false;
                
            var openAction = dict.Get(PdfName.OPENACTION);
            if (openAction != null && IsProhibitedAction(openAction, prohibitedTypes))
                return true;
            
            var aa = dict.GetAsDict(PdfName.AA);
            if (aa != null)
            {
                foreach (var key in aa.Keys)
                {
                    var action = aa.Get(key);
                    if (action != null && IsProhibitedAction(action, prohibitedTypes))
                        return true;
                }
            }
            
            return false;
        }
        
        private static bool IsProhibitedAction(PdfObject actionObj, HashSet<string> prohibitedTypes)
        {
            if (!actionObj.IsDictionary())
                return false;
                
            var actionDict = (PdfDictionary)actionObj;
            var s = actionDict.GetAsName(PdfName.S);
            
            if (s != null)
            {
                return prohibitedTypes.Contains(s.ToString());
            }
            
            return false;
        }
        
        /// <summary>
        /// Verifica se o PDF contém arquivos embutidos
        /// </summary>
        public static bool CheckForEmbeddedFiles(PdfReader reader)
        {
            try
            {
                var catalog = reader.Catalog;
                var names = catalog.GetAsDict(PdfName.NAMES);
                
                if (names != null)
                {
                    var embeddedFiles = names.GetAsDict(new PdfName("EmbeddedFiles"));
                    if (embeddedFiles != null)
                        return true;
                }
                
                var collection = catalog.GetAsDict(new PdfName("Collection"));
                if (collection != null)
                    return true;
                
                // Verificar anotações FileAttachment
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    var annots = page.GetAsArray(PdfName.ANNOTS);
                    
                    if (annots != null)
                    {
                        for (int i = 0; i < annots.Size; i++)
                        {
                            var annotDict = annots.GetAsDict(i);
                            if (annotDict != null)
                            {
                                var subtype = annotDict.GetAsName(PdfName.SUBTYPE);
                                if (subtype != null && subtype.ToString() == "/FileAttachment")
                                    return true;
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
        
        /// <summary>
        /// Verifica se o PDF contém multimídia
        /// </summary>
        public static bool CheckForMultimedia(PdfReader reader)
        {
            try
            {
                var multimediaTypes = new HashSet<string>
                {
                    "/Sound", "/Movie", "/Screen", "/3D", "/RichMedia"
                };
                
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    var annots = page.GetAsArray(PdfName.ANNOTS);
                    
                    if (annots != null)
                    {
                        for (int i = 0; i < annots.Size; i++)
                        {
                            var annotDict = annots.GetAsDict(i);
                            if (annotDict != null)
                            {
                                var subtype = annotDict.GetAsName(PdfName.SUBTYPE);
                                if (subtype != null && multimediaTypes.Contains(subtype.ToString()))
                                {
                                    return true;
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
        
        /// <summary>
        /// Extrai informações básicas de conformidade PDF/A dos metadados XMP
        /// </summary>
        public static (bool isPDFA, string? conformanceLevel) GetPDFAInfoFromXMP(byte[] xmpBytes)
        {
            if (xmpBytes == null || xmpBytes.Length == 0)
                return (false, null);
                
            try
            {
                string xmpString = Encoding.UTF8.GetString(xmpBytes);
                
                if (xmpString.Contains("pdfaid:part"))
                {
                    // Extrair nível de conformidade
                    var partMatch = System.Text.RegularExpressions.Regex.Match(xmpString, @"<pdfaid:part>(\d+)</pdfaid:part>");
                    var confMatch = System.Text.RegularExpressions.Regex.Match(xmpString, @"<pdfaid:conformance>(\w+)</pdfaid:conformance>");
                    
                    if (partMatch.Success && confMatch.Success)
                    {
                        return (true, $"PDF/A-{partMatch.Groups[1].Value}{confMatch.Groups[1].Value}");
                    }
                    
                    return (true, "PDF/A");
                }
            }
            catch
            {
                // Ignorar erros de parsing
            }
            
            return (false, null);
        }
        
        /// <summary>
        /// Verifica se uma anotação é permitida em PDF/A
        /// </summary>
        public static bool IsAnnotationAllowedInPDFA(string subtype)
        {
            var allowedTypes = new HashSet<string>
            {
                "/Text", "/Link", "/FreeText", "/Line", "/Square", "/Circle",
                "/Polygon", "/PolyLine", "/Highlight", "/Underline", "/Squiggly",
                "/StrikeOut", "/Stamp", "/Caret", "/Ink", "/Popup", "/Widget",
                "/PrinterMark", "/TrapNet", "/Watermark"
            };
            
            return allowedTypes.Contains(subtype);
        }
        
        /// <summary>
        /// Verifica se uma ação é permitida em PDF/A
        /// </summary>
        public static bool IsActionAllowedInPDFA(string actionType)
        {
            var allowedTypes = new HashSet<string>
            {
                "/GoTo", "/GoToR", "/GoToE", "/Thread", "/URI", "/Named", "/SubmitForm"
            };
            
            return allowedTypes.Contains(actionType);
        }
    }
}