using System;
using System.IO;
using RAID_Util.Services;

namespace RAID_Util.Services;

public static class LogService
{
    private static readonly object _lock = new();

    private static string LogFilePath =>
        Path.Combine(ConfigManager.Get().LogsPath, "raid-util.log");

    // ============================================================
    //  MÉTODO PRINCIPAL (siempre escribe)
    // ============================================================
    public static void Write(string message)
    {
        try
        {
            EnsureDirectory();

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string line = $"[{timestamp}] {message}";

            lock (_lock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Nunca lanzar excepciones desde el logger
        }
    }

    // ============================================================
    //  ERROR (siempre escribe)
    // ============================================================
    public static void Error(string message)
    {
        Write($"[ERROR] {message}");
    }

    // ============================================================
    //  DEBUG (siempre escribe)
    // ============================================================
    public static void Debug(string message)
    {
        Write($"[DEBUG] {message}");
    }

    // ============================================================
    //  ASEGURAR DIRECTORIO
    // ============================================================
    private static void EnsureDirectory()
    {
        try
        {
            string logDir = ConfigManager.Get().LogsPath;

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
        }
        catch
        {
            // Silencio total
        }
    }
}