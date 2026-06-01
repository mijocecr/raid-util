using System.Collections.Generic;

namespace RAID_Util.Models;

public class RaidDiskInfo
{
    // ============================================================
    // IDENTIFICACIÓN BÁSICA
    // ============================================================
    public string Name { get; set; } = string.Empty; // /dev/sda, /dev/nvme0n1
    public string Model { get; set; } = "Unknown"; // Samsung SSD, Seagate HDD, etc.
    public string Size { get; set; } = "Unknown"; // 500G, 1.8T, etc.

    // Tipo físico / categoría
    public string Type { get; set; } = "Unknown"; // disk, rom, loop, etc.
    public bool IsRotational { get; set; } = false; // HDD = true, SSD/NVMe = false

    // Información hardware adicional
    public string Serial { get; set; } = "Unknown"; // Número de serie
    public string Temperature { get; set; } = "N/A"; // 34°C, 41°C, etc.

    // ============================================================
    // ESTADO RAID
    // ============================================================
    public string Role { get; set; } = "unknown"; // active, spare, faulty, rebuilding
    public string State { get; set; } = "UNKNOWN"; // OK, FAULTY, WARN, UNKNOWN
    public string ArrayName { get; set; } = string.Empty;

    // ============================================================
    // ICONO GUI
    // ============================================================
    public string Icon { get; set; } = string.Empty;

    // ============================================================
    // FLAGS DE SEGURIDAD (para Create Array)
    // ============================================================
    public bool IsSystemDisk { get; set; } = false; // Contiene /, /boot, EFI, etc.
    public bool IsBoot { get; set; } = false; // /boot o EFI
    public bool IsRoot { get; set; } = false; // /
    public bool IsHome { get; set; } = false; // /home
    public bool IsSwap { get; set; } = false; // swap

    public bool IsMounted { get; set; } = false; // disco montado
    public bool IsUsedByRaid { get; set; } = false; // pertenece a un array RAID

    // ⭐ NUEVO: metadata RAID detectada por mdadm --examine
    public bool HasRaidMetadata { get; set; } = false;

    // ⭐ NUEVO: lista de particiones hijas (sda1, sda2…)
    public List<string> Children { get; set; } = new();

    // ============================================================
    // INFORMACIÓN DE MONTADO / FS
    // ============================================================
    public string MountPoint { get; set; } = string.Empty; // /mnt/data, /, /boot, etc.
    public string Filesystem { get; set; } = string.Empty; // ext4, xfs, btrfs, etc.
    public string Status { get; set; } = string.Empty;
    public bool IsUsb { get; set; }
    public bool IsIscsi { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsNvme { get; set; }
    public bool HasPartitions { get; set; }
    public bool HasFileSystem { get; set; }
    public bool HasValidPartitionTable { get; set; }
    public bool IsRaidInactiveMember { get; set; }
    public bool IsRaidMember { get; set; }


    // ============================================================
    // DEBUG
    // ============================================================
    public override string ToString()
    {
        return $"{Name} | {Model} | {Size} | {Type} | Array={ArrayName} | Role={Role} | State={State}";
    }
}