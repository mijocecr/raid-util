using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RAID_Util.Helpers;

public static class SystemDiskDetector
{
    // Solo montajes realmente críticos
    private static readonly string[] CriticalMounts =
    {
        "/", "/boot", "/boot/efi"
    };

    // ============================================================
    // MÉTODO PRINCIPAL
    // ============================================================
    public static bool IsSystemDisk(string diskName)
    {
        try
        {
            var baseName = NormalizeDiskName(diskName);

            // 1) Obtener TODAS las particiones del disco
            var partitions = GetDiskPartitions(baseName);

            // 2) Revisar /proc/mounts
            var mounted = GetMountedDevices();
            foreach (var part in partitions)
            {
                if (mounted.TryGetValue(part, out var mp))
                    if (CriticalMounts.Contains(mp))
                        return true;
            }

            // 3) Revisar /etc/fstab (UUID, LABEL, PARTUUID, /dev/xxx)
            var fstab = GetFstabEntries();
            foreach (var entry in fstab)
            {
                // Coincidencia exacta con particiones del disco
                if (partitions.Contains(entry.Device))
                {
                    if (CriticalMounts.Contains(entry.MountPoint))
                        return true;
                }

                // Coincidencia por nombre base (caso /dev/sda → /dev/sda1)
                if (entry.Device.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                {
                    if (CriticalMounts.Contains(entry.MountPoint))
                        return true;
                }
            }

            // 4) Revisar si el root está en RAID
            if (IsRootOnRaid(baseName))
                return true;

            return false;
        }
        catch
        {
            // En caso de duda → proteger
            return true;
        }
    }

    // ============================================================
    // NORMALIZAR NOMBRE
    // ============================================================
    private static string NormalizeDiskName(string name)
    {
        name = name.Trim();

        if (name.StartsWith("/dev/"))
            name = name.Substring(5);

        return name;
    }

    // ============================================================
    // OBTENER PARTICIONES DEL DISCO (lsblk -J)
    // ============================================================
    private static List<string> GetDiskPartitions(string baseName)
    {
        var result = new List<string>();

        var json = ShellHelper.EjecutarSinRoot("lsblk -J -o NAME,PKNAME").Stdout;
        if (string.IsNullOrWhiteSpace(json))
            return result;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("blockdevices");

        foreach (var dev in root.EnumerateArray())
        {
            var name = dev.GetProperty("name").GetString() ?? "";
            var pk = dev.TryGetProperty("pkname", out var pkEl) ? pkEl.GetString() ?? "" : "";

            // Partición cuyo padre es el disco
            if (pk == baseName)
                result.Add("/dev/" + name);

            // El propio disco
            if (name == baseName)
                result.Add("/dev/" + name);
        }

        return result;
    }

    // ============================================================
    // /proc/mounts
    // ============================================================
    private static Dictionary<string, string> GetMountedDevices()
    {
        var dict = new Dictionary<string, string>();

        if (!File.Exists("/proc/mounts"))
            return dict;

        foreach (var line in File.ReadAllLines("/proc/mounts"))
        {
            var parts = line.Split(' ');
            if (parts.Length < 2)
                continue;

            var dev = parts[0];
            var mp = parts[1];

            dict[dev] = mp;
        }

        return dict;
    }

    // ============================================================
    // /etc/fstab (UUID, LABEL, PARTUUID, /dev/xxx)
    // ============================================================
    private static List<(string Device, string MountPoint)> GetFstabEntries()
    {
        var list = new List<(string, string)>();

        if (!File.Exists("/etc/fstab"))
            return list;

        foreach (var line in File.ReadAllLines("/etc/fstab"))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var dev = parts[0];
            var mp = parts[1];

            list.Add((dev, mp));
        }

        return list;
    }

    // ============================================================
    // DETECTAR SI EL ROOT ESTÁ EN RAID
    // ============================================================
    private static bool IsRootOnRaid(string baseName)
    {
        var mounts = GetMountedDevices();

        // Buscar qué dispositivo contiene /
        var rootDev = mounts.FirstOrDefault(x => x.Value == "/").Key;
        if (string.IsNullOrWhiteSpace(rootDev))
            return false;

        // Si root está en RAID → todos los discos miembros son sistema
        if (rootDev.StartsWith("/dev/md"))
        {
            var mdstat = File.ReadAllText("/proc/mdstat").ToLower();
            return mdstat.Contains(baseName.ToLower());
        }

        return false;
    }
}
