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
    // ROOT EXECUTION WRAPPER
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

        if (exit != 0)
            return "";

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

    private List<string> ParseArrays(string mdstat)
    {
        var list = new List<string>();

        foreach (var line in mdstat.Split('\n'))
        {
            if (line.StartsWith("md"))
            {
                var name = line.Split(' ')[0].Trim();
                list.Add($"/dev/{name}");
            }
        }

        Console.WriteLine($"[STATUS-SERVICE] Arrays detected: {string.Join(", ", list)}");
        return list;
    }

    // ============================================================
    // RAID HEALTH
    // ============================================================
    public async Task<string> GetOverallRaidHealthAsync()
    {
        Console.WriteLine("[STATUS-SERVICE] GetOverallRaidHealthAsync()");

        var mdstat = ReadMdstat();
        var arrays = ParseArrays(mdstat);

        if (arrays.Count == 0)
            return "No RAID Detected";

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);

            if (string.IsNullOrWhiteSpace(detail))
                continue;

            if (detail.Contains("State : degraded"))
                return "Critical";

            if (detail.Contains("faulty"))
                return "Critical";

            if (detail.Contains("rebuild") || detail.Contains("resync"))
                return "Warning";
        }

        return "Healthy";
    }

    // ============================================================
    // ARRAYS SUMMARY
    // ============================================================
    public async Task<string> GetArraysSummaryAsync()
    {
        Console.WriteLine("[STATUS-SERVICE] GetArraysSummaryAsync()");

        var mdstat = ReadMdstat();
        var arrays = ParseArrays(mdstat);

        if (arrays.Count == 0)
            return "No RAID Detected";

        return $"Total: {arrays.Count}";
    }

    // ============================================================
    // DISKS SUMMARY
    // ============================================================
    public async Task<string> GetDisksSummaryAsync()
    {
        Console.WriteLine("[STATUS-SERVICE] GetDisksSummaryAsync()");

        var mdstat = ReadMdstat();
        var arrays = ParseArrays(mdstat);

        if (arrays.Count == 0)
            return "No RAID Detected";

        int active = 0, failed = 0;

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);

            if (string.IsNullOrWhiteSpace(detail))
                continue;

            int a = Count(detail, "active sync");
            int f = Count(detail, "faulty");

            Console.WriteLine($"[STATUS-SERVICE] {array}: active={a}, faulty={f}");

            active += a;
            failed += f;
        }

        int total = active + failed;
        return $"Total: {total} | Active: {active} | Failed: {failed}";
    }

    // ============================================================
    // REBUILD SUMMARY
    // ============================================================
    
    // ============================================================
// REBUILD SUMMARY (REAL, SIN FALSOS POSITIVOS)
// ============================================================

private bool IsRealRebuild(string detail)
{
    // 🔥 Un rebuild REAL siempre contiene alguno de estos patrones:
    return
        detail.Contains("resync =") ||        // resync = 12.3%
        detail.Contains("rebuild =") ||       // rebuild = 45.6%
        detail.Contains("Rebuild Status") ||  // Rebuild Status : 12% complete
        detail.Contains("finish=") ||         // finish=123.4min
        detail.Contains("speed=");            // speed=123MB/s
}

private string ExtractRebuildLine(string detail)
{
    foreach (var line in detail.Split('\n'))
    {
        if (line.Contains("resync =") ||
            line.Contains("rebuild =") ||
            line.Contains("Rebuild Status") ||
            line.Contains("finish=") ||
            line.Contains("speed="))
        {
            Console.WriteLine($"[STATUS-SERVICE] Rebuild line detected → {line.Trim()}");
            return line.Trim();
        }
    }

    return "";
}

public async Task<string> GetRebuildSummaryAsync()
{
    Console.WriteLine("[STATUS-SERVICE] GetRebuildSummaryAsync()");

    var mdstat = ReadMdstat();
    var arrays = ParseArrays(mdstat);

    if (arrays.Count == 0)
        return "No RAID Detected";

    foreach (var array in arrays)
    {
        Console.WriteLine($"[STATUS-SERVICE] Checking rebuild for {array}");

        var detail = await GetDetailAsync(array);

        if (string.IsNullOrWhiteSpace(detail))
        {
            Console.WriteLine("[STATUS-SERVICE] Empty mdadm detail, skipping.");
            continue;
        }

        //  IGNORAR "Consistency Policy : resync"
        if (!IsRealRebuild(detail))
        {
            Console.WriteLine("[STATUS-SERVICE] No real rebuild detected.");
            continue;
        }

        //  Extraer la línea real del rebuild
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
        Console.WriteLine("[STATUS-SERVICE] GetArraysAtRiskAsync()");

        var list = new List<ArrayRiskInfo>();
        var mdstat = ReadMdstat();
        var arrays = ParseArrays(mdstat);

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);

            if (string.IsNullOrWhiteSpace(detail))
                continue;

            if (detail.Contains("State : degraded"))
            {
                list.Add(new ArrayRiskInfo
                {
                    Name = array,
                    Status = "DEGRADED",
                    Details = "Array is degraded"
                });
            }

            if (detail.Contains("faulty"))
            {
                list.Add(new ArrayRiskInfo
                {
                    Name = array,
                    Status = "FAILED DISK",
                    Details = "One or more disks are faulty"
                });
            }
        }

        return list;
    }

    // ============================================================
    // DISK ALERTS
    // ============================================================
    public async Task<IList<DiskAlertInfo>> GetDiskAlertsAsync()
    {
        Console.WriteLine("[STATUS-SERVICE] GetDiskAlertsAsync()");

        var list = new List<DiskAlertInfo>();
        var mdstat = ReadMdstat();
        var arrays = ParseArrays(mdstat);

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array);

            if (string.IsNullOrWhiteSpace(detail))
                continue;

            if (detail.Contains("faulty"))
            {
                list.Add(new DiskAlertInfo
                {
                    Device = array,
                    Alert = "Faulty disk detected"
                });
            }
        }

        return list;
    }

    // ============================================================
    // RECENT EVENTS
    // ============================================================
    public async Task<IList<string>> GetRecentEventsAsync()
    {
        Console.WriteLine("[STATUS-SERVICE] GetRecentEventsAsync()");

        var list = new List<string>();
        var mdstat = ReadMdstat();
        var arrays = ParseArrays(mdstat);

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

            if (detail.Contains("faulty"))
                list.Add($"{array}: Faulty disk detected");

            if (detail.Contains("State : degraded"))
                list.Add($"{array}: Array degraded");

            if (detail.Contains("rebuild") || detail.Contains("resync"))
                list.Add($"{array}: Rebuild in progress");
        }

        return list;
    }

    // ============================================================
    // SYSTEM INFO (FIXED)
    // ============================================================
    public string GetSystemUptime()
    {
        try
        {
            var seconds = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0]);
            var ts = TimeSpan.FromSeconds(seconds);

            Console.WriteLine($"[STATUS-SERVICE] Uptime raw seconds={seconds}");

            return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS-SERVICE] ERROR uptime: {ex}");
            return "Unknown";
        }
    }

    public int GetCpuUsage()
    {
        try
        {
            var parts1 = File.ReadAllText("/proc/stat").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long idle1 = long.Parse(parts1[4]);
            long total1 = parts1.Skip(1).Take(7).Sum(x => long.Parse(x));

            Task.Delay(120).Wait();

            var parts2 = File.ReadAllText("/proc/stat").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long idle2 = long.Parse(parts2[4]);
            long total2 = parts2.Skip(1).Take(7).Sum(x => long.Parse(x));

            long idleDelta = idle2 - idle1;
            long totalDelta = total2 - total1;

            int usage = (int)(100 * (totalDelta - idleDelta) / totalDelta);

            Console.WriteLine($"[STATUS-SERVICE] CPU usage={usage}%");

            return usage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS-SERVICE] ERROR CPU: {ex}");
            return 0;
        }
    }

    public int GetMemoryUsage()
    {
        try
        {
            long total = 0;
            long available = 0;

            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal"))
                    total = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);

                if (line.StartsWith("MemAvailable"))
                    available = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
            }

            long used = total - available;
            int percent = (int)((used * 100) / total);

            Console.WriteLine($"[STATUS-SERVICE] RAM total={total}kB available={available}kB used%={percent}");

            return percent;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS-SERVICE] ERROR RAM: {ex}");
            return 0;
        }
    }

    public int GetDiskFree()
    {
        try
        {
            var info = new DriveInfo("/");
            int gb = (int)(info.AvailableFreeSpace / 1024 / 1024 / 1024);

            Console.WriteLine($"[STATUS-SERVICE] Disk free={gb}GB");

            return gb;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS-SERVICE] ERROR disk: {ex}");
            return 0;
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private int Count(string text, string token)
    {
        int c = text.Split(token, StringSplitOptions.None).Length - 1;
        Console.WriteLine($"[STATUS-SERVICE] Count('{token}')={c}");
        return c;
    }

    private string ExtractLine(string text, string token)
    {
        foreach (var line in text.Split('\n'))
            if (line.Contains(token))
            {
                Console.WriteLine($"[STATUS-SERVICE] ExtractLine('{token}') → {line.Trim()}");
                return line.Trim();
            }

        return "";
    }
}
