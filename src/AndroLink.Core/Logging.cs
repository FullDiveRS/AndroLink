using System.Globalization;
using System.Text;

namespace AndroLink.Core;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
}

/// <summary>
/// Lightweight thread-safe file+event logger. The Manager UI subscribes to
/// <see cref="Emitted"/> to show live diagnostics; everything is also mirrored
/// to %LOCALAPPDATA%\AndroLink\logs.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static string? _logFilePath;

    /// <summary>Raised on the emitting thread: (level, source, message).</summary>
    public static event Action<LogLevel, string, string>? Emitted;

    public static string? LogFilePath => _logFilePath;

    public static void SetLogFile(string directory, string baseName = "androlink")
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                _logFilePath = Path.Combine(directory, $"{baseName}-{stamp}.log");
                _writer?.Dispose();
                _writer = new StreamWriter(_logFilePath, append: false, Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[androlink] could not open log file: {ex.Message}");
            }
        }
    }

    public static void Debug(string source, string message) => Write(LogLevel.Debug, source, message);
    public static void Info(string source, string message) => Write(LogLevel.Info, source, message);
    public static void Warn(string source, string message) => Write(LogLevel.Warn, source, message);
    public static void Error(string source, string message) => Write(LogLevel.Error, source, message);
    public static void Error(string source, Exception ex) => Write(LogLevel.Error, source, ex.ToString());

    public static void Write(LogLevel level, string source, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] [{source}] {message}";
        lock (Gate)
        {
            Console.WriteLine(line);
            try { _writer?.WriteLine(line); } catch { /* logging must never throw */ }
        }
        Emitted?.Invoke(level, source, message);
    }

    public static void Flush()
    {
        lock (Gate)
        {
            try { _writer?.Flush(); } catch { }
        }
    }
}
