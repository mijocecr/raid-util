namespace RAID_Util.Models;

public class AppConfig
{
    // Logs
    public string LogsPath { get; set; } = "~/.config/raid-util/logs";
    public int LogLevel { get; set; } = 1; // 0=ERROR, 1=INFO, 2=DEBUG
}