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

        LogService.Debug("[MAIN] Constructor EXIT");
    }

    public void UpdateStatus(string text)
    {
        if (StatusBarText != null)
            StatusBarText.Text = text;
    }

    // ============================================================
    //  MAIN INITIALIZATION (CORRECTED → OnLoaded)
    // ============================================================

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        LogService.Debug("[MAIN] OnLoaded ENTER");
        base.OnLoaded(e);

        LogService.Info("[MAIN] RAID-Util startup sequence initiated.");

        _config = ConfigManager.Load();
        LogService.Debug("[MAIN] Configuration loaded.");

        StatusBarText.Text = "Initializing...";
        await Task.Delay(150);

        // ============================================================
        // 1) UNIVERSAL SUDO VALIDATION
        // ============================================================

        // La contraseña se entrega al proceso de sudo y se descarta; nunca se
        // guarda en Credentials.AdminPassword. El prompt se marshalla al hilo UI.
        PrivilegeSession.SetPasswordPrompt(
            () => Dispatcher.UIThread.InvokeAsync(() => SolicitarPassword()));

        try
        {
            // sudo -n se prueba primero: si ya hay timestamp no se muestra diálogo
            // y ningún sudo queda esperando input en segundo plano.
            var probe = await Task.Run(() => PrivilegeSession.RunRootAsync(
                new[] { "true" }, timeout: TimeSpan.FromSeconds(20)));

            if (!probe.Success)
                throw new InvalidOperationException(probe.Stderr.Trim());

            _sudoReady = true;
            Credentials.AllowRaidCalls = true;
            LogService.Info("[MAIN] Privilegios confirmados.");
        }
        catch (Exception ex)
        {
            LogService.Error($"[MAIN] No se pudieron obtener privilegios: {ex.Message}");
            StatusBarText.Text = "Sudo is not accepting authentication.";

            await new InfoDialog(
                "Sudo Authentication Failed",
                "Sudo is not accepting authentication.\n\n" +
                "Possible causes:\n" +
                "• Too many failed attempts (faillock)\n" +
                "• PAM lockout\n" +
                "• sudoers restrictions\n" +
                "• Incorrect password\n" +
                "• doas sin NOPASSWD\n\n" +
                "To avoid triggering an account lockout, RAID-Util will not retry automatically."
            ).ShowDialog(this);

            Close();
            return;
        }

        // ⭐ Ahora sí: la UI está cargada → StatusViewControl existe
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50);

            if (StatusViewControl != null)
            {
                LogService.Debug("[MAIN] StatusViewControl READY → refreshing...");
                await StatusViewControl.RefreshStatusAsync();
            }

        }, DispatcherPriority.Background);

        // ============================================================
        // 2) CHECK mdadm
        // ============================================================

        StatusBarText.Text = "Checking RAID subsystem...";

        var mdadmPath = await Task.Run(() => ToolLocator.Resolve("mdadm"));

        LogService.Debug($"[MAIN] mdadm resuelto en '{mdadmPath}'");

        if (mdadmPath is null)
        {
            StatusBarText.Text = "RAID subsystem unavailable.";
            LogService.Error("[MAIN] mdadm not found.");
            _raidSubsystemReady = false;
            return;
        }

        _raidSubsystemReady = true;

        await Task.Delay(120);

        StatusBarText.Text = "System ready.";

        LogService.Debug("[MAIN] OnLoaded completed.");
        StatusBarText.Text = "Ready.";
    }

    // ============================================================
    //  PASSWORD DIALOG
    // ============================================================

    private async Task<string?> SolicitarPassword()
    {
        LogService.Debug("[MAIN] SolicitarPassword ENTER");
        var dialog = new PasswordDialog();
        var pass = await dialog.ShowDialog<string?>(this);
        LogService.Debug("[MAIN] SolicitarPassword EXIT");
        return pass;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Credentials.AllowRaidCalls = false;
        PrivilegeSession.ForgetPassword();
        base.OnClosing(e);
    }

    // ============================================================
    //  TAB CONTROL
    // ============================================================

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

            if (_raidLoading)
            {
                LogService.Debug("[MAIN] RAID already loading.");
                return;
            }

            LogService.Debug("[MAIN] RAID tab selected → refreshing RAID info...");
            StatusBarText.Text = "Loading RAID information...";

            _raidLoading = true;

            if (RaidViewControl != null)
                await RaidViewControl.RefreshArraysAsync();

            _raidLoaded = true;
            _raidLoading = false;
        }
    }

    // ============================================================
    //  CONFIG WINDOW
    // ============================================================

    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var win = new ConfigWindow(_config);
        await win.ShowDialog(this);
    }

    // ============================================================
    //  STATUS BAR TEXT EVENTS
    // ============================================================

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

        if (MainTabs.SelectedIndex != 1)
            MainTabs.SelectedIndex = 1;
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