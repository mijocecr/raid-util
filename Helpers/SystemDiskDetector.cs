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

                // ⭐ Resolver UUID/LABEL/PARTUUID a /dev/xxx y comparar
                if (IsCriticalByResolvedDevice(entry, partitions, baseName))
                    return true;
            }

            // 4) Revisar si el root está en RAID/LVM/crypt y este disco es miembro
            if (IsRootOnCompositeUsingDisk(baseName))
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

        var exec = ShellHelper.EjecutarSinRoot("lsblk -J -o NAME,PKNAME");
        var json = exec.Stdout;
        if (string.IsNullOrWhiteSpace(json))
            return result;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("blockdevices", out var root))
            return result;

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
    // Resolver UUID/LABEL/PARTUUID y comprobar si es crítico
    // ============================================================
    private static bool IsCriticalByResolvedDevice(
        (string Device, string MountPoint) entry,
        List<string> partitions,
        string baseName)
    {
        if (!CriticalMounts.Contains(entry.MountPoint))
            return false;

        var dev = entry.Device;

        // Si ya es /dev/xxx y contiene el nombre base → marcar como sistema
        if (dev.StartsWith("/dev/", StringComparison.Ordinal))
        {
            if (partitions.Contains(dev))
                return true;

            // ⭐ Coincidencia por nombre base (sda, nvme0n1, etc.)
            if (dev.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        // UUID=..., LABEL=..., PARTUUID=...
        if (dev.StartsWith("UUID=", StringComparison.OrdinalIgnoreCase) ||
            dev.StartsWith("LABEL=", StringComparison.OrdinalIgnoreCase) ||
            dev.StartsWith("PARTUUID=", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var blkid = ShellHelper.EjecutarSinRoot($"blkid -o export").Stdout;
                if (string.IsNullOrWhiteSpace(blkid))
                    return false;

                // Formato típico:
                // DEVNAME=/dev/sda1
                // UUID=...
                // LABEL=...
                // PARTUUID=...
                var blocks = blkid.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in blocks)
                {
                    var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    string devName = "";
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var l in lines)
                    {
                        var idx = l.IndexOf('=');
                        if (idx <= 0) continue;
                        var key = l[..idx].Trim();
                        var val = l[(idx + 1)..].Trim();
                        map[key] = val;
                        if (key.Equals("DEVNAME", StringComparison.OrdinalIgnoreCase))
                            devName = val;
                    }

                    if (string.IsNullOrWhiteSpace(devName))
                        continue;

                    bool match = false;

                    if (dev.StartsWith("UUID=", StringComparison.OrdinalIgnoreCase) &&
                        map.TryGetValue("UUID", out var uuidVal) &&
                        string.Equals(dev, "UUID=" + uuidVal, StringComparison.OrdinalIgnoreCase))
                        match = true;

                    if (dev.StartsWith("LABEL=", StringComparison.OrdinalIgnoreCase) &&
                        map.TryGetValue("LABEL", out var labelVal) &&
                        string.Equals(dev, "LABEL=" + labelVal, StringComparison.OrdinalIgnoreCase))
                        match = true;

                    if (dev.StartsWith("PARTUUID=", StringComparison.OrdinalIgnoreCase) &&
                        map.TryGetValue("PARTUUID", out var partuuidVal) &&
                        string.Equals(dev, "PARTUUID=" + partuuidVal, StringComparison.OrdinalIgnoreCase))
                        match = true;

                    if (!match)
                        continue;

                    // Si el DEVNAME pertenece a nuestras particiones o contiene el baseName → sistema
                    if (partitions.Contains(devName))
                        return true;

                    if (devName.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // Si algo falla, no marcamos aquí, pero el catch global de IsSystemDisk protege
                return false;
            }
        }

        return false;
    }

    // ============================================================
    // DETECTAR SI EL ROOT ESTÁ EN RAID/LVM/CRYPT Y ESTE DISCO ES MIEMBRO
    // ============================================================
    private static bool IsRootOnCompositeUsingDisk(string baseName)
    {
        var mounts = GetMountedDevices();

        // Buscar qué dispositivo contiene /
        var rootDev = mounts.FirstOrDefault(x => x.Value == "/").Key;
        if (string.IsNullOrWhiteSpace(rootDev))
            return false;

        // Si root está directamente en este disco/partición
        if (rootDev.Contains(baseName, StringComparison.OrdinalIgnoreCase))
            return true;

        // ⭐ Usar lsblk para seguir la cadena de padres (LVM, crypt, RAID, etc.)
        try
        {
            var exec = ShellHelper.EjecutarSinRoot("lsblk -J -o NAME,PKNAME");
            var json = exec.Stdout;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("blockdevices", out var root))
                return false;

            // Construir mapa hijo → padre
            var parentMap = new Dictionary<string, string>();

            foreach (var dev in root.EnumerateArray())
            {
                var name = dev.GetProperty("name").GetString() ?? "";
                var pk = dev.TryGetProperty("pkname", out var pkEl) ? pkEl.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(pk))
                    parentMap[name] = pk;
            }

            // rootDev puede venir como /dev/xxx
            var rootName = rootDev.StartsWith("/dev/") ? rootDev[5..] : rootDev;

            // Subir por la cadena de padres hasta llegar al disco físico
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = rootName;

            while (!string.IsNullOrWhiteSpace(current) && !visited.Contains(current))
            {
                visited.Add(current);

                if (string.Equals(current, baseName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!parentMap.TryGetValue(current, out var parent))
                    break;

                current = parent;
            }
        }
        catch
        {
            // Si falla, no marcamos aquí, pero el catch global de IsSystemDisk protege
            return false;
        }

        // Caso especial: root en /dev/mdX → comprobar /proc/mdstat
        if (rootDev.StartsWith("/dev/md", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var mdstat = File.ReadAllText("/proc/mdstat").ToLowerInvariant();
                // ⭐ Buscar baseName explícitamente como sdX o nvmeX
                if (mdstat.Contains(baseName.ToLowerInvariant()))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
