using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;

namespace PDFLayoutPreservingConverter
{
    /// <summary>
    /// Decodes hexadecimal text references in PDFs to actual text
    /// </summary>
    public class HexTextDecoder
    {
        private PdfReader reader;
        private Dictionary<string, string> fontEncodings;
        
        public HexTextDecoder(PdfReader reader)
        {
            this.reader = reader;
            this.fontEncodings = new Dictionary<string, string>();
            BuildFontEncodings();
        }
        
        /// <summary>
        /// Decodes hex-encoded text operations to readable text
        /// </summary>
        public string DecodeHexText(string streamContent, PdfDictionary resources)
        {
            var result = new StringBuilder();
            
            // Find all hex text operations like <0001> Tj
            var hexPattern = @"<([0-9A-Fa-f]+)>\s*Tj";
            var matches = Regex.Matches(streamContent, hexPattern);
            
            string currentFont = null;
            
            // Also look for font changes
            var fontPattern = @"/([A-Za-z0-9]+)\s+[\d.]+\s+Tf";
            var fontMatches = Regex.Matches(streamContent, fontPattern);
            
            foreach (Match match in matches)
            {
                var hexCode = match.Groups[1].Value;
                
                // Find which font is active at this position
                foreach (Match fontMatch in fontMatches)
                {
                    if (fontMatch.Index < match.Index)
                    {
                        currentFont = fontMatch.Groups[1].Value;
                    }
                }
                
                // Decode the hex value
                var decodedText = DecodeHexString(hexCode, currentFont, resources);
                if (!string.IsNullOrEmpty(decodedText))
                {
                    result.Append(decodedText);
                }
            }
            
            return result.ToString();
        }
        
        /// <summary>
        /// Decodes a single hex string based on font encoding
        /// </summary>
        private string DecodeHexString(string hexCode, string fontName, PdfDictionary resources)
        {
            try
            {
                // Convert hex to bytes
                var bytes = HexToBytes(hexCode);
                if (bytes == null || bytes.Length == 0)
                    return "";
                
                // Try to get font encoding
                if (resources != null && fontName != null)
                {
                    var fonts = resources.GetAsDict(PdfName.FONT);
                    if (fonts != null)
                    {
                        var fontDict = fonts.GetAsDict(new PdfName(fontName));
                        if (fontDict != null)
                        {
                            // Check for ToUnicode CMap
                            var toUnicode = fontDict.Get(PdfName.TOUNICODE);
                            if (toUnicode != null && toUnicode.IsIndirect())
                            {
                                var stream = (PRStream)PdfReader.GetPdfObject(toUnicode);
                                if (stream != null)
                                {
                                    var unicodeMap = ParseToUnicodeCMap(stream);
                                    if (unicodeMap.ContainsKey(hexCode))
                                    {
                                        return unicodeMap[hexCode];
                                    }
                                }
                            }
                            
                            // Check encoding
                            var encoding = fontDict.Get(PdfName.ENCODING);
                            if (encoding != null)
                            {
                                return DecodeWithEncoding(bytes, encoding);
                            }
                        }
                    }
                }
                
                // Default decoding attempts
                // Try as UTF-16BE (common for hex codes)
                if (bytes.Length >= 2)
                {
                    var text = Encoding.BigEndianUnicode.GetString(bytes);
                    if (!string.IsNullOrEmpty(text) && !ContainsInvalidChars(text))
                        return text;
                }
                
                // Try as single byte encoding
                if (bytes.Length == 1)
                {
                    var charCode = bytes[0];
                    if (charCode >= 32 && charCode < 127)
                    {
                        return ((char)charCode).ToString();
                    }
                }
                
                // Try Windows-1252
                var win1252 = Encoding.GetEncoding(1252).GetString(bytes);
                if (!ContainsInvalidChars(win1252))
                    return win1252;
                
                return "";
            }
            catch
            {
                return "";
            }
        }
        
        /// <summary>
        /// Parses a ToUnicode CMap stream
        /// </summary>
        private Dictionary<string, string> ParseToUnicodeCMap(PRStream stream)
        {
            var map = new Dictionary<string, string>();
            
            try
            {
                var bytes = PdfReader.GetStreamBytes(stream);
                var content = Encoding.ASCII.GetString(bytes);
                
                // Parse bfchar entries (single character mappings)
                var bfcharPattern = @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>";
                var bfcharMatches = Regex.Matches(content, bfcharPattern);
                
                foreach (Match match in bfcharMatches)
                {
                    var src = match.Groups[1].Value;
                    var dst = match.Groups[2].Value;
                    
                    var dstBytes = HexToBytes(dst);
                    if (dstBytes != null && dstBytes.Length >= 2)
                    {
                        var unicode = Encoding.BigEndianUnicode.GetString(dstBytes);
                        map[src] = unicode;
                    }
                }
                
                // Parse bfrange entries (character range mappings)
                var bfrangePattern = @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>";
                var bfrangeMatches = Regex.Matches(content, bfrangePattern);
                
                foreach (Match match in bfrangeMatches)
                {
                    var startHex = match.Groups[1].Value;
                    var endHex = match.Groups[2].Value;
                    var dstHex = match.Groups[3].Value;
                    
                    var start = Convert.ToInt32(startHex, 16);
                    var end = Convert.ToInt32(endHex, 16);
                    var dst = Convert.ToInt32(dstHex, 16);
                    
                    for (int i = start; i <= end; i++)
                    {
                        var srcCode = i.ToString("X4");
                        var dstCode = (dst + (i - start)).ToString("X4");
                        
                        var dstBytes = HexToBytes(dstCode);
                        if (dstBytes != null && dstBytes.Length >= 2)
                        {
                            var unicode = Encoding.BigEndianUnicode.GetString(dstBytes);
                            map[srcCode] = unicode;
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            
            return map;
        }
        
        /// <summary>
        /// Builds font encoding mappings from the document
        /// </summary>
        private void BuildFontEncodings()
        {
            try
            {
                for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                {
                    var page = reader.GetPageN(pageNum);
                    var resources = page.GetAsDict(PdfName.RESOURCES);
                    
                    if (resources != null)
                    {
                        var fonts = resources.GetAsDict(PdfName.FONT);
                        if (fonts != null)
                        {
                            foreach (var fontName in fonts.Keys)
                            {
                                var fontDict = fonts.GetAsDict(fontName);
                                if (fontDict != null)
                                {
                                    var basefont = fontDict.GetAsName(PdfName.BASEFONT);
                                    if (basefont != null)
                                    {
                                        fontEncodings[fontName.ToString()] = basefont.ToString();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors in font discovery
            }
        }
        
        /// <summary>
        /// Decodes bytes with a specific encoding
        /// </summary>
        private string DecodeWithEncoding(byte[] bytes, PdfObject encoding)
        {
            if (encoding.IsName())
            {
                var encodingName = ((PdfName)encoding).ToString();
                
                if (encodingName.Contains("WinAnsi"))
                {
                    return Encoding.GetEncoding(1252).GetString(bytes);
                }
                else if (encodingName.Contains("MacRoman"))
                {
                    return Encoding.GetEncoding(10000).GetString(bytes);
                }
                else if (encodingName.Contains("Identity"))
                {
                    // Identity encoding - often used with CID fonts
                    if (bytes.Length >= 2)
                    {
                        return Encoding.BigEndianUnicode.GetString(bytes);
                    }
                }
            }
            
            return Encoding.UTF8.GetString(bytes);
        }
        
        /// <summary>
        /// Converts hex string to byte array
        /// </summary>
        private byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return null;
            
            // Ensure even length
            if (hex.Length % 2 != 0)
                hex = "0" + hex;
            
            try
            {
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }
                return bytes;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Checks if text contains invalid characters
        /// </summary>
        private bool ContainsInvalidChars(string text)
        {
            foreach (char c in text)
            {
                if (c == '\0' || (c < 32 && c != '\r' && c != '\n' && c != '\t'))
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Extracts all text from a content stream, handling hex encoding
        /// </summary>
        public List<string> ExtractAllTexts(string streamContent, PdfDictionary resources)
        {
            var texts = new List<string>();
            
            // First try regular text extraction
            var regularPattern = @"\((.*?)\)\s*Tj";
            var regularMatches = Regex.Matches(streamContent, regularPattern);
            
            foreach (Match match in regularMatches)
            {
                var text = UnescapePdfString(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    texts.Add(text);
                }
            }
            
            // Then try hex text extraction
            var hexText = DecodeHexText(streamContent, resources);
            if (!string.IsNullOrWhiteSpace(hexText))
            {
                texts.Add(hexText);
            }
            
            // Also handle TJ arrays
            var tjArrayPattern = @"\[(.*?)\]\s*TJ";
            var tjMatches = Regex.Matches(streamContent, tjArrayPattern);
            
            foreach (Match match in tjMatches)
            {
                var arrayContent = match.Groups[1].Value;
                
                // Extract hex strings from array
                var hexInArray = @"<([0-9A-Fa-f]+)>";
                var hexArrayMatches = Regex.Matches(arrayContent, hexInArray);
                
                var arrayText = new StringBuilder();
                foreach (Match hexMatch in hexArrayMatches)
                {
                    var decoded = DecodeHexString(hexMatch.Groups[1].Value, null, resources);
                    arrayText.Append(decoded);
                }
                
                if (arrayText.Length > 0)
                {
                    texts.Add(arrayText.ToString());
                }
                
                // Also extract regular strings from array
                var stringInArray = @"\((.*?)\)";
                var stringArrayMatches = Regex.Matches(arrayContent, stringInArray);
                
                foreach (Match strMatch in stringArrayMatches)
                {
                    var text = UnescapePdfString(strMatch.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        texts.Add(text);
                    }
                }
            }
            
            return texts;
        }
        
        /// <summary>
        /// Unescapes PDF string literals
        /// </summary>
        private string UnescapePdfString(string pdfString)
        {
            if (string.IsNullOrEmpty(pdfString))
                return "";
            
            var result = pdfString
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\b", "\b")
                .Replace("\\f", "\f")
                .Replace("\\(", "(")
                .Replace("\\)", ")")
                .Replace("\\\\", "\\");
            
            // Handle octal sequences
            result = Regex.Replace(result, @"\\(\d{1,3})", match =>
            {
                var octal = match.Groups[1].Value;
                var value = Convert.ToInt32(octal, 8);
                return ((char)value).ToString();
            });
            
            return result;
        }
    }
}