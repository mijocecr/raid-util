using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Models;
using RAID_Util.Services;

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
    private readonly RaidService _raid = RaidService.Instance;

    // ============================================================
    // SAFE COMMAND EXECUTION (FIXED)
    // ============================================================
    private static async Task<(string StdOut, string StdErr)> Run(string cmd, string args)
    {
        if (!Credentials.AllowRaidCalls)
            return ("", "");

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

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdoutTask, stderrTask);

            // ⭐ CRÍTICO: evita congelamientos
            await process.WaitForExitAsync();

            return (stdoutTask.Result.Trim(), stderrTask.Result.Trim());
        }
        catch (Exception ex)
        {
            return ("", $"Run() failed: {ex.Message}");
        }
    }

    // ============================================================
    // GLOBAL RAID HEALTH
    // ============================================================
    public async Task<string> GetOverallRaidHealthAsync()
    {
        if (!Credentials.AllowRaidCalls)
            return "Waiting for sudo...";

        var arrays = await _raid.GetArraysAsync();
        if (arrays.Count == 0)
            return "No RAID Detected";

        bool anyDegraded = arrays.Any(a => a.State == RaidArrayState.Degraded);
        bool anyRebuilding = arrays.Any(a => a.RebuildProgress > 0);
        bool anyFailed = arrays.Any(a => a.State == RaidArrayState.Failed);

        if (anyFailed)
            return "Critical";

        if (anyDegraded)
            return "Critical";

        if (anyRebuilding)
            return "Warning";

        return "Healthy";
    }

    // ============================================================
    // SUMMARIES
    // ============================================================
    public async Task<string> GetArraysSummaryAsync()
    {
        if (!Credentials.AllowRaidCalls)
            return "Waiting...";

        var arrays = await _raid.GetArraysAsync();
        if (arrays.Count == 0)
            return "No RAID Detected";

        int total = arrays.Count;
        int degraded = arrays.Count(a => a.State == RaidArrayState.Degraded);
        int healthy = arrays.Count(a => a.State == RaidArrayState.Clean || a.State == RaidArrayState.Active);

        return $"Total: {total} | Healthy: {healthy} | Degraded: {degraded}";
    }

    public async Task<string> GetDisksSummaryAsync()
    {
        if (!Credentials.AllowRaidCalls)
            return "Waiting...";

        var disks = await _raid.GetAllDisksAsync();
        if (disks.Count == 0)
            return "No RAID Detected";

        int total = disks.Count;
        int active = disks.Count(d => d.State == "OK");
        int faulty = disks.Count(d => d.State == "faulty");
        int spare = disks.Count(d => d.Role == "spare");

        return $"Total: {total} | Active: {active} | Faulty: {faulty} | Spare: {spare}";
    }

    public async Task<string> GetRebuildSummaryAsync()
    {
        if (!Credentials.AllowRaidCalls)
            return "Waiting...";

        var arrays = await _raid.GetArraysAsync();
        if (arrays.Count == 0)
            return "No RAID Detected";

        var rebuilding = arrays.Where(a => a.RebuildProgress > 0).ToList();
        if (rebuilding.Count == 0)
            return "No rebuilds in progress";

        var fastest = rebuilding.OrderByDescending(a => a.RebuildProgress).First();

        return $"Active: {rebuilding.Count} | Fastest: {fastest.Name} ({fastest.RebuildProgress}%)";
    }

    // ============================================================
    // ARRAYS AT RISK
    // ============================================================
    public async Task<IList<ArrayRiskInfo>> GetArraysAtRiskAsync()
    {
        if (!Credentials.AllowRaidCalls)
            return new List<ArrayRiskInfo>();

        var result = new List<ArrayRiskInfo>();
        var arrays = await _raid.GetArraysAsync();

        foreach (var arr in arrays)
        {
            // ⭐ Healthy = Clean o Active
            if (arr.State == RaidArrayState.Clean || arr.State == RaidArrayState.Active)
                continue;

            var info = new ArrayRiskInfo { Name = arr.Name };

            if (arr.State == RaidArrayState.Degraded)
            {
                info.Status = "DEGRADED";
                info.Details = $"{arr.Disks.Count} disks — degraded";
            }
            else if (arr.State == RaidArrayState.Rebuilding)
            {
                info.Status = "REBUILDING";
                info.Details = $"{arr.RebuildProgress}% — ETA: {arr.RebuildETA}";
            }
            else if (arr.State == RaidArrayState.Failed)
            {
                info.Status = "FAILED";
                info.Details = "Array is FAILED";
            }

            result.Add(info);
        }

        return result;
    }

    // ============================================================
    // DISK ALERTS
    // ============================================================
    public async Task<IList<DiskAlertInfo>> GetDiskAlertsAsync()
    {
        if (!Credentials.AllowRaidCalls)
            return new List<DiskAlertInfo>();

        var alerts = new List<DiskAlertInfo>();
        var disks = await _raid.GetAllDisksAsync();

        foreach (var d in disks)
        {
            if (d.State == "faulty")
                alerts.Add(new DiskAlertInfo
                {
                    Device = d.Name,
                    Alert = $"Disk {d.Name} is FAULTY"
                });

            if (d.Role == "spare")
                alerts.Add(new DiskAlertInfo
                {
                    Device = d.Name,
                    Alert = $"Disk {d.Name} is SPARE"
                });
        }

        return alerts;
    }

    // ============================================================
    // RECENT EVENTS
    // ============================================================
    public async Task<IList<string>> GetRecentEventsAsync()
    {
        if (!Credentials.AllowRaidCalls)
            return new List<string> { "Waiting for sudo..." };

        var events = new List<string>();
        var arrays = await _raid.GetArraysAsync();

        if (arrays.Count == 0)
        {
            events.Add("No RAID arrays detected.");
            return events;
        }

        foreach (var arr in arrays)
        {
            if (arr.State == RaidArrayState.Failed)
                events.Add($"{arr.Name}: FAILED");

            if (arr.State == RaidArrayState.Degraded)
                events.Add($"{arr.Name}: Degraded");

            if (arr.State == RaidArrayState.Rebuilding)
                events.Add($"{arr.Name}: Rebuilding — {arr.RebuildProgress}% (ETA: {arr.RebuildETA})");

            // ⭐ Healthy = Clean o Active
            if (arr.State == RaidArrayState.Clean || arr.State == RaidArrayState.Active)
                events.Add($"{arr.Name}: Healthy");
        }

        return events;
    }
}
