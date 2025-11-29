using System;
using System.Collections.Generic;
using iTextSharp.text.pdf;

namespace FilterPDF
{
    /// <summary>
    /// Extrator que captura TODOS os detalhes das imagens: Width, Height, ColorSpace, Compression, etc.
    /// </summary>
    public class DetailedImageExtractor
    {
        public static List<ImageInfo> ExtractCompleteImageDetails(PdfReader reader, int pageNum)
        {
            var images = new List<ImageInfo>();
            
            try
            {
                var pageDict = reader.GetPageN(pageNum);
                var resources = pageDict.GetAsDict(PdfName.RESOURCES);
                
                if (resources != null)
                {
                    var xObjects = resources.GetAsDict(PdfName.XOBJECT);
                    if (xObjects != null)
                    {
                        foreach (var key in xObjects.Keys)
                        {
                            var obj = xObjects.GetAsIndirectObject(key);
                            if (obj != null)
                            {
                                var xObj = PdfReader.GetPdfObject(obj);
                                if (xObj is PdfStream stream)
                                {
                                    var subType = stream.GetAsName(PdfName.SUBTYPE);
                                    if (PdfName.IMAGE.Equals(subType))
                                    {
                                        var image = ExtractSingleImageDetails(key.ToString(), stream);
                                        if (image != null)
                                        {
                                            images.Add(image);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            
            return images;
        }
        
        private static ImageInfo? ExtractSingleImageDetails(string name, PdfStream stream)
        {
            try
            {
                var image = new ImageInfo
                {
                    Name = name
                };
                
                // TODOS OS DETALHES POSSÍVEIS DA IMAGEM
                
                // Dimensões
                var width = stream.GetAsNumber(PdfName.WIDTH);
                if (width != null) image.Width = width.IntValue;
                
                var height = stream.GetAsNumber(PdfName.HEIGHT);
                if (height != null) image.Height = height.IntValue;
                
                // Bits por componente
                var bpc = stream.GetAsNumber(PdfName.BITSPERCOMPONENT);
                if (bpc != null) image.BitsPerComponent = bpc.IntValue;
                
                // Espaço de cor
                var colorSpace = stream.Get(PdfName.COLORSPACE);
                if (colorSpace != null) 
                {
                    image.ColorSpace = colorSpace.ToString();
                }
                
                // Tipo de compressão/filtro
                var filter = stream.Get(PdfName.FILTER);
                if (filter != null) 
                {
                    if (filter is PdfArray filterArray && filterArray.Size > 0)
                    {
                        image.CompressionType = filterArray.GetAsName(0).ToString();
                    }
                    else if (filter is PdfName filterName)
                    {
                        image.CompressionType = filterName.ToString();
                    }
                    else
                    {
                        image.CompressionType = filter.ToString();
                    }
                }
                
                // Extração dos dados base64 para cache
                try
                {
                    byte[] imageBytes = null;
                    
                    // Tentar extrair os dados da imagem
                    if (stream is PRStream prStream)
                    {
                        var imageFilter = stream.Get(PdfName.FILTER);
                        
                        // Para imagens JPEG, usar dados raw
                        if (IsJpegFilter(imageFilter))
                        {
                            imageBytes = PdfReader.GetStreamBytesRaw(prStream);
                        }
                        else
                        {
                            // Para outros formatos, usar dados decodificados
                            imageBytes = PdfReader.GetStreamBytes(prStream);
                        }
                        
                        // Converter para base64 se temos dados válidos
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            image.Base64Data = Convert.ToBase64String(imageBytes);
                        }
                    }
                }
                catch
                {
                    // Se falhar na extração de dados, continuar sem base64
                    // Isso não deve impedir o resto da análise
                }
                
                // Tamanho já calculado automaticamente pela propriedade EstimatedSize
                
                return image;
            }
            catch 
            {
                return null;
            }
        }
        
        /// <summary>
        /// Verifica se o filtro é de imagem JPEG
        /// </summary>
        private static bool IsJpegFilter(iTextSharp.text.pdf.PdfObject filter)
        {
            if (filter == null) return false;
            
            var filterStr = filter.ToString();
            return filterStr.Contains("DCTDecode") || filterStr.Contains("JPXDecode");
        }
    }
}