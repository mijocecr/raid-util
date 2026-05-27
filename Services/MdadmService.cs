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
            var result = ShellHelper.EjecutarComoRoot($"mdadm --detail {device}");
            return (result.Stdout + "\n" + result.Stderr).Trim();
        }

        // ============================================================
        // CAMBIAR VELOCIDAD DE RESYNC (CORREGIDO)
        // ============================================================
        public static void SetResyncSpeed(int min, int max)
        {
            // Redirección segura usando bash -c
            ShellHelper.EjecutarComoRoot($"bash -c \"echo {min} > /proc/sys/dev/raid/speed_limit_min\"");
            ShellHelper.EjecutarComoRoot($"bash -c \"echo {max} > /proc/sys/dev/raid/speed_limit_max\"");
        }

        // ============================================================
        // LEER /proc/mdstat
        // ============================================================
        public static string GetMdstat()
        {
            if (!File.Exists("/proc/mdstat"))
                return "";

            return File.ReadAllText("/proc/mdstat");
        }

        public static bool IsDegraded(string arrayName)
        {
            string md = GetMdstat().ToLower();

            return md.Contains(arrayName.ToLower()) &&
                   (md.Contains("degraded") ||
                    md.Contains("faulty") ||
                    md.Contains("removed") ||
                    md.Contains("missing"));
        }

        public static bool IsResyncing(string arrayName)
        {
            string md = GetMdstat().ToLower();

            return md.Contains(arrayName.ToLower()) &&
                   (md.Contains("resync") ||
                    md.Contains("recovery") ||
                    md.Contains("reshape"));
        }

        // ============================================================
        // APLICAR CONFIGURACIÓN RAID REAL
        // ============================================================
        public static void ApplyConfig(string arrayName, ArrayConfig cfg)
        {
            // 1) Velocidad de resync
            SetResyncSpeed(cfg.ResyncPriority, cfg.ResyncMaxSpeed);

            // 2) Read-only
            if (cfg.Mount_ReadOnly)
                ShellHelper.EjecutarComoRoot($"mdadm --readwrite /dev/{arrayName}");
            else
                ShellHelper.EjecutarComoRoot($"mdadm --readonly /dev/{arrayName}");

            // 3) Label (si aplica)
            if (!string.IsNullOrWhiteSpace(cfg.FsLabel))
                ShellHelper.EjecutarComoRoot($"e2label /dev/{arrayName} \"{cfg.FsLabel}\"");

            // 4) (Opcional) bitmap, reshape, etc.
            // Aquí puedes añadir más configuraciones avanzadas si quieres.
        }
    }
}

/*
 
   añadir discos
   
   quitar discos
   
   marcar faulty
   
   reemplazar discos
   
   reshape
   
   grow
   
   shrink
   
   activar bitmap
   
   desactivar bitmap
   
   
 *
 * 
 */