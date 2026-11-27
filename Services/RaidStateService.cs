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

    // Operación observada en la LECTURA ANTERIOR. Solo se marca como terminada
    // cuando una lectura nueva confirma que ya no aparece en mdstat.
    private readonly HashSet<string> _operationsInProgress = new(StringComparer.Ordinal);

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

        var raidService = RaidService.Instance;

        var arrays = await raidService.GetArraysAsync().ConfigureAwait(false);
        var disks = await raidService.GetAllDisksAsync().ConfigureAwait(false);

        lock (_lock)
        {
            Arrays = arrays;
            Disks = disks;

            // Se aplica sobre la lectura nueva, nunca sobre un progreso "atascado".
            NormalizeFinishedOperations();
        }

        StateChanged?.Invoke();
    }

    private static bool IsInOperation(RaidArrayInfo array) =>
        array.State is RaidArrayState.Resync or RaidArrayState.Rebuilding or RaidArrayState.Recovering;

    private void NormalizeFinishedOperations()
    {
        var stillRunning = new HashSet<string>(StringComparer.Ordinal);

        foreach (var array in Arrays)
        {
            if (IsInOperation(array))
            {
                stillRunning.Add(array.Name);
                continue;
            }

            // Solo cerrar si la lectura anterior sí mostró una operación activa.
            if (!_operationsInProgress.Contains(array.Name))
                continue;

            LogService.Debug($"[STATE] Operación finalizada confirmada en {array.Name}");

            array.RebuildProgress = 100;
            array.RebuildETA = "0 min";
            array.State = RaidArrayState.Clean;
        }

        _operationsInProgress.Clear();
        foreach (var name in stillRunning)
            _operationsInProgress.Add(name);
    }
}
