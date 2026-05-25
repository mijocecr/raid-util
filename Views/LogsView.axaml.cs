using Avalonia.Controls;
using Avalonia.Interactivity;
using RAID_Util.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using RAID_Util.Helpers;

namespace RAID_Util.Views.Tabs
{
    public partial class LogsView : UserControl
    {
        private string LogPath =>
            Path.Combine(ConfigManager.Get().LogsPath, "raid-util.log");

        public LogsView()
        {
            InitializeComponent();
            HookEvents();
        }

        private void HookEvents()
        {
            RefreshButton.Click += async (_, _) => await LoadLogsAsync();
            OpenFullButton.Click += OnOpenFullLog;
        }

        // ============================================================
        // CARGAR LOGS (OPTIMIZADO)
        // ============================================================
        public async Task LoadLogsAsync()
        {
            try
            {
                if (!File.Exists(LogPath))
                {
                    LogTextBox.Text = "[No log file found]";
                    return;
                }

                // Leer archivo sin bloquear UI
                string content = await File.ReadAllTextAsync(LogPath);

                LogTextBox.Text = content;

                // Auto-scroll estable
                await Task.Yield();
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
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
            catch
            {
                // silencio total
            }
        }
    }
}
