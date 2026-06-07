using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public class RaidStateService
{
    public List<RaidArrayInfo> Arrays { get; private set; } = new();
    public List<RaidDiskInfo> Disks { get; private set; } = new();

    public event Action? StateChanged;

    private readonly object _lock = new();

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
            // ⭐ FIX: usar Singleton
            var raidService = RaidService.Instance;

            var arrays = await raidService.GetArraysAsync();
            var disks = await raidService.GetAllDisksAsync();

            lock (_lock)
            {
                Arrays = arrays;
                Disks = disks;
            }

            StateChanged?.Invoke();
        });
    }
}