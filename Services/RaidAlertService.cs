using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class RaidAlertService
{
    private const int AlertCooldownSeconds = 30;

    private static CancellationTokenSource? _cts;
    private static string _lastMdstat = "";
    private static string _lastDetail = "";
    private static string? _currentArray;
    private static readonly Dictionary<string, DateTime> LastAlertTimes = new(StringComparer.Ordinal);

    // ============================================================
    // RESOLVER NOMBRE BASE (md0)
    // ============================================================
    private static string BaseName(string arrayName)
    {
        if (arrayName.StartsWith("/dev/"))
            return arrayName.Split('/').Last();

        return arrayName;
    }

    public static void StartMonitoring(string arrayName, ArrayConfig cfg, Action<string> onAlert)
    {
        StopMonitoring();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _currentArray = arrayName;
        _lastMdstat = "";
        _lastDetail = "";

        var baseName = BaseName(arrayName);

        LogService.Info($"[RaidAlertService] Monitoring started for {baseName}");

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var mdstat = SafeGetMdstat();
                    var detail = SafeGetDetail(arrayName);

                    var mdstatChanged = mdstat != _lastMdstat;
                    var detailChanged = detail != _lastDetail;

                    if (mdstatChanged || detailChanged)
                    {
                        _lastMdstat = mdstat;
                        _lastDetail = detail;

                        // 1) Degradado
                        if (cfg.AlertDegraded && SafeIsDegraded(arrayName))
                            TryAlert(onAlert, $"Array {baseName} is degraded.");

                        // 2) Disco fallado
                        if (cfg.AlertDiskFail && HasFaultyDisk(mdstat, baseName))
                            TryAlert(onAlert, $"A disk in array {baseName} has failed.");

                        // 3) Resync lento
                        if (cfg.AlertSlowResync && SafeIsResyncing(arrayName))
                        {
                            // speed= vive en /proc/mdstat, no en mdadm --detail.
                            var speedStr = ExtractSpeed(
                                MdstatParser.GetProgressLine(mdstat, arrayName));
                            if (int.TryParse(speedStr, out var kb))
                                if (kb > 0 && kb < cfg.ResyncMaxSpeed / 10)
                                    TryAlert(onAlert, $"Array {baseName} resync is slow ({kb} KB/s).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"[RaidAlertService] Monitor error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(3000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public static void StopMonitoring()
    {
        try { _cts?.Cancel(); } catch { }

        _cts = null;
        _currentArray = null;
        _lastMdstat = "";
        _lastDetail = "";
        lock (LastAlertTimes)
            LastAlertTimes.Clear();

        LogService.Info("[RaidAlertService] Monitoring stopped");
    }

    // ============================================================
    // HELPERS SEGUROS
    // ============================================================
    private static string SafeGetMdstat()
    {
        try { return MdadmService.GetMdstat() ?? string.Empty; }
        catch (Exception ex)
        {
            LogService.Error($"[RaidAlertService] GetMdstat error: {ex.Message}");
            return string.Empty;
        }
    }

    private static string SafeGetDetail(string arrayName)
    {
        try { return MdadmService.GetDetail(arrayName) ?? string.Empty; }
        catch (Exception ex)
        {
            LogService.Error($"[RaidAlertService] GetDetail({arrayName}) error: {ex.Message}");
            return string.Empty;
        }
    }

    private static bool SafeIsDegraded(string arrayName)
    {
        try { return MdadmService.IsDegraded(arrayName); }
        catch (Exception ex)
        {
            LogService.Error($"[RaidAlertService] IsDegraded({arrayName}) error: {ex.Message}");
            return false;
        }
    }

    private static bool SafeIsResyncing(string arrayName)
    {
        try { return MdadmService.IsResyncing(arrayName); }
        catch (Exception ex)
        {
            LogService.Error($"[RaidAlertService] IsResyncing({arrayName}) error: {ex.Message}");
            return false;
        }
    }

    // ============================================================
    // ALERTA CON COOLDOWN POR MENSAJE
    // ============================================================
    private static void TryAlert(Action<string> onAlert, string message)
    {
        var now = DateTime.UtcNow;

        // Antes un único timestamp global hacía que la primera alerta de un
        // array suprimiera el resto durante 30 s, incluso las de otros arrays.
        lock (LastAlertTimes)
        {
            if (LastAlertTimes.TryGetValue(message, out var last) &&
                (now - last).TotalSeconds < AlertCooldownSeconds)
                return;

            LastAlertTimes[message] = now;
        }

        try
        {
            onAlert(message);
            LogService.Info($"[RaidAlertService] ALERT: {message}");
        }
        catch (Exception ex)
        {
            LogService.Error($"[RaidAlertService] onAlert error: {ex.Message}");
        }
    }

    // ============================================================
    // DETECTAR DISCO FALLADO EN MDSTAT
    // ============================================================
    private static bool HasFaultyDisk(string mdstat, string baseName)
    {
        if (string.IsNullOrWhiteSpace(mdstat))
            return false;

        return MdstatParser.HasFaultyMember(MdstatParser.GetArrayLine(mdstat, baseName));
    }

    // ============================================================
    // EXTRAER VELOCIDAD DE RESYNC
    // ============================================================
    private static string ExtractSpeed(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "0";

        var idx = detail.IndexOf("speed=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return "0";

        var end = detail.IndexOf("/sec", idx, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return "0";

        var raw = detail.Substring(idx + 6, end - (idx + 6)).Trim();

        // Convertir M/sec a KB/s si es necesario
        if (raw.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            var num = new string(raw.Where(char.IsDigit).ToArray());
            if (int.TryParse(num, out var m))
                return (m * 1024).ToString();
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? "0" : digits;
    }
}
