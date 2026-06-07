using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Models;
using RAID_Util.Services;
using RAID_Util.Views;

namespace RAID_Util;

public partial class MainWindow : Window
{
    private AppConfig _config = new();
    private bool _raidLoaded;
    private bool _raidLoading;

    private bool _sudoReady;
    private bool _raidSubsystemReady;

    private TimerManager? _timerManager;

    private readonly RaidStateService _stateService = new();

    public MainWindow()
    {
        LogService.Debug("[MAIN] Constructor ENTER");

        try
        {
            LogService.Debug("[MAIN] BEFORE InitializeComponent()");
            InitializeComponent();
            LogService.Debug("[MAIN] AFTER InitializeComponent()");
        }
        catch (Exception ex)
        {
            LogService.Error("[MAIN] InitializeComponent() EXCEPTION:");
            LogService.Error(ex.ToString());
            throw;
        }

        Width = 500;
        Height = 580;
        MinWidth = 500;
        MinHeight = 580;
        MaxWidth = 500;
        MaxHeight = 580;
        Title = "raid-util";

        LogService.Debug("[MAIN] Main window initialized.");

        if (SaveSettingButton != null)
            SaveSettingButton.Click += OnOpenConfig;

        if (MainTabs != null)
            MainTabs.SelectionChanged += OnTabChanged;

        if (RaidViewControl != null)
            RaidViewControl.AttachStateService(_stateService);

        LogService.Debug("[MAIN] Constructor EXIT");
    }

    public void UpdateStatus(string text)
    {
        if (StatusBarText != null)
            StatusBarText.Text = text;
    }

    protected override async void OnOpened(EventArgs e)
    {
        LogService.Debug("[MAIN] OnOpened ENTER");
        base.OnOpened(e);

        LogService.Write("[MAIN] RAID-Util startup sequence initiated.");

        _config = ConfigManager.Load();
        LogService.Debug("[MAIN] Configuration loaded.");

        StatusBarText.Text = "Initializing...";
        await Task.Delay(150);

        // ============================================================
        // 1) UNIVERSAL SUDO VALIDATION
        // ============================================================

        const int maxAttempts = 2;
        var attempts = 0;

        while (true)
        {
            attempts++;

            LogService.Debug($"[MAIN] Requesting admin password... attempt {attempts}");
            await SolicitarPassword();

            if (string.IsNullOrWhiteSpace(Credentials.AdminPassword))
            {
                StatusBarText.Text = "Initialization aborted.";
                LogService.Error("[MAIN] No password provided.");
                return;
            }

            StatusBarText.Text = "Validating password...";
            LogService.Debug("[MAIN] Validating admin password...");

            var sudoResult = await Task.Run(() => ShellHelper.EjecutarComoRoot("true"));
            var (exit, stdout, stderr) = sudoResult;

            LogService.Debug($"[MAIN] sudo test exit={exit}");
            LogService.Debug($"[MAIN] sudo test stdout='{stdout.Trim()}'");
            LogService.Debug($"[MAIN] sudo test stderr='{stderr.Trim()}'");

            if (exit != 0)
            {
                LogService.Error("[MAIN] SUDO BLOCKED or FAILED → exit != 0");
                StatusBarText.Text = "Sudo is not accepting authentication.";

                await new InfoDialog(
                    "Sudo Blocked",
                    "Sudo is not accepting authentication.\n\n" +
                    "Possible causes:\n" +
                    "• Too many failed attempts (faillock)\n" +
                    "• PAM lockout\n" +
                    "• sudoers restrictions\n" +
                    "• Incorrect password\n\n" +
                    "To unlock sudo:\n" +
                    "sudo faillock --reset"
                ).ShowDialog(this);

                if (attempts >= maxAttempts)
                {
                    Close();
                    return;
                }

                continue;
            }

            // ⭐ Password correct
            LogService.Write("[MAIN] Password validated successfully.");
            _sudoReady = true;

            // ⭐ Enable RAID calls only after sudo is validated
            Credentials.AllowRaidCalls = true;

            // ⭐ FIX DEFINITIVO:
            // Esperar a que Avalonia construya el árbol visual ANTES de refrescar StatusView
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(50); // ⭐ Permite que el TabItem se construya

                if (StatusViewControl != null)
                {
                    LogService.Debug("[MAIN] StatusViewControl READY → refreshing...");
                    await StatusViewControl.RefreshStatusAsync();
                }
                else
                {
                    LogService.Error("[MAIN] StatusViewControl STILL NULL after delay.");
                }

            }, DispatcherPriority.Background);

            break;
        }

        // ============================================================
        // 2) CHECK mdadm (UNIVERSAL → SIN ROOT)
        // ============================================================

        StatusBarText.Text = "Checking RAID subsystem...";

        var mdResult = await Task.Run(() => ShellHelper.EjecutarSinRoot("which mdadm"));
        var (mdExit, mdOut, mdErr) = mdResult;

        LogService.Debug($"[MAIN] which mdadm exit={mdExit}, out='{mdOut.Trim()}', err='{mdErr.Trim()}'");

        if (mdExit != 0 || string.IsNullOrWhiteSpace(mdOut))
        {
            StatusBarText.Text = "RAID subsystem unavailable.";
            LogService.Error("[MAIN] mdadm not found.");
            _raidSubsystemReady = false;
            return;
        }

        _raidSubsystemReady = true;

        await Task.Delay(120);

        StatusBarText.Text = "System ready.";

        // ============================================================
        // 3) START TIMERS ONLY WHEN EVERYTHING IS READY
        // ============================================================

        _timerManager = new TimerManager(
            StatusViewControl,
            LogsViewControl,
            _config.GeneralRefreshMs,
            _config.RebuildRefreshMs,
            _config.HotplugRefreshMs,
            _stateService
        );

        LogService.Debug("[MAIN] Starting TimerManager...");
        _timerManager.StartAll();

        LogService.Debug("[MAIN] OnOpened completed.");
        StatusBarText.Text = "Ready.";
    }

    private async Task SolicitarPassword()
    {
        LogService.Debug("[MAIN] SolicitarPassword ENTER");
        var dialog = new PasswordDialog();
        var pass = await dialog.ShowDialog<string?>(this);
        Credentials.AdminPassword = pass ?? string.Empty;
        LogService.Debug("[MAIN] SolicitarPassword EXIT");
    }

    private async void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        LogService.Debug(
            $"[MAIN] TabIndexChanged → SelectedIndex={MainTabs.SelectedIndex}, sudoReady={_sudoReady}, raidReady={_raidSubsystemReady}, raidLoaded={_raidLoaded}");

        // DISKS TAB
        if (MainTabs.SelectedIndex == 2 && _sudoReady)
        {
            LogService.Debug("[MAIN] Initializing DisksView...");
            DisksViewControl?.Initialize(_sudoReady, false);
        }

        // RAID TAB
        if (MainTabs.SelectedIndex == 1)
        {
            if (!_sudoReady)
            {
                LogService.Debug("[MAIN] RAID tab ignored → sudo not ready.");
                return;
            }

            if (!_raidSubsystemReady)
            {
                LogService.Debug("[MAIN] RAID tab ignored → mdadm not available.");
                StatusBarText.Text = "RAID subsystem unavailable.";
                return;
            }

            if (_raidLoaded || _raidLoading)
            {
                LogService.Debug("[MAIN] RAID already loaded or loading.");
                return;
            }

            _raidLoading = true;
            StatusBarText.Text = "Loading RAID information...";

            LogService.Debug("[MAIN] Launching LoadRaidAsync...");
            _ = LoadRaidAsync();
        }
    }

    private async Task LoadRaidAsync()
    {
        LogService.Write("[MAIN] ================= RAID LOAD START =================");

        if (!_sudoReady)
        {
            LogService.Error("[MAIN] LoadRaidAsync aborted → sudo not ready.");
            return;
        }

        if (!_raidSubsystemReady)
        {
            LogService.Error("[MAIN] LoadRaidAsync aborted → mdadm not available.");
            return;
        }

        try
        {
            using (LoadingService.Show("Loading RAID arrays..."))
            {
                // ⭐ FIX: usar Singleton
                var service = RaidService.Instance;

                LogService.Debug("[MAIN] Calling GetArraysAsync...");
                var arrays = await service.GetArraysAsync();

                LogService.Debug($"[MAIN] Arrays returned: {arrays.Count}");

                LogService.Debug("[MAIN] Calling GetAllDisksAsync...");
                var disks = await service.GetAllDisksAsync();

                LogService.Debug($"[MAIN] Disks returned: {disks.Count}");

                if (RaidViewControl != null)
                {
                    RaidViewControl.ApplyTemplate();
                    RaidViewControl.UpdateLayout();

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        RaidViewControl.SetArrays(arrays);
                    });
                }

                StatusBarText.Text = "RAID information loaded.";
            }

            _raidLoaded = true;
            LogService.Write("[MAIN] ================= RAID LOAD END =================");
        }
        catch (Exception ex)
        {
            LogService.Error("[MAIN] LoadRaidAsync EXCEPTION:");
            LogService.Error(ex.ToString());
            StatusBarText.Text = "Error loading RAID information.";
        }
        finally
        {
            _raidLoading = false;
        }
    }

    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var win = new ConfigWindow(_config);
        await win.ShowDialog(this);

        _timerManager?.UpdateIntervals(
            _config.GeneralRefreshMs,
            _config.RebuildRefreshMs,
            _config.HotplugRefreshMs
        );
    }

    private void Set_Status(object? sender, PointerPressedEventArgs e)
    {
        StatusBarText.Text = "Cerratonix  | https://github.com/mijocecr";
    }

    private void onStatus_Clicked(object? sender, PointerPressedEventArgs e)
    {
        StatusBarText.Text = "System Overview";
    }

    private void onRaid_Clicked(object? sender, PointerPressedEventArgs e)
    {
        StatusBarText.Text = "Redundant Array of Independent Disks";
    }

    private void onDisks_Clicked(object? sender, PointerPressedEventArgs e)
    {
        StatusBarText.Text = "Physical Disks";
    }

    private void onLogss_Clicked(object? sender, PointerPressedEventArgs e)
    {
        StatusBarText.Text = "Raid Logs";
    }
}
