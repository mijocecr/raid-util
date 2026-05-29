using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RAID_Util.Helpers;
using RAID_Util.Services;
using RAID_Util.Views;
using RAID_Util.Views.Tabs;
using RAID_Util.Core;
using RAID_Util.Models;

namespace RAID_Util;

public partial class MainWindow : Window
{
    private TimerManager? _timerManager;
    private AppConfig _config = new AppConfig();

    private bool _sudoReady = false;
    private bool _raidLoaded = false;

    public MainWindow()
    {
        InitializeComponent();

        Width = 500;
        Height = 580;
        MinWidth = 500;
        MinHeight = 580;
        MaxWidth = 500;
        MaxHeight = 580;
        Title = "raid-util";

        LogService.Debug("[MAIN] Main window initialized.");

        SaveSettingButton.Click += OnOpenConfig;
        MainTabs.SelectionChanged += OnTabChanged;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        LogService.Write("[MAIN] RAID-Util startup sequence initiated.");

        // ⭐ MODO FAKE: no tocar sudo ni mdadm, pero sí inicializar vistas en fake
        if (RaidViewControl != null && RaidViewControl.IsFakeMode)
        {
            LogService.Write("[MAIN] FakeData mode detected → skipping sudo, mdadm and backend initialization.");
            StatusBarText.Text = "Fake data mode.";

            _sudoReady = true;
            _raidLoaded = true;

            if (DisksViewControl != null)
            {
                LogService.Debug("[MAIN] Inicializando DisksView en modo fake.");
                DisksViewControl.Initialize(sudoReady: true, forceFake: true);
            }

            return;
        }

        _config = ConfigManager.Load();
        LogService.Debug("[MAIN] Configuration loaded.");

        StatusBarText.Text = "Initializing...";
        await Task.Delay(120);

        const int maxAttempts = 2;
        int attempts = 0;

        while (true)
        {
            attempts++;

            LogService.Debug($"[MAIN] Requesting admin password... attempt {attempts}");
            await SolicitarPassword();

            if (string.IsNullOrWhiteSpace(Credentials.AdminPassword))
            {
                StatusBarText.Text = "Initialization aborted.";
                LogService.Error("[MAIN] Initialization aborted: no password provided.");
                return;
            }

            StatusBarText.Text = "Validating password...";
            LogService.Debug("[MAIN] Validating admin password...");

            var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot("echo OK");

            LogService.Debug($"[MAIN] sudo test exit={exit}");
            LogService.Debug($"[MAIN] sudo test stdout='{stdout.Trim()}'");
            if (!string.IsNullOrWhiteSpace(stderr))
                LogService.Debug($"[MAIN] sudo test stderr='{stderr.Trim()}'");

            if (exit == 0)
            {
                LogService.Write("[MAIN] Password validated successfully.");
                _sudoReady = true;

                // ⭐ Inicializar DisksView ahora que sudo está listo
                if (DisksViewControl != null)
                {
                    LogService.Debug("[MAIN] Inicializando DisksView después de validar sudo.");
                    DisksViewControl.Initialize(_sudoReady, forceFake: false);
                }

                break;
            }

            var dlg = new IncorrectPasswordDialog();
            await dlg.ShowDialog(this);

            if (attempts >= maxAttempts)
            {
                StatusBarText.Text = "Too many failed attempts.";
                Close();
                return;
            }
        }

        StatusBarText.Text = "Checking RAID subsystem...";
        var (mdExit, mdOut, mdErr) = ShellHelper.EjecutarComoRoot("which mdadm");

        LogService.Debug($"[MAIN] which mdadm exit={mdExit}, out='{mdOut.Trim()}', err='{mdErr.Trim()}'");

        if (mdExit != 0 || string.IsNullOrWhiteSpace(mdOut))
        {
            StatusBarText.Text = "RAID subsystem unavailable.";
            LogService.Error("[MAIN] RAID subsystem unavailable (mdadm not found).");
            return;
        }

        await Task.Delay(120);

        StatusBarText.Text = "System ready.";

        _timerManager = new TimerManager(
            statusView: StatusViewControl,
            logsView: LogsViewControl,
            generalRefreshMs: _config.GeneralRefreshMs,
            rebuildRefreshMs: _config.RebuildRefreshMs,
            hotplugRefreshMs: _config.HotplugRefreshMs
        );

        _timerManager.StartAll();

        LogService.Debug($"[MAIN] OnOpened completed. Current SelectedIndex={MainTabs.SelectedIndex}, _sudoReady={_sudoReady}, _raidLoaded={_raidLoaded}");
        StatusBarText.Text = "Ready.";
    }

    private async Task SolicitarPassword()
    {
        var dialog = new PasswordDialog();
        var pass = await dialog.ShowDialog<string?>(this);
        Credentials.AdminPassword = pass ?? string.Empty;
    }

    private async void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        LogService.Debug($"[MAIN] TabIndexChanged → SelectedIndex={MainTabs.SelectedIndex}, _sudoReady={_sudoReady}, _raidLoaded={_raidLoaded}");

        // ⭐ Inicializar DisksView si el usuario abre la pestaña Disks
        if (MainTabs.SelectedIndex == 2 && _sudoReady)
        {
            if (DisksViewControl != null)
            {
                LogService.Debug("[MAIN] Inicializando DisksView desde TabChanged.");
                DisksViewControl.Initialize(_sudoReady, forceFake: false);
            }
        }

        if (!_sudoReady)
        {
            LogService.Debug("[MAIN] TabIndexChanged → ignored because _sudoReady=False");
            return;
        }

        if (_raidLoaded)
        {
            LogService.Debug("[MAIN] TabIndexChanged → RAID already loaded, skipping.");
            return;
        }

        if (MainTabs.SelectedIndex == 1) // RAID tab
        {
            LogService.Debug("[MAIN] TabIndexChanged → BEFORE LoadRaidAsync()");
            StatusBarText.Text = "Loading RAID information...";

            await LoadRaidAsync();

            _raidLoaded = true;
            LogService.Debug("[MAIN] TabIndexChanged → AFTER LoadRaidAsync()");
        }
    }

    private async Task LoadRaidAsync()
    {
        LogService.Write("[MAIN] ================= RAID LOAD START =================");
        LogService.Debug("[MAIN] LoadRaidAsync() ENTER");

        try
        {
            using (LoadingService.Show("Loading RAID arrays..."))
            {
                var service = new RaidService();

                LogService.Debug("[MAIN] Calling RaidService.GetArraysAsync()...");
                var arrays = await service.GetArraysAsync();

                LogService.Debug($"[MAIN] Arrays returned: {arrays.Count}");
                foreach (var a in arrays)
                    LogService.Debug($"[MAIN] ARRAY → {a.Name} | Level={a.Level} | State={a.State} | Disks={a.Disks.Count}");

                LogService.Debug("[MAIN] Calling RaidService.GetAllDisksAsync()...");
                var disks = await service.GetAllDisksAsync();

                LogService.Debug($"[MAIN] Disks returned: {disks.Count}");
                foreach (var d in disks)
                    LogService.Debug($"[MAIN] DISK → {d.Name} | Array={d.ArrayName} | Role={d.Role} | State={d.State} | Rota={d.IsRotational}");

                if (RaidViewControl == null)
                {
                    LogService.Error("[MAIN] ERROR: RaidViewControl is NULL → the control was not loaded from XAML.");
                }
                else
                {
                    LogService.Debug("[MAIN] Sending arrays to RaidViewControl.SetArrays()...");
                    RaidViewControl.SetArrays(arrays);
                }

                StatusBarText.Text = "RAID information loaded.";
            }

            LogService.Write("[MAIN] ================= RAID LOAD END =================");
        }
        catch (Exception ex)
        {
            LogService.Error("[MAIN] LoadRaidAsync() EXCEPTION:");
            LogService.Error(ex.ToString());

            StatusBarText.Text = "Error loading RAID information.";

            LogService.Write("[MAIN] ================= RAID LOAD FAILED =================");
        }
        finally
        {
            LogService.Debug("[MAIN] LoadRaidAsync() EXIT");
        }
    }

    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var win = new ConfigWindow(_config);
        await win.ShowDialog(this);

        if (_timerManager is not null)
        {
            _timerManager.UpdateIntervals(
                _config.GeneralRefreshMs,
                _config.RebuildRefreshMs,
                _config.HotplugRefreshMs
            );
        }
    }

    private void Set_Status(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Cerratonix  | https://github.com/mijocecr");
    }

    public void UpdateStatus(string text)
    {
        StatusBarText.Text = text;
    }

    private void onStatus_Clicked(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("System Overview");
    }

    private void onRaid_Clicked(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Redundant Array of Independent Disks");
    }

    private void onDisks_Clicked(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Physical Disks ");
    }

    private void onLogss_Clicked(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Raid Logs ");
    }
}
