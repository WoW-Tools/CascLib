using System;
using System.IO;

namespace CASCLib
{
    public interface ILoggerOptions
    {
        string LogFileName { get; }
        bool TimeStamp { get; }
    }

    public class LoggerOptionsDefault : ILoggerOptions
    {
        public string LogFileName => "debug.log";
        public bool TimeStamp => true;
    }

    public class LoggerOptionsFileNameWithDate : ILoggerOptions
    {
        public string LogFileName => $"debug-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        public bool TimeStamp => true;
    }

    public class Logger
    {
        private static FileStream fs;
        private static StreamWriter logger;
        private static ILoggerOptions options;

        public static void Init(ILoggerOptions opts = null)
        {
            options = opts ?? new LoggerOptionsDefault();
            fs = new FileStream(options.LogFileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            logger = new StreamWriter(fs) { AutoFlush = true };
        }

        public static void WriteLine(string format, params object[] args)
        {
            if (options == null || fs == null || logger == null)
                throw new InvalidOperationException("Logger isn't initialized!");

            if (options.TimeStamp)
                logger.Write($"[{DateTime.Now}]: ");
            logger.WriteLine(format, args);
        }
    }
}
