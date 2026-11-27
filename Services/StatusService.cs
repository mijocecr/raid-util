using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    private sealed record ArrayDetail(bool Success, string Text);

    // ============================================================
    // LECTURA DE /proc/mdstat
    // ============================================================
    private static bool TryReadMdstat(out string mdstat)
    {
        try
        {
            if (!File.Exists("/proc/mdstat"))
            {
                mdstat = "";
                return false;
            }

            mdstat = File.ReadAllText("/proc/mdstat");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[STATUS] No se pudo leer /proc/mdstat: {ex.Message}");
            mdstat = "";
            return false;
        }
    }

    // ============================================================
    // DETALLE DE UN ARRAY (solo si el comando tuvo éxito)
    // ============================================================
    private static async Task<ArrayDetail> GetDetailAsync(string array)
    {
        try
        {
            var result = await ShellHelper.RunRootAsync(
                ToolLocator.ResolveOrThrow("mdadm"), new[] { "--detail", array }).ConfigureAwait(false);

            if (!result.Success)
            {
                LogService.Debug(
                    $"[STATUS] mdadm --detail {array} falló ({result.ExitCode}): {result.Stderr.Trim()}");
                return new ArrayDetail(false, "");
            }

            var text = string.IsNullOrWhiteSpace(result.Stdout) ? result.Stderr : result.Stdout;
            return new ArrayDetail(true, text);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[STATUS] mdadm --detail {array} lanzó: {ex.Message}");
            return new ArrayDetail(false, "");
        }
    }

    private static int CountStateLines(string detail, string token)
    {
        var needle = token.ToLowerInvariant();
        return detail.Split('\n').Count(line => line.ToLowerInvariant().Contains(needle));
    }

    // ============================================================
    // RAID HEALTH
    // ============================================================
    public async Task<string> GetOverallRaidHealthAsync()
    {
        if (!TryReadMdstat(out var mdstat))
            return "Unknown";

        var arrays = MdstatParser.ParseArrays(mdstat);
        if (arrays.Count == 0)
            return "No RAID Detected";

        var failedDetails = false;

        foreach (var array in arrays)
        {
            // mdstat es la fuente autoritativa: un array degradado no depende
            // de que mdadm --detail pueda ejecutarse con éxito.
            if (MdstatParser.IsDegradedLine(MdstatParser.GetArrayLine(mdstat, array)))
                return "Critical";

            var detail = await GetDetailAsync(array).ConfigureAwait(false);
            if (!detail.Success)
            {
                failedDetails = true;
                continue;
            }

            var text = detail.Text.ToLowerInvariant();
            if (text.Contains("degraded") || text.Contains("faulty"))
                return "Critical";

            // Progreso real: solo la línea de este array en /proc/mdstat.
            if (MdstatParser.IsOperationInProgress(mdstat, array))
                return "Warning";
        }

        // Un fallo al consultar mdadm nunca se debe pintar como "Healthy".
        return failedDetails ? "Unknown" : "Healthy";
    }

    // ============================================================
    // RESÚMENES
    // ============================================================
    public Task<string> GetArraysSummaryAsync()
    {
        if (!TryReadMdstat(out var mdstat))
            return Task.FromResult("Unknown");

        var count = MdstatParser.ParseArrays(mdstat).Count;
        return Task.FromResult(count == 0 ? "No RAID Detected" : $"Total: {count}");
    }

    public async Task<string> GetDisksSummaryAsync()
    {
        if (!TryReadMdstat(out var mdstat))
            return "Unknown";

        var arrays = MdstatParser.ParseArrays(mdstat);
        if (arrays.Count == 0)
            return "No RAID Detected";

        int active = 0, failed = 0;
        var unknown = false;

        foreach (var array in arrays)
        {
            var detail = await GetDetailAsync(array).ConfigureAwait(false);
            if (!detail.Success)
            {
                unknown = true;
                continue;
            }

            active += CountStateLines(detail.Text, "active sync");
            failed += CountStateLines(detail.Text, "faulty");
        }

        return unknown
            ? "Unknown"
            : $"Total: {active + failed} | Active: {active} | Failed: {failed}";
    }

    public Task<string> GetRebuildSummaryAsync()
    {
        if (!TryReadMdstat(out var mdstat))
            return Task.FromResult("Unknown");

        var arrays = MdstatParser.ParseArrays(mdstat);
        if (arrays.Count == 0)
            return Task.FromResult("No RAID Detected");

        foreach (var array in arrays)
        {
            var line = MdstatParser.GetProgressLine(mdstat, array);
            if (line.Length > 0)
                return Task.FromResult($"{array}: {line}");
        }

        return Task.FromResult("No rebuild in progress");
    }

    public async Task<IList<ArrayRiskInfo>> GetArraysAtRiskAsync()
    {
        var list = new List<ArrayRiskInfo>();
        if (!TryReadMdstat(out var mdstat))
            return list;

        foreach (var array in MdstatParser.ParseArrays(mdstat))
        {
            var line = MdstatParser.GetArrayLine(mdstat, array);
            var degraded = MdstatParser.IsDegradedLine(line);
            var faulty = MdstatParser.HasFaultyMember(line);

            if (!degraded || !faulty)
            {
                var detail = await GetDetailAsync(array).ConfigureAwait(false);
                if (detail.Success)
                {
                    var text = detail.Text.ToLowerInvariant();
                    degraded |= text.Contains("degraded");
                    faulty |= text.Contains("faulty");
                }
            }

            if (degraded)
                list.Add(new ArrayRiskInfo
                {
                    Name = array, Status = "DEGRADED", Details = "Array is degraded"
                });

            if (faulty)
                list.Add(new ArrayRiskInfo
                {
                    Name = array, Status = "FAILED DISK", Details = "One or more disks are faulty"
                });
        }

        return list;
    }

    public async Task<IList<DiskAlertInfo>> GetDiskAlertsAsync()
    {
        var list = new List<DiskAlertInfo>();
        if (!TryReadMdstat(out var mdstat))
            return list;

        foreach (var array in MdstatParser.ParseArrays(mdstat))
        {
            var faulty = MdstatParser.HasFaultyMember(MdstatParser.GetArrayLine(mdstat, array));

            if (!faulty)
            {
                var detail = await GetDetailAsync(array).ConfigureAwait(false);
                faulty = detail.Success && detail.Text.ToLowerInvariant().Contains("faulty");
            }

            if (faulty)
                list.Add(new DiskAlertInfo
                {
                    Device = array, Alert = "Faulty disk detected"
                });
        }
        // (El detalle se consulta solo si mdstat no lo marcó: evita alertas
        // duplicadas cuando mdadm no está disponible.)

        return list;
    }

    public Task<IList<string>> GetRecentEventsAsync()
    {
        var list = new List<string>();
        if (!TryReadMdstat(out var mdstat))
        {
            list.Add("RAID status unavailable (/proc/mdstat could not be read).");
            return Task.FromResult<IList<string>>(list);
        }

        var arrays = MdstatParser.ParseArrays(mdstat);
        if (arrays.Count == 0)
        {
            list.Add("No RAID detected.");
            return Task.FromResult<IList<string>>(list);
        }

        foreach (var array in arrays)
        {
            var line = MdstatParser.GetArrayLine(mdstat, array);

            if (MdstatParser.HasFaultyMember(line))
                list.Add($"{array}: Faulty disk detected");

            if (MdstatParser.IsDegradedLine(line) && !MdstatParser.HasFaultyMember(line))
                list.Add($"{array}: Array degraded");

            var progress = MdstatParser.GetProgressLine(mdstat, array);
            if (progress.Length > 0)
                list.Add($"{array}: Rebuild in progress ({MdstatParser.ParseProgressPercent(progress)}%)");
        }

        return Task.FromResult<IList<string>>(list);
    }

    // ============================================================
    // INFORMACIÓN DEL SISTEMA
    // ============================================================
    public string GetSessionUptime()
    {
        try
        {
            var result = ShellHelper.Run(ToolLocator.ResolveOrThrow("uptime"), "-p");
            return string.IsNullOrWhiteSpace(result.Stdout) ? "Unknown" : result.Stdout.Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static (long Idle, long Total)? ReadCpu()
    {
        var first = File.ReadLines("/proc/stat").FirstOrDefault();
        if (first is null)
            return null;

        // "cpu user nice system idle iowait irq softirq steal [guest ...]"
        var fields = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 9 || !string.Equals(fields[0], "cpu", StringComparison.Ordinal))
            return null;

        long total = 0;
        for (var i = 1; i <= 8; i++)
            if (!long.TryParse(fields[i], out var value))
                return null;
            else
                total += value;

        if (!long.TryParse(fields[4], out var idle) || !long.TryParse(fields[5], out var iowait))
            return null;

        return (idle + iowait, total);
    }

    public int GetCpuUsage()
    {
        try
        {
            var first = ReadCpu();
            Task.Delay(1000).Wait(); // muestreo de 1 s: 120 ms daba picos 0/100
            var second = ReadCpu();

            if (first is null || second is null)
                return 0;

            var idleDelta = second.Value.Idle - first.Value.Idle;
            var totalDelta = second.Value.Total - first.Value.Total;
            if (totalDelta <= 0)
                return 0;

            return (int)Math.Clamp(Math.Round(100.0 * (totalDelta - idleDelta) / totalDelta), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    public int GetMemoryUsage()
    {
        try
        {
            long total = 0, available = -1, free = 0, buffers = 0, cached = 0;

            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                var colon = line.IndexOf(':');
                if (colon < 0)
                    continue;

                var key = line[..colon].Trim();
                if (!long.TryParse(
                        Regex.Match(line[(colon + 1)..], @"\d+").Value,
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    continue;

                switch (key)
                {
                    case "MemTotal": total = value; break;
                    case "MemAvailable": available = value; break;
                    case "MemFree": free = value; break;
                    case "Buffers": buffers = value; break;
                    case "Cached": cached = value; break;
                }
            }

            if (total <= 0)
                return 0;

            // Kernels antiguos no tienen MemAvailable (Linux >= 3.14 sí).
            if (available < 0)
                available = free + buffers + cached;

            return (int)Math.Clamp(Math.Round(100.0 * (total - available) / total), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    public double GetRebuildPercent(string detail)
    {
        var percent = MdstatParser.ParseProgressPercent(detail);
        if (percent > 0)
            return percent;

        var match = Regex.Match(detail, @"Rebuild Status\s*:\s*(\d+)%", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value, out var value)
            ? Math.Clamp(value, 0, 100)
            : 0;
    }

    public int GetDiskFree()
    {
        try
        {
            var info = new DriveInfo("/");
            return (int)(info.AvailableFreeSpace / 1024 / 1024 / 1024);
        }
        catch
        {
            return 0;
        }
    }
}
