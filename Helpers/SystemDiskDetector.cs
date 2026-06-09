using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RAID_Util.Helpers;

public static class SystemDiskDetector
{
    private static readonly string[] CriticalMounts = { "/", "/boot", "/boot/efi" };

    // ============================================================
    // MÉTODO PRINCIPAL
    // ============================================================
    public static bool IsSystemDisk(string diskName)
    {
        try
        {
            var baseName = NormalizeDiskName(diskName);

            // 1) Obtener particiones del disco
            var partitions = GetDiskPartitions(baseName);

            // 2) Revisar /proc/mounts
            var mounted = GetMountedDevices();
            foreach (var part in partitions)
            {
                if (mounted.TryGetValue(part, out var mp) &&
                    CriticalMounts.Contains(mp))
                    return true;
            }

            // 3) Revisar /etc/fstab
            var fstab = GetFstabEntries();
            foreach (var entry in fstab)
            {
                if (CriticalMounts.Contains(entry.MountPoint))
                {
                    // Coincidencia directa
                    if (partitions.Contains(entry.Device))
                        return true;

                    // Resolver UUID/LABEL/PARTUUID
                    if (IsCriticalByResolvedDevice(entry, partitions, baseName))
                        return true;
                }
            }

            // 4) Revisar si root está en RAID/LVM/crypt
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
        if (string.IsNullOrWhiteSpace(name))
            return "";

        name = name.Trim();

        if (name.StartsWith("/dev/", StringComparison.Ordinal))
            name = name[5..];

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

            if (pk == baseName)
                result.Add("/dev/" + name);

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
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists("/proc/mounts"))
            return dict;

        foreach (var line in File.ReadAllLines("/proc/mounts"))
        {
            var parts = line.Split(' ');
            if (parts.Length >= 2)
                dict[parts[0]] = parts[1];
        }

        return dict;
    }

    // ============================================================
    // /etc/fstab
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
            if (parts.Length >= 2)
                list.Add((parts[0], parts[1]));
        }

        return list;
    }

    // ============================================================
    // Resolver UUID/LABEL/PARTUUID
    // ============================================================
    private static bool IsCriticalByResolvedDevice(
        (string Device, string MountPoint) entry,
        List<string> partitions,
        string baseName)
    {
        var dev = entry.Device;

        // Caso directo: /dev/xxx
        if (dev.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
        {
            if (partitions.Contains(dev))
                return true;

            if (dev.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        // UUID=..., LABEL=..., PARTUUID=...
        if (!(dev.StartsWith("UUID=", StringComparison.OrdinalIgnoreCase) ||
              dev.StartsWith("LABEL=", StringComparison.OrdinalIgnoreCase) ||
              dev.StartsWith("PARTUUID=", StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            var blkid = ShellHelper.EjecutarSinRoot("blkid -o export").Stdout;
            if (string.IsNullOrWhiteSpace(blkid))
                return false;

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

                bool match =
                    (dev.StartsWith("UUID=", StringComparison.OrdinalIgnoreCase) &&
                     map.TryGetValue("UUID", out var uuidVal) &&
                     dev.Equals("UUID=" + uuidVal, StringComparison.OrdinalIgnoreCase))
                    ||
                    (dev.StartsWith("LABEL=", StringComparison.OrdinalIgnoreCase) &&
                     map.TryGetValue("LABEL", out var labelVal) &&
                     dev.Equals("LABEL=" + labelVal, StringComparison.OrdinalIgnoreCase))
                    ||
                    (dev.StartsWith("PARTUUID=", StringComparison.OrdinalIgnoreCase) &&
                     map.TryGetValue("PARTUUID", out var partuuidVal) &&
                     dev.Equals("PARTUUID=" + partuuidVal, StringComparison.OrdinalIgnoreCase));

                if (!match)
                    continue;

                if (partitions.Contains(devName))
                    return true;

                if (devName.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    // ============================================================
    // DETECTAR SI ROOT ESTÁ EN RAID/LVM/CRYPT
    // ============================================================
    private static bool IsRootOnCompositeUsingDisk(string baseName)
    {
        var mounts = GetMountedDevices();

        var rootDev = mounts.FirstOrDefault(x => x.Value == "/").Key;
        if (string.IsNullOrWhiteSpace(rootDev))
            return false;

        if (rootDev.Contains(baseName, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var exec = ShellHelper.EjecutarSinRoot("lsblk -J -o NAME,PKNAME");
            var json = exec.Stdout;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("blockdevices", out var root))
                return false;

            var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dev in root.EnumerateArray())
            {
                var name = dev.GetProperty("name").GetString() ?? "";
                var pk = dev.TryGetProperty("pkname", out var pkEl) ? pkEl.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(pk))
                    parentMap[name] = pk;
            }

            var rootName = rootDev.StartsWith("/dev/") ? rootDev[5..] : rootDev;

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
            return false;
        }

        // Caso especial: root en /dev/mdX
        if (rootDev.StartsWith("/dev/md", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var mdstat = File.ReadAllText("/proc/mdstat").ToLowerInvariant();
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
