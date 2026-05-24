public class DiskInfo
{
    public string Name { get; set; }
    public string State { get; set; }
    public string Role { get; set; }
    public string Size { get; set; }

    public string Icon
    {
        get
        {
            var n = Name.ToLower();

            // NVMe primero (porque empieza por "nvme")
            if (n.StartsWith("nvme"))
                return "avares://RAID-Util/Assets/Icons/disk-nvme.png";

            // SSD SATA (nombres típicos: sda, sdb, etc. pero con etiqueta SSD)
            if (n.Contains("ssd"))
                return "avares://RAID-Util/Assets/Icons/disk-ssd.png";

            // HDD SATA (sda, sdb, sdc…)
            if (n.StartsWith("sd"))
                return "avares://RAID-Util/Assets/Icons/disk-hdd.png";

            // Virtual / loop / mapper / etc.
            return "avares://RAID-Util/Assets/Icons/disk-virtual.png";
        }
    }
}