using System.Diagnostics;
using System.Threading.Tasks;

namespace RAID_Util.Services;

public static class SystemStatusService
{
    private static async Task<string> RunCommand(string cmd, string args)
    {
        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            p.Start();
            string output = await p.StandardOutput.ReadToEndAsync();
            return output.Trim();
        }
        catch
        {
            return "N/A";
        }
    }

    public static Task<string> GetKernel() =>
        RunCommand("uname", "-r");

    public static async Task<string> GetDistro()
    {
        string text = await RunCommand("cat", "/etc/os-release");
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("PRETTY_NAME="))
                return line.Replace("PRETTY_NAME=", "").Replace("\"", "");
        }
        return "Unknown";
    }

    public static Task<string> GetHostname() =>
        RunCommand("hostname", "");

    public static async Task<string> GetServiceStatus(string service)
    {
        string result = await RunCommand("systemctl", $"is-active {service}");
        return result switch
        {
            "active" => "Active",
            "inactive" => "Inactive",
            "failed" => "Failed",
            _ => "Unknown"
        };
    }

    public static async Task<string> GetOverallHealth()
    {
        string mdadm = await GetServiceStatus("mdmonitor");
        string hotplug = await GetServiceStatus("udev");

        if (mdadm == "Active" && hotplug == "Active")
            return "Healthy";

        if (mdadm == "Failed" || hotplug == "Failed")
            return "Degraded";

        return "Unknown";
    }
}
