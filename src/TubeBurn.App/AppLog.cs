using System.Text;

namespace TubeBurn.App;

internal static class AppLog
{
    private static readonly object Sync = new();
    private static string? _logFilePath;

    public static string Initialize()
    {
        if (!string.IsNullOrWhiteSpace(_logFilePath))
        {
            return _logFilePath;
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubeBurn",
            "logs");
        Directory.CreateDirectory(root);

        _logFilePath = Path.Combine(root, $"app-{DateTime.Now:yyyyMMdd}.log");
        Write("INFO", "Logger initialized.");
        return _logFilePath;
    }

    public static string GetLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubeBurn",
            "logs");

    public static string GetCurrentLogFilePath() =>
        _logFilePath ?? Path.Combine(GetLogDirectory(), $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            Write("ERROR", message);
            return;
        }

        var composed = new StringBuilder()
            .AppendLine(message)
            .AppendLine(ex.ToString())
            .ToString()
            .TrimEnd();

        Write("ERROR", composed);
    }

    private static void Write(string level, string message)
    {
        var path = _logFilePath ?? Initialize();
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

        lock (Sync)
        {
            File.AppendAllText(path, line);
        }
    }
}
