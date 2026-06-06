using System;
using Avalonia.Threading;
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

        // TIMER GENERAL
        _generalTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(generalRefreshMs)
        };
        _generalTimer.Tick += async (_, _) =>
        {
            await _stateService.RefreshAsync();

            if (_statusView is not null)
                await _statusView.RefreshStatusAsync();
        };

        // TIMER REBUILD
        _rebuildTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(rebuildRefreshMs)
        };
        _rebuildTimer.Tick += async (_, _) =>
        {
            // UpdateRebuildStatus() irá aquí
        };

        // TIMER HOTPLUG
        _hotplugTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(hotplugRefreshMs)
        };
        _hotplugTimer.Tick += async (_, _) =>
        {
            // CheckHotplug() irá aquí
        };
    }

    public void StartAll()
    {
        _generalTimer.Start();
        _rebuildTimer.Start();
        _hotplugTimer.Start();
    }

    public void StopAll()
    {
        _generalTimer.Stop();
        _rebuildTimer.Stop();
        _hotplugTimer.Stop();
    }

    public void UpdateIntervals(int generalMs, int rebuildMs, int hotplugMs)
    {
        _generalTimer.Interval = TimeSpan.FromMilliseconds(generalMs);
        _rebuildTimer.Interval = TimeSpan.FromMilliseconds(rebuildMs);
        _hotplugTimer.Interval = TimeSpan.FromMilliseconds(hotplugMs);
    }
}
