using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        _ = Task.Run(async () =>
        {
            var raidService = new RaidService();

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

