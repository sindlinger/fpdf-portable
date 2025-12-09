using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace FilterPDF
{
    /// <summary>
    /// Dynamic version management system - reads version from project file
    /// </summary>
    public static class VersionManager
    {
        private static string _currentVersion = null;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the current version dynamically
        /// Priority order:
        /// 1. Assembly version (from compiled binary)
        /// 2. Project file version (from .csproj)
        /// 3. Fallback to last known version
        /// </summary>
        public static string Current
        {
            get
            {
                if (_currentVersion == null)
                {
                    lock (_lock)
                    {
                        if (_currentVersion == null)
                        {
                            _currentVersion = GetVersion();
                        }
                    }
                }
                return _currentVersion;
            }
        }

        private static string GetVersion()
        {
            // Method 1: Try to get from Assembly version (most reliable at runtime)
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                if (version != null && version.Major > 0)
                {
                    // Return only Major.Minor.Build (ignore Revision)
                    return $"{version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch
            {
                // Continue to next method
            }

            // Method 2: Try to read from .csproj file (useful during development)
            try
            {
                // Try multiple possible locations for the .csproj file
                string[] possiblePaths = 
                {
                    "fpdf.csproj",
                    "../fpdf.csproj",
                    "../../fpdf.csproj",
                    "/mnt/b/dev-2/fpdf/fpdf.csproj",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fpdf.csproj"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "fpdf.csproj"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "fpdf.csproj")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        var doc = XDocument.Load(path);
                        var versionElement = doc.Descendants("Version").FirstOrDefault();
                        if (versionElement != null && !string.IsNullOrWhiteSpace(versionElement.Value))
                        {
                            return versionElement.Value;
                        }
                    }
                }
            }
            catch
            {
                // Continue to fallback
            }

            // Method 3: Fallback to a default version
            // This will be automatically updated when we compile
            return "3.39.0";
        }

        /// <summary>
        /// Gets the full version string including patch number
        /// </summary>
        public static string FullVersion
        {
            get
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var version = assembly.GetName().Version;
                    if (version != null)
                    {
                        return version.ToString();
                    }
                }
                catch
                {
                    // Fallback
                }
                return Current + ".0";
            }
        }

        /// <summary>
        /// Force refresh the version (useful for testing)
        /// </summary>
        public static void RefreshVersion()
        {
            lock (_lock)
            {
                _currentVersion = null;
            }
        }
    }
}