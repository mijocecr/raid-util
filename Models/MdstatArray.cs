using System.Collections.Generic;

namespace RAID_Util.Models;

public class MdstatArray
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string State { get; set; } = "";
    public List<string> Devices { get; set; } = new();
    public string Flags { get; set; } = "";
    public string? RebuildProgress { get; set; }
    public string? RebuildEta { get; set; }

    public string Progress { get; set; } = "";


    public string Status { get; set; } = "";

    public string MetadataVersion { get; set; } = "";
    public string Size { get; set; } = "";
}