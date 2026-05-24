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
        public LogsView()
        {
            InitializeComponent();
            HookEvents();
        }

        private void HookEvents()
        {
            RefreshButton.Click += async (_, _) => await LoadLogs();
            OpenFullButton.Click += OnOpenFullLog;
        }

        // ============================================================
        // CARGAR LOGS
        // ============================================================
        public async Task LoadLogs()
        {
            try
            {
                string logPath = Path.Combine(ConfigManager.Get().LogsPath, "raid-util.log");

                if (!File.Exists(logPath))
                {
                    LogTextBox.Text = "[No log file found]";
                    return;
                }

                string content = await File.ReadAllTextAsync(logPath);
                LogTextBox.Text = content;

                // Auto-scroll al final
                await Task.Delay(10);
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
                string logPath = Path.Combine(ConfigManager.Get().LogsPath, "raid-util.log");

                if (File.Exists(logPath))
                    ShellHelper.OpenFile(logPath);
            }
            catch
            {
                // silencio
            }
        }
    }
}
