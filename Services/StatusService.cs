// File: RAID-Util/Services/StatusService.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Models;

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
    // EJECUCIÓN DE COMANDOS
    // ============================================================
    private static async Task<(string StdOut, string StdErr)> Run(string cmd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdoutTask, stderrTask);

            return (stdoutTask.Result.Trim(), stderrTask.Result.Trim());
        }
        catch (Exception ex)
        {
            return ("", $"Run() failed: {ex.Message}");
        }
    }

    // ============================================================
    // ESTADO GLOBAL
    // ============================================================
    public async Task<string> GetOverallRaidHealthAsync()
    {
        var arrays = await ParseMdstatAsync();
        if (arrays.Count == 0)
            return "No RAID Detected";

        bool anyDegraded = arrays.Any(a => a.Flags.Contains("_"));
        bool anyRebuilding = arrays.Any(a => !string.IsNullOrWhiteSpace(a.RebuildProgress));
        bool anyInactive = arrays.Any(a => !a.State.Contains("active", StringComparison.OrdinalIgnoreCase));

        if (anyInactive)
            return "Critical";

        if (anyDegraded)
            return "Critical";

        if (anyRebuilding)
            return "Warning";

        return "Healthy";
    }

    // ============================================================
    // RESÚMENES
    // ============================================================
    public async Task<string> GetArraysSummaryAsync()
    {
        var arrays = await ParseMdstatAsync();
        if (arrays.Count == 0)
            return "No RAID Detected";

        int total = arrays.Count;
        int degraded = arrays.Count(a => a.Flags.Contains("_") || !a.State.Contains("active"));
        int healthy = total - degraded;

        return $"Total: {total} | Healthy: {healthy} | Degraded: {degraded}";
    }

    public async Task<string> GetDisksSummaryAsync()
    {
        var arrays = await ParseMdstatAsync();
        if (arrays.Count == 0)
            return "No RAID Detected";

        int total = 0, active = 0, faulty = 0, spare = 0, smartAlerts = 0;

        foreach (var arr in arrays)
        {
            var diskStates = await GetMdadmDiskStatesAsync(arr.Name);

            foreach (var (device, state) in diskStates)
            {
                total++;

                if (state.Contains("active") || state.Contains("sync"))
                    active++;

                if (state.Contains("faulty"))
                    faulty++;

                if (state.Contains("spare"))
                    spare++;

                var smart = await GetSmartHealthAsync(device);
                if (smart != null)
                    smartAlerts++;
            }
        }

        return $"Total: {total} | Active: {active} | Faulty: {faulty} | Spare: {spare} | SMART Alerts: {smartAlerts}";
    }

    public async Task<string> GetRebuildSummaryAsync()
    {
        var arrays = await ParseMdstatAsync();
        if (arrays.Count == 0)
            return "No RAID Detected";

        var rebuilding = arrays.Where(a => !string.IsNullOrWhiteSpace(a.RebuildProgress)).ToList();
        if (rebuilding.Count == 0)
            return "No rebuilds in progress";

        var fastest = rebuilding.OrderByDescending(a =>
        {
            if (a.RebuildProgress.EndsWith("%") &&
                double.TryParse(a.RebuildProgress.TrimEnd('%'), out var val))
                return val;

            return 0;
        }).First();

        return $"Active: {rebuilding.Count} | Fastest: {fastest.Name} ({fastest.RebuildProgress})";
    }

    // ============================================================
    // ARRAYS EN RIESGO
    // ============================================================
    public async Task<IList<ArrayRiskInfo>> GetArraysAtRiskAsync()
    {
        var result = new List<ArrayRiskInfo>();
        var arrays = await ParseMdstatAsync();

        foreach (var arr in arrays)
        {
            bool degraded = arr.Flags.Contains("_");
            bool rebuilding = !string.IsNullOrWhiteSpace(arr.RebuildProgress);
            bool inactive = !arr.State.Contains("active");

            if (!degraded && !rebuilding && !inactive)
                continue;

            var info = new ArrayRiskInfo { Name = arr.Name };

            if (inactive)
            {
                info.Status = "INACTIVE";
                info.Details = $"Array is inactive — Level: {arr.Level}";
            }
            else if (rebuilding)
            {
                info.Status = "RECOVERING";
                info.Details = $"Progress: {arr.RebuildProgress} — ETA: {arr.RebuildEta}";
            }
            else if (degraded)
            {
                info.Status = "DEGRADED";
                info.Details = $"Flags: {arr.Flags} — Devices: {string.Join(", ", arr.Devices)}";
            }

            result.Add(info);
        }

        return result;
    }

    // ============================================================
    // ALERTAS DE DISCO
    // ============================================================
    public async Task<IList<DiskAlertInfo>> GetDiskAlertsAsync()
    {
        var alerts = new List<DiskAlertInfo>();
        var arrays = await ParseMdstatAsync();

        foreach (var arr in arrays)
        {
            if (arr.Flags.Contains("_"))
                alerts.Add(new DiskAlertInfo
                {
                    Device = arr.Name,
                    Alert = $"Array {arr.Name} degraded — Flags: {arr.Flags}"
                });

            var diskStates = await GetMdadmDiskStatesAsync(arr.Name);

            foreach (var (device, state) in diskStates)
            {
                if (state.Contains("faulty"))
                    alerts.Add(new DiskAlertInfo
                    {
                        Device = device,
                        Alert = $"Disk {device} is FAULTY in {arr.Name}"
                    });

                if (state.Contains("spare"))
                    alerts.Add(new DiskAlertInfo
                    {
                        Device = device,
                        Alert = $"Disk {device} is SPARE in {arr.Name}"
                    });

                var smart = await GetSmartHealthAsync(device);
                if (smart != null)
                    alerts.Add(new DiskAlertInfo
                    {
                        Device = device,
                        Alert = smart
                    });
            }
        }

        return alerts;
    }

    // ============================================================
    // EVENTOS
    // ============================================================
    public async Task<IList<string>> GetRecentEventsAsync()
    {
        var events = new List<string>();
        var arrays = await ParseMdstatAsync();

        if (arrays.Count == 0)
        {
            events.Add("No RAID arrays detected on this system.");
            return events;
        }

        foreach (var arr in arrays)
        {
            if (!arr.State.Contains("active"))
                events.Add($"{arr.Name}: Array is INACTIVE (state: {arr.State})");

            if (arr.Flags.Contains("_"))
                events.Add($"{arr.Name}: Degraded — Flags: {arr.Flags}");

            if (!string.IsNullOrWhiteSpace(arr.RebuildProgress))
                events.Add($"{arr.Name}: Recovery in progress — {arr.RebuildProgress} (ETA: {arr.RebuildEta})");

            if (string.IsNullOrWhiteSpace(arr.RebuildProgress) &&
                !arr.Flags.Contains("_") &&
                arr.State.Contains("active"))
                events.Add($"{arr.Name}: Healthy — All disks OK");
        }

        return events;
    }

    // ============================================================
    // PARSEO DE /proc/mdstat
    // ============================================================
    public async Task<List<MdstatArray>> ParseMdstatAsync()
    {
        var result = new List<MdstatArray>();

        var (text, _) = await Run("cat", "/proc/mdstat");
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        MdstatArray? current = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith("md"))
            {
                current = new MdstatArray();

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                current.Name = parts[0].TrimEnd(':');
                current.State = parts.Length > 2 ? parts[2] : "";
                current.Level = parts.Length > 3 ? parts[3] : "";

                for (var i = 4; i < parts.Length; i++)
                    if (parts[i].Contains("["))
                        current.Devices.Add(parts[i]);

                result.Add(current);
                continue;
            }

            if (current != null && line.Contains("[") && line.Contains("]"))
            {
                current.Flags = line.Trim();
                continue;
            }

            if (current != null && line.Contains("recovery"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var p in parts)
                {
                    if (p.EndsWith("%"))
                        current.RebuildProgress = p;

                    if (p.StartsWith("finish="))
                        current.RebuildEta = p.Replace("finish=", "");
                }
            }
        }

        return result;
    }

    // ============================================================
    // MDADM --DETAIL
    // ============================================================
    private async Task<List<(string Device, string State)>> GetMdadmDiskStatesAsync(string arrayName)
    {
        var result = new List<(string, string)>();

        var path = MdadmService.GetDetail(arrayName);
        var (outp, _) = await Run("mdadm", $"--detail {path}");

        if (string.IsNullOrWhiteSpace(outp))
            return result;

        var lines = outp.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!trimmed.Contains("/dev/"))
                continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var dev = parts.Last();
            var state = string.Join(' ', parts.Where(p =>
                p.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("faulty", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("spare", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("sync", StringComparison.OrdinalIgnoreCase)));

            result.Add((dev, state));
        }

        return result;
    }

    // ============================================================
    // SMART HEALTH
    // ============================================================
    private async Task<string?> GetSmartHealthAsync(string device)
    {
        var (outp, _) = await Run("smartctl", $"-H /dev/{device}");

        if (string.IsNullOrWhiteSpace(outp))
            return null;

        if (outp.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
            return "SMART FAIL";

        if (outp.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
            return "SMART WARNING";

        return null;
    }
}
