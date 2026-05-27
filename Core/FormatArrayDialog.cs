using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RAID_Util.Core;

public class FormatArrayDialog : Window
{
    private ComboBox _fsSelector;
    private TextBox _labelBox;

    public FormatArrayDialog(string arrayName)
    {
        Width = 460;
        Height = 320;
        Title = $"Format {arrayName}";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        Background = this.FindResource("BMWSurfaceBrush") as IBrush;

        var panel = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 18
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Format {arrayName}",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        // Filesystem selector
        var fsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        fsRow.Children.Add(new TextBlock
        {
            Text = "Filesystem:",
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        _fsSelector = new ComboBox
        {
            ItemsSource = new[]
            {
                "ext4",
                "xfs",
                "btrfs",
                "f2fs",
                "vfat (FAT32)",
                "exfat",
                "ntfs",
                "swap"
            },
            SelectedIndex = 0,
            Width = 200,
            Background = this.FindResource("BMWInputBrush") as IBrush,
            Foreground = this.FindResource("BMWTextBrush") as IBrush,
            BorderBrush = this.FindResource("BMWBorderBrush") as IBrush
        };
        fsRow.Children.Add(_fsSelector);
        panel.Children.Add(fsRow);

        // Label
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        labelRow.Children.Add(new TextBlock
        {
            Text = "Label:",
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        _labelBox = new TextBox
        {
            Watermark = "optional",
            Width = 200,
            Background = this.FindResource("BMWInputBrush") as IBrush,
            Foreground = this.FindResource("BMWTextBrush") as IBrush,
            BorderBrush = this.FindResource("BMWBorderBrush") as IBrush
        };
        labelRow.Children.Add(_labelBox);
        panel.Children.Add(labelRow);

        // Buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        buttons.Children.Add(new Button
        {
            Content = "Cancel",
            Width = 100,
            Classes = { "DialogButton" },
            Command = new LambdaCommand(() => Close(null))
        });

        buttons.Children.Add(new Button
        {
            Content = "Format",
            Width = 100,
            Classes = { "PrimaryButton" },
            Command = new LambdaCommand(() => Close(GetResult()))
        });

        panel.Children.Add(buttons);

        Content = panel;
    }

    private FormatArrayResult? GetResult()
    {
        return new FormatArrayResult
        {
            Filesystem = _fsSelector.SelectedItem?.ToString() ?? "ext4",
            Label = _labelBox.Text ?? ""
        };
    }
}

public class FormatArrayResult
{
    public string Filesystem { get; set; }
    public string Label { get; set; }
}
