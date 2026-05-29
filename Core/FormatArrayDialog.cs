using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RAID_Util.Services;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace RAID_Util.Core
{
    public class FormatArrayDialog : Window
    {
        private ComboBox _fsSelector;
        private TextBox _labelBox;
        private readonly string _arrayName;

        public FormatArrayDialog(string arrayName)
        {
            _arrayName = arrayName;

            Width = 360;
            Height = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            Background = this.FindResource("BMWBackgroundBrush") as IBrush;
            Foreground = this.FindResource("BMWTextBrush") as IBrush;
            Title = $"Format {arrayName}";

            var root = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Margin = new Thickness(16)
            };

            // ⭐ TITLE
            root.Children.Add(new TextBlock
            {
                Text = $"Format {arrayName}",
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                Foreground = this.FindResource("BMWTextBrush") as IBrush,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // ⭐ MAIN CONTENT
            var panel = new StackPanel { Spacing = 14 };
            Grid.SetRow(panel, 1);

            // SECTION: Filesystem
            panel.Children.Add(new TextBlock
            {
                Text = "Filesystem",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeight.SemiBold,
                Foreground = this.FindResource("BMWAccentBrush") as IBrush
            });

            var fsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            fsRow.Children.Add(new TextBlock
            {
                Text = "Type:",
                Width = 110,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = this.FindResource("BMWTextBrush") as IBrush
            });

            // ⭐ Construir lista dinámica de FS
            var fsList = new List<string> { "ext4", "xfs", "btrfs", "swap" };

            if (HasF2FS())
                fsList.Add("f2fs");

            _fsSelector = new ComboBox
            {
                ItemsSource = fsList,
                SelectedIndex = 0,
                Width = 180,
                Background = this.FindResource("BMWInputBrush") as IBrush,
                Foreground = this.FindResource("BMWTextBrush") as IBrush,
                BorderBrush = this.FindResource("BMWBorderBrush") as IBrush
            };

            fsRow.Children.Add(_fsSelector);
            panel.Children.Add(fsRow);

            // SECTION: Label
            panel.Children.Add(new TextBlock
            {
                Text = "Label",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeight.SemiBold,
                Foreground = this.FindResource("BMWAccentBrush") as IBrush
            });

            var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            labelRow.Children.Add(new TextBlock
            {
                Text = "Volume label:",
                TextAlignment = TextAlignment.Right,
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = this.FindResource("BMWTextBrush") as IBrush
            });

            _labelBox = new TextBox
            {
                Watermark = "optional",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Width = 180,
                Background = this.FindResource("BMWInputBrush") as IBrush,
                Foreground = this.FindResource("BMWTextBrush") as IBrush,
                BorderBrush = this.FindResource("BMWBorderBrush") as IBrush
            };
            labelRow.Children.Add(_labelBox);
            panel.Children.Add(labelRow);

            root.Children.Add(panel);

            // ⭐ BUTTON BAR
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(buttons, 2);

            var btnCancel = new Button
            {
                Content = "Cancel",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Width = 90,
                Height = 30,
                Classes = { "DialogButton" }
            };
            btnCancel.Click += (_, _) => Close(null);

            var btnFormat = new Button
            {
                Content = "Format",
                Width = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Height = 30,
                Classes = { "PrimaryButton" }
            };
            btnFormat.Click += OnFormatClicked;

            buttons.Children.Add(btnCancel);
            buttons.Children.Add(btnFormat);

            root.Children.Add(buttons);

            Content = root;
        }

        // ⭐ Detectar si mkfs.f2fs está instalado
        private bool HasF2FS()
        {
            string[] paths =
            {
                "/usr/bin/mkfs.f2fs",
                "/usr/sbin/mkfs.f2fs",
                "/sbin/mkfs.f2fs",
                "/bin/mkfs.f2fs"
            };

            return paths.Any(File.Exists);
        }

        private async void OnFormatClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            using (LoadingService.Show("Formatting array...", this))
            {
                await Task.Delay(200);
                Close(GetResult());
            }
        }

        private FormatArrayResult GetResult()
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
}
