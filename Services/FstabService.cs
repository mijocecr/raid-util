using System;
using System.IO;
using System.Linq;
using RAID_Util.Helpers;

namespace RAID_Util.Services
{
    public static class FstabService
    {
        private const string FstabPath = "/etc/fstab";
        private const string BackupPath = "/etc/fstab.raidutil.bak";

        // ============================================================
        // BACKUP
        // ============================================================
        public static void Backup()
        {
            try
            {
                if (File.Exists(FstabPath))
                {
                    // Copia con permisos root
                    ShellHelper.EjecutarComoRoot($"cp {FstabPath} {BackupPath}");
                }
            }
            catch
            {
                // No romper si falla el backup
            }
        }

        // ============================================================
        // DETECTAR FILESYSTEM REAL
        // ============================================================
        public static string DetectFilesystem(string device)
        {
            try
            {
                var result = ShellHelper.EjecutarComoRoot($"blkid {device}");

                string output = result.Stdout + result.Stderr;

                if (output.Contains("TYPE=\"ext4\"")) return "ext4";
                if (output.Contains("TYPE=\"xfs\"")) return "xfs";
                if (output.Contains("TYPE=\"btrfs\"")) return "btrfs";
            }
            catch { }

            return "auto";
        }

        // ============================================================
        // ESCRIBIR / ACTUALIZAR ENTRADA
        // ============================================================
        public static void WriteEntry(string device, string mountPoint, string fs, string options)
        {
            Backup();

            // Leer fstab como usuario normal (solo lectura)
            var lines = File.ReadAllLines(FstabPath).ToList();

            string prefix = device + " ";

            // Eliminar entradas previas del mismo dispositivo
            lines.RemoveAll(l => l.TrimStart().StartsWith(prefix));

            // Construir entrada nueva
            string entry = $"{device} {mountPoint} {fs} {options} 0 0";

            lines.Add(entry);

            // Guardar en archivo temporal
            string temp = Path.GetTempFileName();
            File.WriteAllLines(temp, lines);

            // Copiar con permisos root
            ShellHelper.EjecutarComoRoot($"cp {temp} {FstabPath}");
        }

        // ============================================================
        // ELIMINAR ENTRADA
        // ============================================================
        public static void RemoveEntry(string device)
        {
            Backup();

            var lines = File.ReadAllLines(FstabPath).ToList();

            string prefix = device + " ";

            lines.RemoveAll(l => l.TrimStart().StartsWith(prefix));

            string temp = Path.GetTempFileName();
            File.WriteAllLines(temp, lines);

            ShellHelper.EjecutarComoRoot($"cp {temp} {FstabPath}");
        }
    }
}
