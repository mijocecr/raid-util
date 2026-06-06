using System.Collections.Generic;

namespace RAID_Util.Models;

public class RaidDiskInfo
{
    // Identidad
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Size { get; set; } = "";

    // Icono
    public string Icon { get; set; } = "";

    // Tipo físico
    public bool IsRotational { get; set; }
    public bool IsNvme { get; set; }
    public bool IsUsb { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsIscsi { get; set; }

    // Sistema de archivos
    public string FsType { get; set; } = "";

    // Montaje
    public string MountPath { get; set; } = "";
    public bool IsMounted => !string.IsNullOrWhiteSpace(MountPath);

    // RAID
    public string ArrayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string State { get; set; } = "UNKNOWN";
    public RaidMembership RaidMembership { get; set; } = RaidMembership.None;

    public bool IsUsedByRaid =>
        RaidMembership == RaidMembership.Active ||
        RaidMembership == RaidMembership.Spare ||
        RaidMembership == RaidMembership.Faulty ||
        RaidMembership == RaidMembership.Rebuilding ||
        RaidMembership == RaidMembership.Syncing;

    // Hijos (lsblk)
    public List<string> Children { get; set; } = new();

    // Sistema
    public bool IsSystemDisk { get; set; }
}

public enum RaidMembership
{
    None,
    Active,
    Spare,
    Faulty,
    Rebuilding,
    Syncing
}