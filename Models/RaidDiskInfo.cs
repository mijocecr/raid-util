namespace RAID_Util.Models
{
    public class RaidDiskInfo
    {
        public string Name { get; set; } = string.Empty;     // /dev/sda, /dev/nvme0n1
        public string Role { get; set; } = "unknown";        // active, spare, faulty, rebuilding
        public string State { get; set; } = "UNKNOWN";       // OK, FAULTY, WARN, UNKNOWN

        public string Size { get; set; } = "Unknown";        // 500G, 1.8T, etc.
        public string Model { get; set; } = "Unknown";       // Samsung SSD, Seagate HDD, etc.

        public string Icon { get; set; } = string.Empty;     // icono asignado por la GUI

        public bool IsRotational { get; set; } = false;
        
        public string ArrayName { get; set; } = string.Empty;

        public override string ToString()
            => $"{Name} | Array={ArrayName} | Role={Role} | State={State}";
    }
}