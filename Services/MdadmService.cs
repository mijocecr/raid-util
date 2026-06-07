using System;
using System.IO;
using System.Linq;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class MdadmService
{
    // ============================================================
    // RESOLVER PATH REAL DEL ARRAY
    // ============================================================
    private static string ResolveArrayPath(string arrayName)
    {
        if (arrayName.StartsWith("/dev/"))
            return arrayName;

        // ⭐ FIX: usar Singleton
        var arrays = RaidService.Instance.GetArraysAsync().Result;

        var a = arrays.FirstOrDefault(x =>
            x.Name == arrayName ||
            x.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
            x.Path.EndsWith(arrayName, StringComparison.Ordinal));

        if (a != null)
            return a.Path;

        return "/dev/" + arrayName;
    }

    // ============================================================
    // OBTENER DETALLE REAL DEL ARRAY
    // ============================================================
    public static string GetDetail(string arrayName)
    {
        var path = ResolveArrayPath(arrayName);

        var result = ShellHelper.EjecutarComoRoot(
            $"/usr/sbin/mdadm --detail \"{path}\""
        );

        return (result.Stdout + "\n" + result.Stderr).Trim();
    }

    // ============================================================
    // CAMBIAR VELOCIDAD DE RESYNC
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
            return string.Empty;

        return File.ReadAllText("/proc/mdstat");
    }

    // ============================================================
    // DETECTAR ARRAY DEGRADADO / ROTO
    // ============================================================
    public static bool IsDegraded(string arrayName)
    {
        var md = GetMdstat().ToLower();

        var baseName = arrayName.StartsWith("/dev/")
            ? arrayName.Split('/').Last()
            : arrayName;

        if (!md.Contains(baseName.ToLower()))
            return true;

        var lines = md.Split('\n');
        var line = Array.Find(lines, l => l.Contains(baseName.ToLower()));

        if (line == null)
            return true;

        if (line.Contains("inactive") ||
            line.Contains("failed") ||
            line.Contains("faulty") ||
            line.Contains("read-only") ||
            line.Contains("broken"))
            return true;

        if (line.Contains("degraded") ||
            line.Contains("removed") ||
            line.Contains("missing") ||
            line.Contains("(f)") ||
            line.Contains("_"))
            return true;

        return false;
    }

    // ============================================================
    // DETECTAR RESYNC / RECOVERY / RESHAPE
    // ============================================================
    public static bool IsResyncing(string arrayName)
    {
        var md = GetMdstat().ToLower();

        var baseName = arrayName.StartsWith("/dev/")
            ? arrayName.Split('/').Last()
            : arrayName;

        if (!md.Contains(baseName.ToLower()))
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
        var device = ResolveArrayPath(arrayName);

        // Velocidad de resync
        SetResyncSpeed(cfg.ResyncPriority, cfg.ResyncMaxSpeed);

        // Read-only / Read-write
        if (cfg.Mount_ReadOnly)
            ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --readonly \"{device}\"");
        else
            ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --readwrite \"{device}\"");

        // Label solo para EXT*
        if (!string.IsNullOrWhiteSpace(cfg.FsLabel))
        {
            var fs = FstabService.DetectFilesystem(device);

            if (fs.StartsWith("ext"))
            {
                ShellHelper.EjecutarComoRoot(
                    $"/usr/sbin/e2label \"{device}\" \"{cfg.FsLabel}\""
                );
            }
        }
    }
}
