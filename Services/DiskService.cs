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
            // Mostrar solo disk y part
            if (d.Type != "disk" && d.Type != "part")
                continue;

            // ============================================================
            // DETECCIÓN REAL DE DISCO DEL SISTEMA
            // ============================================================
            bool isSystem = false;

            // 1) mountpoints del propio disco
            if (d.MountPoints != null)
            {
                foreach (var mp in d.MountPoints)
                {
                    if (mp == "/" ||
                        mp == "/boot" ||
                        mp == "/boot/efi" ||
                        mp.StartsWith("/var") ||
                        mp.StartsWith("/home"))
                    {
                        isSystem = true;
                        break;
                    }
                }
            }

            // 2) mountpoints de las particiones
            if (!isSystem && d.Children != null)
            {
                foreach (var child in d.Children)
                {
                    if (child.MountPoints != null)
                    {
                        foreach (var mp in child.MountPoints)
                        {
                            if (mp == "/" ||
                                mp == "/boot" ||
                                mp == "/boot/efi" ||
                                mp.StartsWith("/var") ||
                                mp.StartsWith("/home"))
                            {
                                isSystem = true;
                                break;
                            }
                        }
                    }
                    if (isSystem) break;
                }
            }

            // Ignorar discos del sistema
            if (isSystem)
                continue;

            // Ignorar discos que están en un array activo
            var isActiveRaidMember = MdadmService_IsDiskInArray(d.Name, out var arrayName);
            if (isActiveRaidMember)
                continue;

            // Ignorar virtuales
            if (d.Name.StartsWith("zram", StringComparison.OrdinalIgnoreCase) ||
                d.Name.StartsWith("loop", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isRot =
                d.Rotational == "1" ||
                d.Rotational.Equals("true", StringComparison.OrdinalIgnoreCase);

            var info = new RaidDiskInfo
            {
                Name = d.Name,
                Model = d.Model,
                Size = d.Size,
                Serial = d.Serial,
                Filesystem = d.FsType,
                MountPoint = d.MountPoint,
                IsMounted = !string.IsNullOrWhiteSpace(d.MountPoint),
                IsRotational = isRot,
                Type = GetDiskType(d),
                Temperature = GetTemperature(d.Name),

                Icon = DiskIconService.GetIcon(d.Name, d.Model, isRot),

                // RAID
                IsUsedByRaid = false,
                ArrayName = "",
                Role = "none",
                Status = "OK"
            };

            // ============================================================
            // DETECTAR DISCOS REMOVIDOS / FAULTY
            // ============================================================
            var mdadmInfo = ShellHelper.EjecutarComoRoot($"mdadm --examine /dev/{d.Name}").Stdout;

            if (!string.IsNullOrWhiteSpace(mdadmInfo))
            {
                if (mdadmInfo.Contains("MBR Magic", StringComparison.OrdinalIgnoreCase) &&
                    mdadmInfo.Contains("type ee", StringComparison.OrdinalIgnoreCase))
                {
                    info.Status = "FAULTY";
                    info.Role = "removed";
                }
                else if (mdadmInfo.Contains("Raid Level", StringComparison.OrdinalIgnoreCase))
                {
                    info.Status = "FAULTY";
                    info.Role = "faulty";
                }
            }

            if (info.Status == "OK")
                info.Status = GetDiskStatus(info);

            disks.Add(info);
        }

        return disks;
    }

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

        // mountpoints (plural)
        if (dev.TryGetProperty("mountpoints", out var mps) && mps.ValueKind == JsonValueKind.Array)
        {
            entry.MountPoints = mps.EnumerateArray()
                .Select(x => x.GetString() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        // children
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

        // NVMe
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

        // HDD/SSD atributo 194
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
    // DETECTAR SI UN DISCO ESTÁ EN UN ARRAY RAID
    // ============================================================
    private static bool MdadmService_IsDiskInArray(string diskName, out string arrayName)
    {
        arrayName = string.Empty;
        var disk = diskName.Trim().ToLowerInvariant();

        var scanRes = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --detail --scan");
        var scan = (scanRes.Stdout + "\n" + scanRes.Stderr).Trim();

        if (string.IsNullOrWhiteSpace(scan))
            return false;

        foreach (var raw in scan.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("ARRAY"))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var arrayPath = parts[1];
            var name = arrayPath.Split('/').Last();

            var detailRes = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail {arrayPath}");
            var detail = (detailRes.Stdout + "\n" + detailRes.Stderr).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(detail))
                continue;

            foreach (var r in detail.Split('\n'))
            {
                var l = r.Trim().ToLowerInvariant();

                if (!l.Contains($"/dev/{disk}"))
                    continue;

                if (l.Contains("faulty") || l.Contains("removed"))
                    continue;

                arrayName = name;
                return true;
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

    public List<string>? MountPoints { get; set; }
    public List<LsblkEntry>? Children { get; set; }

    public string FsType { get; set; } = "";
    public string MountPoint { get; set; } = "";
}
