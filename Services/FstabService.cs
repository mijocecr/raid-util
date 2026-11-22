using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

public static class FstabService
{
    private const string FstabPath = "/etc/fstab";
    private const string BackupPath = "/etc/fstab.raidutil.bak";

    private static readonly object SyncRoot = new();

    /// <summary>
    /// Serializa también entre procesos: el semaphore de PrivilegeSession solo
    /// cubre los comandos root de este proceso, no la lectura/escritura de fstab.
    /// </summary>
    private static FileStream AcquireLock()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "raid-util-fstab.lock");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 99)
            {
                Thread.Sleep(50);
            }
        }

        throw new IOException("No se pudo adquirir el bloqueo de /etc/fstab.");
    }

    // ============================================================
    // NORMALIZAR DEVICE (ruta real)
    // ============================================================
    internal static string NormalizeDevice(string device)
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
    internal static string BaseName(string device)
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
                ShellHelper.RunRoot("cp", FstabPath, BackupPath);
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

            var result = ShellHelper.RunRoot("blkid", device);
            var output = (result.Stdout + result.Stderr).ToLowerInvariant();

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
        lock (SyncRoot)
        using (AcquireLock())
        {
            WriteEntryCore(device, mountPoint, fs, options);
        }
    }

    private static void WriteEntryCore(string device, string mountPoint, string fs, string options)
    {
        Backup();

        device = NormalizeDevice(device);
        var baseDev = BaseName(device);

        if (!File.Exists(FstabPath))
            ShellHelper.RunRoot("touch", FstabPath);

        var error = InputValidation.ValidateMountPoint(mountPoint);
        if (error is not null)
            throw new ArgumentException(error, nameof(mountPoint));

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

        ShellHelper.RunRoot("cp", temp, FstabPath);
        ShellHelper.RunRoot("chmod", "644", FstabPath);
        ShellHelper.RunRoot("chown", "root:root", FstabPath);
        SystemIntegrationService.ReloadSystemdAsync().GetAwaiter().GetResult();
    }

    // ============================================================
    // ELIMINAR ENTRADA
    // ============================================================
    public static void RemoveEntry(string device)
    {
        lock (SyncRoot)
        using (AcquireLock())
        {
            RemoveEntryCore(device);
        }
    }

    private static void RemoveEntryCore(string device)
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

        ShellHelper.RunRoot("cp", temp, FstabPath);
        ShellHelper.RunRoot("chmod", "644", FstabPath);
        ShellHelper.RunRoot("chown", "root:root", FstabPath);
        SystemIntegrationService.ReloadSystemdAsync().GetAwaiter().GetResult();
    }
}
