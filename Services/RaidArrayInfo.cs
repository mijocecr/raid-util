using System.Collections.Generic;

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
}





public class RaidDiskInfo
{
    public string Name { get; set; }
    public string Role { get; set; }
    public string State { get; set; }
    public string Icon { get; set; }
}
