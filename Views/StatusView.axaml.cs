using Avalonia.Controls;
using RAID_Util.Services;
using System.Threading.Tasks;

namespace RAID_Util.Views.Tabs;

public partial class StatusView : UserControl
{
    public StatusView()
    {
        InitializeComponent();
    }

    public async Task UpdateStatus()
    {
        TxtKernel.Text = await SystemStatusService.GetKernel();
        TxtDistro.Text = await SystemStatusService.GetDistro();
        TxtHostname.Text = await SystemStatusService.GetHostname();

        TxtMdadmStatus.Text = await SystemStatusService.GetServiceStatus("mdmonitor");
        TxtRaidMonitorStatus.Text = await SystemStatusService.GetServiceStatus("mdmonitor");
        TxtHotplugStatus.Text = await SystemStatusService.GetServiceStatus("udev");

        TxtOverallHealth.Text = await SystemStatusService.GetOverallHealth();
    }
}