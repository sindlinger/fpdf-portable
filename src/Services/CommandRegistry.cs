using System;
using System.Collections.Generic;
using System.Linq;
using FilterPDF.Commands;
using FilterPDF.Interfaces;

namespace FilterPDF.Services
{
    /// <summary>
    /// Implementation of command registry service
    /// Manages registration and retrieval of CLI commands
    /// </summary>
    public class CommandRegistry : ICommandRegistry
    {
        private readonly Dictionary<string, Command> _commands;

        public CommandRegistry()
        {
            _commands = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Register a command with the registry
        /// </summary>
        public void RegisterCommand(string name, Command command)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Command name cannot be null or empty", nameof(name));
            
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            _commands[name] = command;
        }

        /// <summary>
        /// Get a command by name
        /// </summary>
        public Command? GetCommand(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            _commands.TryGetValue(name, out var command);
            return command;
        }

        /// <summary>
        /// Check if a command exists
        /// </summary>
        public bool HasCommand(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Check registered commands
            if (_commands.ContainsKey(name))
                return true;

            // Check direct commands (formerly filter commands)
            var directCommands = new[] { "pages", "bookmarks", "words", "annotations", 
                                        "objects", "fonts", "metadata", "structure", 
                                        "modifications", "documents", "images", "base64", "scanned", "stats", "doctypes",
                                        "doctype-set" };
            return directCommands.Contains(name.ToLower());
        }

        /// <summary>
        /// Get all registered command names
        /// </summary>
        public IEnumerable<string> GetCommandNames()
        {
            return _commands.Keys.ToList();
        }

        /// <summary>
        /// Create a command instance by name
        /// </summary>
        public Command CreateCommand(string name)
        {
            return GetCommand(name);
        }

        /// <summary>
        /// Get all registered commands with descriptions
        /// </summary>
        public Dictionary<string, string> GetAllCommands()
        {
            var result = new Dictionary<string, string>();
            foreach (var kvp in _commands)
            {
                result[kvp.Key] = GetCommandDescription(kvp.Key);
            }
            return result;
        }

        private string GetCommandDescription(string commandName)
        {
            return commandName.ToLower() switch
            {
                "stats" => "Statistical analysis of PDFs",
                "pages" => "Search pages by criteria",
                "bookmarks" => "Search bookmarks",
                "words" => "Search for words in PDF",
                "annotations" => "Search annotations",
                "objects" => "Search PDF objects",
                "fonts" => "List fonts used in PDF",
                "metadata" => "Show PDF metadata",
                "structure" => "Show PDF structure",
                "modifications" => "Detect modifications",
                "documents" => "Search documents",
                "images" => "Extract images from PDF",
                "base64" => "Extract base64 encoded data",
                "extract" => "Extract text from PDF",
                "load" => "Load PDF into cache",
                "cache" => "Manage cache files",
                "ocr" => "Perform OCR on PDF",
                "doctypes" => "Identify document types in PDF",
                _ => "Command"
            };
        }

        /// <summary>
        /// Initialize all commands
        /// Registers the core set of commands used by the application
        /// </summary>
        public void InitializeCommands()
        {
            // Register file commands
            RegisterCommand("extract", new ExtractCommand());
            RegisterCommand("load", new FpdfLoadCommand());
            RegisterCommand("cache", new FpdfCacheCommand());
            RegisterCommand("stats", new FpdfStatsCommand());
            RegisterCommand("ingest-db", new FpdfIngestDbCommand());
            
            // Register language commands
            RegisterCommand("idioma", new FpdfLanguageCommand());
            RegisterCommand("language", new FpdfLanguageCommand());
            
            // Register config command
            RegisterCommand("config", new FpdfConfigCommand());
            
            // Note: Direct commands (pages, bookmarks, etc.) are handled separately
            // in CommandExecutor since they don't inherit from Command
            
            // Register OCR command (now direct, not under extract)
            RegisterCommand("ocr", new FpdfOCRCommand());
            
        }
    }
}
