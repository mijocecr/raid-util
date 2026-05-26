using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using RAID_Util.Core;

namespace RAID_Util.Views;

public class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        Width = 380;
        Height = 150;
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 18,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = message,
                    FontSize = 14
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children =
                    {
                        new Button
                        {
                            Content = "Cancel",
                            Width = 80,
                            HorizontalContentAlignment =  HorizontalAlignment.Center,
                            Command = new LambdaCommand(() => Close(false))
                        },
                        new Button
                        {
                            Content = "Delete",
                            Width = 80,
                            HorizontalContentAlignment =  HorizontalAlignment.Center,
                            Classes = { "DangerButton" },
                            Command = new LambdaCommand(() => Close(true))
                        }
                    }
                }
            }
        };
    }
}
