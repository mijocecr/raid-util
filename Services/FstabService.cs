using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
                    ShellHelper.EjecutarComoRoot($"cp \"{FstabPath}\" \"{BackupPath}\"");
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
                if (output.Contains("TYPE=\"f2fs\"")) return "f2fs";
                if (output.Contains("TYPE=\"vfat\"")) return "vfat";
                if (output.Contains("TYPE=\"exfat\"")) return "exfat";
                if (output.Contains("TYPE=\"ntfs\"")) return "ntfs";
                if (output.Contains("TYPE=\"swap\"")) return "swap";
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

            if (!File.Exists(FstabPath))
                ShellHelper.EjecutarComoRoot($"touch \"{FstabPath}\"");

            var lines = File.ReadAllLines(FstabPath).ToList();

            // Normalizar espacios
            List<string> cleaned = new();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                // Saltar comentarios
                if (trimmed.StartsWith("#"))
                {
                    cleaned.Add(line);
                    continue;
                }

                // Saltar líneas vacías
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    cleaned.Add(line);
                    continue;
                }

                // Parsear columnas
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2)
                {
                    string dev = parts[0];
                    string mp = parts[1];

                    // Eliminar entradas previas del mismo dispositivo o mismo mountpoint
                    if (dev == device || mp == mountPoint)
                        continue;
                }

                cleaned.Add(line);
            }

            // Construir entrada nueva
            string entry = $"{device} {mountPoint} {fs} {options} 0 0";

            cleaned.Add(entry);

            // Guardar en archivo temporal
            string temp = Path.GetTempFileName();
            File.WriteAllLines(temp, cleaned);

            // Copiar con permisos root
            ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{FstabPath}\"");
            ShellHelper.EjecutarComoRoot($"chmod 644 \"{FstabPath}\"");
        }

        // ============================================================
        // ELIMINAR ENTRADA
        // ============================================================
        public static void RemoveEntry(string device)
        {
            Backup();

            if (!File.Exists(FstabPath))
                return;

            var lines = File.ReadAllLines(FstabPath).ToList();
            List<string> cleaned = new();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                {
                    cleaned.Add(line);
                    continue;
                }

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 1)
                {
                    string dev = parts[0];

                    // Eliminar solo si coincide EXACTAMENTE
                    if (dev == device)
                        continue;
                }

                cleaned.Add(line);
            }

            string temp = Path.GetTempFileName();
            File.WriteAllLines(temp, cleaned);

            ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{FstabPath}\"");
            ShellHelper.EjecutarComoRoot($"chmod 644 \"{FstabPath}\"");
        }
    }
}
