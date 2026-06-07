using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using RAID_Util.Services;
using RAID_Util.Core;
using RAID_Util.Helpers;

namespace RAID_Util.Views.Tabs;

public partial class StatusView : UserControl
{
    private readonly StatusService _statusService;

    public StatusView()
    {
        InitializeComponent();

        _statusService = new StatusService();

        // Load only when attached AND sudo is validated
        AttachedToVisualTree += OnAttached;
    }

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!Credentials.AllowRaidCalls)
        {
            // Prevent RAID calls before sudo validation
            Console.WriteLine("[STATUSVIEW] Load blocked → AllowRaidCalls = false");
            return;
        }

        await LoadStatusAsync();
    }

    /// <summary>
    /// Called by TimerManager to refresh the status.
    /// </summary>
    public async Task RefreshStatusAsync()
    {
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[STATUSVIEW] Refresh blocked → AllowRaidCalls = false");
            return;
        }

        await LoadStatusAsync();
    }

    // ============================================================
    // MAIN STATUS LOADING (OPTIMIZED)
    // ============================================================
    private async Task LoadStatusAsync()
    {
        if (!Credentials.AllowRaidCalls)
        {
            Console.WriteLine("[STATUSVIEW] LoadStatusAsync blocked → AllowRaidCalls = false");
            return;
        }

        try
        {
            // --------------------------------------------------------
            // 0) Launch all tasks in parallel (MUCH FASTER)
            // --------------------------------------------------------
            var tHealth = _statusService.GetOverallRaidHealthAsync();
            var tArrays = _statusService.GetArraysSummaryAsync();
            var tDisks = _statusService.GetDisksSummaryAsync();
            var tRebuild = _statusService.GetRebuildSummaryAsync();
            var tAtRisk = _statusService.GetArraysAtRiskAsync();
            var tDiskAlerts = _statusService.GetDiskAlertsAsync();
            var tEvents = _statusService.GetRecentEventsAsync();

            await Task.WhenAll(tHealth, tArrays, tDisks, tRebuild, tAtRisk, tDiskAlerts, tEvents);

            // --------------------------------------------------------
            // 1) Global RAID health
            // --------------------------------------------------------
            var raidHealth = tHealth.Result;
            TxtOverallRaidHealth.Text = raidHealth;

            EnsureTransitions();
            TxtOverallRaidHealth.Foreground = GetHealthBrush(raidHealth);

            // --------------------------------------------------------
            // 2) Numeric summaries
            // --------------------------------------------------------
            TxtArraysSummary.Text = tArrays.Result;
            TxtDisksSummary.Text = tDisks.Result;
            TxtRebuildSummary.Text = tRebuild.Result;

            // --------------------------------------------------------
            // 3) Arrays at risk
            // --------------------------------------------------------
            ListArraysAtRisk.ItemsSource = tAtRisk.Result;

            // --------------------------------------------------------
            // 4) Disk alerts
            // --------------------------------------------------------
            ListDiskAlerts.ItemsSource = tDiskAlerts.Result;

            // --------------------------------------------------------
            // 5) Recent events
            // --------------------------------------------------------
            ListRecentEvents.ItemsSource = tEvents.Result;
        }
        catch (Exception ex)
        {
            TxtOverallRaidHealth.Text = $"Error: {ex.Message}";
            TxtOverallRaidHealth.Foreground = this.FindResource("BMWHealthCriticalBrush") as IBrush;
        }
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

    private IBrush GetHealthBrush(string state)
    {
        return state switch
        {
            "Healthy" => this.FindResource("BMWHealthHealthyBrush") as IBrush,
            "Warning" => this.FindResource("BMWHealthWarningBrush") as IBrush,
            "Critical" => this.FindResource("BMWHealthCriticalBrush") as IBrush,
            _ => this.FindResource("BMWHealthNoneBrush") as IBrush
        };
    }
}
