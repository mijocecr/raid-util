using System;
using System.IO;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class LogService
{
    private static readonly object _lock = new();

    private static string _currentLogPath = "";
    private static int _currentLevel = 1; // 0=ERROR, 1=INFO, 2=DEBUG

    // ============================================================
    //  APLICAR CONFIG (llamado desde ConfigWindow)
    // ============================================================
    public static void ApplyConfig(AppConfig cfg)
    {
        try
        {
            // Nivel de logs
            _currentLevel = cfg.LogLevel is >= 0 and <= 2 ? cfg.LogLevel : 1;

            // Ruta del archivo
            var dir = cfg.LogsPath;

            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "raid-util",
                    "logs"
                );
            }

            if (!Path.IsPathRooted(dir))
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "raid-util",
                    dir
                );
            }

            Directory.CreateDirectory(dir);

            _currentLogPath = Path.Combine(dir, "raid-util.log");
        }
        catch
        {
            // Nunca romper el logger
        }
    }

    // ============================================================
    //  OBTENER RUTA ACTUAL DEL LOG
    // ============================================================
    private static string LogFilePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_currentLogPath))
                return _currentLogPath;

            // Fallback seguro
            var cfg = ConfigManager.Get();
            ApplyConfig(cfg);
            return _currentLogPath;
        }
    }

    // ============================================================
    //  ROTACIÓN SIMPLE (si > 5 MB)
    // ============================================================
    private static void RotateIfNeeded()
    {
        try
        {
            var file = LogFilePath;

            if (!File.Exists(file))
                return;

            var info = new FileInfo(file);

            if (info.Length < 5 * 1024 * 1024) // 5 MB
                return;

            var backup = file + ".1";

            if (File.Exists(backup))
                File.Delete(backup);

            File.Move(file, backup);
        }
        catch
        {
            // Silencio total
        }
    }

    // ============================================================
    //  MÉTODO PRINCIPAL (respeta nivel de logs)
    // ============================================================
    private static void WriteInternal(string prefix, string message, int level)
    {
        try
        {
            if (level > _currentLevel)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);

            RotateIfNeeded();

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var line = $"[{timestamp}] {prefix}{message}";

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
    //  API PÚBLICA
    // ============================================================
    public static void Error(string message)
        => WriteInternal("[ERROR] ", message, level: 0);

    public static void Info(string message)
        => WriteInternal("[INFO] ", message, level: 1);

    public static void Debug(string message)
        => WriteInternal("[DEBUG] ", message, level: 2);
}
