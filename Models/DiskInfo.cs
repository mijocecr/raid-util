public class DiskInfo
{
    public string Name { get; set; } = string.Empty;   // sda, nvme0n1, loop0, mapper/vg-lv
    public string Role { get; set; } = "Unknown";      // active, spare, faulty, rebuilding
    public string State { get; set; } = "Unknown";     // OK, FAULTY, WARN, UNKNOWN

    public bool IsRotational { get; set; }             // rota=1 → HDD, rota=0 → SSD
    public string Type { get; set; } = "unknown";      // disk, loop, lvm, raid, rom

    public string Size { get; set; } = "Unknown";      // 500G, 1.8T, etc.
    public string Model { get; set; } = "Unknown";     // Samsung SSD, Seagate HDD, etc.

    public string Icon
    {
        get
        {
            var n = Name.ToLower();

            // NVMe (siempre antes que nada)
            if (n.StartsWith("nvme"))
                return "avares://RAID-Util/Assets/Icons/disk-nvme.png";

            // Loop devices (snap, squashfs, AppImage, etc.)
            if (Type == "loop")
                return "avares://RAID-Util/Assets/Icons/disk-virtual.png";

            // LVM / mapper
            if (Type == "lvm" || n.Contains("mapper"))
                return "avares://RAID-Util/Assets/Icons/disk-virtual.png";

            // SSD SATA (rota=0)
            if (!IsRotational)
                return "avares://RAID-Util/Assets/Icons/disk-ssd.png";

            // HDD SATA (rota=1)
            if (IsRotational)
                return "avares://RAID-Util/Assets/Icons/disk-hdd.png";

            // Fallback
            return "avares://RAID-Util/Assets/Icons/disk-virtual.png";
        }
    }
}