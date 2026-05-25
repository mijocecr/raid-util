using Avalonia.Controls;
using Avalonia.Interactivity;
using RAID_Util.Services;
using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Media;
using System.Collections.Generic;

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
        // CARGA PRINCIPAL DEL STATUS (OPTIMIZADA)
        // ============================================================
        private async Task LoadStatusAsync()
        {
            try
            {
                // --------------------------------------------------------
                // 0) Lanzar todas las tareas en paralelo (MUCHO MÁS RÁPIDO)
                // --------------------------------------------------------
                var tHealth = _statusService.GetOverallRaidHealthAsync();
                var tArrays = _statusService.GetArraysSummaryAsync();
                var tDisks = _statusService.GetDisksSummaryAsync();
                var tRebuild = _statusService.GetRebuildSummaryAsync();
                var tAtRisk = _statusService.GetArraysAtRiskAsync();
                var tDiskAlerts = _statusService.GetDiskAlertsAsync();
                var tEvents = _statusService.GetRecentEventsAsync();

                await Task.WhenAll(tHealth, tArrays, tDisks, tRebuild, tAtRisk, tDiskAlerts, tEvents);

                // --------------------------------------------------------
                // 1) Estado global RAID
                // --------------------------------------------------------
                string raidHealth = tHealth.Result;
                TxtOverallRaidHealth.Text = raidHealth;

                EnsureTransitions();

                TxtOverallRaidHealth.Foreground = GetHealthBrush(raidHealth);

                // --------------------------------------------------------
                // 2) Resúmenes numéricos
                // --------------------------------------------------------
                TxtArraysSummary.Text = tArrays.Result;
                TxtDisksSummary.Text = tDisks.Result;
                TxtRebuildSummary.Text = tRebuild.Result;

                // --------------------------------------------------------
                // 3) Arrays en riesgo
                // --------------------------------------------------------
                ListArraysAtRisk.ItemsSource = tAtRisk.Result;

                // --------------------------------------------------------
                // 4) Alertas de discos
                // --------------------------------------------------------
                ListDiskAlerts.ItemsSource = tDiskAlerts.Result;

                // --------------------------------------------------------
                // 5) Eventos recientes
                // --------------------------------------------------------
                ListRecentEvents.ItemsSource = tEvents.Result;
            }
            catch (Exception ex)
            {
                TxtOverallRaidHealth.Text = $"Error: {ex.Message}";
                TxtOverallRaidHealth.Foreground = this.FindResource("BMWHealthCriticalBrush") as IBrush;
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private void EnsureTransitions()
        {
            if (TxtOverallRaidHealth.Transitions != null)
                return;

            TxtOverallRaidHealth.Transitions = new Transitions
            {
                new BrushTransition
                {
                    Property = TextBlock.ForegroundProperty,
                    Duration = TimeSpan.FromMilliseconds(250)
                }
            };
        }

        private IBrush GetHealthBrush(string state)
        {
            return state switch
            {
                "Healthy"  => this.FindResource("BMWHealthHealthyBrush") as IBrush,
                "Warning"  => this.FindResource("BMWHealthWarningBrush") as IBrush,
                "Critical" => this.FindResource("BMWHealthCriticalBrush") as IBrush,
                _          => this.FindResource("BMWHealthNoneBrush") as IBrush
            };
        }
    }
}
