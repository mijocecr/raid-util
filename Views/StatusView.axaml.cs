using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using RAID_Util.Services;
using RAID_Util.Helpers;

namespace RAID_Util.Views.Tabs;

public partial class StatusView : UserControl
{
    private readonly StatusService _status;

    public StatusView()
    {
        InitializeComponent();
        _status = new StatusService();

        AttachedToVisualTree += OnAttached;
    }

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[STATUS] Sudo not ready → skipping initial load.");
            return;
        }

        await LoadStatusAsync();
    }

    public async Task RefreshStatusAsync()
    {
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[STATUS] Refresh blocked → sudo not ready.");
            return;
        }

        await LoadStatusAsync();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
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
        Console.WriteLine("\n===============================");
        Console.WriteLine("[STATUS] REFRESH START");
        Console.WriteLine("===============================");

        try
        {
            if (!Credentials.AllowRaidCalls)
            {
                Console.WriteLine("[STATUS] LoadStatusAsync aborted → sudo not ready.");
                return;
            }

            // -------------------------------
            // RAID INFO
            // -------------------------------
            var raidHealth = await _status.GetOverallRaidHealthAsync();
            var arraysSummary = await _status.GetArraysSummaryAsync();
            var disksSummary = await _status.GetDisksSummaryAsync();
            var rebuildSummary = await _status.GetRebuildSummaryAsync();
            var arraysAtRisk = await _status.GetArraysAtRiskAsync();
            var diskAlerts = await _status.GetDiskAlertsAsync();
            var recentEvents = await _status.GetRecentEventsAsync();

            // -------------------------------
            // SYSTEM INFO
            // -------------------------------
            var uptime = _status.GetSystemUptime();
            var cpu = _status.GetCpuUsage();
            var mem = _status.GetMemoryUsage();
            var diskFree = _status.GetDiskFree();

            // -------------------------------
            // ASIGNAR A LA UI
            // -------------------------------
            UpdateRaidHealthUI(raidHealth);

            TxtArraysSummary.Text = arraysSummary;
            TxtDisksSummary.Text = disksSummary;
            TxtRebuildSummary.Text = rebuildSummary;

            ListArraysAtRisk.ItemsSource = arraysAtRisk;
            ListDiskAlerts.ItemsSource = diskAlerts;
            ListRecentEvents.ItemsSource = recentEvents;

            TxtSystemUptime.Text = $"Uptime: {uptime}";
            TxtSystemCpu.Text = $"CPU: {cpu}%";
            TxtSystemRam.Text = $"RAM: {mem}%";
            TxtSystemDisk.Text = $"Disk Free: {diskFree} GB";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS] ERROR: {ex}");
            TxtOverallRaidHealth.Text = $"Error: {ex.Message}";
            RaidHealthCard.Background = this.FindResource("RaidCriticalBrush") as IBrush;
            TxtOverallRaidHealth.Foreground = Brushes.IndianRed;
        }

        swTotal.Stop();
        Console.WriteLine($"[STATUS] TOTAL REFRESH TIME = {swTotal.ElapsedMilliseconds} ms");
        Console.WriteLine("===============================");
        Console.WriteLine("[STATUS] REFRESH END");
        Console.WriteLine("===============================\n");
    }

    // ============================================================
    // RAID HEALTH UI (COLOR DINÁMICO)
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

    // ============================================================
    // HELPERS
    // ============================================================
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
