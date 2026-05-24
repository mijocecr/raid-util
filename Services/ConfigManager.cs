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
    private static AppConfig _config = new AppConfig();

    // ============================================================
    // LOAD → devuelve AppConfig
    // ============================================================
    public static AppConfig Load()
    {
        try
        {
            EnsureDirectories();

            if (!File.Exists(ConfigPath))
            {
                Save(_config);
                return _config;
            }

            string json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json);

            if (cfg is null)
            {
                Save(_config);
                return _config;
            }

            // Guardamos la instancia cargada
            _config = cfg;

            EnsureDirectories();
            return _config;
        }
        catch
        {
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
            EnsureDirectories();

            _config = config; // actualizamos instancia interna

            string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // silencio
        }
    }

    // ============================================================
    // GET → obtener instancia actual
    // ============================================================
    public static AppConfig Get() => _config;

    // ============================================================
    // DIRECTORIOS
    // ============================================================
    private static void EnsureDirectories()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

            if (!Directory.Exists(_config.LogsPath))
                Directory.CreateDirectory(_config.LogsPath);
        }
        catch
        {
            // silencio
        }
    }
}
