using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class DiskService
{
    // ============================================================
    // OBTENER TODOS LOS DISCOS FÍSICOS Y PARTICIONES ÚTILES
    // ============================================================
  
    
public static List<RaidDiskInfo> GetAllDisks()
{
    var disks = new List<RaidDiskInfo>();

    var json = ShellHelper.EjecutarSinRoot(
        "lsblk -J -o NAME,TYPE,SIZE,MODEL,SERIAL,ROTA,MOUNTPOINTS,FSTYPE,PKNAME"
    ).Stdout;

    if (string.IsNullOrWhiteSpace(json))
        return disks;

    var parsed = ParseLsblk(json);

    foreach (var d in parsed)
    {
        if (d.Type != "disk")
            continue;

        // 1) Ignorar loop/zram
        if (d.Name.StartsWith("loop") || d.Name.StartsWith("zram"))
            continue;

        // 2) Detectar si es disco del sistema
        bool isSystem = false;

        if (d.MountPoints != null)
        {
            foreach (var mp in d.MountPoints)
            {
                if (mp == "/" || mp == "/boot" || mp == "/boot/efi" ||
                    mp.StartsWith("/var") || mp.StartsWith("/home"))
                {
                    isSystem = true;
                    break;
                }
            }
        }

        if (!isSystem && d.Children != null)
        {
            foreach (var child in d.Children)
            {
                if (child.MountPoints != null)
                {
                    foreach (var mp in child.MountPoints)
                    {
                        if (mp == "/" || mp == "/boot" || mp == "/boot/efi" ||
                            mp.StartsWith("/var") || mp.StartsWith("/home"))
                        {
                            isSystem = true;
                            break;
                        }
                    }
                }
                if (isSystem) break;
            }
        }

        if (isSystem)
            continue;

        // 3) Detectar RAID (pero NO excluirlo)
        bool isActiveRaid = MdadmService_IsDiskInArray(d.Name, out var arrayName);
        bool isRaidFs = string.Equals(d.FsType, "linux_raid_member", StringComparison.OrdinalIgnoreCase);

        var info = new RaidDiskInfo
        {
            Name = d.Name,
            Model = d.Model,
            Size = d.Size,
            Serial = d.Serial,
            Filesystem = d.FsType,
            MountPoint = d.MountPoint,
            IsMounted = !string.IsNullOrWhiteSpace(d.MountPoint),
            IsRotational = d.Rotational == "1",
            IsNvme = d.Name.StartsWith("nvme"),
            HasPartitions = d.Children != null && d.Children.Count > 0,
            HasFileSystem = !string.IsNullOrWhiteSpace(d.FsType),

            // RAID
            IsRaidMember = isActiveRaid || isRaidFs,
            ArrayName = isActiveRaid ? arrayName : "",

            // ⭐ NUEVO: nombre real del disco
            PkName = d.PkName,

            Status = "OK"
        };

        disks.Add(info);
    }

    return disks;
}


    
    
    /* 
    public static List<RaidDiskInfo> GetAllDisks()
{
    var disks = new List<RaidDiskInfo>();

    var json = ShellHelper.EjecutarSinRoot(
        "lsblk -J -o NAME,TYPE,SIZE,MODEL,SERIAL,ROTA,MOUNTPOINTS,FSTYPE,PKNAME"
    ).Stdout;

    if (string.IsNullOrWhiteSpace(json))
        return disks;

    var parsed = ParseLsblk(json);

    var mdadmScan = ShellHelper.EjecutarComoRoot("mdadm --detail --scan").Stdout;

    foreach (var d in parsed)
    {
        if (d.Type != "disk")
            continue;

        // 1) Discos del sistema
        bool isSystem = false;

        if (d.MountPoints != null)
        {
            foreach (var mp in d.MountPoints)
            {
                if (mp == "/" || mp == "/boot" || mp == "/boot/efi" ||
                    mp.StartsWith("/var") || mp.StartsWith("/home"))
                {
                    isSystem = true;
                    break;
                }
            }
        }

        if (!isSystem && d.Children != null)
        {
            foreach (var child in d.Children)
            {
                if (child.MountPoints != null)
                {
                    foreach (var mp in child.MountPoints)
                    {
                        if (mp == "/" || mp == "/boot" || mp == "/boot/efi" ||
                            mp.StartsWith("/var") || mp.StartsWith("/home"))
                        {
                            isSystem = true;
                            break;
                        }
                    }
                }
                if (isSystem) break;
            }
        }

        if (isSystem)
            continue;

        // 2) Ignorar loop/zram
        if (d.Name.StartsWith("loop") || d.Name.StartsWith("zram"))
            continue;

        // 3) Detectar RAID
        bool isActiveRaid = MdadmService_IsDiskInArray(d.Name, out var arrayName);
        bool isRaidFs = string.Equals(d.FsType, "linux_raid_member", StringComparison.OrdinalIgnoreCase);

        if (isActiveRaid)
            continue;

        var info = new RaidDiskInfo
        {
            Name = d.Name,
            Model = d.Model,
            Size = d.Size,
            Serial = d.Serial,
            Filesystem = d.FsType,
            MountPoint = d.MountPoint,
            IsMounted = !string.IsNullOrWhiteSpace(d.MountPoint),
            IsRotational = d.Rotational == "1",
            IsNvme = d.Name.StartsWith("nvme"),
            HasPartitions = d.Children != null && d.Children.Count > 0,
            HasFileSystem = !string.IsNullOrWhiteSpace(d.FsType),
            IsRaidMember = isActiveRaid || isRaidFs,
            ArrayName = isActiveRaid ? arrayName : "",
            Status = "OK"
        };

        disks.Add(info);
    }

    return disks;
}

*/


    // ============================================================
    // PARSEAR JSON DE LSBKL (RECURSIVO)
    // ============================================================
    private static List<LsblkEntry> ParseLsblk(string json)
    {
        var list = new List<LsblkEntry>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("blockdevices");

        foreach (var dev in root.EnumerateArray())
            list.Add(ParseEntry(dev));

        return list;
    }

    private static LsblkEntry ParseEntry(JsonElement dev)
    {
        var entry = new LsblkEntry
        {
            Name = dev.GetProperty("name").GetString() ?? "",
            Type = dev.GetProperty("type").GetString() ?? "",
            Size = dev.GetProperty("size").GetString() ?? "",
            Model = dev.TryGetProperty("model", out var model) ? model.GetString() ?? "" : "",
            Serial = dev.TryGetProperty("serial", out var serial) ? serial.GetString() ?? "" : "",
            Rotational = dev.TryGetProperty("rota", out var rota) ? rota.GetRawText() : "0",
            FsType = dev.TryGetProperty("fstype", out var fs) ? fs.GetString() ?? "" : "",
            MountPoint = dev.TryGetProperty("mountpoint", out var mp) ? mp.GetString() ?? "" : ""
        };

        if (dev.TryGetProperty("mountpoints", out var mps) && mps.ValueKind == JsonValueKind.Array)
        {
            entry.MountPoints = mps.EnumerateArray()
                .Select(x => x.GetString() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        if (dev.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            entry.Children = children.EnumerateArray()
                .Select(ParseEntry)
                .ToList();
        }

        return entry;
    }

    // ============================================================
    // DETECTAR TIPO DE DISCO
    // ============================================================
    private static string GetDiskType(LsblkEntry d)
    {
        var model = d.Model?.ToLower() ?? "";
        var name = d.Name.ToLower();

        if (name.StartsWith("nvme"))
            return "NVMe";

        if (model.Contains("usb"))
            return "USB";

        if (model.Contains("fileio"))
            return "iSCSI";

        if (model.Contains("loop") || model.Contains("zram"))
            return "Virtual";

        if (d.Rotational == "1" || d.Rotational.Equals("true", StringComparison.OrdinalIgnoreCase))
            return "HDD";

        return "SSD";
    }

    // ============================================================
    // TEMPERATURA REAL (SMARTCTL)
    // ============================================================
    private static string GetTemperature(string name)
    {
        var r = ShellHelper.EjecutarComoRoot($"smartctl -A /dev/{name}");

        if (string.IsNullOrWhiteSpace(r.Stdout))
            return "N/A";

        var lines = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
            if (line.Contains("Temperature:", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("Celsius", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Sensor", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                    if (int.TryParse(p, out var temp))
                        return $"{temp}°C";
            }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("194") &&
                !trimmed.Contains("Temperature_Celsius", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var lastNumberBeforeParen = -1;
            foreach (var p in parts)
            {
                if (p.StartsWith("("))
                    break;

                if (int.TryParse(p, out var val))
                    lastNumberBeforeParen = val;
            }

            if (lastNumberBeforeParen >= 0)
                return $"{lastNumberBeforeParen}°C";
        }

        return "N/A";
    }

    // ============================================================
    // STATUS REAL DEL DISCO
    // ============================================================
    private static string GetDiskStatus(RaidDiskInfo info)
    {
        if (info.IsUsedByRaid)
            return "In RAID";

        var r = ShellHelper.EjecutarComoRoot($"smartctl -H /dev/{info.Name}");

        if (string.IsNullOrWhiteSpace(r.Stdout))
            return "Unknown";

        var output = r.Stdout.ToLower();

        if (output.Contains("passed"))
            return "Healthy";

        if (output.Contains("warning") || output.Contains("prefail"))
            return "Warning";

        if (output.Contains("failed"))
            return "Failed";

        return "Unknown";
    }

    // ============================================================
    // DETECTAR SI UN DISCO ESTÁ EN UN ARRAY RAID ACTIVO
    // ============================================================
    
    
    private static bool MdadmService_IsDiskInArray(string diskName, out string arrayName)
{
    arrayName = string.Empty;

    if (string.IsNullOrWhiteSpace(diskName))
        return false;

    // Normalizar nombre
    var disk = diskName.Trim().ToLowerInvariant();
    if (disk.StartsWith("/dev/"))
        disk = disk.Substring(5);

    var scanRes = ShellHelper.EjecutarComoRoot("mdadm --detail --scan");
    var scan = (scanRes.Stdout + "\n" + scanRes.Stderr).Trim();

    if (string.IsNullOrWhiteSpace(scan))
        return false;

    foreach (var raw in scan.Split('\n'))
    {
        var line = raw.Trim();
        if (!line.StartsWith("ARRAY", StringComparison.OrdinalIgnoreCase))
            continue;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            continue;

        var arrayPath = parts[1];
        var name = arrayPath.Split('/').Last();

        var detailRes = ShellHelper.EjecutarComoRoot($"mdadm --detail {arrayPath}");
        var detail = (detailRes.Stdout + "\n" + detailRes.Stderr);

        foreach (var r in detail.Split('\n'))
        {
            var l = r.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(l))
                continue;

            // ⭐ Ignorar discos que mdadm ya NO usa
            if (l.Contains("removed"))
                continue;

            if (l.Contains("faulty"))
                continue;

            // ⭐ Parsear tokens
            var tokens = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Si la línea contiene un /dev/XXX, procesarlo
            foreach (var t in tokens)
            {
                if (!t.StartsWith("/dev/"))
                    continue;

                var dev = t.Substring(5);

                // ⭐ Matching EXACTO
                if (dev == disk)
                {
                    arrayName = name;
                    return true;
                }
            }
        }
    }

    return false;
}





    
    
}

// ============================================================
// MODELO INTERNO PARA LSBKL
// ============================================================
public class LsblkEntry
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Size { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Rotational { get; set; } = "";
    public string PkName { get; set; }

    public List<string>? MountPoints { get; set; }
    public List<LsblkEntry>? Children { get; set; }

    public string FsType { get; set; } = "";
    public string MountPoint { get; set; } = "";
}
