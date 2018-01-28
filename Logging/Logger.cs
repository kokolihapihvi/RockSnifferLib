using System;

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
        /// Log HIRC memory scan related information
        /// </summary>
        public static bool logHIRCScan = false;

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
        /// Logs an error into the console, in red text
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="p"></param>
        internal static void LogError(string pattern, params object[] p)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(pattern, p);
            Console.ResetColor();
        }

        /// <summary>
        /// Logs a message into the console
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="p"></param>
        internal static void Log(string pattern, params object[] p)
        {
            Console.WriteLine(pattern, p);
        }
    }
}
