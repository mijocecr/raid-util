using System.Collections.Generic;

namespace RAID_Util.Models;

public enum RaidArrayState
{
    Unknown,
    Clean,
    Active,
    Degraded,
    Recovering,
    Rebuilding,
    Resync,
    Failed,
    ReadOnly
}

public class RaidArrayInfo
{
    // ============================================================
    // IDENTIDAD DEL ARRAY
    // ============================================================

    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    public string BaseName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
                return Name;

            var last = System.IO.Path.GetFileName(Path);

            if (last.Contains(":"))
                return last.Split(':')[^1];

            return last;
        }
    }

    public string Level { get; set; } = "";
    public RaidArrayState State { get; set; } = RaidArrayState.Unknown;
    public string StateIcon { get; set; } = "";

    // ============================================================
    // PROGRESO / ESTADÍSTICAS
    // ============================================================

    public int RebuildProgress { get; set; } = 0;
    public string RebuildETA { get; set; } = "";

    public string TotalSize { get; set; } = "";
    public string UsableSize { get; set; } = "";
    public string ParitySize { get; set; } = "Unknown";

    public int AverageTemp { get; set; } = 0;
    public string DiskSummary { get; set; } = "";
    public string Uptime { get; set; } = "Unknown";

    // ============================================================
    // DISK MEMBERS
    // ============================================================

    public List<RaidDiskInfo> Disks { get; set; } = new();

    // ============================================================
    // MOUNT / FSTAB
    // ============================================================

    public bool IsMounted { get; set; }
    public bool PersistMount { get; set; }
    public bool AutoMount { get; set; }
    public string? MountPath { get; set; }

    // ============================================================
    // UI FLAGS
    // ============================================================

    public bool IsExpanded { get; set; } = false;
    public bool IsSelected { get; set; } = false;

    // ============================================================
    // PROPIEDADES CALCULADAS (compatibles con mdadm)
    // ============================================================

    public bool IsDegraded =>
        State == RaidArrayState.Degraded ||
        State == RaidArrayState.Failed;

    public bool IsResyncing =>
        State == RaidArrayState.Resync ||
        State == RaidArrayState.Rebuilding;

    public bool IsRecovering =>
        State == RaidArrayState.Recovering;

    public bool IsChecking =>
        StateIcon.Contains("check", System.StringComparison.OrdinalIgnoreCase);

    public bool IsRepairing =>
        StateIcon.Contains("repair", System.StringComparison.OrdinalIgnoreCase);

    public bool IsActive =>
        State == RaidArrayState.Active ||
        State == RaidArrayState.Clean;

    public bool IsClean =>
        State == RaidArrayState.Clean;

    // ============================================================
    // CAPACIDADES DEL ARRAY
    // ============================================================

    public bool SupportsCheck =>
        Level is "raid1" or "raid5" or "raid6" or "raid10";

    public bool SupportsRepair =>
        Level is "raid1" or "raid5" or "raid6" or "raid10";
}
