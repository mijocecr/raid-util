using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace RAID_Util.Core;

public class ConsoleDialog : Window
{
    public ConsoleDialog(string title, string text)
    {
        Title = title;
        Width = 700;
        Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        Background = Brushes.Black;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = Brushes.LightGreen,
                Background = Brushes.Black,
                TextWrapping = TextWrapping.NoWrap
            }
        };

        Content = scroll;
    }
}