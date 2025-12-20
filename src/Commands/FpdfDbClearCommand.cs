using System;
using Npgsql;
using FilterPDF.Utils;

namespace FilterPDF.Commands
{
    /// <summary>
    /// Clear Postgres tables (raw_processes, raw_files, processes).
    /// Usage: fpdf db-clear <password>
    /// </summary>
    public class FpdfDbClearCommand : Command
    {
        public override string Name => "db-clear";
        public override string Description => "Clear Postgres tables (raw_processes/raw_files/processes)";

        public override void ShowHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  fpdf db-clear <password>");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("  fpdf db-clear fpdf");
        }

        public override void Execute(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return;
            }

            var password = args[0];
            if (!string.Equals(password, "fpdf", StringComparison.Ordinal))
            {
                Console.WriteLine("Senha incorreta.");
                return;
            }

            var pgUri = $"postgres://fpdf:{password}@localhost:5432/fpdf";
            try
            {
                var connString = PgDocStore.NormalizePgUri(pgUri);
                using var conn = new NpgsqlConnection(connString);
                conn.Open();

                TruncateIfExists(conn, "raw_processes");
                TruncateIfExists(conn, "raw_files");
                TruncateIfExists(conn, "processes");
                TruncateIfExists(conn, "documents");

                Console.WriteLine("Postgres limpo: raw_processes, raw_files, processes (e documents se existir).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao limpar Postgres: {ex.Message}");
            }
        }

        private static void TruncateIfExists(NpgsqlConnection conn, string table)
        {
            using var cmd = new NpgsqlCommand($"TRUNCATE TABLE {table} RESTART IDENTITY;", conn);
            cmd.CommandTimeout = 120;
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (PostgresException ex) when (ex.SqlState == "0A000")
            {
                // FK constraint, retry with CASCADE
                using var cmdCascade = new NpgsqlCommand($"TRUNCATE TABLE {table} RESTART IDENTITY CASCADE;", conn);
                cmdCascade.CommandTimeout = 120;
                cmdCascade.ExecuteNonQuery();
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                // undefined_table: ignore
            }
        }
    }
}
