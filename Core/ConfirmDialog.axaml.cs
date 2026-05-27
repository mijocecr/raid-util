using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RAID_Util.Core;

namespace RAID_Util.Views;

public class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        Width = 380;
        Height = 170;
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        // Fondo BMW
        Background = this.FindResource("BMWSurfaceBrush") as IBrush;

        var panel = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14
        };

        // TITLE
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        // MESSAGE
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = this.FindResource("BMWTextDimBrush") as IBrush
        });

        // BUTTONS
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };

        // CANCEL BUTTON
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "DialogButton" },
            Command = new LambdaCommand(() => Close(false))
        };

        // DELETE BUTTON
        var deleteBtn = new Button
        {
            Content = "Accept",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "DangerButton" },
            Command = new LambdaCommand(() => Close(true))
        };

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(deleteBtn);
        panel.Children.Add(buttons);

        Content = panel;

        // ⭐ Manejo de teclas
        this.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Close(true);
                e.Handled = true;
            }
        };
    }
}
