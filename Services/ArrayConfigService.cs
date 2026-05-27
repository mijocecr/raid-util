using System;
using System.IO;
using Newtonsoft.Json;
using RAID_Util.Models;
using RAID_Util.Helpers;

namespace RAID_Util.Services
{
    public static class ArrayConfigService
    {
        // ⭐ Ruta real del sistema (requiere root)
        private static string BasePath => "/etc/raid-util/arrays";

        // ⭐ Cargar configuración del array
        public static ArrayConfig Load(string arrayName)
        {
            try
            {
                string path = Path.Combine(BasePath, $"{arrayName}.json");

                if (!File.Exists(path))
                    return new ArrayConfig(); // Config por defecto

                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<ArrayConfig>(json) ?? new ArrayConfig();
            }
            catch
            {
                return new ArrayConfig();
            }
        }

        // ⭐ Guardar configuración usando ShellHelper (root)
        public static void Save(string arrayName, ArrayConfig cfg)
        {
            string json = JsonConvert.SerializeObject(cfg, Formatting.Indented);

            // Crear carpeta root
            ShellHelper.EjecutarComoRoot($"mkdir -p {BasePath}");

            // Guardar en archivo temporal
            string temp = Path.GetTempFileName();
            File.WriteAllText(temp, json);

            // Copiar con permisos root
            ShellHelper.EjecutarComoRoot($"cp {temp} {BasePath}/{arrayName}.json");

            // Ajustar permisos (opcional)
            ShellHelper.EjecutarComoRoot($"chmod 644 {BasePath}/{arrayName}.json");
        }
    }
}