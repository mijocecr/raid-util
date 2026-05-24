using System;
using Avalonia.Threading;
using RAID_Util.Views.Tabs;

namespace RAID_Util.Core;

public class TimerManager
{
    private readonly StatusView? _statusView;
   // private readonly RaidView? _raidView;
    //private readonly DisksView? _disksView;
    private readonly LogsView? _logsView;

    private readonly DispatcherTimer _generalTimer;
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _hotplugTimer;

    public TimerManager(
        StatusView? statusView,
     //   RaidView? raidView,
      //  DisksView? disksView,
        LogsView? logsView,
        int generalRefreshMs,
        int rebuildRefreshMs,
        int hotplugRefreshMs)
    {
        _statusView = statusView;
       // _raidView = raidView;
       // _disksView = disksView;
        _logsView = logsView;

        // TIMER GENERAL
        _generalTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(generalRefreshMs)
        };
        _generalTimer.Tick += async (_, _) =>
        {
            if (_statusView is not null)
                await _statusView.UpdateStatus();

         //   if (_disksView is not null)
          //      await _disksView.UpdateDisks();

            // Si quieres, aquí también algo ligero de RAID
          //  if (_raidView is not null)
           //     await _raidView.UpdateRaidSummary();
        };

        // TIMER REBUILD
        _rebuildTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(rebuildRefreshMs)
        };
        _rebuildTimer.Tick += async (_, _) =>
        {
           // if (_raidView is not null)
             //   await _raidView.UpdateRebuildStatus();
        };

        // TIMER HOTPLUG
        _hotplugTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(hotplugRefreshMs)
        };
        _hotplugTimer.Tick += async (_, _) =>
        {
          //  if (_disksView is not null)
           //     await _disksView.CheckHotplug();
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
