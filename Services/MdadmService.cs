
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
            var result = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail {device}");
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
            // Velocidad de resync
            SetResyncSpeed(cfg.ResyncPriority, cfg.ResyncMaxSpeed);

            // Read-only / Read-write
            if (cfg.Mount_ReadOnly)
                ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --readonly /dev/{arrayName}");
            else
                ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --readwrite /dev/{arrayName}");

            // Label
            if (!string.IsNullOrWhiteSpace(cfg.FsLabel))
                ShellHelper.EjecutarComoRoot($"/usr/sbin/e2label /dev/{arrayName} \"{cfg.FsLabel}\"");
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