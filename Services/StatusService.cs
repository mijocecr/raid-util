// File: RAID_Util/Services/StatusService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Models;

namespace RAID_Util.Services;

// ============================================================
// MODELOS DE INFORMACIÓN PARA STATUS VIEW
// ============================================================

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

// ============================================================
// SERVICIO PRINCIPAL DE ESTADO RAID
// ============================================================

public class StatusService
{
    // ============================================================
    // EJECUCIÓN DE COMANDOS DEL SISTEMA
    // ============================================================
    private static async Task<(string StdOut, string StdErr)> Run(string cmd, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process
            {
                StartInfo = psi,
                EnableRaisingEvents = false
            };

            process.Start();

            // Leer stdout y stderr en paralelo
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Esperar a que termine
            await Task.WhenAll(stdoutTask, stderrTask);

            return (stdoutTask.Result.Trim(), stderrTask.Result.Trim());
        }
        catch (Exception ex)
        {
            return ("", $"Run() failed: {ex.Message}");
        }
    }


    // ============================================================
    // ESTADO GLOBAL DEL RAID
    // ============================================================

    /// <summary>
    /// Devuelve el estado general del sistema RAID:
    /// Healthy, Warning, Critical o No RAID Detected.
    /// </summary>
    public async Task<string> GetOverallRaidHealthAsync()
    {
        var arrays = await ParseMdstatAsync();

        if (arrays.Count == 0)
            return "No RAID Detected";

        bool anyDegraded = false;
        bool anyRebuilding = false;

        foreach (var arr in arrays)
        {
            // Flags como [UU], [U_], [__]
            if (!string.IsNullOrWhiteSpace(arr.Flags) && arr.Flags.Contains("_"))
                anyDegraded = true;

            // Reconstrucción activa
            if (!string.IsNullOrWhiteSpace(arr.RebuildProgress))
                anyRebuilding = true;

            // Estado del array
            if (!arr.State.Contains("active", StringComparison.OrdinalIgnoreCase))
                anyDegraded = true;
        }

        if (anyDegraded && !anyRebuilding)
            return "Critical";

        if (anyRebuilding)
            return "Warning";

        return "Healthy";
    }

    // ============================================================
    // RESÚMENES NUMÉRICOS
    // ============================================================

    /// <summary>
    /// Devuelve un resumen de arrays RAID:
    /// Total, Healthy, Degraded.
    /// </summary>
    public async Task<string> GetArraysSummaryAsync()
    {
        var arrays = await ParseMdstatAsync();

        if (arrays.Count == 0)
            return "No RAID Detected";

        int total = arrays.Count;
        int healthy = 0;
        int degraded = 0;

        foreach (var arr in arrays)
        {
            bool isDegraded = false;

            if (!string.IsNullOrWhiteSpace(arr.Flags) && arr.Flags.Contains("_"))
                isDegraded = true;

            if (!arr.State.Contains("active", StringComparison.OrdinalIgnoreCase))
                isDegraded = true;

            if (isDegraded)
                degraded++;
            else
                healthy++;
        }

        return $"Total: {total} | Healthy: {healthy} | Degraded: {degraded}";
    }

    /// <summary>
    /// Resumen de discos (pendiente de implementación real).
    /// </summary>
    public async Task<string> GetDisksSummaryAsync()
    {
        var arrays = await ParseMdstatAsync();

        if (arrays.Count == 0)
            return "No RAID Detected";

        int total = 0;
        int active = 0;
        int faulty = 0;
        int spare = 0;
        int smartAlerts = 0;

        foreach (var arr in arrays)
        {
            var diskStates = await GetMdadmDiskStatesAsync(arr.Name);

            foreach (var (device, state) in diskStates)
            {
                total++;

                // Estado ACTIVE
                if (state.Contains("active", StringComparison.OrdinalIgnoreCase) ||
                    state.Contains("sync", StringComparison.OrdinalIgnoreCase))
                    active++;

                // Estado FAULTY
                if (state.Contains("faulty", StringComparison.OrdinalIgnoreCase))
                    faulty++;

                // Estado SPARE
                if (state.Contains("spare", StringComparison.OrdinalIgnoreCase))
                    spare++;

                // SMART
                var smart = await GetSmartHealthAsync(device);
                if (smart != null)
                    smartAlerts++;
            }
        }

        return $"Total: {total} | Active: {active} | Faulty: {faulty} | Spare: {spare} | SMART Alerts: {smartAlerts}";
    }


    /// <summary>
    /// Devuelve un resumen de reconstrucciones activas.
    /// </summary>
    public async Task<string> GetRebuildSummaryAsync()
    {
        var arrays = await ParseMdstatAsync();

        if (arrays.Count == 0)
            return "No RAID Detected";

        var rebuilding = arrays
            .Where(a => !string.IsNullOrWhiteSpace(a.RebuildProgress))
            .ToList();

        if (rebuilding.Count == 0)
            return "No rebuilds in progress";

        var fastest = rebuilding
            .OrderByDescending(a =>
            {
                if (a.RebuildProgress?.EndsWith("%") == true &&
                    double.TryParse(a.RebuildProgress.TrimEnd('%'), out double val))
                    return val;

                return 0;
            })
            .First();

        return $"Active: {rebuilding.Count} | Fastest: {fastest.Name} ({fastest.RebuildProgress})";
    }

    // ============================================================
    // ARRAYS EN RIESGO
    // ============================================================

    /// <summary>
    /// Devuelve una lista de arrays en estado crítico, degradado o en reconstrucción.
    /// </summary>
    public async Task<IList<ArrayRiskInfo>> GetArraysAtRiskAsync()
    {
        var result = new List<ArrayRiskInfo>();
        var arrays = await ParseMdstatAsync();

        if (arrays.Count == 0)
            return result;

        foreach (var arr in arrays)
        {
            bool isDegraded = !string.IsNullOrWhiteSpace(arr.Flags) && arr.Flags.Contains("_");
            bool isRebuilding = !string.IsNullOrWhiteSpace(arr.RebuildProgress);
            bool isInactive = !arr.State.Contains("active", StringComparison.OrdinalIgnoreCase);

            if (!isDegraded && !isRebuilding && !isInactive)
                continue;

            var info = new ArrayRiskInfo { Name = arr.Name };

            if (isInactive)
            {
                info.Status = "INACTIVE";
                info.Details = $"Array is inactive — Level: {arr.Level}";
            }
            else if (isRebuilding)
            {
                info.Status = "RECOVERING";
                info.Details = $"Progress: {arr.RebuildProgress} — ETA: {arr.RebuildEta}";
            }
            else if (isDegraded)
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

    /// <summary>
    /// Devuelve alertas de discos basadas en mdstat, mdadm y SMART.
    /// </summary>
    public async Task<IList<DiskAlertInfo>> GetDiskAlertsAsync()
    {
        var alerts = new List<DiskAlertInfo>();
        var arrays = await ParseMdstatAsync();

        // 1) Alertas basadas en mdstat
        foreach (var arr in arrays)
        {
            if (!string.IsNullOrWhiteSpace(arr.Flags) && arr.Flags.Contains("_"))
            {
                alerts.Add(new DiskAlertInfo
                {
                    Device = arr.Name,
                    Alert = $"Array {arr.Name} degraded — Flags: {arr.Flags}"
                });
            }
        }

        // 2) Alertas basadas en mdadm
        foreach (var arr in arrays)
        {
            var diskStates = await GetMdadmDiskStatesAsync(arr.Name);

            foreach (var (device, state) in diskStates)
            {
                if (state.Contains("faulty", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new DiskAlertInfo
                    {
                        Device = device,
                        Alert = $"Disk {device} is FAULTY in {arr.Name}"
                    });
                }

                if (state.Contains("spare", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new DiskAlertInfo
                    {
                        Device = device,
                        Alert = $"Disk {device} is SPARE in {arr.Name}"
                    });
                }
            }
        }

        // 3) Alertas SMART
        foreach (var arr in arrays)
        {
            var diskStates = await GetMdadmDiskStatesAsync(arr.Name);

            foreach (var (device, _) in diskStates)
            {
                var smart = await GetSmartHealthAsync(device);
                if (smart != null)
                {
                    alerts.Add(new DiskAlertInfo
                    {
                        Device = device,
                        Alert = smart
                    });
                }
            }
        }

        return alerts;
    }

    // ============================================================
    // EVENTOS RECIENTES
    // ============================================================

    /// <summary>
    /// Genera una lista de eventos RAID basados en el estado actual.
    /// </summary>
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
            if (!arr.State.Contains("active", StringComparison.OrdinalIgnoreCase))
                events.Add($"{arr.Name}: Array is INACTIVE (state: {arr.State})");

            if (!string.IsNullOrWhiteSpace(arr.Flags) && arr.Flags.Contains("_"))
                events.Add($"{arr.Name}: Degraded — Flags: {arr.Flags}");

            if (!string.IsNullOrWhiteSpace(arr.RebuildProgress))
                events.Add($"{arr.Name}: Recovery in progress — {arr.RebuildProgress} (ETA: {arr.RebuildEta})");

            if (string.IsNullOrWhiteSpace(arr.RebuildProgress) &&
                (string.IsNullOrWhiteSpace(arr.Flags) || !arr.Flags.Contains("_")) &&
                arr.State.Contains("active", StringComparison.OrdinalIgnoreCase))
            {
                events.Add($"{arr.Name}: Healthy — All disks OK");
            }
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

            // Línea que define un array
            if (line.StartsWith("md"))
            {
                current = new MdstatArray();

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                current.Name = parts[0].TrimEnd(':');
                current.State = parts[2];
                current.Level = parts[3];

                // Discos
                for (int i = 4; i < parts.Length; i++)
                {
                    if (parts[i].Contains("["))
                        current.Devices.Add(parts[i]);
                }

                result.Add(current);
                continue;
            }

            // Flags como [UU] o [U_]
            if (current != null && line.Contains("[") && line.Contains("]") && !line.Contains("recovery"))
            {
                current.Flags = line.Trim();
                continue;
            }

            // Progreso de reconstrucción
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

        var (outp, _) = await Run("mdadm", $"--detail /dev/{arrayName}");
        if (string.IsNullOrWhiteSpace(outp))
            return result;

        var lines = outp.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Contains("/dev/"))
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string dev = parts.Last();
                string state = string.Join(' ', parts.Skip(3).Take(parts.Length - 4));

                result.Add((dev, state));
            }
        }

        return result;
    }

    // ============================================================
    // SMART HEALTH
    // ============================================================

    private async Task<string?> GetSmartHealthAsync(string device)
    {
        var (outp, _) = await Run("smartctl", $"-H {device}");

        if (string.IsNullOrWhiteSpace(outp))
            return null;

        if (outp.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
            return "SMART FAIL";

        if (outp.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
            return "SMART WARNING";

        return null;
    }
    
   
    
    
}
