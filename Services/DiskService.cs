using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class DiskService
{
    // ============================================================
    // MÉTODO PRINCIPAL
    // ============================================================
    
    public static List<RaidDiskInfo> GetAllDisks()
    {
        var list = new List<RaidDiskInfo>();

        // ⭐ Blindaje: NO ejecutar lsblk antes de validar sudo
        if (!Credentials.AllowRaidCalls)
        {
            LogService.Debug("[RAID] GetAllDisks blocked → AllowRaidCalls = false");
            return list;
        }

        var json = ShellHelper.EjecutarSinRoot(
            "lsblk -J -o NAME,TYPE,SIZE,MODEL,SERIAL,ROTA,FSTYPE,MOUNTPOINT,PKNAME,PTTYPE,TRAN"
        ).Stdout;

        if (string.IsNullOrWhiteSpace(json))
            return list;

        var entries = ParseLsblk(json);

        foreach (var dev in entries)
        {
            // Solo discos físicos
            if (dev.Type != "disk")
                continue;

            // Ignorar loop/zram
            if (dev.Name.StartsWith("loop") || dev.Name.StartsWith("zram"))
                continue;

            var info = new RaidDiskInfo
            {
                // Identidad
                Name = dev.Name,
                Model = dev.Model,
                Serial = dev.Serial,
                Size = dev.Size,

                // Tipo físico
                IsRotational = dev.Rotational == "1",
                IsNvme = dev.Name.StartsWith("nvme"),
                IsUsb = dev.Transport == "usb",
                IsVirtual = IsVirtualDisk(dev.Model),
                IsIscsi = dev.Transport == "iscsi",

                // Sistema de archivos
                FsType = dev.FsType ?? "",

                // Montaje
                MountPath = dev.MountPoint ?? "",

                // Hijos (particiones)
                Children = dev.Children?.Select(c => c.Name).ToList() ?? new List<string>(),

                // RAID (se completará en RaidService)
                ArrayName = "",
                Role = "",
                State = "UNKNOWN",
                RaidMembership = RaidMembership.None,

                // Sistema
                IsSystemDisk = SystemDiskDetector.IsSystemDisk(dev.Name),

                // Icono (se normaliza en DisksView)
                Icon = ""
            };

            list.Add(info);
        }

        return list;
    }


    // ============================================================
    // DETECTAR SI ES DISCO VIRTUAL
    // ============================================================
    private static bool IsVirtualDisk(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        model = model.ToLower();

        return model.Contains("vmware") ||
               model.Contains("vbox") ||
               model.Contains("virtual") ||
               model.Contains("qemu") ||
               model.Contains("hyper-v") ||
               model.Contains("virtio");
    }

    // ============================================================
    // PARSEAR LSBKL (RECURSIVO)
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
            MountPoint = dev.TryGetProperty("mountpoint", out var mp) ? mp.GetString() ?? "" : "",
            PkName = dev.TryGetProperty("pkname", out var pk) ? pk.GetString() ?? "" : "",
            PartitionTable = dev.TryGetProperty("pttype", out var pt) ? pt.GetString() ?? "" : "",
            Transport = dev.TryGetProperty("tran", out var tr) ? tr.GetString() ?? "" : ""
        };

        if (dev.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            entry.Children = children.EnumerateArray()
                .Select(ParseEntry)
                .ToList();
        }

        return entry;
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
    public string FsType { get; set; } = "";
    public string MountPoint { get; set; } = "";
    public string PkName { get; set; } = "";
    public string PartitionTable { get; set; } = "";
    public string Transport { get; set; } = "";

    public List<LsblkEntry>? Children { get; set; }
}
