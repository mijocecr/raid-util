using System;
using Avalonia.Threading;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Services;
using RAID_Util.Views.Tabs;

public class TimerManager
{
    private readonly DispatcherTimer _generalTimer;
    private readonly DispatcherTimer _hotplugTimer;
    private readonly DispatcherTimer _rebuildTimer;

    private readonly StatusView? _statusView;
    private readonly LogsView? _logsView;
    private readonly RaidStateService _stateService;

    public TimerManager(
        StatusView? statusView,
        LogsView? logsView,
        int generalRefreshMs,
        int rebuildRefreshMs,
        int hotplugRefreshMs,
        RaidStateService stateService)
    {
        _statusView = statusView;
        _logsView = logsView;
        _stateService = stateService;

        // GENERAL TIMER
        _generalTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(generalRefreshMs)
        };
        _generalTimer.Tick += GeneralTimer_Tick;

        // REBUILD TIMER
        _rebuildTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(rebuildRefreshMs)
        };
        _rebuildTimer.Tick += RebuildTimer_Tick;

        // HOTPLUG TIMER
        _hotplugTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(hotplugRefreshMs)
        };
        _hotplugTimer.Tick += HotplugTimer_Tick;
    }

    // ============================================================
    // TIMER HANDLERS (async void is required by Avalonia)
    // ============================================================

    private async void GeneralTimer_Tick(object? sender, EventArgs e)
    {
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[TIMER] General tick blocked → AllowRaidCalls = false");
            return;
        }

        await _stateService.RefreshAsync();

        if (_statusView is not null)
            await _statusView.RefreshStatusAsync();
    }

    private async void RebuildTimer_Tick(object? sender, EventArgs e)
    {
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[TIMER] Rebuild tick blocked → AllowRaidCalls = false");
            return;
        }

        // Future: UpdateRebuildStatus()
    }

    private async void HotplugTimer_Tick(object? sender, EventArgs e)
    {
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[TIMER] Hotplug tick blocked → AllowRaidCalls = false");
            return;
        }

        // Future: CheckHotplug()
    }

    // ============================================================
    // START / STOP
    // ============================================================

    public void StartAll()
    {
        // Do NOT start timers if system is not ready
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[TIMER] StartAll blocked → AllowRaidCalls = false");
            return;
        }

        Console.WriteLine("[TIMER] Starting timers...");

        // Small delay to avoid immediate tick during UI load
        var delayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        delayTimer.Tick += (s, e) =>
        {
            delayTimer.Stop();

            _generalTimer.Start();
            _rebuildTimer.Start();
            _hotplugTimer.Start();

            Console.WriteLine("[TIMER] All timers started.");
        };

        delayTimer.Start();
    }

    public void StopAll()
    {
        _generalTimer.Stop();
        _rebuildTimer.Stop();
        _hotplugTimer.Stop();

        Console.WriteLine("[TIMER] All timers stopped.");
    }

    public void UpdateIntervals(int generalMs, int rebuildMs, int hotplugMs)
    {
        _generalTimer.Interval = TimeSpan.FromMilliseconds(generalMs);
        _rebuildTimer.Interval = TimeSpan.FromMilliseconds(rebuildMs);
        _hotplugTimer.Interval = TimeSpan.FromMilliseconds(hotplugMs);

        Console.WriteLine("[TIMER] Intervals updated.");
    }
}
