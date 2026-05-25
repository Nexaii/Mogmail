using System;
using System.IO;

namespace Mogmail.Services;

public static class MogLog
{
    private static readonly object FileLock = new();
    private static StreamWriter? _writer;
    private static string? _writerPath;

    public static void Information(string message)
    {
        Plugin.Log.Information(message);
        WriteFile("INF", message);
    }

    public static void Warning(string message)
    {
        Plugin.Log.Warning(message);
        WriteFile("WRN", message);
    }

    public static void Error(string message)
    {
        Plugin.Log.Error(message);
        WriteFile("ERR", message);
    }

    public static void Shutdown()
    {
        lock (FileLock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            _writerPath = null;
        }
    }

    private static void WriteFile(string level, string message)
    {
        if (!Plugin.Config.EnableExternalLogFile) return;
        var path = Plugin.Config.ExternalLogFilePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (FileLock)
        {
            try
            {
                EnsureWriter(path);
                _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | {level} | {message}");
                _writer?.Flush();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[Mogmail] external log write failed: {ex.Message}. Disabling.");
                Plugin.Config.EnableExternalLogFile = false;
                Plugin.Config.Save();
                _writer?.Dispose();
                _writer = null;
                _writerPath = null;
            }
        }
    }

    private static void EnsureWriter(string path)
    {
        if (_writer != null && _writerPath == path) return;

        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _writerPath = null;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read));
        _writerPath = path;
    }
}
