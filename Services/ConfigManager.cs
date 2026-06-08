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

    private static AppConfig _config = new();

    // ============================================================
    // LOAD
    // ============================================================
    public static AppConfig Load()
    {
        try
        {
            EnsureConfigDirectory();

            if (!File.Exists(ConfigPath))
            {
                NormalizeConfig(_config);
                Save(_config);
                return _config;
            }

            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json);

            if (cfg is null)
            {
                BackupCorruptConfig();
                NormalizeConfig(_config);
                Save(_config);
                return _config;
            }

            NormalizeConfig(cfg);
            _config = cfg;

            EnsureLogsDirectory();
            return _config;
        }
        catch
        {
            BackupCorruptConfig();
            NormalizeConfig(_config);
            Save(_config);
            return _config;
        }
    }

    // ============================================================
    // SAVE
    // ============================================================
    public static void Save(AppConfig config)
    {
        try
        {
            EnsureConfigDirectory();
            NormalizeConfig(config);

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
    // NORMALIZAR CONFIG
    // ============================================================
    private static void NormalizeConfig(AppConfig cfg)
    {
        // Ruta de logs
        if (string.IsNullOrWhiteSpace(cfg.LogsPath))
            cfg.LogsPath = Path.Combine(ConfigDir, "logs");

        if (!Path.IsPathRooted(cfg.LogsPath))
            cfg.LogsPath = Path.Combine(ConfigDir, cfg.LogsPath);

        // Nivel de logs
        if (cfg.LogLevel < 0 || cfg.LogLevel > 2)
            cfg.LogLevel = 1;
    }

    // ============================================================
    // GET
    // ============================================================
    public static AppConfig Get() => _config;

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
    // BACKUP
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
