namespace RAID_Util.Services;

public static class DiskIconService
{
    public static string GetIcon(string? currentIcon, string? model, bool isRotational)
    {
        // Si ya viene con ruta completa, respetarla
        if (!string.IsNullOrWhiteSpace(currentIcon) && currentIcon.StartsWith("avares://"))
            return currentIcon;

        // Forzados desde la vista (sin cambiar firmas)
        var forced = (currentIcon ?? "").ToLowerInvariant();

        if (forced == "nvme")
            return "avares://RAID-Util/Assets/Icons/disk-nvme.png";

        if (forced == "usb")
            return "avares://RAID-Util/Assets/Icons/disk-usb.png";

        if (forced == "iscsi")
            return "avares://RAID-Util/Assets/Icons/disk-virtual.png";

        if (forced == "virtual")
            return "avares://RAID-Util/Assets/Icons/disk-virtual.png";

        // Heurísticas antiguas (fallback)
        var m = (model ?? "").ToLowerInvariant();

        // 1) NVMe por modelo
        if (m.Contains("nvme"))
            return "avares://RAID-Util/Assets/Icons/disk-nvme.png";

        // 2) USB por modelo
        if (m.Contains("usb") || m.Contains("flash") || m.Contains("pen"))
            return "avares://RAID-Util/Assets/Icons/disk-usb.png";

        // 3) iSCSI / FILEIO / LUN
        if (m.Contains("iscsi") || m.Contains("fileio") || m.Contains("lun"))
            return "avares://RAID-Util/Assets/Icons/disk-virtual.png";

        // 4) Virtual
        if (m.Contains("virtual") ||
            m.Contains("vmware") ||
            m.Contains("qemu") ||
            m.Contains("vbox") ||
            m.Contains("loop") ||
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

        // 5) HDD
        if (isRotational)
            return "avares://RAID-Util/Assets/Icons/disk-hdd.png";

        // 6) SSD SATA (default)
        return "avares://RAID-Util/Assets/Icons/disk-ssd.png";
    }
}
