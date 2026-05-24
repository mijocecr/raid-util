using Avalonia.Controls;
using Avalonia.Interactivity;
using RAID_Util.Services;
using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Media;

namespace RAID_Util.Views.Tabs
{
    public partial class StatusView : UserControl
    {
        private readonly StatusService _statusService;

        public StatusView()
        {
            InitializeComponent();

            _statusService = new StatusService();

            // Cargar datos cuando el control entra al árbol visual
            AttachedToVisualTree += async (_, __) => await LoadStatusAsync();
        }

        /// <summary>
        /// Método público llamado por TimerManager para refrescar el estado.
        /// </summary>
        public async Task RefreshStatusAsync()
        {
            await LoadStatusAsync();
        }

        // ============================================================
        // CARGA PRINCIPAL DEL STATUS
        // ============================================================
        private async Task LoadStatusAsync()
{
    try
    {
        // --------------------------------------------------------
        // 1) Estado global RAID
        // --------------------------------------------------------
        var raidHealth = await _statusService.GetOverallRaidHealthAsync();
        TxtOverallRaidHealth.Text = raidHealth;

        // Transición suave de color (solo se crea una vez)
        if (TxtOverallRaidHealth.Transitions == null)
        {
            TxtOverallRaidHealth.Transitions = new Transitions
            {
                new BrushTransition
                {
                    Property = TextBlock.ForegroundProperty,
                    Duration = TimeSpan.FromMilliseconds(250)
                }
            };
        }

        // Aplicar color dinámico según estado
        switch (raidHealth)
        {
            case "Healthy":
                TxtOverallRaidHealth.Foreground =
                    this.FindResource("BMWHealthHealthyBrush") as IBrush;
                break;

            case "Warning":
                TxtOverallRaidHealth.Foreground =
                    this.FindResource("BMWHealthWarningBrush") as IBrush;
                break;

            case "Critical":
                TxtOverallRaidHealth.Foreground =
                    this.FindResource("BMWHealthCriticalBrush") as IBrush;
                break;

            default:
                TxtOverallRaidHealth.Foreground =
                    this.FindResource("BMWHealthNoneBrush") as IBrush;
                break;
        }

        // --------------------------------------------------------
        // 2) Resúmenes numéricos
        // --------------------------------------------------------
        TxtArraysSummary.Text =
            await _statusService.GetArraysSummaryAsync();

        TxtDisksSummary.Text =
            await _statusService.GetDisksSummaryAsync();

        TxtRebuildSummary.Text =
            await _statusService.GetRebuildSummaryAsync();

        // --------------------------------------------------------
        // 3) Arrays en riesgo
        // --------------------------------------------------------
        ListArraysAtRisk.ItemsSource =
            await _statusService.GetArraysAtRiskAsync();

        // --------------------------------------------------------
        // 4) Alertas de discos
        // --------------------------------------------------------
        ListDiskAlerts.ItemsSource =
            await _statusService.GetDiskAlertsAsync();

        // --------------------------------------------------------
        // 5) Eventos recientes
        // --------------------------------------------------------
        ListRecentEvents.ItemsSource =
            await _statusService.GetRecentEventsAsync();
    }
    catch (Exception ex)
    {
        TxtOverallRaidHealth.Text = $"Error: {ex.Message}";
        TxtOverallRaidHealth.Foreground =
            this.FindResource("BMWHealthCriticalBrush") as IBrush;
    }
}

    }
}
