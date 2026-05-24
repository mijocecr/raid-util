namespace RAID_Util.Models
{
    public  class AppConfig
    {
        // Timers
        public  int GeneralRefreshMs { get; set; } = 2000;
        public  int RebuildRefreshMs { get; set; } = 500;
        public  int HotplugRefreshMs { get; set; } = 1500;

        // Logs
        public  string LogsPath { get; set; } = "/var/log/raid-util/";
        public  int LogLevel { get; set; } = 1;
    }
}