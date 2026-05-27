using System;
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

        public static void StartMonitoring(string arrayName, ArrayConfig cfg, Action<string> onAlert)
        {
            StopMonitoring();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string mdstat = MdadmService.GetMdstat();

                        if (mdstat != _lastMdstat)
                        {
                            _lastMdstat = mdstat;

                            // Degradado
                            if (cfg.AlertDegraded && MdadmService.IsDegraded(arrayName))
                                onAlert($"Array {arrayName} is degraded.");

                            // Disco fallado
                            if (cfg.AlertDiskFail && mdstat.Contains("faulty"))
                                onAlert($"A disk in array {arrayName} has failed.");

                            // Resync lento
                            if (cfg.AlertSlowResync && MdadmService.IsResyncing(arrayName))
                            {
                                var detail = MdadmService.GetDetail(arrayName);
                                var speed = ExtractSpeed(detail);

                                if (int.TryParse(speed, out int kb) &&
                                    kb < cfg.ResyncMaxSpeed / 10)
                                {
                                    onAlert($"Array {arrayName} resync is slow ({kb} KB/s).");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // No rompemos el bucle por errores puntuales
                    }

                    await Task.Delay(3000, token);
                }
            }, token);
        }

        public static void StopMonitoring()
        {
            try { _cts?.Cancel(); } catch { }
        }

        private static string ExtractSpeed(string detail)
        {
            // Busca "speed=1234K/sec"
            int idx = detail.IndexOf("speed=", StringComparison.Ordinal);
            if (idx < 0) return "0";

            int end = detail.IndexOf("K/sec", idx, StringComparison.Ordinal);
            if (end < 0) return "0";

            string raw = detail.Substring(idx + 6, end - (idx + 6));
            return raw.Trim();
        }
    }
}
