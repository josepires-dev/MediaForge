using System.Text.Json;

namespace MediaForge.Services;

public static class ErrorLogger
{
    private static readonly object SyncLock = new();

    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MediaForge",
        "logs",
        "error.log");

    public static void Log(Exception exception, string context, string? customLogPath = null)
    {
        try
        {
            var logPath = customLogPath ?? DefaultLogPath;
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var record = new
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Context = context,
                ExceptionType = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = exception.StackTrace
            };

            var line = JsonSerializer.Serialize(record) + Environment.NewLine;

            lock (SyncLock)
            {
                File.AppendAllText(logPath, line);
            }
        }
        catch
        {
            // Failsafe: logging failure must never crash the host application
        }
    }
}
