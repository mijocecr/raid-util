
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

        // ============================================================
        // DETECTAR ARRAY DEGRADADO / ROTO (VERSIÓN COMPLETA)
        // ============================================================
        public static bool IsDegraded(string arrayName)
        {
            string md = GetMdstat().ToLower();

            if (!md.Contains(arrayName.ToLower()))
                return false;

            return
                md.Contains("degraded") ||     // RAID1/5/6/10 degradado
                md.Contains("faulty") ||       // disco faulty
                md.Contains("(f)") ||          // RAID1 faulty
                md.Contains("removed") ||      // disco removido
                md.Contains("missing") ||      // disco faltante
                md.Contains("broken") ||       // RAID0 roto (tu caso)
                md.Contains("read-only") ||    // RAID0 roto (tu caso)
                md.Contains("_");              // RAID1/10 disco faltante
        }

        // ============================================================
        // DETECTAR RESYNC / RECOVERY / RESHAPE
        // ============================================================
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

            // 2) Read-only / Read-write
            if (cfg.Mount_ReadOnly)
                ShellHelper.EjecutarComoRoot($"mdadm --readonly /dev/{arrayName}");
            else
                ShellHelper.EjecutarComoRoot($"mdadm --readwrite /dev/{arrayName}");

            // 3) Label (solo si aplica)
            if (!string.IsNullOrWhiteSpace(cfg.FsLabel))
                ShellHelper.EjecutarComoRoot($"e2label /dev/{arrayName} \"{cfg.FsLabel}\"");
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