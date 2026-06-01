using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class ArrayConfigService
{
    private static string BasePath => "/etc/raid-util/arrays";

    // ============================
    // 1) Normalizar nombre seguro
    // ============================
    private static string Normalize(string arrayName)
    {
        // Solo letras, números, guion y guion bajo
        return Regex
            .Replace(arrayName, @"[^a-zA-Z0-9\-_]", "");
    }

    // ============================
    // 2) Ruta completa del archivo
    // ============================
    private static string GetPath(string arrayName)
    {
        var clean = Normalize(arrayName);
        return Path.Combine(BasePath, $"{clean}.json");
    }

    // ============================
    // 3) Cargar configuración
    // ============================
    public static ArrayConfig Load(string arrayName)
    {
        try
        {
            // Garantizar directorio
            ShellHelper.EjecutarComoRoot($"mkdir -p \"{BasePath}\"");

            var path = GetPath(arrayName);

            if (!File.Exists(path))
            {
                LogService.Write($"[CFG] No existe config para {arrayName}, usando defaults.");
                return new ArrayConfig();
            }

            var json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                LogService.Error($"[CFG] Archivo vacío: {path}");
                return new ArrayConfig();
            }

            var cfg = JsonConvert.DeserializeObject<ArrayConfig>(json) ?? new ArrayConfig();

            // ============================
            // Compatibilidad con versiones antiguas
            // ============================
            cfg.MountPermissions ??= "755";
            cfg.Name ??= "";
            cfg.FsLabel ??= "";
            cfg.MountPoint ??= "";

            // Validación de rangos
            cfg.ResyncPriority = Math.Clamp(cfg.ResyncPriority, 1, 200000);
            cfg.ResyncMaxSpeed = Math.Clamp(cfg.ResyncMaxSpeed, 100, 500000);

            return cfg;
        }
        catch (Exception ex)
        {
            LogService.Error($"[CFG] Error cargando config de {arrayName}: {ex}");
            return new ArrayConfig();
        }
    }

    // ============================
    // 4) Guardar configuración
    // ============================
    public static void Save(string arrayName, ArrayConfig cfg)
    {
        try
        {
            var path = GetPath(arrayName);
            var dir = BasePath;

            // Crear carpeta root
            ShellHelper.EjecutarComoRoot($"mkdir -p \"{dir}\"");

            // Serializar JSON
            var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);

            // Archivo temporal seguro
            var temp = Path.GetTempFileName();
            File.WriteAllText(temp, json);

            // Backup si existe
            if (File.Exists(path))
            {
                var backup = path + ".bak";
                ShellHelper.EjecutarComoRoot($"cp \"{path}\" \"{backup}\"");
                LogService.Write($"[CFG] Backup creado: {backup}");
            }

            // Copiar con permisos root
            ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{path}\"");

            // Permisos correctos
            ShellHelper.EjecutarComoRoot($"chmod 644 \"{path}\"");
            ShellHelper.EjecutarComoRoot($"chown root:root \"{path}\"");

            LogService.Write($"[CFG] Config guardada correctamente: {path}");
        }
        catch (Exception ex)
        {
            LogService.Error($"[CFG] Error guardando config de {arrayName}: {ex}");
        }
    }
}