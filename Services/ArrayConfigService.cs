using System;
using System.IO;
using Newtonsoft.Json;
using RAID_Util.Models;
using RAID_Util.Helpers;

namespace RAID_Util.Services
{
    public static class ArrayConfigService
    {
        private static string BasePath => "/etc/raid-util/arrays";

        // Normaliza nombre (md0 → md0.json)
        private static string Normalize(string arrayName)
        {
            return arrayName.Trim().Replace("/", "").Replace("\\", "");
        }

        // Ruta completa del archivo
        private static string GetPath(string arrayName)
        {
            string clean = Normalize(arrayName);
            return Path.Combine(BasePath, $"{clean}.json");
        }

        // ⭐ Cargar configuración con validación estricta
        public static ArrayConfig Load(string arrayName)
        {
            try
            {
                string path = GetPath(arrayName);

                if (!File.Exists(path))
                {
                    LogService.Write($"[CFG] No existe config para {arrayName}, usando defaults.");
                    return new ArrayConfig();
                }

                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                {
                    LogService.Error($"[CFG] Archivo vacío: {path}");
                    return new ArrayConfig();
                }

                var cfg = JsonConvert.DeserializeObject<ArrayConfig>(json);

                if (cfg == null)
                {
                    LogService.Error($"[CFG] JSON inválido en {path}");
                    return new ArrayConfig();
                }

                // ⭐ Compatibilidad con versiones antiguas
                // Si el JSON viejo tenía AutoMount, lo ignoramos.
                // PersistMount ya viene en el nuevo modelo.
                if (cfg.MountPermissions is null)
                    cfg.MountPermissions = "755";

                return cfg;
            }
            catch (Exception ex)
            {
                LogService.Error($"[CFG] Error cargando config de {arrayName}: {ex}");
                return new ArrayConfig();
            }
        }

        // ⭐ Guardar con backup, permisos y atomicidad
        public static void Save(string arrayName, ArrayConfig cfg)
        {
            try
            {
                string path = GetPath(arrayName);
                string dir = BasePath;

                // Crear carpeta root
                ShellHelper.EjecutarComoRoot($"mkdir -p {dir}");

                // Serializar JSON
                string json = JsonConvert.SerializeObject(cfg, Formatting.Indented);

                // Archivo temporal seguro
                string temp = Path.GetTempFileName();
                File.WriteAllText(temp, json);

                // Backup si existe
                if (File.Exists(path))
                {
                    string backup = path + ".bak";
                    ShellHelper.EjecutarComoRoot($"cp {path} {backup}");
                    LogService.Write($"[CFG] Backup creado: {backup}");
                }

                // Copiar con permisos root
                ShellHelper.EjecutarComoRoot($"cp {temp} {path}");

                // Permisos correctos
                ShellHelper.EjecutarComoRoot($"chmod 644 {path}");

                LogService.Write($"[CFG] Config guardada correctamente: {path}");
            }
            catch (Exception ex)
            {
                LogService.Error($"[CFG] Error guardando config de {arrayName}: {ex}");
            }
        }
    }
}
