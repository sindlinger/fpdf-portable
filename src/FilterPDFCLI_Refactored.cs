using System;
using System.IO;
using System.Linq;
using FilterPDF.Services;
using FilterPDF.Utils;

namespace FilterPDF
{
    /// <summary>
    /// Refactored CLI entry point - focuses only on routing and argument parsing.
    /// Delegates all command execution to appropriate services.
    /// </summary>
    public class FilterPDFCLI_Refactored
    {
        private readonly CommandRegistry _registry;
        private readonly CommandExecutor _executor;
        
        public FilterPDFCLI_Refactored()
        {
            _registry = new CommandRegistry();
            _registry.InitializeCommands(); // Initialize all commands
            _executor = new CommandExecutor(_registry);
        }

        public static void Main(string[] args)
        {
            // Register encoding provider
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            
            var cli = new FilterPDFCLI_Refactored();
            cli.Run(args);
        }

        private void Run(string[] args)
        {
            // Check for version/help BEFORE initializing logging
            if (args.Length > 0)
            {
                var firstArg = args[0].ToLower();
                if (firstArg == "-v" || firstArg == "--version" || firstArg == "version")
                {
                    ShowVersion();
                    return;
                }
                if (firstArg == "-h" || firstArg == "--help" || firstArg == "help")
                {
                    ShowHelp();
                    return;
                }
            }
            
            // Check if raw output is requested to suppress logging
            bool suppressLogging = false;
            foreach (var arg in args)
            {
                if (arg == "-F" || arg == "--format")
                {
                    var index = Array.IndexOf(args, arg);
                    if (index + 1 < args.Length && args[index + 1] == "raw")
                    {
                        suppressLogging = true;
                        break;
                    }
                }
            }
            
            // Initialize logging only for actual operations (unless raw output)
            if (!suppressLogging)
            {
                InitializeLogging();
            }
            
            // Show help if no arguments
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            // Parse command line arguments
            var (success, commandType, arguments) = ParseArguments(args);
            
            if (!success)
            {
                ShowHelp();
                return;
            }

            // Route to appropriate handler
            switch (commandType)
            {
                case CommandType.Help:
                    ShowHelp();
                    break;
                    
                case CommandType.Version:
                    ShowVersion();
                    break;
                    
                case CommandType.CacheRange:
                    HandleCacheRangeCommand(arguments);
                    break;
                    
                case CommandType.CacheIndex:
                    HandleCacheIndexCommand(arguments);
                    break;
                    
                case CommandType.FileCommand:
                    HandleFileCommand(arguments);
                    break;
                    
                case CommandType.AnalysisCommandWithHelp:
                    ShowCommandHelp(arguments.CommandName);
                    break;
                    
                case CommandType.AnalysisCommandWithoutContext:
                    if (arguments.CommandName.Equals("find", StringComparison.OrdinalIgnoreCase))
                    {
                        // Permitir find direto no SQLite com --db-path (sem cache index)
                        var finder = new FpdfFindCommand();
                        finder.Execute(string.Empty, null, arguments.Arguments ?? Array.Empty<string>());
                    }
                    else
                    {
                        Console.WriteLine(LanguageManager.GetMessage("error_command_needs_cache", arguments.CommandName));
                        Console.WriteLine();
                        Console.WriteLine(LanguageManager.GetMessage("how_to_use"));
                        Console.WriteLine(LanguageManager.GetMessage("step_1_load"));
                        Console.WriteLine("   fpdf load arquivo.pdf ultra");
                        Console.WriteLine();
                        Console.WriteLine(LanguageManager.GetMessage("step_2_use_cache"));
                        Console.WriteLine($"   fpdf 1 {arguments.CommandName} [opções]");
                        Console.WriteLine();
                        Console.WriteLine(LanguageManager.GetMessage("examples"));
                        Console.WriteLine($"   fpdf 1 {arguments.CommandName}");
                        Console.WriteLine($"   fpdf 1 {arguments.CommandName} --help");
                        Console.WriteLine();
                        Console.WriteLine(LanguageManager.GetMessage("view_loaded_pdfs"));
                    }
                    break;
                    
                default:
                    Console.WriteLine("Error: Invalid command format.");
                    ShowHelp();
                    break;
            }
        }

        private (bool success, CommandType type, CommandArguments args) ParseArguments(string[] args)
        {
            var firstArg = args[0].ToLower();
            // Parse arguments
            
            // Check for help/version
            if (firstArg == "-h" || firstArg == "--help" || firstArg == "help")
            {
                return (true, CommandType.Help, null);
            }
            
            if (firstArg == "-v" || firstArg == "--version" || firstArg == "version")
            {
                return (true, CommandType.Version, null);
            }
            
            // Check for cache range (e.g., "1-20", "1,3,5", "all", "0")
            if (IsCacheRange(firstArg))
            {
                if (args.Length < 2)
                {
                    return (false, CommandType.Invalid, null);
                }
                
                return (true, CommandType.CacheRange, new CommandArguments
                {
                    CacheSpec = firstArg,
                    CommandName = args[1],
                    Arguments = args.Skip(2).ToArray()
                });
            }
            
            // Check for cache index (single number)
            if (int.TryParse(firstArg, out _))
            {
                if (args.Length < 2)
                {
                    return (false, CommandType.Invalid, null);
                }
                
                return (true, CommandType.CacheIndex, new CommandArguments
                {
                    CacheSpec = firstArg,
                    CommandName = args[1],
                    Arguments = args.Skip(2).ToArray()
                });
            }
            
            // Check for file command (PDF file or command name)
            if (File.Exists(firstArg) || firstArg.EndsWith(".pdf"))
            {
                if (args.Length < 2)
                {
                    return (false, CommandType.Invalid, null);
                }
                
                return (true, CommandType.FileCommand, new CommandArguments
                {
                    FilePath = firstArg,
                    CommandName = args[1],
                    Arguments = args.Skip(2).ToArray()
                });
            }
            
            // Check if it's a direct command (load, extract, cache, etc.)
            // BUT exclude analysis commands that need context
            Console.WriteLine($"🔧 DEBUG: Checking if HasCommand({firstArg}): {_registry.HasCommand(firstArg)}");
            Console.WriteLine($"🔧 DEBUG: IsAnalysisCommand({firstArg}): {IsAnalysisCommand(firstArg)}");
            
            if (_registry.HasCommand(firstArg) && !IsAnalysisCommand(firstArg))
            {
                Console.WriteLine($"🔧 DEBUG: Returning FileCommand for {firstArg}");
                return (true, CommandType.FileCommand, new CommandArguments
                {
                    CommandName = firstArg,
                    Arguments = args.Skip(1).ToArray()
                });
            }
            
            // Check if it's an analysis command without context
            if (IsAnalysisCommand(firstArg))
            {
                // Check if user wants help for this command
                var remainingArgs = args.Skip(1).ToArray();
                if (remainingArgs.Length > 0 && 
                    (remainingArgs[0] == "--help" || remainingArgs[0] == "-h"))
                {
                    return (true, CommandType.AnalysisCommandWithHelp, new CommandArguments
                    {
                        CommandName = firstArg,
                        Arguments = remainingArgs
                    });
                }
                
                return (true, CommandType.AnalysisCommandWithoutContext, new CommandArguments
                {
                    CommandName = firstArg,
                    Arguments = remainingArgs
                });
            }
            
            return (false, CommandType.Invalid, null);
        }

        private void HandleCacheRangeCommand(CommandArguments args)
        {
            _executor.ExecuteWithCacheRange(args.CacheSpec, args.CommandName, args.Arguments);
        }

        private void HandleCacheIndexCommand(CommandArguments args)
        {
            _executor.ExecuteWithCache(args.CacheSpec, args.CommandName, args.Arguments);
        }

        private void HandleFileCommand(CommandArguments args)
        {
            Console.WriteLine($"🔧 DEBUG: HandleFileCommand called. CommandName: {args.CommandName}, FilePath: {args.FilePath}");
            
            if (string.IsNullOrEmpty(args.FilePath))
            {
                // Direct command without file
                Console.WriteLine($"🔧 DEBUG: Direct command without file");
                var command = _registry.CreateCommand(args.CommandName);
                if (command != null)
                {
                    Console.WriteLine($"🔧 DEBUG: Command found, executing");
                    command.Execute(args.Arguments);
                }
                else
                {
                    Console.WriteLine($"Error: Unknown command '{args.CommandName}'");
                }
            }
            else
            {
                // Command with file
                Console.WriteLine($"🔧 DEBUG: Command with file");
                _executor.ExecuteFileCommand(args.CommandName, args.FilePath, args.Arguments);
            }
        }

        private bool IsCacheRange(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return false;
            spec = spec.ToLower();
            if (spec == "all" || spec == "0") return true;
            return spec.Contains("-") || spec.Contains(",") || spec.Contains(":");
        }

        private void InitializeLogging()
        {
            // Initialize Serilog or other logging framework
            // This is simplified for brevity
            Console.WriteLine(LanguageManager.GetMessage("services_initialized"));
            Console.WriteLine(LanguageManager.GetMessage("application_starting"));
        }

        private void ShowHelp()
        {
            Console.WriteLine("FPDF Portable CLI\n");
            Console.WriteLine("Uso geral (SQLite-only, db-path padrão: data/sqlite/sqlite-mcp.db):");
            Console.WriteLine("  fpdf load <pdf|dir|pattern> [opções]                 [--db-path <db>]");
            Console.WriteLine("  fpdf find <termos> [flags]                           [--db-path <db>] [--cache <nome>]");
            Console.WriteLine("  fpdf cache list                                      [--db-path <db>]\n");

            Console.WriteLine("Flags do find:");
            Console.WriteLine("  --text term (default) | --header term | --footer term | --docs term | --meta term | --fonts term | --objects term");
            Console.WriteLine("  --pages A-B | --min-words N | --max-words N | --type T | --limit N | -F txt|json|csv|count | --bbox");
            Console.WriteLine("  AND por espaço, OR com '|', sensível com '!'. Default busca no texto.");
            Console.WriteLine("  --cache <nome> limita a um cache específico (opcional)");
            Console.WriteLine("  --db-path <db> opcional (default: data/sqlite/sqlite-mcp.db)\n");

            Console.WriteLine("Exemplos:");
            Console.WriteLine("  fpdf load arquivos/*.pdf ultra");
            Console.WriteLine("  fpdf find assessoria magistratura certidao Robson --limit 20 -F txt");
            Console.WriteLine("  fpdf find \"nota de empenho\" --pages 1-5 -F json\n");
        }

        private void ShowCommandHelp(string commandName)
        {
            switch (commandName.ToLower())
            {
                case "find":
                    Console.WriteLine("fpdf find <cache|range|pdf|dir> [--text term] [--header term] [--footer term] [--docs term] [--meta term] [--fonts term] [--objects term] [--pages A-B] [--min-words N] [--max-words N] [--type T] [--limit N] [-F txt|json|csv|count] [--bbox]\\nAND por espaço, OR com '|', sensível com '!'. Default busca no texto.");
                    break;
                // demais comandos não exibem help detalhado aqui
                default:
                    Console.WriteLine(LanguageManager.GetMessage("no_help_available", commandName));
                    break;
            }
        }

        private void ShowVersion()
        {
            Console.WriteLine($"FilterPDF version {Version.Current}");
            Console.WriteLine($"Author: {Version.Author}");
            Console.WriteLine(Version.Copyright);
            Console.WriteLine();
            // Show the most recent release notes
            Console.WriteLine(Version.ReleaseNotes.V3390);
        }

        private bool IsAnalysisCommand(string name)
        {
            var analysisCommands = new[] { "find" };
            return analysisCommands.Contains(name.ToLower());
        }

        private enum CommandType
        {
            Invalid,
            Help,
            Version,
            CacheRange,
            CacheIndex,
            FileCommand,
            AnalysisCommandWithoutContext,
            AnalysisCommandWithHelp
        }

        private class CommandArguments
        {
            public string CacheSpec { get; set; }
            public string FilePath { get; set; }
            public string CommandName { get; set; }
            public string[] Arguments { get; set; }
        }
    }
}
