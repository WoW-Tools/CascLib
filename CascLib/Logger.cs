using System;
using System.IO;

namespace CASCLib
{
    public interface ILoggerOptions
    {
        string LogFileName { get; }
    }

    public class LoggerOptionsDefault : ILoggerOptions
    {
        public string LogFileName => "debug.log";
    }

    public class LoggerOptionsFileNameWithDate : ILoggerOptions
    {
        public string LogFileName => $"debug-{DateTime.Now:yyyyMMdd-HHmmss}.log";
    }

    public class Logger
    {
        private static FileStream fs;
        private static StreamWriter logger;

        public static void Init(ILoggerOptions opts = null)
        {
            opts = opts ?? new LoggerOptionsDefault();
            fs = new FileStream(opts.LogFileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            logger = new StreamWriter(fs) { AutoFlush = true };
        }

        public static void WriteLine(string format, params object[] args)
        {
            if (fs == null || logger == null)
                throw new InvalidOperationException("Logger isn't initialized!");

            logger.Write("[{0}]: ", DateTime.Now);
            logger.WriteLine(format, args);
        }
    }
}
