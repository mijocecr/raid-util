using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RAID_Util.Helpers;

public static class SystemDiskDetector
{
    // Particiones críticas del sistema
    private static readonly string[] CriticalMounts =
    {
        "/", "/boot", "/boot/efi", "/usr", "/var", "/etc", "/opt", "/srv"
    };

    // ============================================================
    // MÉTODO PRINCIPAL
    // ============================================================
    public static bool IsSystemDisk(string diskName)
    {
        try
        {
            // 1) Obtener todas las particiones montadas
            var mounts = GetMountedDevices();

            // 2) Si alguna partición del disco está montada en un punto crítico → ES SISTEMA
            foreach (var m in mounts)
            {
                if (!m.Device.Contains(diskName))
                    continue;

                if (CriticalMounts.Contains(m.MountPoint))
                    return true;
            }

            // 3) Revisar /etc/fstab por si el disco contiene particiones del sistema
            var fstab = GetFstabDevices();
            foreach (var entry in fstab)
            {
                if (!entry.Device.Contains(diskName))
                    continue;

                if (CriticalMounts.Contains(entry.MountPoint))
                    return true;
            }

            // 4) Revisar si el disco contiene la partición usada para arrancar
            if (IsBootDevice(diskName))
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
    // DETECTAR DISPOSITIVOS MONTADOS
    // ============================================================
    private static List<(string Device, string MountPoint)> GetMountedDevices()
    {
        var list = new List<(string, string)>();

        if (!File.Exists("/proc/mounts"))
            return list;

        foreach (var line in File.ReadAllLines("/proc/mounts"))
        {
            var parts = line.Split(' ');
            if (parts.Length < 2)
                continue;

            var dev = parts[0];
            var mp = parts[1];

            if (dev.StartsWith("/dev/"))
                list.Add((dev, mp));
        }

        return list;
    }

    // ============================================================
    // LEER /etc/fstab
    // ============================================================
    private static List<(string Device, string MountPoint)> GetFstabDevices()
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

            if (dev.StartsWith("/dev/"))
                list.Add((dev, mp));
        }

        return list;
    }

    // ============================================================
    // DETECTAR SI EL DISCO CONTIENE /boot o EFI
    // ============================================================
    private static bool IsBootDevice(string diskName)
    {
        var mounts = GetMountedDevices();

        foreach (var m in mounts)
        {
            if (!m.Device.Contains(diskName))
                continue;

            if (m.MountPoint == "/boot" || m.MountPoint == "/boot/efi")
                return true;
        }

        return false;
    }
}