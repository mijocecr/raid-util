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
                var result = ShellHelper.EjecutarComoRoot($"blkid \"{device}\"");
                string output = (result.Stdout + result.Stderr).ToLower();

                if (output.Contains("type=\"ext4\"")) return "ext4";
                if (output.Contains("type=\"xfs\"")) return "xfs";
                if (output.Contains("type=\"btrfs\"")) return "btrfs";
                if (output.Contains("type=\"f2fs\"")) return "f2fs";
                if (output.Contains("type=\"vfat\"")) return "vfat";
                if (output.Contains("type=\"exfat\"")) return "exfat";
                if (output.Contains("type=\"ntfs\"")) return "ntfs";
                if (output.Contains("type=\"swap\"")) return "swap";
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

            // Validación básica
            if (mountPoint.Contains(" "))
                throw new Exception("Mount point cannot contain spaces in /etc/fstab.");

            var lines = File.ReadAllLines(FstabPath).ToList();
            List<string> cleaned = new();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                // Mantener comentarios y líneas vacías
                if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                {
                    cleaned.Add(line);
                    continue;
                }

                // Parsear columnas
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

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
            ShellHelper.EjecutarComoRoot($"chown root:root \"{FstabPath}\"");
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

                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

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
            ShellHelper.EjecutarComoRoot($"chown root:root \"{FstabPath}\"");
        }
    }
}
