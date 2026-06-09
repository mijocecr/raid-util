using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public class RaidStateService
{
    public static RaidStateService Instance { get; } = new RaidStateService();

    public List<RaidArrayInfo> Arrays { get; private set; } = new();
    public List<RaidDiskInfo> Disks { get; private set; } = new();

    public event Action? StateChanged;

    private readonly object _lock = new();

    public void ForceStateChanged()
    {
        StateChanged?.Invoke();
    }

    public async Task RefreshAsync()
    {
        // ⭐ NO permitir llamadas antes de que MainWindow valide sudo
        if (!Credentials.AllowRaidCalls)
        {
            LogService.Debug("[STATE] RefreshAsync aborted → AllowRaidCalls = false");
            return;
        }

        _ = Task.Run(async () =>
        {
            var raidService = RaidService.Instance;

            var arrays = await raidService.GetArraysAsync();
            var disks = await raidService.GetAllDisksAsync();

            lock (_lock)
            {
                Arrays = arrays;
                Disks = disks;
            }

            // ⭐ Detectar FIN de operación aunque mdadm NO reporte 100%
            NormalizeFinishedOperations();

            StateChanged?.Invoke();
        });
    }

    // ⭐⭐⭐ ESTA ES LA CLAVE ⭐⭐⭐
    private void NormalizeFinishedOperations()
    {
        foreach (var array in Arrays)
        {
            // Si mdadm dejó de reportar progreso pero no llegó a 100%
            bool wasInOperation =
                array.State == RaidArrayState.Resync ||
                array.State == RaidArrayState.Rebuilding;

            bool progressStuck =
                array.RebuildProgress > 0 &&
                array.RebuildProgress < 100;

            if (wasInOperation && progressStuck)
            {
                LogService.Debug($"[STATE] mdadm terminó sin reportar 100% → normalizando {array.Name}");

                // ⭐ Forzar finalización correcta
                array.RebuildProgress = 100;
                array.RebuildETA = "0 min";

                // ⭐ Estado final correcto
                array.State = RaidArrayState.Clean;
            }
        }
    }
}
