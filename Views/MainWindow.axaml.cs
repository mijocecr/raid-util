using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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

        // Ruta B: cargar RAID al entrar a la pestaña
        MainTabs.SelectionChanged += OnTabChanged;
    }

    // ============================================================
    // FLUJO PRINCIPAL OnOpened 
    // ============================================================
   
    protected override async void OnOpened(EventArgs e)
{
    base.OnOpened(e);

    LogService.Write("[MAIN] RAID-Util startup sequence initiated.");

    // 0) Config
    _config = ConfigManager.Load();
    LogService.Debug("[MAIN] Configuration loaded.");

    StatusBarText.Text = "Initializing...";
    await Task.Delay(120);

    // 1) Password + sudo
    const int maxAttempts = 2;
    int attempts = 0;

    while (true)
    {
        attempts++;

        LogService.Debug($"[MAIN] Requesting admin password... attempt {attempts}");
        await SolicitarPassword();

        LogService.Debug($"[MAIN] Password length: {Credentials.AdminPassword?.Length ?? 0}");

        if (string.IsNullOrWhiteSpace(Credentials.AdminPassword))
        {
            StatusBarText.Text = "Initialization aborted.";
            LogService.Error("[MAIN] Initialization aborted: no password provided.");
            return;
        }

        StatusBarText.Text = "Validating password...";
        LogService.Debug("[MAIN] Validating admin password...");

        // 👇 OJO: aquí ya NO pasamos 'bash -c ...'
        var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot("echo OK");

        LogService.Debug($"[MAIN] sudo test exit={exit}");
        LogService.Debug($"[MAIN] sudo stdout='{stdout}'");
        LogService.Debug($"[MAIN] sudo stderr='{stderr}'");

        if (exit == 0)
        {
            LogService.Write("[MAIN] Password validated successfully.");
            break;
        }

        LogService.Error($"[MAIN] Incorrect administrator password. attempt={attempts}");

        var dlg = new IncorrectPasswordDialog();
        await dlg.ShowDialog(this);

        if (attempts >= maxAttempts)
        {
            LogService.Error("[MAIN] Too many failed attempts. Closing to avoid sudo lockout.");
            StatusBarText.Text = "Too many failed attempts. Try again later.";
            Close();
            return;
        }
    }

    // 2) Comprobar mdadm (también sin doble bash)
    StatusBarText.Text = "Checking RAID subsystem...";
    LogService.Debug("[MAIN] Checking mdadm availability...");

    var (mdExit, mdOut, mdErr) = ShellHelper.EjecutarComoRoot("which mdadm");
    LogService.Debug($"[MAIN] which mdadm exit={mdExit}, out='{mdOut}', err='{mdErr}'");

    if (mdExit != 0 || string.IsNullOrWhiteSpace(mdOut))
    {
        LogService.Error("[MAIN] mdadm not found. RAID subsystem unavailable.");
        StatusBarText.Text = "RAID subsystem unavailable.";
        return;
    }

    await Task.Delay(120);

    // 3) Status inicial
    StatusBarText.Text = "System ready.";
    LogService.Debug("[MAIN] Status tab initialized.");

    // 4) TimerManager
    LogService.Debug("[MAIN] Initializing TimerManager...");

    _timerManager = new TimerManager(
        statusView: StatusViewControl,
        logsView: LogsViewControl,
        generalRefreshMs: _config.GeneralRefreshMs,
        rebuildRefreshMs: _config.RebuildRefreshMs,
        hotplugRefreshMs: _config.HotplugRefreshMs
    );

    _timerManager.StartAll();

    LogService.Write("[MAIN] TimerManager started.");

    // 5) Fin
    StatusBarText.Text = "Ready.";
    LogService.Write("[MAIN] Initialization completed. System ready.");
}

    
    // ============================================================
    // DIÁLOGO DE CONTRASEÑA
    // ============================================================
    private async Task SolicitarPassword()
    {
        LogService.Debug("[MAIN] Opening password dialog...");

        var dialog = new PasswordDialog();
        var pass = await dialog.ShowDialog<string?>(this);

        Credentials.AdminPassword = pass ?? string.Empty;

        LogService.Debug("[MAIN] Password dialog closed.");
    }

    // ============================================================
    // RUTA B: CARGAR RAID AL ENTRAR A LA PESTAÑA
    // ============================================================
    private async void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedIndex == 1) // pestaña RAID
        {
            StatusBarText.Text = "Loading RAID information...";
            LogService.Debug("[MAIN] RAID tab selected → triggering LoadRaidAsync()");
            await LoadRaidAsync();
        }
    }

    // ============================================================
    // CARGA REAL DE RAID (CON LOGS DETALLADOS)
    // ============================================================
    private async Task LoadRaidAsync()
    {
        LogService.Write("[MAIN] ================= RAID LOAD START =================");

        try
        {
            using (LoadingService.Show("Loading RAID arrays..."))
            {
                var service = new RaidService();

                // ------------------------------------------------------------
                // 1) Obtener arrays
                // ------------------------------------------------------------
                LogService.Debug("[MAIN] Calling RaidService.GetArraysAsync()...");
                var arrays = await service.GetArraysAsync();
                LogService.Debug($"[MAIN] Arrays returned: {arrays.Count}");

                foreach (var a in arrays)
                {
                    LogService.Debug($"[MAIN] ARRAY → {a.Name} | Level={a.Level} | State={a.State}");
                }

                // ------------------------------------------------------------
                // 2) Obtener discos
                // ------------------------------------------------------------
                LogService.Debug("[MAIN] Calling RaidService.GetAllDisksAsync()...");
                var disks = await service.GetAllDisksAsync();
                LogService.Debug($"[MAIN] Disks returned: {disks.Count}");

                foreach (var d in disks)
                {
                    LogService.Debug($"[MAIN] DISK → {d.Name} | Array={d.ArrayName} | Role={d.Role} | State={d.State}");
                }

                // ------------------------------------------------------------
                // 3) Asociar discos a arrays
                // ------------------------------------------------------------
                LogService.Debug("[MAIN] Associating disks to arrays...");

                foreach (var array in arrays)
                {
                    array.Disks = disks.FindAll(d => d.ArrayName == array.Name);

                    LogService.Debug($"[MAIN] ARRAY {array.Name} → {array.Disks.Count} disks associated");

                    foreach (var d in array.Disks)
                    {
                        LogService.Debug($"[MAIN]   ↳ DISK {d.Name} | Role={d.Role} | State={d.State}");
                    }
                }

                // ------------------------------------------------------------
                // 4) Enviar datos a la GUI
                // ------------------------------------------------------------
                LogService.Debug("[MAIN] Sending arrays to RaidView.SetArrays()...");
               // RaidView.SetArrays(arrays);

                StatusBarText.Text = "RAID information loaded.";
            }

            LogService.Write("[MAIN] ================= RAID LOAD END =================");
        }
        catch (Exception ex)
        {
            LogService.Error("[MAIN] EXCEPTION DURING RAID LOAD:");
            LogService.Error(ex.ToString());

            StatusBarText.Text = "Error loading RAID information.";

            // fallback seguro
           // RaidView.SetArrays(new());

            LogService.Write("[MAIN] ================= RAID LOAD FAILED =================");
        }
    }

    // ============================================================
    // CONFIG WINDOW
    // ============================================================
    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        LogService.Debug("[MAIN] Opening configuration window...");

        var win = new ConfigWindow(_config);
        await win.ShowDialog(this);

        LogService.Write("[MAIN] Configuration updated.");

        if (_timerManager is not null)
        {
            _timerManager.UpdateIntervals(
                _config.GeneralRefreshMs,
                _config.RebuildRefreshMs,
                _config.HotplugRefreshMs
            );

            LogService.Debug("[MAIN] Timer intervals updated.");
        }
    }
}
