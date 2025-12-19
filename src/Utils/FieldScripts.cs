using System.Collections.Generic;

namespace FilterPDF.Utils
{
    public class FieldScript
    {
        public string Name { get; set; } = "";
        public string Pattern { get; set; } = "";
        public double Weight { get; set; }
    }

    public static class FieldScripts
    {
        public static List<FieldScript> LoadScripts(string path)
        {
            return new List<FieldScript>();
        }

        public static List<Dictionary<string, object>> RunScripts(List<FieldScript> scripts, string pdfName, string fullText, int startPage, string bucket)
        {
            return new List<Dictionary<string, object>>();
        }

        public static List<Dictionary<string, object>> RunScripts(List<FieldScript> scripts, string pdfName, string fullText, List<Dictionary<string, object>> words, int startPage, string bucket)
        {
            return new List<Dictionary<string, object>>();
        }
    }
}
