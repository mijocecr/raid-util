namespace RAID_Util.Models;

public class ArrayConfig
{
    // Identity
    public string Name { get; set; } = "";
    public string FsLabel { get; set; } = "";

    // Mount
    public string MountPoint { get; set; } = "";
    public bool Mount_NoAtime { get; set; } = false;
    public bool Mount_NoDirAtime { get; set; } = false;
    public bool Mount_Discard { get; set; } = false;
    public bool Mount_Sync { get; set; } = false;
    public bool Mount_ReadOnly { get; set; } = false;
    public bool AutoMount { get; set; } = true;

    // Performance
    public int ResyncPriority { get; set; } = 1000;
    public int ResyncMaxSpeed { get; set; } = 200000;

    // Alerts
    public bool AlertDegraded { get; set; } = true;
    public bool AlertDiskFail { get; set; } = true;
    public bool AlertSlowResync { get; set; } = true;
    public string MountPermissions { get; set; } = "777";
}
