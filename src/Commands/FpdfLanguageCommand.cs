using System;
using System.Linq;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Language command - allows users to set application language
    /// </summary>
    public class FpdfLanguageCommand : Command
    {
        public override string Name => "idioma/language";
        public override string Description => "Set application language / Definir idioma da aplicação";
        
        public override void ShowHelp()
        {
            Console.WriteLine(LanguageManager.GetMessage("language_help"));
        }
        public override void Execute(string[] args)
        {
            // Remove the first argument if it's the command name
            if (args.Length > 0 && (args[0] == "idioma" || args[0] == "language"))
            {
                args = args.Skip(1).ToArray();
            }
            
            if (args.Length == 0)
            {
                // Show current language and available options
                ShowCurrentLanguage();
                return;
            }
            
            if (args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return;
            }
            
            string requestedLanguage = args[0].ToLower();
            
            // Handle language setting
            if (LanguageManager.SetLanguage(requestedLanguage))
            {
                string languageName = LanguageManager.GetLanguageName(requestedLanguage);
                Console.WriteLine(LanguageManager.GetMessage("language_set_to", languageName));
            }
            else
            {
                Console.WriteLine($"Error: Unknown language '{requestedLanguage}'");
                Console.WriteLine();
                ShowAvailableLanguages();
            }
        }
        
        
        private void ShowCurrentLanguage()
        {
            string currentLang = LanguageManager.CurrentLanguage;
            string languageName = LanguageManager.GetLanguageName(currentLang);
            Console.WriteLine(LanguageManager.GetMessage("current_language", languageName));
            Console.WriteLine();
            ShowAvailableLanguages();
        }
        
        private void ShowAvailableLanguages()
        {
            Console.WriteLine(LanguageManager.GetMessage("available_languages"));
            foreach (string lang in LanguageManager.GetAvailableLanguages())
            {
                string name = LanguageManager.GetLanguageName(lang);
                string current = lang == LanguageManager.CurrentLanguage ? " (current)" : "";
                Console.WriteLine($"    {lang} - {name}{current}");
            }
        }
    }
}