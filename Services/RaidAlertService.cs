using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Services
{
    public static class RaidAlertService
    {
        private static CancellationTokenSource? _cts;
        private static string _lastMdstat = "";
        private static string _lastDetail = "";
        private static string? _currentArray;
        private static DateTime _lastAlertTime = DateTime.MinValue;

        // Evitar spam de alertas (ej: 30s entre alertas iguales)
        private const int AlertCooldownSeconds = 30;

        public static void StartMonitoring(string arrayName, ArrayConfig cfg, Action<string> onAlert)
        {
            StopMonitoring();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _currentArray = arrayName;
            _lastMdstat = "";
            _lastDetail = "";

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string mdstat = SafeGetMdstat();
                        string detail = SafeGetDetail(arrayName);

                        bool mdstatChanged = mdstat != _lastMdstat;
                        bool detailChanged = detail != _lastDetail;

                        if (mdstatChanged || detailChanged)
                        {
                            _lastMdstat = mdstat;
                            _lastDetail = detail;

                            // 1) Degradado
                            if (cfg.AlertDegraded && SafeIsDegraded(arrayName))
                                TryAlert(onAlert, $"Array {arrayName} is degraded.");

                            // 2) Disco fallado (por array, no global)
                            if (cfg.AlertDiskFail && HasFaultyDisk(mdstat, arrayName))
                                TryAlert(onAlert, $"A disk in array {arrayName} has failed.");

                            // 3) Resync lento
                            if (cfg.AlertSlowResync && SafeIsResyncing(arrayName))
                            {
                                var speedStr = ExtractSpeed(detail);
                                if (int.TryParse(speedStr, out int kb))
                                {
                                    // Ej: si cfg.ResyncMaxSpeed = 200000 KB/s → alerta si < 20000
                                    if (kb > 0 && kb < cfg.ResyncMaxSpeed / 10)
                                        TryAlert(onAlert, $"Array {arrayName} resync is slow ({kb} KB/s).");
                                }
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
                        // Cancelado → salir del bucle
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
        }

        // ----------------- HELPERS SEGUROS -----------------

        private static string SafeGetMdstat()
        {
            try
            {
                return MdadmService.GetMdstat() ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogService.Error($"[RaidAlertService] GetMdstat error: {ex.Message}");
                return string.Empty;
            }
        }

        private static string SafeGetDetail(string arrayName)
        {
            try
            {
                return MdadmService.GetDetail(arrayName) ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogService.Error($"[RaidAlertService] GetDetail({arrayName}) error: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool SafeIsDegraded(string arrayName)
        {
            try
            {
                return MdadmService.IsDegraded(arrayName);
            }
            catch (Exception ex)
            {
                LogService.Error($"[RaidAlertService] IsDegraded({arrayName}) error: {ex.Message}");
                return false;
            }
        }

        private static bool SafeIsResyncing(string arrayName)
        {
            try
            {
                return MdadmService.IsResyncing(arrayName);
            }
            catch (Exception ex)
            {
                LogService.Error($"[RaidAlertService] IsResyncing({arrayName}) error: {ex.Message}");
                return false;
            }
        }

        // ----------------- LÓGICA DE ALERTA -----------------

        private static void TryAlert(Action<string> onAlert, string message)
        {
            var now = DateTime.UtcNow;

            if ((now - _lastAlertTime).TotalSeconds < AlertCooldownSeconds)
                return;

            _lastAlertTime = now;

            try
            {
                onAlert(message);
                LogService.Write($"[RaidAlertService] ALERT: {message}");
            }
            catch (Exception ex)
            {
                LogService.Error($"[RaidAlertService] onAlert error: {ex.Message}");
            }
        }

        // ----------------- PARSEO DE MDSTAT -----------------

        private static bool HasFaultyDisk(string mdstat, string arrayName)
        {
            if (string.IsNullOrWhiteSpace(mdstat))
                return false;

            // mdstat suele tener líneas tipo:
            // md0 : active raid1 sda1[0] sdb1[1](F)
            // o "faulty" en la línea del array
            var lines = mdstat.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Buscar la línea del array
            var line = lines.FirstOrDefault(l => l.Contains(arrayName + " ", StringComparison.Ordinal) ||
                                                 l.StartsWith(arrayName + " :", StringComparison.Ordinal));
            if (line == null)
                return false;

            // Si contiene "faulty" o "(F)" → disco fallado
            if (line.Contains("faulty", StringComparison.OrdinalIgnoreCase))
                return true;

            if (line.Contains("(F)", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string ExtractSpeed(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return "0";

            // mdadm --detail suele tener líneas tipo:
            // "  Resyncing :  12.3% complete ... speed=12345K/sec"
            // Buscamos "speed=" y "K/sec"
            int idx = detail.IndexOf("speed=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return "0";

            int end = detail.IndexOf("K/sec", idx, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                return "0";

            var raw = detail.Substring(idx + 6, end - (idx + 6)).Trim();

            // A veces puede venir con decimales o espacios raros → nos quedamos con dígitos
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digits) ? "0" : digits;
        }
    }
}
