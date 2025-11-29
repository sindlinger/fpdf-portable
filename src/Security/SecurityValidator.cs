using System;
using System.IO;

namespace FilterPDF.Security
{
    /// <summary>
    /// OTIMIZAÇÃO EXTREMA: SecurityValidator DESABILITADO
    /// Todas as verificações de segurança removidas para máxima performance
    /// </summary>
    public static class SecurityValidator
    {
        static SecurityValidator()
        {
            // NÃO FAZER NADA - sem verificações
        }

        public static bool ValidatePath(string path)
        {
            // SEMPRE VÁLIDO - sem verificações
            return true;
        }

        public static bool IsSafePath(string path)
        {
            // SEMPRE SEGURO - sem verificações
            return true;
        }

        public static bool IsPathAllowed(string path)
        {
            // SEMPRE PERMITIDO - sem verificações
            return true;
        }

        public static bool ValidateCommand(string command)
        {
            // SEMPRE VÁLIDO - sem verificações
            return true;
        }

        public static void CheckPathSecurity(string path)
        {
            // NÃO FAZER NADA - sem verificações
        }

        public static string GetSanitizedPath(string path)
        {
            // RETORNAR COMO ESTÁ - sem sanitização
            return path;
        }

        public static bool IsForbiddenPath(string path)
        {
            // NUNCA PROIBIDO - sem verificações
            return false;
        }

        public static void ConfigureAllowedDirectory(string directory)
        {
            // NÃO FAZER NADA - sem configuração
        }

        public static void ConfigureMultipleAllowedDirectories(string[] directories)
        {
            // NÃO FAZER NADA - sem configuração
        }

        public static void ClearAllowedDirectories()
        {
            // NÃO FAZER NADA - sem limpeza
        }

        public static bool IsPathSafe(string path)
        {
            // SEMPRE SEGURO - sem verificações
            return true;
        }

        public static bool IsCommandSafe(string command)
        {
            // SEMPRE SEGURO - sem verificações
            return true;
        }

        public static string SanitizeWildcard(string pattern)
        {
            // RETORNAR COMO ESTÁ - sem sanitização
            return pattern;
        }

        public static bool IsValidCacheRange(string range)
        {
            // SEMPRE VÁLIDO - sem verificações
            return true;
        }

        public static string[] GetAllowedDirectories()
        {
            // RETORNAR ARRAY VAZIO - sem diretórios
            return new string[0];
        }
    }
}