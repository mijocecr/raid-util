using System;

namespace RAID_Util.Services
{
    public static class DiskIconService
    {
        public static string GetIcon(string? name, string? model, bool isRotational)
        {
            // Si ya viene con ruta completa, respetarla
            if (!string.IsNullOrWhiteSpace(name) && name.Contains("avares://"))
                return name;

            string m = (model ?? "").ToLowerInvariant();
            string n = (name ?? "").ToLowerInvariant();

            // ============================
            // 1) NVMe (detección REAL)
            // ============================
            if (n.StartsWith("nvme"))
                return "avares://RAID-Util/Assets/Icons/disk-nvme.png";

            // ============================
            // 2) USB
            // ============================
            if (m.Contains("usb"))
                return "avares://RAID-Util/Assets/Icons/disk-usb.png";

            // ============================
            // 3) Discos virtuales (FileIO, loop, dm, nbd, zram…)
            // ============================
            if (m.Contains("virtual") ||
                m.Contains("vmware") ||
                m.Contains("qemu") ||
                m.Contains("vbox") ||
                m.Contains("loop") ||
                m.Contains("file") ||
                m.Contains("nbd") ||
                m.Contains("ram") ||
                m.Contains("zram") ||
                m.Contains("mapper") ||
                m.Contains("fuse") ||
                m.Contains("img") ||
                m.Contains("qcow") ||
                m.Contains("vmdk"))
            {
                return "avares://RAID-Util/Assets/Icons/disk-virtual.png";
            }

            // ============================
            // 4) HDD
            // ============================
            if (isRotational)
                return "avares://RAID-Util/Assets/Icons/disk-hdd.png";

            // ============================
            // 5) SSD SATA (default)
            // ============================
            return "avares://RAID-Util/Assets/Icons/disk-ssd.png";
        }
    }
}
