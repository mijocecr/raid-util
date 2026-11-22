using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class LoadingDialog : Window
{
    private TextBlock? _messageBlock;

    public LoadingDialog(string message)
    {
        InitializeComponent();

        Width = 320;
        Height = 140;
        CanResize = false;

        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;

        Background = this.FindResource("BMWSurfaceBrush") as IBrush;

        _messageBlock = this.FindControl<TextBlock>("MessageText");
        if (_messageBlock is not null)
            _messageBlock.Text = message;
        else
            LogService.Warn("[LOADING] MessageText no encontrado en LoadingDialog.axaml");

        // Evitar cierre accidental
        this.KeyDown += (_, e) => e.Handled = true;
    }

    public void SetMessage(string msg)
    {
        if (_messageBlock is not null)
            _messageBlock.Text = msg;
    }
}