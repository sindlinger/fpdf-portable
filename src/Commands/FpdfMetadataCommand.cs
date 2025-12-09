using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using FilterPDF.Options;
using FilterPDF.Utils;


namespace FilterPDF
{
    /// <summary>
    /// Filter Metadata Command - Extract and filter PDF metadata
    /// </summary>
    public class FpdfMetadataCommand
    {
        private Dictionary<string, string> filterOptions = new Dictionary<string, string>();
        private Dictionary<string, string> outputOptions = new Dictionary<string, string>();
        private PDFAnalysisResult analysisResult = new PDFAnalysisResult();
        private string inputFilePath = "";
        private bool isUsingCache = false;
        
        public void Execute(string inputFile, PDFAnalysisResult analysis, Dictionary<string, string> filters, Dictionary<string, string> outputs)
        {
            inputFilePath = inputFile;
            analysisResult = analysis;
            filterOptions = filters;
            outputOptions = outputs;
            isUsingCache = inputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            
            ExecuteMetadataSearch();
        }

        public void ExecuteFromPg(int processId, Dictionary<string,string> filters, Dictionary<string,string> outputs)
        {
            filterOptions = filters;
            outputOptions = outputs;

            var processes = PgAnalysisLoader.ListProcesses();
            var row = processes.FirstOrDefault(r => r.Id == processId);
            if (row == null)
            {
                Console.WriteLine($"Processo {processId} não encontrado no Postgres.");
                return;
            }

            var summary = PgAnalysisLoader.GetProcessSummaryById(processId);
            if (summary == null)
            {
                Console.WriteLine("Metadados não encontrados.");
                return;
            }

            Console.WriteLine($"METADATA (PG) - {row.ProcessNumber}");
            Console.WriteLine($"  Title     : {summary.MetaTitle}");
            Console.WriteLine($"  Author    : {summary.MetaAuthor}");
            Console.WriteLine($"  Subject   : {summary.MetaSubject}");
            Console.WriteLine($"  Keywords  : {summary.MetaKeywords}");
            Console.WriteLine($"  Encrypted : {summary.IsEncrypted}");
            Console.WriteLine($"  Perms     : copy={summary.PermCopy} print={summary.PermPrint} annotate={summary.PermAnnotate} forms={summary.PermFillForms} extract={summary.PermExtract} assemble={summary.PermAssemble} print_hq={summary.PermPrintHq}");
            Console.WriteLine($"  Resources : js={summary.HasJs} embedded={summary.HasEmbedded} attachments={summary.HasAttachments} multimedia={summary.HasMultimedia} forms={summary.HasForms}");
        }
        
        private void ExecuteMetadataSearch()
        {
            Console.WriteLine($"Extracting METADATA from: {Path.GetFileName(inputFilePath)}");
            ShowActiveFilters();
            Console.WriteLine();
            
            // Primeiro verificar se o documento atende aos filtros universais
            if (!DocumentMatchesUniversalFilters())
            {
                Console.WriteLine("Document does not match filter criteria.");
                OutputMetadataResults(new MetadataMatch()); // Resultado vazio
                return;
            }
            
            var metadataResult = new MetadataMatch();
            
            // Se nao ha filtros OU tem filtros especificos de metadata, mostrar metadata
            bool showAllMetadata = filterOptions.Count == 0;
            
            // Incluir metadata
            metadataResult.Metadata = analysisResult.Metadata;
            
            // XMP Metadata
            if (showAllMetadata || filterOptions.ContainsKey("--xmp"))
            {
                metadataResult.XMPMetadata = analysisResult.XMPMetadata;
            }
            
            // Dublin Core
            if (showAllMetadata || filterOptions.ContainsKey("--dublin-core"))
            {
                if (analysisResult.XMPMetadata != null)
                {
                    metadataResult.DublinCore = new DublinCoreData
                    {
                        Title = analysisResult.XMPMetadata.DublinCoreTitle,
                        Creator = analysisResult.XMPMetadata.DublinCoreCreator,
                        Subject = analysisResult.XMPMetadata.DublinCoreSubject,
                        Description = analysisResult.XMPMetadata.DublinCoreDescription,
                        Keywords = analysisResult.XMPMetadata.DublinCoreKeywords
                    };
                }
            }
            
            // History
            if (showAllMetadata || filterOptions.ContainsKey("--history"))
            {
                if (analysisResult.XMPMetadata != null)
                {
                    metadataResult.EditHistory = analysisResult.XMPMetadata.EditHistory;
                }
            }
            
            // Rights
            if (showAllMetadata || filterOptions.ContainsKey("--rights"))
            {
                if (analysisResult.XMPMetadata != null)
                {
                    metadataResult.CopyrightInfo = new CopyrightData
                    {
                        Notice = analysisResult.XMPMetadata.CopyrightNotice,
                        Owner = analysisResult.XMPMetadata.CopyrightOwner,
                        Date = analysisResult.XMPMetadata.CopyrightDate
                    };
                }
            }
            
            // Custom Properties
            if (showAllMetadata || filterOptions.ContainsKey("--custom"))
            {
                metadataResult.CustomProperties = analysisResult.XMPMetadata?.CustomProperties ?? new Dictionary<string, string>();
            }
            
            OutputMetadataResults(metadataResult);
        }
        
        private bool DocumentMatchesUniversalFilters()
        {
            // Check metadata filters
            var metadata = analysisResult.Metadata;
            if (metadata == null)
                return false;

            foreach (var filter in filterOptions)
            {
                string pattern = filter.Value;
                
                switch (filter.Key)
                {
                    case "--title":
                        if (!WordOption.Matches(metadata.Title, pattern))
                            return false;
                        break;
                        
                    case "--author":
                        if (!WordOption.Matches(metadata.Author, pattern))
                            return false;
                        break;
                        
                    case "--creator":
                        if (!WordOption.Matches(metadata.Creator, pattern))
                            return false;
                        break;
                        
                    case "--producer":
                        if (!WordOption.Matches(metadata.Producer, pattern))
                            return false;
                        break;
                        
                    case "--subject":
                        if (!WordOption.Matches(metadata.Subject, pattern))
                            return false;
                        break;
                        
                    case "--keywords":
                        if (!WordOption.Matches(metadata.Keywords, pattern))
                            return false;
                        break;
                }
            }
            
            return true;
        }
        
        private void ShowActiveFilters()
        {
            if (filterOptions.Count == 0)
            {
                Console.WriteLine("No filters specified - showing all metadata");
            }
            else
            {
                Console.WriteLine("Active filters:");
                foreach (var filter in filterOptions)
                {
                    Console.WriteLine($"   {GetFilterDescription(filter.Key, filter.Value)}");
                }
            }
        }
        
        private string GetFilterDescription(string key, string value)
        {
            switch (key)
            {
                case "--title":
                    return $"Title contains: {WordOption.GetSearchDescription(value)}";
                case "--author":
                    return $"Author contains: {WordOption.GetSearchDescription(value)}";
                case "--creator":
                    return $"Creator contains: {WordOption.GetSearchDescription(value)}";
                case "--producer":
                    return $"Producer contains: {WordOption.GetSearchDescription(value)}";
                case "--subject":
                    return $"Subject contains: {WordOption.GetSearchDescription(value)}";
                case "--keywords":
                    return $"Keywords contains: {WordOption.GetSearchDescription(value)}";
                case "--xmp":
                    return "Show XMP metadata";
                case "--dublin-core":
                    return "Show Dublin Core metadata";
                case "--history":
                    return "Show edit history";
                case "--rights":
                    return "Show copyright information";
                case "--custom":
                    return "Show custom properties";
                default:
                    return $"{key}: {value}";
            }
        }
        
        private void OutputMetadataResults(MetadataMatch metadata)
        {
            string output = "";
            string format = "json"; // Default para metadata
            
            if (outputOptions.ContainsKey("-F"))
            {
                format = outputOptions["-F"].ToLower();
            }
            
            switch (format)
            {
                case "json":
                    output = FormatMetadataAsJson(metadata);
                    break;
                case "xml":
                    output = FormatMetadataAsXml(metadata);
                    break;
                case "csv":
                    output = FormatMetadataAsCsv(metadata);
                    break;
                case "md":
                    output = FormatMetadataAsMarkdown(metadata);
                    break;
                case "raw":
                    output = FormatMetadataAsRaw(metadata);
                    break;
                case "count":
                    output = "1"; // Metadata sempre retorna 1 documento
                    break;
                case "png":
                    Console.WriteLine("⚠️ Formato PNG não é aplicável para metadados pois retorna apenas dados textuais, não páginas.");
                    Console.WriteLine("💡 Usando formato JSON como alternativa...");
                    Console.WriteLine();
                    output = FormatMetadataAsJson(metadata);
                    break;
                case "txt":
                default:
                    output = FormatMetadataAsText(metadata);
                    break;
            }
            
            Console.WriteLine(output);
        }
        
        private string FormatMetadataAsJson(MetadataMatch metadata)
        {
            return JsonConvert.SerializeObject(metadata, Formatting.Indented);
        }
        
        private string FormatMetadataAsXml(MetadataMatch metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<metadata>");
            
            if (metadata.Metadata != null)
            {
                sb.AppendLine("  <basic>");
                sb.AppendLine($"    <title>{System.Security.SecurityElement.Escape(metadata.Metadata.Title ?? "")}</title>");
                sb.AppendLine($"    <author>{System.Security.SecurityElement.Escape(metadata.Metadata.Author ?? "")}</author>");
                sb.AppendLine($"    <subject>{System.Security.SecurityElement.Escape(metadata.Metadata.Subject ?? "")}</subject>");
                sb.AppendLine($"    <creator>{System.Security.SecurityElement.Escape(metadata.Metadata.Creator ?? "")}</creator>");
                sb.AppendLine($"    <producer>{System.Security.SecurityElement.Escape(metadata.Metadata.Producer ?? "")}</producer>");
                sb.AppendLine($"    <creationDate>{metadata.Metadata.CreationDate}</creationDate>");
                sb.AppendLine($"    <modificationDate>{metadata.Metadata.ModificationDate}</modificationDate>");
                sb.AppendLine("  </basic>");
            }
            
            if (metadata.DublinCore != null)
            {
                sb.AppendLine("  <dublin-core>");
                sb.AppendLine($"    <title>{System.Security.SecurityElement.Escape(metadata.DublinCore.Title ?? "")}</title>");
                sb.AppendLine($"    <creator>{System.Security.SecurityElement.Escape(metadata.DublinCore.Creator ?? "")}</creator>");
                sb.AppendLine($"    <subject>{System.Security.SecurityElement.Escape(metadata.DublinCore.Subject ?? "")}</subject>");
                sb.AppendLine($"    <description>{System.Security.SecurityElement.Escape(metadata.DublinCore.Description ?? "")}</description>");
                if (metadata.DublinCore.Keywords != null)
                {
                    sb.AppendLine("    <keywords>");
                    foreach (var keyword in metadata.DublinCore.Keywords)
                    {
                        sb.AppendLine($"      <keyword>{System.Security.SecurityElement.Escape(keyword)}</keyword>");
                    }
                    sb.AppendLine("    </keywords>");
                }
                sb.AppendLine("  </dublin-core>");
            }
            
            if (metadata.CopyrightInfo != null)
            {
                sb.AppendLine("  <copyright>");
                sb.AppendLine($"    <notice>{System.Security.SecurityElement.Escape(metadata.CopyrightInfo.Notice ?? "")}</notice>");
                sb.AppendLine($"    <owner>{System.Security.SecurityElement.Escape(metadata.CopyrightInfo.Owner ?? "")}</owner>");
                if (metadata.CopyrightInfo.Date.HasValue)
                    sb.AppendLine($"    <date>{metadata.CopyrightInfo.Date}</date>");
                sb.AppendLine("  </copyright>");
            }
            
            if (metadata.EditHistory != null && metadata.EditHistory.Count > 0)
            {
                sb.AppendLine("  <edit-history>");
                foreach (var entry in metadata.EditHistory)
                {
                    sb.AppendLine("    <entry>");
                    sb.AppendLine($"      <action>{System.Security.SecurityElement.Escape(entry.Action ?? "")}</action>");
                    sb.AppendLine($"      <when>{entry.When}</when>");
                    sb.AppendLine($"      <software>{System.Security.SecurityElement.Escape(entry.SoftwareAgent ?? "")}</software>");
                    sb.AppendLine("    </entry>");
                }
                sb.AppendLine("  </edit-history>");
            }
            
            if (metadata.CustomProperties != null && metadata.CustomProperties.Count > 0)
            {
                sb.AppendLine("  <custom-properties>");
                foreach (var prop in metadata.CustomProperties)
                {
                    sb.AppendLine($"    <property name=\"{System.Security.SecurityElement.Escape(prop.Key)}\">{System.Security.SecurityElement.Escape(prop.Value)}</property>");
                }
                sb.AppendLine("  </custom-properties>");
            }
            
            sb.AppendLine("</metadata>");
            return sb.ToString();
        }
        
        private string FormatMetadataAsCsv(MetadataMatch metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Category,Field,Value");
            
            if (metadata.Metadata != null)
            {
                sb.AppendLine($"\"Basic\",\"Title\",\"{EscapeCsvValue(metadata.Metadata.Title)}\"");
                sb.AppendLine($"\"Basic\",\"Author\",\"{EscapeCsvValue(metadata.Metadata.Author)}\"");
                sb.AppendLine($"\"Basic\",\"Subject\",\"{EscapeCsvValue(metadata.Metadata.Subject)}\"");
                sb.AppendLine($"\"Basic\",\"Creator\",\"{EscapeCsvValue(metadata.Metadata.Creator)}\"");
                sb.AppendLine($"\"Basic\",\"Producer\",\"{EscapeCsvValue(metadata.Metadata.Producer)}\"");
                sb.AppendLine($"\"Basic\",\"Creation Date\",\"{metadata.Metadata.CreationDate}\"");
                sb.AppendLine($"\"Basic\",\"Modification Date\",\"{metadata.Metadata.ModificationDate}\"");
            }
            
            if (metadata.DublinCore != null)
            {
                sb.AppendLine($"\"Dublin Core\",\"Title\",\"{EscapeCsvValue(metadata.DublinCore.Title)}\"");
                sb.AppendLine($"\"Dublin Core\",\"Creator\",\"{EscapeCsvValue(metadata.DublinCore.Creator)}\"");
                sb.AppendLine($"\"Dublin Core\",\"Subject\",\"{EscapeCsvValue(metadata.DublinCore.Subject)}\"");
                sb.AppendLine($"\"Dublin Core\",\"Description\",\"{EscapeCsvValue(metadata.DublinCore.Description)}\"");
                if (metadata.DublinCore.Keywords != null)
                {
                    sb.AppendLine($"\"Dublin Core\",\"Keywords\",\"{EscapeCsvValue(string.Join("; ", metadata.DublinCore.Keywords))}\"");
                }
            }
            
            if (metadata.CopyrightInfo != null)
            {
                sb.AppendLine($"\"Copyright\",\"Notice\",\"{EscapeCsvValue(metadata.CopyrightInfo.Notice)}\"");
                sb.AppendLine($"\"Copyright\",\"Owner\",\"{EscapeCsvValue(metadata.CopyrightInfo.Owner)}\"");
                if (metadata.CopyrightInfo.Date.HasValue)
                    sb.AppendLine($"\"Copyright\",\"Date\",\"{metadata.CopyrightInfo.Date}\"");
            }
            
            return sb.ToString();
        }
        
        private string FormatMetadataAsMarkdown(MetadataMatch metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PDF Metadata");
            sb.AppendLine();
            
            if (metadata.Metadata != null)
            {
                sb.AppendLine("## Basic Information");
                sb.AppendLine();
                sb.AppendLine($"- **Title:** {metadata.Metadata.Title}");
                sb.AppendLine($"- **Author:** {metadata.Metadata.Author}");
                sb.AppendLine($"- **Subject:** {metadata.Metadata.Subject}");
                sb.AppendLine($"- **Creator:** {metadata.Metadata.Creator}");
                sb.AppendLine($"- **Producer:** {metadata.Metadata.Producer}");
                sb.AppendLine($"- **Creation Date:** {metadata.Metadata.CreationDate}");
                sb.AppendLine($"- **Modification Date:** {metadata.Metadata.ModificationDate}");
                sb.AppendLine();
            }
            
            if (metadata.DublinCore != null)
            {
                sb.AppendLine("## Dublin Core Metadata");
                sb.AppendLine();
                sb.AppendLine($"- **Title:** {metadata.DublinCore.Title}");
                sb.AppendLine($"- **Creator:** {metadata.DublinCore.Creator}");
                sb.AppendLine($"- **Subject:** {metadata.DublinCore.Subject}");
                sb.AppendLine($"- **Description:** {metadata.DublinCore.Description}");
                if (metadata.DublinCore.Keywords != null && metadata.DublinCore.Keywords.Count > 0)
                {
                    sb.AppendLine($"- **Keywords:** {string.Join(", ", metadata.DublinCore.Keywords)}");
                }
                sb.AppendLine();
            }
            
            if (metadata.CopyrightInfo != null)
            {
                sb.AppendLine("## Copyright Information");
                sb.AppendLine();
                sb.AppendLine($"- **Notice:** {metadata.CopyrightInfo.Notice}");
                sb.AppendLine($"- **Owner:** {metadata.CopyrightInfo.Owner}");
                if (metadata.CopyrightInfo.Date.HasValue)
                    sb.AppendLine($"- **Date:** {metadata.CopyrightInfo.Date}");
                sb.AppendLine();
            }
            
            if (metadata.EditHistory != null && metadata.EditHistory.Count > 0)
            {
                sb.AppendLine("## Edit History");
                sb.AppendLine();
                sb.AppendLine("| Action | When | Software |");
                sb.AppendLine("|--------|------|----------|");
                foreach (var entry in metadata.EditHistory)
                {
                    sb.AppendLine($"| {entry.Action} | {entry.When} | {entry.SoftwareAgent} |");
                }
                sb.AppendLine();
            }
            
            if (metadata.CustomProperties != null && metadata.CustomProperties.Count > 0)
            {
                sb.AppendLine("## Custom Properties");
                sb.AppendLine();
                foreach (var prop in metadata.CustomProperties)
                {
                    sb.AppendLine($"- **{prop.Key}:** {prop.Value}");
                }
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private string FormatMetadataAsRaw(MetadataMatch metadata)
        {
            var sb = new StringBuilder();
            
            if (metadata.Metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.Metadata.Title))
                    sb.AppendLine(metadata.Metadata.Title);
                if (!string.IsNullOrEmpty(metadata.Metadata.Author))
                    sb.AppendLine(metadata.Metadata.Author);
                if (!string.IsNullOrEmpty(metadata.Metadata.Subject))
                    sb.AppendLine(metadata.Metadata.Subject);
            }
            
            return sb.ToString();
        }
        
        private string FormatMetadataAsText(MetadataMatch metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PDF Metadata:");
            sb.AppendLine();
            
            if (metadata.Metadata != null)
            {
                sb.AppendLine("Basic Information:");
                sb.AppendLine($"  Title: {metadata.Metadata.Title}");
                sb.AppendLine($"  Author: {metadata.Metadata.Author}");
                sb.AppendLine($"  Subject: {metadata.Metadata.Subject}");
                sb.AppendLine($"  Creator: {metadata.Metadata.Creator}");
                sb.AppendLine($"  Producer: {metadata.Metadata.Producer}");
                sb.AppendLine($"  Creation Date: {metadata.Metadata.CreationDate}");
                sb.AppendLine($"  Modification Date: {metadata.Metadata.ModificationDate}");
                sb.AppendLine();
            }
            
            if (metadata.DublinCore != null)
            {
                sb.AppendLine("Dublin Core:");
                sb.AppendLine($"  Title: {metadata.DublinCore.Title}");
                sb.AppendLine($"  Creator: {metadata.DublinCore.Creator}");
                sb.AppendLine($"  Subject: {metadata.DublinCore.Subject}");
                sb.AppendLine($"  Description: {metadata.DublinCore.Description}");
                if (metadata.DublinCore.Keywords != null && metadata.DublinCore.Keywords.Count > 0)
                {
                    sb.AppendLine($"  Keywords: {string.Join(", ", metadata.DublinCore.Keywords)}");
                }
                sb.AppendLine();
            }
            
            if (metadata.CopyrightInfo != null)
            {
                sb.AppendLine("Copyright Information:");
                sb.AppendLine($"  Notice: {metadata.CopyrightInfo.Notice}");
                sb.AppendLine($"  Owner: {metadata.CopyrightInfo.Owner}");
                if (metadata.CopyrightInfo.Date.HasValue)
                    sb.AppendLine($"  Date: {metadata.CopyrightInfo.Date}");
                sb.AppendLine();
            }
            
            if (metadata.EditHistory != null && metadata.EditHistory.Count > 0)
            {
                sb.AppendLine("Edit History:");
                foreach (var entry in metadata.EditHistory)
                {
                    sb.AppendLine($"  - {entry.Action} at {entry.When} using {entry.SoftwareAgent}");
                }
                sb.AppendLine();
            }
            
            if (metadata.CustomProperties != null && metadata.CustomProperties.Count > 0)
            {
                sb.AppendLine("Custom Properties:");
                foreach (var prop in metadata.CustomProperties)
                {
                    sb.AppendLine($"  {prop.Key}: {prop.Value}");
                }
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\"", "\"\"");
        }
    }
}
