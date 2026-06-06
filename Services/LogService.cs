using System;
using System.IO;

namespace RAID_Util.Services;

public static class LogService
{
    private static readonly object _lock = new();

    private static string LogFilePath
    {
        get
        {
            var cfg = ConfigManager.Get();

            // Validar LogsPath
            var dir = cfg.LogsPath;

            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "raid-util",
                    "logs"
                );

            // Asegurar ruta absoluta
            if (!Path.IsPathRooted(dir))
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "raid-util",
                    dir
                );
            }

            return Path.Combine(dir, "raid-util.log");
        }
    }

    // ============================================================
    //  MÉTODO PRINCIPAL (siempre escribe)
    // ============================================================
    public static void Write(string message)
    {
        try
        {
            EnsureDirectory();

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var line = $"[{timestamp}] {message}";

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
            var dir = Path.GetDirectoryName(LogFilePath);

            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch
        {
            // Silencio total
        }
    }
}
