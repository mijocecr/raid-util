namespace RAID_Util.Models
{
    public class ArrayConfig
    {
        // ============================
        // IDENTITY
        // ============================
        public string Name { get; set; } = "";
        public string FsLabel { get; set; } = "";

        // ============================
        // MOUNT OPTIONS
        // ============================
        public string MountPoint { get; set; } = "";
        public bool Mount_NoAtime { get; set; } = false;
        public bool Mount_NoDirAtime { get; set; } = false;
        public bool Mount_Discard { get; set; } = false;
        public bool Mount_Sync { get; set; } = false;
        public bool Mount_ReadOnly { get; set; } = false;

        // ⭐ Persistencia REAL (fstab)
        public bool PersistMount { get; set; } = false;

        // ⭐ Permisos chmod aplicados al mountpoint
        public string MountPermissions { get; set; } = "755";

        // ============================
        // PERFORMANCE
        // ============================
        public int ResyncPriority { get; set; } = 1000;
        public int ResyncMaxSpeed { get; set; } = 200000;

        // ============================
        // ALERTS
        // ============================
        public bool AlertDegraded { get; set; } = true;
        public bool AlertDiskFail { get; set; } = true;
        public bool AlertSlowResync { get; set; } = true;
    }
}