using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RAID_Util.Core;

public class InfoDialog : Window
{
    public InfoDialog(string title, string message)
    {
        Width = 420;
        Height = 260;
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        Background = this.FindResource("BMWSurfaceBrush") as IBrush;

        var panel = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        panel.Children.Add(new ScrollViewer
        {
            Content = new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = this.FindResource("BMWTextDimBrush") as IBrush
            },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 140
        });

        var btn = new Button
        {
            Content = "OK",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "DialogButton" },
            HorizontalAlignment = HorizontalAlignment.Right,
            Command = new LambdaCommand(() => Close())
        };

        panel.Children.Add(btn);

        Content = panel;

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }
}