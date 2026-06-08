using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Services;

namespace RAID_Util.Views.Tabs;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        HookEvents();
    }

    private string LogPath =>
        Path.Combine(ConfigManager.Get().LogsPath, "raid-util.log");

    private void HookEvents()
    {
        RefreshButton.Click += async (_, _) => await LoadLogsAsync();
        OpenFullButton.Click += OnOpenFullLog;
    }

    // ============================================================
    // CARGAR LOGS (OPTIMIZADO + SEGURO)
    // ============================================================
    public async Task LoadLogsAsync()
    {
        try
        {
            // Crear archivo si no existe
            if (!File.Exists(LogPath))
            {
                Directory.CreateDirectory(ConfigManager.Get().LogsPath);
                File.WriteAllText(LogPath, "");
            }

            // Leer solo últimas 2000 líneas
            var lines = await File.ReadAllLinesAsync(LogPath);
            var tail = lines.Reverse().Take(2000).Reverse();

            LogTextBox.Text = string.Join('\n', tail);

            // Auto-scroll solo si el usuario ya estaba abajo
            if (LogTextBox.CaretIndex >= LogTextBox.Text.Length - 5)
            {
                await Task.Yield();
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
            }
        }
        catch (Exception ex)
        {
            LogTextBox.Text = $"[Error reading log]\n{ex.Message}";
        }
    }

    // ============================================================
    // ABRIR LOG COMPLETO EN EDITOR
    // ============================================================
    private void OnOpenFullLog(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(LogPath))
                ShellHelper.OpenFile(LogPath);
        }
        catch (Exception ex)
        {
            _ = new InfoDialog("Error", $"Could not open log file:\n{ex.Message}")
                .ShowDialog(GetWindow());
        }
    }

    private Window? GetWindow() => this.VisualRoot as Window;
}
