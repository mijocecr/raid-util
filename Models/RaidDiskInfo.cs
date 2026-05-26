namespace RAID_Util.Models
{
    public class RaidDiskInfo
    {
        // Identificación básica
        public string Name { get; set; } = string.Empty;     // /dev/sda, /dev/nvme0n1
        public string Model { get; set; } = "Unknown";       // Samsung SSD, Seagate HDD, etc.
        public string Size { get; set; } = "Unknown";        // 500G, 1.8T, etc.

        // Estado RAID
        public string Role { get; set; } = "unknown";        // active, spare, faulty, rebuilding
        public string State { get; set; } = "UNKNOWN";       // OK, FAULTY, WARN, UNKNOWN
        public string ArrayName { get; set; } = string.Empty;

        // Icono asignado por la GUI
        public string Icon { get; set; } = string.Empty;

        // Tipo físico
        public bool IsRotational { get; set; } = false;      // HDD = true, SSD/NVMe = false

        // ⭐ Seguridad: flags críticos para Create Array
        public bool IsSystemDisk { get; set; } = false;      // Contiene /, /boot, EFI, etc.
        public bool IsBoot { get; set; } = false;            // /boot o EFI
        public bool IsRoot { get; set; } = false;            // /
        public bool IsHome { get; set; } = false;            // /home
        public bool IsSwap { get; set; } = false;            // swap
        public bool IsMounted { get; set; } = false;         // cualquier punto de montaje
        public bool IsUsedByRaid { get; set; } = false;      // pertenece a otro array
        public bool IsUsbSystemSource { get; set; } = false; // si RAID-util corre desde USB

        // ⭐ Información adicional útil
        public string MountPoint { get; set; } = string.Empty;   // /mnt/data, /, /boot, etc.
        public string Filesystem { get; set; } = string.Empty;   // ext4, xfs, btrfs, etc.

        public override string ToString()
            => $"{Name} | Array={ArrayName} | Role={Role} | State={State}";
    }
}