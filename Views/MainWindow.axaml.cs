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
    private TimerManager? _timerManager;

    // ⭐ Servicio de estado RAID en memoria
    private readonly RaidStateService _stateService = new();

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

        // ⭐ Conectar RaidView al estado en memoria
        if (RaidViewControl != null)
            RaidViewControl.AttachStateService(_stateService);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        LogService.Write("[MAIN] RAID-Util startup sequence initiated.");

        // ⭐ MODO FAKE
        if (RaidViewControl != null && RaidViewControl.IsFakeMode)
        {
            LogService.Write("[MAIN] FakeData mode detected → skipping sudo, mdadm and backend initialization.");
            StatusBarText.Text = "Fake data mode.";

            _sudoReady = true;
            _raidLoaded = true;

            if (DisksViewControl != null)
            {
                LogService.Debug("[MAIN] Inicializando DisksView en modo fake.");
                DisksViewControl.Initialize(true, true);
            }

            return;
        }

        _config = ConfigManager.Load();
        LogService.Debug("[MAIN] Configuration loaded.");

        StatusBarText.Text = "Initializing...";
        await Task.Delay(120);

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
                LogService.Error("[MAIN] Initialization aborted: no password provided.");
                return;
            }

            StatusBarText.Text = "Validating password...";
            LogService.Debug("[MAIN] Validating admin password...");

            // ⭐ sudo test en background para no bloquear UI
            var sudoResult = await Task.Run(() => ShellHelper.EjecutarComoRoot("echo OK"));
            var (exit, stdout, stderr) = sudoResult;

            LogService.Debug($"[MAIN] sudo test exit={exit}");
            LogService.Debug($"[MAIN] sudo test stdout='{stdout.Trim()}'");
            if (!string.IsNullOrWhiteSpace(stderr))
                LogService.Debug($"[MAIN] sudo test stderr='{stderr.Trim()}'");

            if (exit == 0)
            {
                LogService.Write("[MAIN] Password validated successfully.");
                _sudoReady = true;
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

        // ⭐ which mdadm también en background
        var mdResult = await Task.Run(() => ShellHelper.EjecutarComoRoot("which mdadm"));
        var (mdExit, mdOut, mdErr) = mdResult;

        LogService.Debug($"[MAIN] which mdadm exit={mdExit}, out='{mdOut.Trim()}', err='{mdErr.Trim()}'");

        if (mdExit != 0 || string.IsNullOrWhiteSpace(mdOut))
        {
            StatusBarText.Text = "RAID subsystem unavailable.";
            LogService.Error("[MAIN] RAID subsystem unavailable (mdadm not found).");
            return;
        }

        await Task.Delay(120);

        StatusBarText.Text = "System ready.";

        // ⭐ TimerManager ahora usa el estado en memoria
        _timerManager = new TimerManager(
            StatusViewControl,
            LogsViewControl,
            _config.GeneralRefreshMs,
            _config.RebuildRefreshMs,
            _config.HotplugRefreshMs,
            _stateService
        );

        _timerManager.StartAll();

        LogService.Debug(
            $"[MAIN] OnOpened completed. Current SelectedIndex={MainTabs.SelectedIndex}, _sudoReady={_sudoReady}, _raidLoaded={_raidLoaded}");
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
        LogService.Debug(
            $"[MAIN] TabIndexChanged → SelectedIndex={MainTabs.SelectedIndex}, _sudoReady={_sudoReady}, _raidLoaded={_raidLoaded}");

        // ⭐ Inicializar DisksView SOLO cuando el usuario abre la pestaña Disks
        if (MainTabs.SelectedIndex == 2 && _sudoReady)
            if (DisksViewControl != null)
            {
                LogService.Debug("[MAIN] Inicializando DisksView desde TabChanged.");
                DisksViewControl.Initialize(_sudoReady, false);
            }

        if (!_sudoReady)
        {
            LogService.Debug("[MAIN] TabIndexChanged → ignored because _sudoReady=False");
            return;
        }

        // ⭐ RAID: cargar una sola vez, en background, sin bloquear la UI
        if (MainTabs.SelectedIndex == 1) // RAID tab
        {
            if (_raidLoaded || _raidLoading)
            {
                LogService.Debug("[MAIN] TabIndexChanged → RAID already loaded or loading, skipping.");
                return;
            }

            _raidLoading = true;
            StatusBarText.Text = "Loading RAID information...";

            LogService.Debug("[MAIN] TabIndexChanged → BEFORE LoadRaidAsync() (fire-and-forget)");

            _ = LoadRaidAsync();
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
                    LogService.Debug(
                        $"[MAIN] ARRAY → {a.Name} | Level={a.Level} | State={a.State} | Disks={a.Disks.Count}");

                LogService.Debug("[MAIN] Calling RaidService.GetAllDisksAsync()...");
                var disks = await service.GetAllDisksAsync();

                LogService.Debug($"[MAIN] Disks returned: {disks.Count}");
                foreach (var d in disks)
                    LogService.Debug(
                        $"[MAIN] DISK → {d.Name} | Array={d.ArrayName} | Role={d.Role} | State={d.State} | Rota={d.IsRotational}");

                if (RaidViewControl == null)
                {
                    LogService.Error("[MAIN] ERROR: RaidViewControl is NULL → the control was not loaded from XAML.");
                }
                else
                {
                    LogService.Debug("[MAIN] Sending arrays to RaidViewControl.SetArrays()...");

                    // ⭐ Asegurar actualización en hilo UI
                    await Dispatcher.UIThread.InvokeAsync(() => { RaidViewControl.SetArrays(arrays); });
                }

                StatusBarText.Text = "RAID information loaded.";
            }

            _raidLoaded = true;
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
            _raidLoading = false;
            LogService.Debug("[MAIN] LoadRaidAsync() EXIT");
        }
    }

    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var win = new ConfigWindow(_config);
        await win.ShowDialog(this);

        if (_timerManager is not null)
            _timerManager.UpdateIntervals(
                _config.GeneralRefreshMs,
                _config.RebuildRefreshMs,
                _config.HotplugRefreshMs
            );
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
