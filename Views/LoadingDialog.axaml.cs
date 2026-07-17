using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RAID_Util.Views;

public partial class LoadingDialog : Window
{
    private TextBlock _messageBlock;

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
        _messageBlock.Text = message;

        // Evitar cierre accidental
        this.KeyDown += (_, e) => e.Handled = true;
    }

    public void SetMessage(string msg)
    {
        _messageBlock.Text = msg;
    }
}