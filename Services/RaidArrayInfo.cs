using System.Collections.Generic;
using RAID_Util.Models;

namespace RAID_Util.Services;

public class RaidArrayInfo
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string StateIcon { get; set; } = "";

    // NUEVO: estado del array
    public string State { get; set; } = "Healthy";
    // valores típicos:
    // Healthy, Degraded, Rebuilding, Resync, ReadOnly, Faulty

    // NUEVO: progreso de rebuild (0–100)
    public int RebuildProgress { get; set; } = 0;

    // NUEVO: tamaño total del array
    public string TotalSize { get; set; } = "";

    // NUEVO: tamaño usable
    public string UsableSize { get; set; } = "";

    // Lista de discos
    public List<RaidDiskInfo> Disks { get; set; } = new();

    // Para expandir/cerrar la tarjeta
    public bool IsExpanded { get; set; } = false;

    public bool IsMounted { get; set; }
    public bool PersistMount { get; set; }
    public bool AutoMount { get; set; }
    public string? MountPath { get; set; }


    public string ParitySize { get; set; } = "Unknown";
    public int AverageTemp { get; set; } = 0;
    public string DiskSummary { get; set; } = "";
    public string Uptime { get; set; } = "Unknown";

    public string RebuildETA { get; set; } = "";

    public string Path { get; set; } // ej: /dev/md0

    public bool IsSelected { get; set; } = false;
}