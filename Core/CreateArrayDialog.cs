using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RAID_Util.Models;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Primitives;
using RAID_Util.Views;

namespace RAID_Util.Core;

public class CreateArrayDialog : Window
{
    private readonly List<RaidDiskInfo> _disks;
    private readonly List<CheckBox> _diskChecks = new();
    private ComboBox _levelSelector;
    private TextBox _nameBox;
    private TextBlock _summary;

    public CreateArrayDialog(List<RaidDiskInfo> disks)
    {
        _disks = disks;

        Width = 450;
        Height = 500;
        Title = "Create Array";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        Background = this.FindResource("BMWSurfaceBrush") as IBrush;

        var panel = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16
        };

        // TITLE
        panel.Children.Add(new TextBlock
        {
            Text = "Create Array",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        // FRIENDLY NAME FIELD
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        nameRow.Children.Add(new TextBlock
        {
            Text = "Friendly name:",
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        _nameBox = new TextBox
        {
            Watermark = "optional",
            Text = "",
            Width = 200,
            Background = this.FindResource("BMWInputBrush") as IBrush,
            Foreground = this.FindResource("BMWTextBrush") as IBrush,
            BorderBrush = this.FindResource("BMWBorderBrush") as IBrush
        };
        nameRow.Children.Add(_nameBox);
        panel.Children.Add(nameRow);

        // LEVEL SELECTOR (JBOD + RAID)
        var levelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        levelRow.Children.Add(new TextBlock
        {
            Text = "Array Type:",
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        _levelSelector = new ComboBox
        {
            ItemsSource = new[] { "JBOD (Linear)", "RAID0", "RAID1", "RAID5", "RAID6", "RAID10" },
            SelectedIndex = 0,
            Width = 200,
            Background = this.FindResource("BMWInputBrush") as IBrush,
            Foreground = this.FindResource("BMWTextBrush") as IBrush,
            BorderBrush = this.FindResource("BMWBorderBrush") as IBrush
        };
        _levelSelector.SelectionChanged += (_, _) => UpdateSummary();
        levelRow.Children.Add(_levelSelector);
        panel.Children.Add(levelRow);

        // DISK LIST LABEL
        panel.Children.Add(new TextBlock
        {
            Text = "Select disks:",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = this.FindResource("BMWTextBrush") as IBrush
        });

        // DISK LIST
        var diskPanel = new StackPanel { Spacing = 6 };

        foreach (var d in _disks)
        {
            var chk = new CheckBox
            {
                Content = $"{d.Name}  ({d.Size})  [{d.Model}]",
                Tag = d,
                Foreground = this.FindResource("BMWTextBrush") as IBrush
            };
            chk.Checked += (_, _) => UpdateSummary();
            chk.Unchecked += (_, _) => UpdateSummary();

            _diskChecks.Add(chk);
            diskPanel.Children.Add(chk);
        }

        panel.Children.Add(new ScrollViewer
        {
            Height = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = diskPanel
        });

        // SUMMARY
        _summary = new TextBlock
        {
            Text = "Summary will appear here.",
            FontSize = 14,
            Foreground = this.FindResource("BMWTextDimBrush") as IBrush,
            Margin = new Thickness(0, 10, 0, 0)
        };
        panel.Children.Add(_summary);

        // BUTTONS
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };

        buttons.Children.Add(new Button
        {
            Content = "Cancel",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "DialogButton" },
            Command = new LambdaCommand(() => Close(null))
        });

        buttons.Children.Add(new Button
        {
            Content = "Create",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "PrimaryButton" },
            Command = new LambdaCommand(() =>
            {
                var result = GetResult();
                if (result != null)
                    Close(result);
            })
        });

        panel.Children.Add(buttons);

        Content = panel;

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = _diskChecks
            .Where(c => c.IsChecked == true)
            .Select(c => (RaidDiskInfo)c.Tag)
            .ToList();

        string level = _levelSelector.SelectedItem?.ToString() ?? "JBOD (Linear)";

        if (selected.Count == 0)
        {
            _summary.Text = "Select at least one disk.";
            return;
        }

        // JBOD (linear) summary
        if (level == "JBOD (Linear)")
        {
            if (selected.Count < 2)
            {
                _summary.Text = "JBOD (Linear) requires at least 2 disks.";
                return;
            }

            double sum = selected
                .Select(d => ParseSizeToGB(d.Size))
                .Sum();

            string sizeText = sum >= 1024
                ? $"{sum / 1024.0:F2} TB"
                : $"{sum:F0} GB";

            _summary.Text =
                $"Type: JBOD (Linear)\n" +
                $"Disks: {selected.Count}\n" +
                $"Total size: {sizeText}";
            return;
        }

        // RAID summary
        var sizesGb = selected
            .Select(d => ParseSizeToGB(d.Size))
            .Where(v => v > 0)
            .ToList();

        if (sizesGb.Count == 0)
        {
            _summary.Text = "Could not parse disk sizes.";
            return;
        }

        double usableGb = CalculateRaidUsableSize(level, sizesGb);

        string sizeText2 = usableGb >= 1024
            ? $"{usableGb / 1024.0:F2} TB"
            : $"{usableGb:F0} GB";

        _summary.Text =
            $"Level: {level}\n" +
            $"Disks: {selected.Count}\n" +
            $"Estimated size: {sizeText2} usable";
    }

    private string? ValidateSelection(string level, int count)
    {
        if (level == "JBOD (Linear)")
            return count < 2 ? "JBOD (Linear) requires at least 2 disks." : null;

        return level switch
        {
            "RAID0" when count < 2 => "RAID0 requires at least 2 disks.",
            "RAID1" when count < 2 => "RAID1 requires at least 2 disks.",
            "RAID5" when count < 3 => "RAID5 requires at least 3 disks.",
            "RAID6" when count < 4 => "RAID6 requires at least 4 disks.",
            "RAID10" when count < 4 => "RAID10 requires at least 4 disks.",
            "RAID10" when count % 2 != 0 => "RAID10 requires an even number of disks.",
            _ => null
        };
    }

    private double ParseSizeToGB(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 0;

        string s = size.Trim().ToUpper();
        s = s.Replace(",", ".");
        s = string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

        s = s.Replace("GIB", "")
             .Replace("GI", "")
             .Replace("GB", "")
             .Replace("G", "")
             .Replace("TIB", "")
             .Replace("TI", "")
             .Replace("TB", "")
             .Replace("MIB", "")
             .Replace("MI", "")
             .Replace("MB", "");

        if (!double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double value))
            return 0;

        if (size.ToUpper().Contains("TB"))
            return value * 1024.0;

        if (size.ToUpper().Contains("MB"))
            return value / 1024.0;

        return value;
    }

    private double CalculateRaidUsableSize(string level, List<double> sizes)
    {
        double min = sizes.Min();
        double sum = sizes.Sum();

        return level switch
        {
            "RAID0" => sum,
            "RAID1" => min,
            "RAID5" => sum - min,
            "RAID6" => sum - (2 * min),
            "RAID10" => sum / 2,
            _ => 0
        };
    }

    private CreateArrayResult? GetResult()
    {
        string friendlyName = _nameBox.Text?.Trim() ?? "";
        string level = _levelSelector.SelectedItem?.ToString() ?? "JBOD (Linear)";

        var selected = _diskChecks
            .Where(c => c.IsChecked == true)
            .Select(c => (RaidDiskInfo)c.Tag)
            .ToList();

        if (selected.Count == 0)
        {
            new ConfirmDialog("No disks", "Select at least one disk.")
                .ShowDialog(this);
            return null;
        }

        string? error = ValidateSelection(level, selected.Count);
        if (error != null)
        {
            new ConfirmDialog("Invalid configuration", error)
                .ShowDialog(this);
            return null;
        }

        // Convert UI label to mdadm level
        string mdadmLevel = level switch
        {
            "JBOD (Linear)" => "linear",
            _ => level
        };

        return new CreateArrayResult
        {
            FriendlyName = friendlyName,
            Level = mdadmLevel,
            Disks = selected
        };
    }
}

public class CreateArrayResult
{
    public string FriendlyName { get; set; }
    public string Level { get; set; }
    public List<RaidDiskInfo> Disks { get; set; }
}
