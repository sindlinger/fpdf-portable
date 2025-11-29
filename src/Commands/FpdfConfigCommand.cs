using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using FilterPDF.Configuration;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Configuration command - manage fpdf settings
    /// </summary>
    public class FpdfConfigCommand : Command
    {
        public override string Name => "config";
        public override string Description => "Manage fpdf configuration / Gerenciar configuração do fpdf";
        
        public override void ShowHelp()
        {
            Console.WriteLine(LanguageManager.GetMessage("config_help_title"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("config_help_usage"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_usage_line"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("config_help_subcommands"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_add_dir"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_remove_dir"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_list_dirs"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_show"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_reset"));
            Console.WriteLine();
            Console.WriteLine(LanguageManager.GetMessage("config_help_examples"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_ex1"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_ex2"));
            Console.WriteLine(LanguageManager.GetMessage("config_help_ex3"));
        }
        
        public override void Execute(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }
            
            if (args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return;
            }
            
            string subcommand = args[0].ToLower();
            
            switch (subcommand)
            {
                case "add-dir":
                    if (args.Length < 2)
                    {
                        Console.WriteLine(LanguageManager.GetMessage("config_error_specify_dir_add"));
                        return;
                    }
                    AddDirectory(args[1]);
                    break;
                    
                case "remove-dir":
                    if (args.Length < 2)
                    {
                        Console.WriteLine(LanguageManager.GetMessage("config_error_specify_dir_remove"));
                        return;
                    }
                    RemoveDirectory(args[1]);
                    break;
                    
                case "list-dirs":
                    ListDirectories();
                    break;
                    
                case "show":
                    ShowConfiguration();
                    break;
                    
                case "reset":
                    ResetConfiguration();
                    break;
                    
                default:
                    Console.WriteLine(LanguageManager.GetMessage("config_error_unknown_subcommand", subcommand));
                    ShowHelp();
                    break;
            }
        }
        
        private void AddDirectory(string directory)
        {
            try
            {
                var fullPath = Path.GetFullPath(directory);
                
                if (!Directory.Exists(fullPath))
                {
                    Console.WriteLine(LanguageManager.GetMessage("config_error_dir_not_exists", fullPath));
                    return;
                }
                
                var configPath = GetConfigPath();
                var config = LoadOrCreateConfig(configPath);
                
                if (config.Security.AllowedDirectories.Contains(fullPath))
                {
                    Console.WriteLine(LanguageManager.GetMessage("config_dir_already_allowed", fullPath));
                    return;
                }
                
                config.Security.AllowedDirectories.Add(fullPath);
                SaveConfig(configPath, config);
                
                Console.WriteLine(LanguageManager.GetMessage("config_dir_added", fullPath));
                Console.WriteLine(LanguageManager.GetMessage("config_restart_required"));
            }
            catch (Exception ex)
            {
                Console.WriteLine(LanguageManager.GetMessage("config_error_adding_dir", ex.Message));
            }
        }
        
        private void RemoveDirectory(string directory)
        {
            try
            {
                var fullPath = Path.GetFullPath(directory);
                var configPath = GetConfigPath();
                var config = LoadOrCreateConfig(configPath);
                
                if (config.Security.AllowedDirectories.Remove(fullPath))
                {
                    SaveConfig(configPath, config);
                    Console.WriteLine(LanguageManager.GetMessage("config_dir_removed", fullPath));
                }
                else
                {
                    Console.WriteLine(LanguageManager.GetMessage("config_dir_not_in_list", fullPath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(LanguageManager.GetMessage("config_error_removing_dir", ex.Message));
            }
        }
        
        private void ListDirectories()
        {
            var config = FpdfConfig.Instance;
            
            Console.WriteLine(LanguageManager.GetMessage("config_allowed_directories"));
            
            if (config.Security.AllowedDirectories.Any())
            {
                foreach (var dir in config.Security.AllowedDirectories)
                {
                    Console.WriteLine($"  • {dir}");
                }
            }
            else
            {
                Console.WriteLine(LanguageManager.GetMessage("config_none_configured"));
            }
        }
        
        private void ShowConfiguration()
        {
            var config = FpdfConfig.Instance;
            
            Console.WriteLine(LanguageManager.GetMessage("config_current_config"));
            Console.WriteLine();
            
            Console.WriteLine("Security:");
            Console.WriteLine($"  AllowedDirectories: {config.Security.AllowedDirectories.Count} " + 
                LanguageManager.GetMessage("config_directories_count"));
            foreach (var dir in config.Security.AllowedDirectories)
            {
                Console.WriteLine($"    • {dir}");
            }
            Console.WriteLine($"  DisablePathValidation: {config.Security.DisablePathValidation}");
            Console.WriteLine($"  MaxFileSize: {config.Security.MaxFileSize / (1024 * 1024)}MB");
            
            Console.WriteLine();
            Console.WriteLine("Performance:");
            Console.WriteLine($"  DefaultWorkers: {config.Performance.DefaultWorkers}");
        }
        
        private void ResetConfiguration()
        {
            var configPath = GetConfigPath();
            
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
                Console.WriteLine(LanguageManager.GetMessage("config_reset_success"));
            }
            else
            {
                Console.WriteLine(LanguageManager.GetMessage("config_no_config_file"));
            }
        }
        
        private string GetConfigPath()
        {
            var fpdfDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fpdf");
            if (!Directory.Exists(fpdfDir))
            {
                Directory.CreateDirectory(fpdfDir);
            }
            return Path.Combine(fpdfDir, "config.json");
        }
        
        private FpdfConfig LoadOrCreateConfig(string configPath)
        {
            // Always use the singleton instance and reload if config file exists
            var config = FpdfConfig.Instance;
            
            if (File.Exists(configPath))
            {
                try
                {
                    // Force reload from the config file
                    config.Load();
                }
                catch
                {
                    // If loading fails, use existing instance
                }
            }
            
            return config;
        }
        
        private void SaveConfig(string configPath, FpdfConfig config)
        {
            // Use the official SaveToFile method from FpdfConfig
            config.SaveToFile(configPath);
        }
    }
}