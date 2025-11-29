using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using FilterPDF.Interfaces;
using FilterPDF.Services;

namespace FilterPDF
{
    /// <summary>
    /// Service configuration for dependency injection
    /// Configures and registers all services used by the application
    /// </summary>
    public static class ServiceConfiguration
    {
        /// <summary>
        /// Configure services for dependency injection
        /// </summary>
        /// <returns>Configured service provider</returns>
        public static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("logs/fpdf-.txt", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            // Add logging
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog();
            });

            // Register application services
            services.AddSingleton<IOutputService, OutputService>();
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<ICommandRegistry, CommandRegistry>();
            services.AddSingleton<ICacheService, CacheService>();

            // Build and return service provider
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Initialize services with required setup
        /// </summary>
        /// <param name="serviceProvider">Service provider to use</param>
        public static void InitializeServices(ServiceProvider serviceProvider)
        {
            // Initialize command registry
            var commandRegistry = serviceProvider.GetRequiredService<ICommandRegistry>();
            commandRegistry.InitializeCommands();

            // Ensure cache directory exists
            var cacheService = serviceProvider.GetRequiredService<ICacheService>();
            var fileSystem = serviceProvider.GetRequiredService<IFileSystemService>();
            var cacheDirectory = cacheService.GetCacheDirectory();
            fileSystem.EnsureDirectoryExists(cacheDirectory);

            // Log initialization
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("FilterPDF services initialized successfully");
        }

        /// <summary>
        /// Cleanup resources on shutdown
        /// </summary>
        public static void Cleanup()
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Program class for logger category
    /// </summary>
    public class Program
    {
        // This class is used only for logger categorization
    }
}