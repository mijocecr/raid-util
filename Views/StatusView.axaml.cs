using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RAID_Util.Services;
using RAID_Util.Helpers;

namespace RAID_Util.Views.Tabs;

public partial class StatusView : UserControl
{
    private readonly StatusService _status;
    private CancellationTokenSource? _autoRefreshCts;
    private int _refreshing;

    private sealed record StatusSnapshot(
        string RaidHealth,
        string ArraysSummary,
        string DisksSummary,
        string RebuildSummary,
        IList<ArrayRiskInfo> ArraysAtRisk,
        IList<DiskAlertInfo> DiskAlerts,
        IList<string> RecentEvents,
        string Uptime,
        int Cpu,
        int Memory,
        int DiskFree);

    public StatusView()
    {
        InitializeComponent();
        _status = new StatusService();

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    // ============================================================
    // AUTO REFRESH (CORREGIDO)
    // ============================================================
    private void StartAutoRefresh()
    {
        StopAutoRefresh(); // evitar duplicados

        _autoRefreshCts = new CancellationTokenSource();
        var token = _autoRefreshCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Toda comprobación visual debe hacerse en UI thread
                    bool isVisible = await Dispatcher.UIThread.InvokeAsync(() => this.IsVisible);

                    if (isVisible && Credentials.AllowRaidCalls)
                        await RefreshStatusAsync();
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteLine($"[STATUS] Auto-refresh error: {ex}");
                }

                await Task.Delay(5000, token); // refresco cada 5s
            }
        }, token);
    }

    private void StopAutoRefresh()
    {
        if (_autoRefreshCts != null)
        {
            _autoRefreshCts.Cancel();
            _autoRefreshCts.Dispose();
            _autoRefreshCts = null;
        }
    }

    // ============================================================
    // EVENTOS DE VIDA DEL CONTROL
    // ============================================================
    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!Credentials.AllowRaidCalls)
        {
            DebugConsole.WriteLine("[STATUS] Sudo not ready → skipping initial load.");
            return;
        }

        await Task.Delay(150); // evitar carrera con MainWindow
        await LoadStatusAsync();

        StartAutoRefresh();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        StopAutoRefresh();
    }

    // ============================================================
    // REFRESH MANUAL
    // ============================================================
    public async Task RefreshStatusAsync()
    {
        if (!Credentials.AllowRaidCalls)
        {
            DebugConsole.WriteLine("[STATUS] Refresh blocked → sudo not ready.");
            return;
        }

        // Evita solapar refrescos del timer con el botón manual: sin esto se
        // podían mezclar datos de dos lecturas distintas en la misma pantalla.
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            return;

        try
        {
            await LoadStatusAsync();
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (!BtnRefresh.IsEnabled)
            return;

        BtnRefresh.IsEnabled = false;
        await RefreshStatusAsync();
        BtnRefresh.IsEnabled = true;
    }

    // ============================================================
    // CARGA PRINCIPAL
    // ============================================================
    private async Task LoadStatusAsync()
    {
        var swTotal = Stopwatch.StartNew();

        try
        {
            // Toda la consulta (incluido el muestreo de CPU de 1 s) se hace
            // fuera del hilo UI; antes bloqueaba la ventana cada 5 s.
            var snapshot = await Task.Run(async () =>
            {
                var raidHealth = await _status.GetOverallRaidHealthAsync();
                var arraysSummary = await _status.GetArraysSummaryAsync();
                var disksSummary = await _status.GetDisksSummaryAsync();
                var rebuildSummary = await _status.GetRebuildSummaryAsync();
                var arraysAtRisk = await _status.GetArraysAtRiskAsync();
                var diskAlerts = await _status.GetDiskAlertsAsync();
                var recentEvents = await _status.GetRecentEventsAsync();

                return new StatusSnapshot(
                    raidHealth,
                    arraysSummary,
                    disksSummary,
                    rebuildSummary,
                    arraysAtRisk,
                    diskAlerts,
                    recentEvents,
                    _status.GetSessionUptime(),
                    _status.GetCpuUsage(),
                    _status.GetMemoryUsage(),
                    _status.GetDiskFree());
            });

            await Dispatcher.UIThread.InvokeAsync(() => ApplySnapshot(snapshot));
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[STATUS] ERROR: {ex}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TxtOverallRaidHealth.Text = $"Error: {ex.Message}";
                RaidHealthCard.Background = this.FindResource("RaidCriticalBrush") as IBrush;
                TxtOverallRaidHealth.Foreground = Brushes.IndianRed;
            });
        }

        swTotal.Stop();
        DebugConsole.WriteLine($"[STATUS] REFRESH END ({swTotal.ElapsedMilliseconds} ms)");
    }

    private void ApplySnapshot(StatusSnapshot snapshot)
    {
        UpdateRaidHealthUI(snapshot.RaidHealth);

        TxtArraysSummary.Text = snapshot.ArraysSummary;
        TxtDisksSummary.Text = snapshot.DisksSummary;
        TxtRebuildSummary.Text = snapshot.RebuildSummary;

        ListArraysAtRisk.ItemsSource = snapshot.ArraysAtRisk;
        ListDiskAlerts.ItemsSource = snapshot.DiskAlerts;
        ListRecentEvents.ItemsSource = snapshot.RecentEvents;

        TxtSystemUptime.Text = $"Uptime: {snapshot.Uptime}";
        TxtSystemCpu.Text = $"CPU: {snapshot.Cpu}%";
        TxtSystemRam.Text = $"RAM: {snapshot.Memory}%";
        // DriveInfo("/") mide la raíz, no /home: evitar llamarlo "Disk Free".
        TxtSystemDisk.Text = $"Root free: {snapshot.DiskFree} GB";
    }

    // ============================================================
    // RAID HEALTH UI
    // ============================================================
    private void UpdateRaidHealthUI(string state)
    {
        switch (state)
        {
            case "Healthy":
                RaidHealthCard.Background = this.FindResource("RaidHealthyBrush") as IBrush;
                TxtOverallRaidHealth.Foreground = Brushes.LimeGreen;
                TxtOverallRaidHealth.Text = "Healthy";
                break;

            case "Warning":
                RaidHealthCard.Background = this.FindResource("RaidWarningBrush") as IBrush;
                TxtOverallRaidHealth.Foreground = Brushes.Gold;
                TxtOverallRaidHealth.Text = "Warning";
                break;

            case "Critical":
                RaidHealthCard.Background = this.FindResource("RaidCriticalBrush") as IBrush;
                TxtOverallRaidHealth.Foreground = Brushes.IndianRed;
                TxtOverallRaidHealth.Text = "Critical";
                break;

            default:
                RaidHealthCard.Background = this.FindResource("RaidUnknownBrush") as IBrush;
                TxtOverallRaidHealth.Foreground = Brushes.Gray;
                TxtOverallRaidHealth.Text = "Unknown";
                break;
        }

        EnsureTransitions();
    }

    private void EnsureTransitions()
    {
        if (TxtOverallRaidHealth.Transitions != null)
            return;

        TxtOverallRaidHealth.Transitions = new Transitions
        {
            new BrushTransition
            {
                Property = TextBlock.ForegroundProperty,
                Duration = TimeSpan.FromMilliseconds(250)
            }
        };
    }
}
