using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

public static class FstabService
{
    private const string FstabPath = "/etc/fstab";
    private const string BackupPath = "/etc/fstab.raidutil.bak";

    // ============================================================
    // NORMALIZAR DEVICE (ruta real)
    // ============================================================
    private static string NormalizeDevice(string device)
    {
        if (string.IsNullOrWhiteSpace(device))
            return device;

        device = device.Trim();

        // Si ya es ruta absoluta → OK
        if (device.StartsWith("/dev/"))
            return device;

        // Si es md0 → convertir a /dev/md0
        if (device.StartsWith("md"))
            return "/dev/" + device;

        // Si es sda1 → convertir a /dev/sda1
        if (device.StartsWith("sd") || device.StartsWith("nvme"))
            return "/dev/" + device;

        return device;
    }

    // ============================================================
    // EXTRAER NOMBRE BASE (md0, host:md0, etc.)
    // ============================================================
    private static string BaseName(string device)
    {
        if (string.IsNullOrWhiteSpace(device))
            return "";

        var d = device.Trim();

        if (d.StartsWith("/dev/"))
            d = d.Substring(5);

        return d;
    }

    // ============================================================
    // BACKUP
    // ============================================================
    public static void Backup()
    {
        try
        {
            if (File.Exists(FstabPath))
                ShellHelper.EjecutarComoRoot($"cp \"{FstabPath}\" \"{BackupPath}\"");
        }
        catch { }
    }

    // ============================================================
    // DETECTAR FILESYSTEM REAL
    // ============================================================
    public static string DetectFilesystem(string device)
    {
        try
        {
            device = NormalizeDevice(device);

            var result = ShellHelper.EjecutarComoRoot($"blkid \"{device}\"");
            var output = (result.Stdout + result.Stderr).ToLower();

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

        device = NormalizeDevice(device);
        var baseDev = BaseName(device);

        if (!File.Exists(FstabPath))
            ShellHelper.EjecutarComoRoot($"touch \"{FstabPath}\"");

        if (mountPoint.Contains(" "))
            throw new Exception("Mount point cannot contain spaces in /etc/fstab.");

        var lines = File.ReadAllLines(FstabPath).ToList();
        List<string> cleaned = new();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
            {
                cleaned.Add(line);
                continue;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                var dev = parts[0];
                var mp = parts[1];

                var baseExisting = BaseName(dev);

                // Eliminar entradas previas del mismo dispositivo o mountpoint
                if (baseExisting == baseDev || mp == mountPoint)
                    continue;
            }

            cleaned.Add(line);
        }

        var entry = $"{device} {mountPoint} {fs} {options} 0 0";
        cleaned.Add(entry);

        var temp = Path.GetTempFileName();
        File.WriteAllLines(temp, cleaned);

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

        device = NormalizeDevice(device);
        var baseDev = BaseName(device);

        if (!File.Exists(FstabPath))
            return;

        var lines = File.ReadAllLines(FstabPath).ToList();
        List<string> cleaned = new();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
            {
                cleaned.Add(line);
                continue;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 1)
            {
                var dev = parts[0];
                var baseExisting = BaseName(dev);

                if (baseExisting == baseDev)
                    continue;
            }

            cleaned.Add(line);
        }

        var temp = Path.GetTempFileName();
        File.WriteAllLines(temp, cleaned);

        ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{FstabPath}\"");
        ShellHelper.EjecutarComoRoot($"chmod 644 \"{FstabPath}\"");
        ShellHelper.EjecutarComoRoot($"chown root:root \"{FstabPath}\"");
    }
}
