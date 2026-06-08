using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

public class ArrayRiskInfo
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
}

public class DiskAlertInfo
{
    public string Device { get; set; } = "";
    public string Alert { get; set; } = "";
}

public class StatusService
{
    // ============================================================
    // ROOT EXECUTION WRAPPER (MEJORADO)
    // ============================================================
    private async Task<string> RunRoot(string cmd)
    {
        Console.WriteLine($"[STATUS-SERVICE] RunRoot(): {cmd}");

        var (exit, stdout, stderr) = await Task.Run(() =>
            ShellHelper.EjecutarComoRoot(cmd)
        );

        Console.WriteLine($"[STATUS-SERVICE] RunRoot exit={exit}");
        Console.WriteLine($"[STATUS-SERVICE] stdout:\n{stdout}");
        Console.WriteLine($"[STATUS-SERVICE] stderr:\n{stderr}");

        // No devolver vacío si exit != 0
        if (string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(stderr))
            return stderr;

        return stdout ?? "";
    }

    // ============================================================
    // /proc/mdstat
    // ============================================================
    private string ReadMdstat()
    {
        try
        {
            var txt = File.ReadAllText("/proc/mdstat");
            Console.WriteLine("[STATUS-SERVICE] /proc/mdstat:");
            Console.WriteLine(txt);
            return txt;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS-SERVICE] ERROR reading /proc/mdstat: {ex}");
            return "";
        }
    }

    private async Task<string> GetDetailAsync(string array)
    {
        Console.WriteLine($"[STATUS-SERVICE] mdadm --detail {array}");
        return await RunRoot($"mdadm --detail {array}");
    }

    // ============================================================
    // PARSE ARRAYS (CORREGIDO)
    // ============================================================
    private List<string> ParseArrays(string mdstat)
    {
        var list = new List<string>();

        foreach (var line in mdstat.Split('\n'))
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("md") && trimmed.Contains(" :"))
            {
                var name = trimmed.Split(' ')[0].Trim();
                list.Add($"/dev/{name}");
            }
        }

        Console.WriteLine($"[STATUS-SERVICE] Arrays detected: {string.Join(", ", list)}");
        return list;
    }

    // ============================================================
    // RAID HEALTH (CORREGIDO)
    // ============================================================
    public async Task<string> GetOverallRaidHealthAsync()
    {
        var arrays = ParseArrays(ReadMdstat());
        if (arrays.Count == 0)
            return "No RAID Detected";

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);
            if (string.IsNullOrWhiteSpace(detail))
                continue;

            var d = detail.ToLower();

            if (d.Contains("degraded") || d.Contains("faulty"))
                return "Critical";

            // Rebuild REAL
            if (d.Contains("resync =") ||
                d.Contains("rebuild =") ||
                d.Contains("finish=") ||
                d.Contains("speed="))
                return "Warning";
        }

        return "Healthy";
    }

    // ============================================================
    // ARRAYS SUMMARY
    // ============================================================
    public async Task<string> GetArraysSummaryAsync()
    {
        var arrays = ParseArrays(ReadMdstat());
        return arrays.Count == 0 ? "No RAID Detected" : $"Total: {arrays.Count}";
    }

    // ============================================================
    // DISKS SUMMARY
    // ============================================================
    public async Task<string> GetDisksSummaryAsync()
    {
        var arrays = ParseArrays(ReadMdstat());
        if (arrays.Count == 0)
            return "No RAID Detected";

        int active = 0, failed = 0;

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);
            if (string.IsNullOrWhiteSpace(detail))
                continue;

            active += Count(detail, "active sync");
            failed += Count(detail, "faulty");
        }

        int total = active + failed;
        return $"Total: {total} | Active: {active} | Failed: {failed}";
    }

    // ============================================================
    // REBUILD SUMMARY (CORREGIDO)
    // ============================================================
    private bool IsRealRebuild(string detail)
    {
        var d = detail.ToLower();
        return d.Contains("resync =") ||
               d.Contains("rebuild =") ||
               d.Contains("finish=") ||
               d.Contains("speed=");
    }

    private string ExtractRebuildLine(string detail)
    {
        foreach (var line in detail.Split('\n'))
        {
            var l = line.ToLower();
            if (l.Contains("resync =") ||
                l.Contains("rebuild =") ||
                l.Contains("finish=") ||
                l.Contains("speed="))
                return line.Trim();
        }
        return "";
    }

    public async Task<string> GetRebuildSummaryAsync()
    {
        var arrays = ParseArrays(ReadMdstat());
        if (arrays.Count == 0)
            return "No RAID Detected";

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);
            if (string.IsNullOrWhiteSpace(detail))
                continue;

            if (!IsRealRebuild(detail))
                continue;

            var line = ExtractRebuildLine(detail);
            if (!string.IsNullOrWhiteSpace(line))
                return $"{array}: {line}";
        }

        return "No rebuild in progress";
    }

    // ============================================================
    // ARRAYS AT RISK
    // ============================================================
    public async Task<IList<ArrayRiskInfo>> GetArraysAtRiskAsync()
    {
        var list = new List<ArrayRiskInfo>();
        var arrays = ParseArrays(ReadMdstat());

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);
            if (string.IsNullOrWhiteSpace(detail))
                continue;

            var d = detail.ToLower();

            if (d.Contains("degraded"))
                list.Add(new ArrayRiskInfo { Name = array, Status = "DEGRADED", Details = "Array is degraded" });

            if (d.Contains("faulty"))
                list.Add(new ArrayRiskInfo { Name = array, Status = "FAILED DISK", Details = "One or more disks are faulty" });
        }

        return list;
    }

    // ============================================================
    // DISK ALERTS
    // ============================================================
    public async Task<IList<DiskAlertInfo>> GetDiskAlertsAsync()
    {
        var list = new List<DiskAlertInfo>();
        var arrays = ParseArrays(ReadMdstat());

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);
            if (string.IsNullOrWhiteSpace(detail))
                continue;

            if (detail.ToLower().Contains("faulty"))
                list.Add(new DiskAlertInfo { Device = array, Alert = "Faulty disk detected" });
        }

        return list;
    }

    // ============================================================
    // RECENT EVENTS (CORREGIDO)
    // ============================================================
    public async Task<IList<string>> GetRecentEventsAsync()
    {
        var list = new List<string>();
        var arrays = ParseArrays(ReadMdstat());

        if (arrays.Count == 0)
        {
            list.Add("No RAID detected.");
            return list;
        }

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);
            if (string.IsNullOrWhiteSpace(detail))
                continue;

            var d = detail.ToLower();

            if (d.Contains("faulty"))
                list.Add($"{array}: Faulty disk detected");

            if (d.Contains("degraded"))
                list.Add($"{array}: Array degraded");

            // Rebuild REAL
            if (d.Contains("resync =") ||
                d.Contains("rebuild =") ||
                d.Contains("finish=") ||
                d.Contains("speed="))
                list.Add($"{array}: Rebuild in progress");
        }

        return list;
    }

    // ============================================================
    // SYSTEM INFO
    // ============================================================
    public string GetSessionUptime()
    {
        try
        {
            var (exit, stdout, stderr) = ShellHelper.EjecutarSinRoot("uptime -p");
            if (!string.IsNullOrWhiteSpace(stdout))
                return stdout.Trim();
            return "Unknown";
        }
        catch { return "Unknown"; }
    }

    public int GetCpuUsage()
    {
        try
        {
            var p1 = File.ReadAllText("/proc/stat").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long idle1 = long.Parse(p1[4]);
            long total1 = p1.Skip(1).Take(7).Sum(x => long.Parse(x));

            Task.Delay(120).Wait();

            var p2 = File.ReadAllText("/proc/stat").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long idle2 = long.Parse(p2[4]);
            long total2 = p2.Skip(1).Take(7).Sum(x => long.Parse(x));

            long idleDelta = idle2 - idle1;
            long totalDelta = total2 - total1;

            return (int)(100 * (totalDelta - idleDelta) / totalDelta);
        }
        catch { return 0; }
    }

    public int GetMemoryUsage()
    {
        try
        {
            long total = 0, available = 0;

            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal"))
                    total = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);

                if (line.StartsWith("MemAvailable"))
                    available = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
            }

            long used = total - available;
            return (int)((used * 100) / total);
        }
        catch { return 0; }
    }

    public int GetDiskFree()
    {
        try
        {
            var info = new DriveInfo("/");
            return (int)(info.AvailableFreeSpace / 1024 / 1024 / 1024);
        }
        catch { return 0; }
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private int Count(string text, string token)
    {
        return text.Split(token, StringSplitOptions.None).Length - 1;
    }
}
