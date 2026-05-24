using System;
using Avalonia.Threading;
using RAID_Util.Views.Tabs;

namespace RAID_Util.Core;

public class TimerManager
{
    private readonly StatusView? _statusView;
    private readonly LogsView? _logsView;

    private readonly DispatcherTimer _generalTimer;
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _hotplugTimer;

    public TimerManager(
        StatusView? statusView,
        LogsView? logsView,
        int generalRefreshMs,
        int rebuildRefreshMs,
        int hotplugRefreshMs)
    {
        _statusView = statusView;
        _logsView = logsView;

        // TIMER GENERAL
        _generalTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(generalRefreshMs)
        };
        _generalTimer.Tick += async (_, _) =>
        {
            if (_statusView is not null)
                await _statusView.RefreshStatusAsync();
        };

        // TIMER REBUILD (vacío por ahora)
        _rebuildTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(rebuildRefreshMs)
        };
        _rebuildTimer.Tick += async (_, _) =>
        {
            // Aquí irá UpdateRebuildStatus() cuando exista RaidView
        };

        // TIMER HOTPLUG (vacío por ahora)
        _hotplugTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(hotplugRefreshMs)
        };
        _hotplugTimer.Tick += async (_, _) =>
        {
            // Aquí irá CheckHotplug() cuando exista DisksView
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
