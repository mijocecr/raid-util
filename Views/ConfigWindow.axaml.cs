using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class ConfigWindow : Window
{
    private readonly AppConfig _config;

    // ============================================================
    // CONSTRUCTOR PRINCIPAL (recibe AppConfig real)
    // ============================================================
    public ConfigWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        LoadConfig();
        HookEvents();
    }

    // ============================================================
    // CONSTRUCTOR SECUNDARIO (compatibilidad)
    // ============================================================
    public ConfigWindow()
    {
        InitializeComponent();
        _config = ConfigManager.Load(); // fallback
        LoadConfig();
        HookEvents();
    }

    private void HookEvents()
    {
        BrowseLogsButton.Click += OnBrowseLogs;
        SaveSettingsButton.Click += OnSaveSettings;
        CloseButton.Click += (_, _) => Close();
    }

    // ============================================================
    // CARGAR CONFIG EN LOS CONTROLES
    // ============================================================
    private void LoadConfig()
    {
        GeneralRefreshBox.Text = _config.GeneralRefreshMs.ToString();
        RebuildRefreshBox.Text = _config.RebuildRefreshMs.ToString();
        HotplugRefreshBox.Text = _config.HotplugRefreshMs.ToString();

        LogsPathBox.Text = _config.LogsPath;

        LogLevelCombo.SelectedIndex =
            _config.LogLevel is >= 0 and <= 2
                ? _config.LogLevel
                : 1;
    }

    // ============================================================
    // VALIDAR Y NORMALIZAR LOGSPATH
    // ============================================================
    private string NormalizeLogsPath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return _config.LogsPath;

        var path = input.Trim();

        // Convertir rutas relativas a absolutas
        if (!Path.IsPathRooted(path))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, ".config", "raid-util", path);
        }

        // Crear si no existe
        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        catch
        {
            // fallback seguro
            return _config.LogsPath;
        }

        return path;
    }

    // ============================================================
    // GUARDAR CONFIG DESDE LOS CONTROLES
    // ============================================================
    private void SaveConfig()
    {
        _config.GeneralRefreshMs = ParseInt(GeneralRefreshBox.Text, 2000);
        _config.RebuildRefreshMs = ParseInt(RebuildRefreshBox.Text, 500);
        _config.HotplugRefreshMs = ParseInt(HotplugRefreshBox.Text, 1500);

        // ⭐ Normalización robusta
        _config.LogsPath = NormalizeLogsPath(LogsPathBox.Text);

        _config.LogLevel =
            LogLevelCombo.SelectedIndex is >= 0 and <= 2
                ? LogLevelCombo.SelectedIndex
                : 1;

        ConfigManager.Save(_config);
    }

    private int ParseInt(string? text, int fallback)
    {
        return int.TryParse(text, out var value) ? value : fallback;
    }

    private async void OnBrowseLogs(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select logs folder"
        };

        var result = await dialog.ShowAsync(this);

        if (!string.IsNullOrWhiteSpace(result))
            LogsPathBox.Text = result;
    }

    private void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        SaveConfig();
        Close();
    }
}
