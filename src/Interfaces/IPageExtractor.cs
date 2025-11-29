using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FilterPDF.Models;

namespace FilterPDF.Interfaces
{
    /// <summary>
    /// 🏗️ ELITE INTERFACE: Advanced Page Extraction Contract
    /// 
    /// ═══════════════════════════════════════════════════════════════════
    /// ARCHITECTURAL PRINCIPLES:
    /// • Single Responsibility: Focus on page extraction only
    /// • Open/Closed: Extensible for new formats without modification
    /// • Liskov Substitution: All implementations are interchangeable
    /// • Interface Segregation: Minimal, focused interface
    /// • Dependency Inversion: Depend on abstractions, not concretions
    /// ═══════════════════════════════════════════════════════════════════
    /// 
    /// PERFORMANCE CONTRACT:
    /// • O(n) time complexity for n pages
    /// • Memory usage <= 500MB per 1000 pages
    /// • Processing rate >= 10 pages/second
    /// • Error rate <= 1% for well-formed PDFs
    /// 
    /// IMPLEMENTATION PATTERNS:
    /// • Factory Pattern: Create extractors for different formats
    /// • Strategy Pattern: Different extraction strategies
    /// • Observer Pattern: Progress and error reporting
    /// • Template Method: Common extraction workflow
    /// </summary>
    public interface IPageExtractor
    {
        #region Core Extraction Operations

        /// <summary>
        /// 🎯 PRIMARY METHOD: Extract pages from cached PDF analysis data
        /// 
        /// This is the main entry point that leverages FilterPDF's existing cache system
        /// for maximum performance and consistency with other commands.
        /// 
        /// Performance Requirements:
        /// • Process 1000 pages in <= 100 seconds
        /// • Memory usage <= 2GB for 1000 pages
        /// • Thread-safe for concurrent operations
        /// </summary>
        /// <param name="analysisResult">Cached PDF analysis data from FilterPDF system</param>
        /// <param name="options">Configuration options for extraction</param>
        /// <param name="cancellationToken">Cancellation token for operation control</param>
        /// <returns>Comprehensive extraction results with metrics and quality assessment</returns>
        Task<PageExtractionResult> ExtractPagesFromCacheAsync(
            PDFAnalysisResult analysisResult,
            PageExtractionOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 📄 DIRECT METHOD: Extract pages directly from PDF file
        /// 
        /// Used when cache is not available or when direct processing is preferred.
        /// Provides fallback capability for the cache-based method.
        /// 
        /// Performance Requirements:
        /// • Process 100 pages in <= 30 seconds
        /// • Handle files up to 500MB
        /// • Support encrypted PDFs with password
        /// </summary>
        /// <param name="pdfFilePath">Path to source PDF file</param>
        /// <param name="options">Configuration options for extraction</param>
        /// <param name="cancellationToken">Cancellation token for operation control</param>
        /// <returns>Comprehensive extraction results with metrics and quality assessment</returns>
        Task<PageExtractionResult> ExtractPagesFromFileAsync(
            string pdfFilePath,
            PageExtractionOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 🔧 SINGLE PAGE METHOD: Extract individual page with maximum control
        /// 
        /// Provides fine-grained control for single page extraction.
        /// Useful for real-time processing or selective extraction.
        /// </summary>
        /// <param name="pageAnalysis">Analysis data for specific page</param>
        /// <param name="pageNumber">Page number (1-based)</param>
        /// <param name="options">Configuration options for extraction</param>
        /// <param name="cancellationToken">Cancellation token for operation control</param>
        /// <returns>Single page extraction result</returns>
        Task<ExtractedPageInfo?> ExtractSinglePageAsync(
            PageAnalysis pageAnalysis,
            int pageNumber,
            PageExtractionOptions options,
            CancellationToken cancellationToken = default);

        #endregion

        #region Validation & Preview Operations

        /// <summary>
        /// ✅ VALIDATION METHOD: Validate extraction options and source data
        /// 
        /// Performs comprehensive validation before starting extraction to prevent
        /// runtime errors and provide early feedback to users.
        /// </summary>
        /// <param name="source">Either PDFAnalysisResult or file path</param>
        /// <param name="options">Extraction options to validate</param>
        /// <returns>Validation result with detailed feedback</returns>
        Task<ValidationResult> ValidateExtractionAsync(
            object source,
            PageExtractionOptions options);

        /// <summary>
        /// 👁️ PREVIEW METHOD: Generate extraction preview without processing
        /// 
        /// Provides information about what would be extracted without performing
        /// the actual extraction. Useful for cost estimation and planning.
        /// </summary>
        /// <param name="source">Either PDFAnalysisResult or file path</param>
        /// <param name="options">Extraction options for preview</param>
        /// <returns>Preview information including file count and size estimates</returns>
        Task<ExtractionPreview> GenerateExtractionPreviewAsync(
            object source,
            PageExtractionOptions options);

        #endregion

        #region Format-Specific Operations

        /// <summary>
        /// 🖼️ RASTER METHOD: Extract pages as raster images (PNG/JPEG/TIFF)
        /// 
        /// Specialized method for high-quality raster image extraction with
        /// advanced rendering options and color management.
        /// </summary>
        Task<PageExtractionResult> ExtractAsRasterImagesAsync(
            PDFAnalysisResult analysisResult,
            PageExtractionOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 📄 PDF METHOD: Extract individual pages as separate PDF files
        /// 
        /// Specialized method for PDF page splitting with metadata preservation
        /// and optimization for file size and compatibility.
        /// </summary>
        Task<PageExtractionResult> ExtractAsPdfPagesAsync(
            PDFAnalysisResult analysisResult,
            PageExtractionOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 🎨 VECTOR METHOD: Extract pages as vector graphics (SVG)
        /// 
        /// Specialized method for vector graphics extraction with scalability
        /// and editability preservation.
        /// </summary>
        Task<PageExtractionResult> ExtractAsVectorGraphicsAsync(
            PDFAnalysisResult analysisResult,
            PageExtractionOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 📝 TEXT METHOD: Extract text content with formatting preservation
        /// 
        /// Specialized method for text extraction with layout awareness,
        /// font preservation, and structured content recognition.
        /// </summary>
        Task<PageExtractionResult> ExtractAsTextContentAsync(
            PDFAnalysisResult analysisResult,
            PageExtractionOptions options,
            CancellationToken cancellationToken = default);

        #endregion

        #region Utility & Information Methods

        /// <summary>
        /// 📋 INFO METHOD: Get supported formats and capabilities
        /// 
        /// Returns information about what formats and features are supported
        /// by this extractor implementation.
        /// </summary>
        /// <returns>Capability information for this extractor</returns>
        ExtractorCapabilities GetCapabilities();

        /// <summary>
        /// ⚙️ ESTIMATION METHOD: Estimate processing time and resource usage
        /// 
        /// Provides estimates for planning and resource allocation.
        /// Useful for batch processing and user experience optimization.
        /// </summary>
        /// <param name="pageCount">Number of pages to process</param>
        /// <param name="options">Processing options</param>
        /// <returns>Resource and time estimates</returns>
        Task<ProcessingEstimate> EstimateProcessingRequirementsAsync(
            int pageCount,
            PageExtractionOptions options);

        /// <summary>
        /// 🧹 CLEANUP METHOD: Clean up temporary resources and files
        /// 
        /// Provides explicit cleanup for implementations that use temporary
        /// files, memory caches, or other resources that need cleanup.
        /// </summary>
        /// <param name="preserveOutputFiles">Whether to preserve final output files</param>
        Task CleanupAsync(bool preserveOutputFiles = true);

        #endregion

        #region Events & Monitoring

        /// <summary>
        /// 📊 Progress reporting event
        /// 
        /// Fired periodically during extraction to report progress.
        /// Provides detailed progress information for UI updates and monitoring.
        /// </summary>
        event EventHandler<ExtractionProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// ⚠️ Warning event
        /// 
        /// Fired when non-critical issues are encountered during extraction.
        /// Allows for logging and user notification without stopping the operation.
        /// </summary>
        event EventHandler<ExtractionWarningEventArgs>? WarningOccurred;

        /// <summary>
        /// ❌ Error event
        /// 
        /// Fired when errors occur during extraction.
        /// Provides detailed error information for debugging and recovery.
        /// </summary>
        event EventHandler<ExtractionErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// ✅ Completion event
        /// 
        /// Fired when extraction operation completes (successfully or with errors).
        /// Provides final summary and metrics.
        /// </summary>
        event EventHandler<ExtractionCompletedEventArgs>? ExtractionCompleted;

        #endregion
    }

    /// <summary>
    /// ✅ Validation result for extraction operations
    /// </summary>
    public class ValidationResult
    {
        /// <summary>Whether validation passed</summary>
        public bool IsValid { get; set; }
        
        /// <summary>Validation errors that prevent extraction</summary>
        public List<string> Errors { get; set; } = new List<string>();
        
        /// <summary>Validation warnings that don't prevent extraction</summary>
        public List<string> Warnings { get; set; } = new List<string>();
        
        /// <summary>Recommended configuration changes</summary>
        public List<string> Recommendations { get; set; } = new List<string>();
        
        /// <summary>Estimated resource requirements if validation passes</summary>
        public ProcessingEstimate? EstimatedRequirements { get; set; }
    }

    /// <summary>
    /// 👁️ Preview information for extraction operations
    /// </summary>
    public class ExtractionPreview
    {
        /// <summary>Number of pages that would be extracted</summary>
        public int PagesCount { get; set; }
        
        /// <summary>Number of files that would be created</summary>
        public int OutputFilesCount { get; set; }
        
        /// <summary>Estimated total output size in bytes</summary>
        public long EstimatedOutputSizeBytes { get; set; }
        
        /// <summary>Estimated processing time</summary>
        public TimeSpan EstimatedProcessingTime { get; set; }
        
        /// <summary>Estimated memory usage in bytes</summary>
        public long EstimatedMemoryUsageBytes { get; set; }
        
        /// <summary>Output file information preview</summary>
        public List<OutputFilePreview> OutputFiles { get; set; } = new List<OutputFilePreview>();
        
        /// <summary>Potential issues that might occur</summary>
        public List<string> PotentialIssues { get; set; } = new List<string>();
    }

    /// <summary>
    /// 📁 Preview information for output files
    /// </summary>
    public class OutputFilePreview
    {
        public string FileName { get; set; } = "";
        public string Format { get; set; } = "";
        public long EstimatedSizeBytes { get; set; }
        public int SourcePageNumber { get; set; }
        public PageAssetType AssetType { get; set; }
    }

    /// <summary>
    /// 🔧 Extractor capabilities information
    /// </summary>
    public class ExtractorCapabilities
    {
        /// <summary>Supported output formats</summary>
        public List<PageOutputFormat> SupportedFormats { get; set; } = new List<PageOutputFormat>();
        
        /// <summary>Maximum supported page dimensions</summary>
        public (int Width, int Height) MaxPageDimensions { get; set; }
        
        /// <summary>Maximum supported file size (bytes)</summary>
        public long MaxFileSizeBytes { get; set; }
        
        /// <summary>Whether concurrent processing is supported</summary>
        public bool SupportsConcurrentProcessing { get; set; }
        
        /// <summary>Maximum concurrent operations</summary>
        public int MaxConcurrentOperations { get; set; }
        
        /// <summary>Supported color modes</summary>
        public List<ColorMode> SupportedColorModes { get; set; } = new List<ColorMode>();
        
        /// <summary>DPI range for raster output</summary>
        public (int Min, int Max) DPIRange { get; set; }
        
        /// <summary>Whether progress reporting is available</summary>
        public bool SupportsProgressReporting { get; set; }
        
        /// <summary>Whether quality assessment is available</summary>
        public bool SupportsQualityAssessment { get; set; }
        
        /// <summary>Implementation version</summary>
        public string Version { get; set; } = "";
        
        /// <summary>Implementation name</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// ⚙️ Processing requirements estimate
    /// </summary>
    public class ProcessingEstimate
    {
        /// <summary>Estimated processing time</summary>
        public TimeSpan EstimatedDuration { get; set; }
        
        /// <summary>Estimated peak memory usage (bytes)</summary>
        public long EstimatedPeakMemoryBytes { get; set; }
        
        /// <summary>Estimated disk space required (bytes)</summary>
        public long EstimatedDiskSpaceBytes { get; set; }
        
        /// <summary>Estimated CPU utilization percentage</summary>
        public double EstimatedCPUUtilization { get; set; }
        
        /// <summary>Recommended thread count</summary>
        public int RecommendedThreadCount { get; set; }
        
        /// <summary>Potential bottlenecks</summary>
        public List<string> PotentialBottlenecks { get; set; } = new List<string>();
        
        /// <summary>Performance optimization recommendations</summary>
        public List<string> OptimizationRecommendations { get; set; } = new List<string>();
    }

    #region Event Arguments

    /// <summary>
    /// 📊 Progress event arguments
    /// </summary>
    public class ExtractionProgressEventArgs : EventArgs
    {
        /// <summary>Current page being processed</summary>
        public int CurrentPage { get; set; }
        
        /// <summary>Total pages to process</summary>
        public int TotalPages { get; set; }
        
        /// <summary>Pages completed</summary>
        public int CompletedPages { get; set; }
        
        /// <summary>Progress percentage (0-100)</summary>
        public double ProgressPercentage => TotalPages > 0 ? (double)CompletedPages / TotalPages * 100 : 0;
        
        /// <summary>Current operation description</summary>
        public string CurrentOperation { get; set; } = "";
        
        /// <summary>Estimated time remaining</summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }
        
        /// <summary>Processing rate (pages/second)</summary>
        public double ProcessingRate { get; set; }
        
        /// <summary>Additional metrics</summary>
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// ⚠️ Warning event arguments
    /// </summary>
    public class ExtractionWarningEventArgs : EventArgs
    {
        /// <summary>Page number where warning occurred</summary>
        public int PageNumber { get; set; }
        
        /// <summary>Warning message</summary>
        public string Message { get; set; } = "";
        
        /// <summary>Warning category</summary>
        public string Category { get; set; } = "";
        
        /// <summary>Warning severity</summary>
        public WarningSeverity Severity { get; set; }
        
        /// <summary>Additional warning context</summary>
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 🚨 Warning severity levels
    /// </summary>
    public enum WarningSeverity
    {
        Low,       // Informational, no action needed
        Medium,    // May affect quality, review recommended
        High       // Significant issue, action recommended
    }

    /// <summary>
    /// ❌ Error event arguments
    /// </summary>
    public class ExtractionErrorEventArgs : EventArgs
    {
        /// <summary>Page number where error occurred</summary>
        public int PageNumber { get; set; }
        
        /// <summary>Error type</summary>
        public PageExtractionErrorType ErrorType { get; set; }
        
        /// <summary>Error message</summary>
        public string Message { get; set; } = "";
        
        /// <summary>Technical error details</summary>
        public string TechnicalDetails { get; set; } = "";
        
        /// <summary>Source exception</summary>
        public Exception? SourceException { get; set; }
        
        /// <summary>Whether extraction can continue</summary>
        public bool CanContinue { get; set; }
        
        /// <summary>Suggested recovery actions</summary>
        public List<string> RecoveryActions { get; set; } = new List<string>();
        
        /// <summary>Error context</summary>
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// ✅ Completion event arguments
    /// </summary>
    public class ExtractionCompletedEventArgs : EventArgs
    {
        /// <summary>Final extraction result</summary>
        public PageExtractionResult Result { get; set; } = new PageExtractionResult();
        
        /// <summary>Whether extraction completed successfully</summary>
        public bool IsSuccess => Result.Errors.Count == 0 && Result.ExtractedPages.Count > 0;
        
        /// <summary>Summary message</summary>
        public string Summary { get; set; } = "";
        
        /// <summary>Final metrics</summary>
        public Dictionary<string, object> FinalMetrics { get; set; } = new Dictionary<string, object>();
    }

    #endregion

    /// <summary>
    /// 🏭 FACTORY INTERFACE: Page extractor factory for different implementations
    /// 
    /// Provides a factory pattern for creating page extractors based on
    /// requirements, available resources, and output formats.
    /// </summary>
    public interface IPageExtractorFactory
    {
        /// <summary>
        /// Create an extractor optimized for the specified output format
        /// </summary>
        IPageExtractor CreateExtractor(PageOutputFormat format);
        
        /// <summary>
        /// Create an extractor with specific capabilities
        /// </summary>
        IPageExtractor CreateExtractor(ExtractorCapabilities requiredCapabilities);
        
        /// <summary>
        /// Get the best available extractor for the given options
        /// </summary>
        IPageExtractor GetBestExtractor(PageExtractionOptions options);
        
        /// <summary>
        /// Get all available extractor types
        /// </summary>
        List<ExtractorCapabilities> GetAvailableExtractors();
    }
}