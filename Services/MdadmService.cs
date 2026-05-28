using System;
using System.IO;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services
{
    public static class MdadmService
    {
        // ============================================================
        // OBTENER DETALLE REAL DEL ARRAY
        // ============================================================
        public static string GetDetail(string arrayName)
        {
            string device = $"/dev/{arrayName}";
            var result = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail \"{device}\"");
            return (result.Stdout + "\n" + result.Stderr).Trim();
        }

        // ============================================================
        // CAMBIAR VELOCIDAD DE RESYNC
        // ============================================================
        public static void SetResyncSpeed(int min, int max)
        {
            // Se asume que ya vienen validados/rangeados desde ArrayConfigService
            ShellHelper.EjecutarComoRoot($"bash -c \"echo {min} > /proc/sys/dev/raid/speed_limit_min\"");
            ShellHelper.EjecutarComoRoot($"bash -c \"echo {max} > /proc/sys/dev/raid/speed_limit_max\"");
        }

        // ============================================================
        // LEER /proc/mdstat
        // ============================================================
        public static string GetMdstat()
        {
            if (!File.Exists("/proc/mdstat"))
                return string.Empty;

            return File.ReadAllText("/proc/mdstat");
        }

        // ============================================================
        // DETECTAR ARRAY DEGRADADO / ROTO
        // ============================================================
        public static bool IsDegraded(string arrayName)
        {
            string md = GetMdstat().ToLower();

            // Si no aparece en mdstat → array muerto
            if (!md.Contains(arrayName.ToLower()))
                return true; // FAILED

            // Línea específica del array
            string[] lines = md.Split('\n');
            string? line = Array.Find(lines, l => l.Contains(arrayName.ToLower()));

            if (line == null)
                return true; // FAILED

            // Estados realmente rotos
            if (line.Contains("inactive") ||
                line.Contains("failed") ||
                line.Contains("faulty") ||
                line.Contains("read-only") ||
                line.Contains("broken"))
                return true; // FAILED

            // Estados degradados
            if (line.Contains("degraded") ||
                line.Contains("removed") ||
                line.Contains("missing") ||
                line.Contains("(f)") ||
                line.Contains("_"))
                return true; // DEGRADED

            return false;
        }

        // ============================================================
        // DETECTAR RESYNC / RECOVERY / RESHAPE
        // ============================================================
        public static bool IsResyncing(string arrayName)
        {
            string md = GetMdstat().ToLower();

            if (!md.Contains(arrayName.ToLower()))
                return false;

            return md.Contains("resync") ||
                   md.Contains("recovery") ||
                   md.Contains("reshape") ||
                   md.Contains("rebuild");
        }

        // ============================================================
        // APLICAR CONFIGURACIÓN RAID REAL
        // ============================================================
        public static void ApplyConfig(string arrayName, ArrayConfig cfg)
        {
            string device = $"/dev/{arrayName}";

            // Velocidad de resync
            SetResyncSpeed(cfg.ResyncPriority, cfg.ResyncMaxSpeed);

            // Read-only / Read-write
            if (cfg.Mount_ReadOnly)
                ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --readonly \"{device}\"");
            else
                ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --readwrite \"{device}\"");

            // Label (OJO: esto solo es válido para ext2/3/4)
            if (!string.IsNullOrWhiteSpace(cfg.FsLabel))
            {
                // Aquí asumes ext*; si el FS puede ser xfs/btrfs/exfat, habría que
                // o bien detectar FS antes, o no tocar label si no es ext*.
                ShellHelper.EjecutarComoRoot($"/usr/sbin/e2label \"{device}\" \"{cfg.FsLabel}\"");
            }
        }
    }
}
