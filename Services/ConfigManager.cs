using System;
using System.IO;
using System.Text.Json;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class ConfigManager
{
    private static readonly string ConfigDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "raid-util"
        );

    private static readonly string ConfigPath =
        Path.Combine(ConfigDir, "config.json");

    // Instancia interna real
    private static AppConfig _config = new();

    // ============================================================
    // LOAD → devuelve AppConfig
    // ============================================================
    public static AppConfig Load()
    {
        try
        {
            EnsureConfigDirectory();

            if (!File.Exists(ConfigPath))
            {
                Save(_config);
                return _config;
            }

            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json);

            if (cfg is null)
            {
                BackupCorruptConfig();
                Save(_config);
                return _config;
            }

            // Validar campos críticos
            cfg.LogsPath ??= Path.Combine(ConfigDir, "logs");
            cfg.GeneralRefreshMs = Math.Clamp(cfg.GeneralRefreshMs, 500, 60000);

            _config = cfg;

            EnsureLogsDirectory();
            return _config;
        }
        catch
        {
            BackupCorruptConfig();
            Save(_config);
            return _config;
        }
    }

    // ============================================================
    // SAVE → guarda AppConfig
    // ============================================================
    public static void Save(AppConfig config)
    {
        try
        {
            EnsureConfigDirectory();

            // Validar campos antes de guardar
            config.LogsPath ??= Path.Combine(ConfigDir, "logs");
            config.GeneralRefreshMs = Math.Clamp(config.GeneralRefreshMs, 500, 60000);

            _config = config;

            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(ConfigPath, json);

            EnsureLogsDirectory();
        }
        catch
        {
            // silencio
        }
    }

    // ============================================================
    // GET → obtener instancia actual
    // ============================================================
    public static AppConfig Get()
    {
        return _config;
    }

    // ============================================================
    // DIRECTORIOS
    // ============================================================
    private static void EnsureConfigDirectory()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);
        }
        catch { }
    }

    private static void EnsureLogsDirectory()
    {
        try
        {
            if (!Directory.Exists(_config.LogsPath))
                Directory.CreateDirectory(_config.LogsPath);
        }
        catch { }
    }

    // ============================================================
    // BACKUP DE CONFIGURACIÓN CORRUPTA
    // ============================================================
    private static void BackupCorruptConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var backup = ConfigPath + ".corrupt-" + DateTime.Now.Ticks;
                File.Copy(ConfigPath, backup, overwrite: true);
            }
        }
        catch { }
    }
}
