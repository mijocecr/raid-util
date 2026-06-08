using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RAID_Util.Core;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class ConfigWindow : Window
{
    private readonly AppConfig _config;

    // ============================================================
    // CONSTRUCTOR PRINCIPAL
    // ============================================================
    public ConfigWindow(AppConfig config)
    {
        LogService.Debug("[CONFIG] Constructor ENTER");
        InitializeComponent();
        _config = config;
        LoadConfig();
        HookEvents();
        LogService.Debug("[CONFIG] Constructor EXIT");
    }

    // ============================================================
    // CONSTRUCTOR SECUNDARIO
    // ============================================================
    public ConfigWindow()
    {
        LogService.Debug("[CONFIG] Constructor (fallback) ENTER");
        InitializeComponent();
        _config = ConfigManager.Load();
        LoadConfig();
        HookEvents();
        LogService.Debug("[CONFIG] Constructor (fallback) EXIT");
    }

    private void HookEvents()
    {
        BrowseLogsButton.Click += OnBrowseLogs;
        SaveSettingsButton.Click += OnSaveSettingsAsync;
        CloseButton.Click += (_, _) => Close();
    }

    // ============================================================
    // CARGAR CONFIG
    // ============================================================
    private void LoadConfig()
    {
        LogService.Debug("[CONFIG] LoadConfig ENTER");

        GeneralRefreshBox.Text = _config.GeneralRefreshMs.ToString();
        RebuildRefreshBox.Text = _config.RebuildRefreshMs.ToString();
        HotplugRefreshBox.Text = _config.HotplugRefreshMs.ToString();

        LogsPathBox.Text = _config.LogsPath;

        LogLevelCombo.SelectedIndex =
            _config.LogLevel is >= 0 and <= 2
                ? _config.LogLevel
                : 1;

        LogService.Debug("[CONFIG] LoadConfig EXIT");
    }

    // ============================================================
    // NORMALIZAR LOGSPATH
    // ============================================================
    private string NormalizeLogsPath(string? input)
    {
        var sw = Stopwatch.StartNew();
        LogService.Debug("[CONFIG] NormalizeLogsPath ENTER");

        if (string.IsNullOrWhiteSpace(input))
        {
            LogService.Debug("[CONFIG] NormalizeLogsPath → empty, returning existing path");
            return _config.LogsPath;
        }

        var path = input.Trim();
        LogService.Debug($"[CONFIG] NormalizeLogsPath → raw input: '{path}'");

        if (!Path.IsPathRooted(path))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, ".config", "raid-util", path);
            LogService.Debug($"[CONFIG] NormalizeLogsPath → converted to absolute: '{path}'");
        }

        try
        {
            if (!Directory.Exists(path))
            {
                LogService.Debug($"[CONFIG] NormalizeLogsPath → creating directory: '{path}'");
                Directory.CreateDirectory(path);
            }
        }
        catch (Exception ex)
        {
            LogService.Error("[CONFIG] NormalizeLogsPath EXCEPTION:");
            LogService.Error(ex.ToString());
            return _config.LogsPath;
        }

        sw.Stop();
        LogService.Debug($"[CONFIG] NormalizeLogsPath EXIT ({sw.ElapsedMilliseconds} ms)");

        return path;
    }

    // ============================================================
    // GUARDAR CONFIG (ASYNC + INSTRUMENTADO)
    // ============================================================
    private async Task SaveConfigAsync()
    {
        LogService.Debug("====================================================");
        LogService.Debug("[CONFIG] SaveConfigAsync ENTER");
        LogService.Debug($"[CONFIG] Thread: {Environment.CurrentManagedThreadId} (UI? {Dispatcher.UIThread.CheckAccess()})");

        var swTotal = Stopwatch.StartNew();

        try
        {
            // -----------------------------
            // PARSE REFRESH INTERVALS
            // -----------------------------
            var swParse = Stopwatch.StartNew();
            _config.GeneralRefreshMs = ParseInt(GeneralRefreshBox.Text, 2000);
            _config.RebuildRefreshMs = ParseInt(RebuildRefreshBox.Text, 500);
            _config.HotplugRefreshMs = ParseInt(HotplugRefreshBox.Text, 1500);
            swParse.Stop();
            LogService.Debug($"[CONFIG] Parse refresh values: {swParse.ElapsedMilliseconds} ms");

            // -----------------------------
            // NORMALIZE PATH
            // -----------------------------
            var swNorm = Stopwatch.StartNew();
            _config.LogsPath = NormalizeLogsPath(LogsPathBox.Text);
            swNorm.Stop();
            LogService.Debug($"[CONFIG] NormalizeLogsPath duration: {swNorm.ElapsedMilliseconds} ms");

            // -----------------------------
            // LOG LEVEL
            // -----------------------------
            var swLevel = Stopwatch.StartNew();
            _config.LogLevel =
                LogLevelCombo.SelectedIndex is >= 0 and <= 2
                    ? LogLevelCombo.SelectedIndex
                    : 1;
            swLevel.Stop();
            LogService.Debug($"[CONFIG] LogLevel parse: {swLevel.ElapsedMilliseconds} ms");

            // -----------------------------
            // SAVE TO DISK (BACKGROUND)
            // -----------------------------
            var swSave = Stopwatch.StartNew();
            LogService.Debug("[CONFIG] Saving config to disk (background thread)...");
            await Task.Run(() =>
            {
                LogService.Debug($"[CONFIG] Save thread: {Environment.CurrentManagedThreadId}");
                ConfigManager.Save(_config);
            });
            swSave.Stop();
            LogService.Debug($"[CONFIG] ConfigManager.Save duration: {swSave.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            LogService.Error("[CONFIG] SaveConfigAsync EXCEPTION:");
            LogService.Error(ex.ToString());
            throw;
        }
        finally
        {
            swTotal.Stop();
            LogService.Debug($"[CONFIG] SaveConfigAsync EXIT (TOTAL {swTotal.ElapsedMilliseconds} ms)");
            LogService.Debug("====================================================");
        }
    }

    private int ParseInt(string? text, int fallback)
    {
        if (!int.TryParse(text, out var value))
        {
            LogService.Debug($"[CONFIG] ParseInt fallback → '{text}'");
            return fallback;
        }

        return value;
    }

    private async void OnBrowseLogs(object? sender, RoutedEventArgs e)
    {
        LogService.Debug("[CONFIG] OnBrowseLogs ENTER");

        var dialog = new OpenFolderDialog
        {
            Title = "Select logs folder"
        };

        var result = await dialog.ShowAsync(this);

        if (!string.IsNullOrWhiteSpace(result))
        {
            LogService.Debug($"[CONFIG] OnBrowseLogs → selected '{result}'");
            LogsPathBox.Text = result;
        }

        LogService.Debug("[CONFIG] OnBrowseLogs EXIT");
    }

    // ============================================================
    // BOTÓN GUARDAR (ASYNC + INSTRUMENTADO)
    // ============================================================
    private async void OnSaveSettingsAsync(object? sender, RoutedEventArgs e)
    {
        LogService.Debug("[CONFIG] OnSaveSettingsAsync ENTER");

        SaveSettingsButton.IsEnabled = false;

        try
        {
            await SaveConfigAsync();
        }
        catch (Exception ex)
        {
            LogService.Error("[CONFIG] OnSaveSettingsAsync ERROR:");
            LogService.Error(ex.ToString());

            await new InfoDialog("Error", $"Could not save configuration:\n{ex.Message}")
                .ShowDialog(this);
        }

        SaveSettingsButton.IsEnabled = true;

        LogService.Debug("[CONFIG] Closing window...");
        Close();

        LogService.Debug("[CONFIG] OnSaveSettingsAsync EXIT");
    }
}
