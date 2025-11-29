using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FilterPDF.Interfaces;

namespace FilterPDF.Services
{
    /// <summary>
    /// Implementation of console output service
    /// Provides abstraction for console operations to enable testing and flexibility
    /// </summary>
    public class OutputService : IOutputService
    {
        /// <summary>
        /// Write a line to the console
        /// </summary>
        public void WriteLine(string message = "")
        {
            Console.WriteLine(message);
        }

        /// <summary>
        /// Write text to the console without a newline
        /// </summary>
        public void Write(string message)
        {
            Console.Write(message);
        }

        /// <summary>
        /// Write an error message to stderr
        /// </summary>
        public void WriteError(string message)
        {
            Console.Error.WriteLine(message);
        }

        /// <summary>
        /// Write output to a file
        /// </summary>
        public async Task WriteToFileAsync(string filePath, string content)
        {
            await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false));
        }

        /// <summary>
        /// Flush output streams
        /// </summary>
        public void Flush()
        {
            Console.Out.Flush();
            Console.Error.Flush();
        }

        /// <summary>
        /// Redirect console output to capture for processing
        /// Returns a disposable that restores original output when disposed
        /// </summary>
        public IDisposable RedirectOutput(out StringWriter outputCapture)
        {
            var originalOut = Console.Out;
            outputCapture = new StringWriter();
            Console.SetOut(outputCapture);
            
            return new OutputRedirection(originalOut);
        }

        /// <summary>
        /// Helper class to restore console output
        /// </summary>
        private class OutputRedirection : IDisposable
        {
            private readonly TextWriter _originalOut;

            public OutputRedirection(TextWriter originalOut)
            {
                _originalOut = originalOut;
            }

            public void Dispose()
            {
                Console.SetOut(_originalOut);
            }
        }
    }
}