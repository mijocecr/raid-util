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
    }

    // ============================================================
    // FLUJO PRINCIPAL OnOpened 
    // ============================================================
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        LogService.Write("[MAIN] RAID-Util startup sequence initiated.");

        // ------------------------------------------------------------
        // 0) CARGAR CONFIGURACIÓN GLOBAL
        // ------------------------------------------------------------
        _config = ConfigManager.Load();   // <--- AHORA DEVUELVE AppConfig
        LogService.Debug("[MAIN] Configuration loaded.");

        StatusBarText.Text = "Initializing...";
        await Task.Delay(120);

        // ------------------------------------------------------------
        // 1) VALIDACIÓN DE CONTRASEÑA
        // ------------------------------------------------------------
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

            var result = ShellHelper.EjecutarComoRoot("bash -c \"echo OK\"");

            if (result.ExitCode == 0)
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

        // ------------------------------------------------------------
        // 2) COMPROBAR SUBSISTEMA RAID
        // ------------------------------------------------------------
        StatusBarText.Text = "Checking RAID subsystem...";
        LogService.Debug("[MAIN] Checking mdadm availability...");

        var mdadmCheck = ShellHelper.EjecutarComoRoot("which mdadm");

        if (mdadmCheck.ExitCode != 0)
        {
            LogService.Error("[MAIN] mdadm not found. RAID subsystem unavailable.");
            StatusBarText.Text = "RAID subsystem unavailable.";
            return;
        }

        await Task.Delay(120);

        // ------------------------------------------------------------
        // 3) CARGAR INFORMACIÓN RAID
        // ------------------------------------------------------------
        StatusBarText.Text = "Loading RAID information...";
        LogService.Debug("[MAIN] Loading RAID arrays overview...");
        await LoadRaidAsync();

        // ------------------------------------------------------------
        // 4) INICIALIZAR TIMER MANAGER
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        // 5) FINALIZADO
        // ------------------------------------------------------------
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
    // CARGAR RAID
    // ============================================================
    private async Task LoadRaidAsync()
    {
        LogService.Debug("[MAIN] Loading RAID arrays...");

        using (LoadingService.Show("Loading RAID arrays..."))
        {
            LogService.Debug("[MAIN] RAID arrays loaded.");
        }
    }

    // ============================================================
    // CONFIG WINDOW
    // ============================================================
    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        LogService.Debug("[MAIN] Opening configuration window...");

        var win = new ConfigWindow(_config);   // <--- PASAMOS LA INSTANCIA
        await win.ShowDialog(this);

        LogService.Write("[MAIN] Configuration updated.");

        // ACTUALIZAR INTERVALOS DEL TIMER
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
