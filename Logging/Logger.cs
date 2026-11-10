using System;
using System.IO;
using System.Text;

namespace RockSnifferLib.Logging
{
    /// <summary>
    /// Static booleans to enable/disable parts of log output
    /// </summary>
    public static class Logger
    {
        /// <summary>
        /// Log cache related information
        /// </summary>
        public static bool logCache = false;

        /// <summary>
        /// Log memory readout results
        /// </summary>
        public static bool logMemoryReadout = false;

        /// <summary>
        /// Log song detail sniffing results
        /// </summary>
        public static bool logSongDetails = false;

        /// <summary>
        /// Log system file handle query related details
        /// </summary>
        public static bool logSystemHandleQuery = false;

        /// <summary>
        /// Log file handle detail query information
        /// </summary>
        public static bool logFileDetailQuery = false;

        /// <summary>
        /// Log state changes
        /// </summary>
        public static bool logStateMachine;

        /// <summary>
        /// Log psarc file processing queue status
        /// </summary>
        public static bool logProcessingQueue;

        /// <summary>
        /// Logs an error into the console, in red text
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="p"></param>
        public static void LogError(string pattern, params object[] p)
        {
            pattern = "[" + DateTime.Now + "] " + pattern;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(pattern, p);
            Console.ResetColor();

            WriteToFile("sniffer.log", string.Format(pattern, p) + "\r\n");
        }

        /// <summary>
        /// Logs an exception with message and stacktrace
        /// </summary>
        /// <param name="e"></param>
        public static void LogException(Exception e)
        {
            string pattern = $"{e.Message}\n{e.StackTrace}";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(pattern);
            Console.ResetColor();

            WriteToFile("sniffer.log", pattern + "\r\n");
        }

        /// <summary>
        /// Logs a message into the console
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="p"></param>
        public static void Log(string pattern, params object[] p)
        {
            pattern = "[" + DateTime.Now + "] " + pattern;

            Console.WriteLine(pattern, p);

            WriteToFile("sniffer.log", string.Format(pattern, p) + "\r\n");
        }

        private static void WriteToFile(string path, string text)
        {
            try
            {
                using (var fstream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);

                    fstream.Write(bytes, 0, bytes.Length);
                }
            }
            catch
            {

            }
        }
    }
}
