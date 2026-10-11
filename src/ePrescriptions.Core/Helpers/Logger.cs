using System;
using System.IO;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// مسجل سجلات الأخطاء في ملف نصي داخل مسار ProgramData\ePrescriptions\logs
    /// </summary>
    public static class Logger
    {
        private static readonly object LockObj = new();

        public static string LogDirectory => AppPaths.LogsDirectory;

        public static void LogException(Exception ex, string? context = null)
        {
            try
            {
                lock (LockObj)
                {
                    if (!Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                    }

                    string fileName = $"error_{DateTime.Now:yyyy-MM-dd}.log";
                    string filePath = Path.Combine(LogDirectory, fileName);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string message = $"[{timestamp}] [{context ?? "General"}] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";

                    if (ex.InnerException != null)
                    {
                        message += $" --> InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}{Environment.NewLine}";
                    }

                    message += new string('-', 80) + Environment.NewLine;
                    File.AppendAllText(filePath, message);
                }
            }
            catch
            {
                // منع التوقف إذا فشلت كتابة السجل
            }
        }

        public static void LogInfo(string message, string? context = null)
        {
            try
            {
                lock (LockObj)
                {
                    if (!Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                    }

                    string fileName = $"info_{DateTime.Now:yyyy-MM-dd}.log";
                    string filePath = Path.Combine(LogDirectory, fileName);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logLine = $"[{timestamp}] [{context ?? "Info"}] {message}{Environment.NewLine}";
                    File.AppendAllText(filePath, logLine);
                }
            }
            catch
            {
                // منع التوقف
            }
        }
    }
}
