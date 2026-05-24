using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Styling;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class RaidView : UserControl
{
    private List<RaidArrayInfo> _arrays = new();

    public RaidView()
    {
        InitializeComponent();
        LoadFakeData();
        BuildUI();
    }

   private void LoadFakeData()
{
    _arrays = new()
    {
        // 1) RAID1 — Healthy (HDD)
        new RaidArrayInfo
        {
            Name = "md0",
            Level = "RAID1",
            State = "Healthy",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-ok.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sda", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-hdd.png" },
                new RaidDiskInfo { Name="/dev/sdb", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-hdd.png" }
            }
        },

        // 2) RAID5 — Degraded (SSD faulty)
        new RaidArrayInfo
        {
            Name = "md1",
            Level = "RAID5",
            State = "Degraded",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sdc", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-ssd.png" },
                new RaidDiskInfo { Name="/dev/sdd", Role="faulty", State="FAULTY", Icon="avares://RAID-Util/Assets/Icons/disk-ssd.png" },
                new RaidDiskInfo { Name="/dev/sde", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-ssd.png" }
            }
        },

        // 3) RAID6 — Rebuilding (NVMe)
        new RaidArrayInfo
        {
            Name = "md2",
            Level = "RAID6",
            State = "Rebuilding",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sdf", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" },
                new RaidDiskInfo { Name="/dev/sdg", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" },
                new RaidDiskInfo { Name="/dev/sdh", Role="rebuilding", State="WARN", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" },
                new RaidDiskInfo { Name="/dev/sdi", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" }
            }
        },

        // 4) RAID10 — Missing disk (Virtual)
        new RaidArrayInfo
        {
            Name = "md3",
            Level = "RAID10",
            State = "Degraded",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sdj", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-virtual.png" },
                new RaidDiskInfo { Name="/dev/sdk", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-virtual.png" },
                new RaidDiskInfo { Name="/dev/sdl", Role="missing", State="MISSING", Icon="avares://RAID-Util/Assets/Icons/disk-virtual.png" },
                new RaidDiskInfo { Name="/dev/sdm", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-virtual.png" }
            }
        },

        // 5) RAID0 — Mixed disks (HDD + SSD)
        new RaidArrayInfo
        {
            Name = "md4",
            Level = "RAID0",
            State = "Warning",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sdn", Role="stripe", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-hdd.png" },
                new RaidDiskInfo { Name="/dev/sdo", Role="stripe", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-ssd.png" }
            }
        },

        // 6) RAID5 — Spare disk (NVMe spare)
        new RaidArrayInfo
        {
            Name = "md5",
            Level = "RAID5",
            State = "Healthy",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-ok.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sdp", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" },
                new RaidDiskInfo { Name="/dev/sdq", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" },
                new RaidDiskInfo { Name="/dev/sdr", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" },
                new RaidDiskInfo { Name="/dev/sds", Role="spare", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-nvme.png" }
            }
        },

        // 7) RAID1 — Read-only (SSD)
        new RaidArrayInfo
        {
            Name = "md6",
            Level = "RAID1",
            State = "Read-Only",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-readonly.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sdt", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-ssd.png" },
                new RaidDiskInfo { Name="/dev/sdu", Role="active", State="OK", Icon="avares://RAID-Util/Assets/Icons/disk-ssd.png" }
            }
        },

        // 8) RAID5 — Fully failed (HDD faulty)
        new RaidArrayInfo
        {
            Name = "md7",
            Level = "RAID5",
            State = "Failed",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-error.png",
            Disks =
            {
                new RaidDiskInfo { Name="/dev/sdv", Role="faulty", State="FAULTY", Icon="avares://RAID-Util/Assets/Icons/disk-hdd.png" },
                new RaidDiskInfo { Name="/dev/sdw", Role="faulty", State="FAULTY", Icon="avares://RAID-Util/Assets/Icons/disk-hdd.png" },
                new RaidDiskInfo { Name="/dev/sdx", Role="faulty", State="FAULTY", Icon="avares://RAID-Util/Assets/Icons/disk-hdd.png" }
            }
        }
    };
}

private Color GetArrayGlowColor(string state)
{
    bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;

    return state switch
    {
        "Healthy" => isDark ? Color.Parse("#1C69D4") : Color.Parse("#0A4FB3"),
        "Degraded" => isDark ? Color.Parse("#F2C94C") : Color.Parse("#D9B03F"),
        "Rebuilding" => isDark ? Color.Parse("#F2C94C") : Color.Parse("#D9B03F"),
        "Warning" => isDark ? Color.Parse("#F2C94C") : Color.Parse("#D9B03F"),
        "Read-Only" => isDark ? Color.Parse("#A7C7FF") : Color.Parse("#7AA8FF"),
        "Failed" => isDark ? Color.Parse("#D32F2F") : Color.Parse("#B71C1C"),
        _ => isDark ? Color.Parse("#F2C94C") : Color.Parse("#D9B03F")
    };
}


private async void AnimateArrayGlow(Border border, SolidColorBrush brush)
{
    while (true)
    {
        brush.Opacity = 0.25;
        await Task.Delay(500);

        brush.Opacity = 0.55;
        await Task.Delay(500);
    }
}

   
    
    private void BuildUI()
    {
        ListArrays.Children.Clear();

        foreach (var array in _arrays)
        {
            var card = BuildArrayCard(array);
            ListArrays.Children.Add(card);

            if (array.IsExpanded)
            {
                var expanded = BuildExpandedCard(array);
                ListArrays.Children.Add(expanded);
            }
        }
    }


///---------------------

private Border BuildArrayCard(RaidArrayInfo array)
{
    // BIG ICON (left column, spans all rows)
    var icon = LoadImage(array.StateIcon, 150);
    icon.Margin = new Thickness(4);
    icon.VerticalAlignment = VerticalAlignment.Center;

    // ARRAY NAME
    var name = new TextBlock
    {
        Text = array.Name,
        FontSize = 22,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!,
        FontWeight = FontWeight.Bold,
        
        Margin = new Thickness(0, 0, 0, 4)
    };

    // MENU BUTTON (top-right)
    var btnMenu = new Button
    {
        Content = "⋮",
        Width = 32,
        Height = 32,
        BorderBrush=(IBrush)Application.Current!.FindResource("BMWAccentBrush")!,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    // INFO BLOCK
    var level = new TextBlock
    {
        Text = array.Level,
        FontSize = 15,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    var state = new TextBlock
    {
        Text = array.State,
        FontSize = 13,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    var diskCount = new TextBlock
    {
        Text = $"{array.Disks.Count} disks",
        FontSize = 13,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    var infoStack = new StackPanel
    {
        Spacing = 2,
        Children = { level, state, diskCount }
    };

    // NAME + INFO (center column)
    var nameAndInfo = new StackPanel
    {
        Spacing = 6,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { name, infoStack }
    };

    // ACTION BUTTONS (BOTTOM)
    var btnDetails = new Button { Content = "Details", Margin = new Thickness(0, 0, 6, 0) };
    var btnCheck = new Button { Content = "Check", Margin = new Thickness(0, 0, 6, 0) };
    var btnRepair = new Button { Content = "Repair", Margin = new Thickness(0, 0, 6, 0) };

    var bottomButtons = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 8, 0, 0)
    };

    bottomButtons.Children.Add(btnDetails);
    bottomButtons.Children.Add(btnCheck);
    bottomButtons.Children.Add(btnRepair);

    // MAIN GRID (2 rows, 3 columns)
    var grid = new Grid
    {
        RowDefinitions =
        {
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto)
        },
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Auto),   // icon
            new ColumnDefinition(GridLength.Star),   // name + info
            new ColumnDefinition(GridLength.Auto)    // menu
        }
    };

    // ICON spans both rows
    grid.Children.Add(icon);
    Grid.SetRow(icon, 0);
    Grid.SetColumn(icon, 0);
    Grid.SetRowSpan(icon, 2);

    // NAME + INFO (center)
    grid.Children.Add(nameAndInfo);
    Grid.SetRow(nameAndInfo, 0);
    Grid.SetColumn(nameAndInfo, 1);

    // MENU BUTTON (top-right)
    grid.Children.Add(btnMenu);
    Grid.SetRow(btnMenu, 0);
    Grid.SetColumn(btnMenu, 2);

    // BOTTOM BUTTONS (full width)
    grid.Children.Add(bottomButtons);
    Grid.SetRow(bottomButtons, 1);
    Grid.SetColumn(bottomButtons, 1);
    Grid.SetColumnSpan(bottomButtons, 2);

    // -------------------------------
    // GLOW EXTERIOR BMW (DynamicResource)
    // -------------------------------
    var glowColor = GetArrayGlowColor(array.State);
    var glowBrush = new SolidColorBrush(glowColor) { Opacity = 0.35 };

    var glowBorder = new Border
    {
        Background = glowBrush,
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(0),
        Margin = new Thickness(0, 0, 0, 8)
    };

    // Tarjeta interior BMW (usa BMWSurfaceElevatedBrush)
    var cardBorder = new Border
    {
        Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
        CornerRadius = new CornerRadius(10),
        Cursor = new Cursor(StandardCursorType.Hand),
        Padding = new Thickness(12),
        Child = grid
    };


    glowBorder.Child = cardBorder;

    // Animación del glow BMW
    AnimateArrayGlow(glowBorder, glowBrush);

    // Expand/collapse
    cardBorder.PointerPressed += (_, _) =>
    {
        array.IsExpanded = !array.IsExpanded;
        BuildUI();
    };

    return glowBorder;
}



///----------------------

    
    private Border BuildExpandedCard(RaidArrayInfo array)
    {
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = array.Name,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var disk in array.Disks)
            panel.Children.Add(BuildDiskCard(disk));

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
            Child = panel
        };
    }

   private Border BuildDiskCard(RaidDiskInfo disk)
{
    var icon = LoadImage(disk.Icon, 72);
    icon.Margin = new Thickness(2);

    // TEXTOS BMW (Light/Dark automáticos)
    var name = new TextBlock
    {
        Text = disk.Name,
        FontSize = 17,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!
    };

    var role = new TextBlock
    {
        Text = disk.Role,
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    var state = new TextBlock
    {
        Text = disk.State,
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    var textStack = new StackPanel
    {
        Spacing = 2,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { name, role, state }
    };

    // DOT con glow y animación (ya BMW)
    var statusDot = BuildStatusDot(disk.State);

    // Botón ⋮ (se estiliza vía BMWStyles.axaml)
    var manageButton = new Button
    {
        Content = "⋮",
        Width = 32,
        Height = 32,
        BorderBrush=(IBrush)Application.Current!.FindResource("BMWAccentBrush")!,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        
        VerticalAlignment = VerticalAlignment.Center
    };

    
    var grid = new Grid
    {
        ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(GridLength.Auto),   // icono
            new ColumnDefinition(GridLength.Star),   // texto
            new ColumnDefinition(GridLength.Auto),   // botón ⋮
            new ColumnDefinition(GridLength.Auto)    // dot
        },
        VerticalAlignment = VerticalAlignment.Center
    };

    grid.Children.Add(icon);
    Grid.SetColumn(icon, 0);

    grid.Children.Add(textStack);
    Grid.SetColumn(textStack, 1);

    grid.Children.Add(manageButton);
    Grid.SetColumn(manageButton, 2);

    grid.Children.Add(statusDot);
    Grid.SetColumn(statusDot, 3);

    // TARJETA BMW (Light/Dark automático)
    return new Border
    {
        Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        BorderBrush = (IBrush)Application.Current!.FindResource("BMWBorderBrush")!,
        BorderThickness = new Thickness(1),
        Child = grid
    };
}

    
    private Border BuildStatusDot(string state)
    {
        Color color = state switch
        {
            "OK" => Color.FromRgb(0, 255, 0),
            "FAULTY" => Color.FromRgb(255, 0, 0),
            _ => Color.FromRgb(255, 200, 0)
        };

        // Glow (círculo grande difuso)
        var glow = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(color) { Opacity = 0.35 },
            Margin = new Thickness(0),
        };

        // Punto real (círculo pequeño)
        var dot = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Contenedor apilado (glow detrás, punto encima)
        var container = new Grid
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(8, 0, 8, 0)
        };

        container.Children.Add(glow);
        container.Children.Add(dot);

        // Animaciones
        if (state == "FAULTY")
            AnimateSOS(dot, glow);
        else if (state != "OK")
            AnimateWarning(dot, glow);

        return new Border
        {
            Child = container
        };
    }

    private async void AnimateWarning(Border dot, Border glow)
    {
        var b1 = (SolidColorBrush)dot.Background;
        var b2 = (SolidColorBrush)glow.Background;

        while (true)
        {
            b1.Opacity = 0.4;
            b2.Opacity = 0.15;
            await Task.Delay(500);

            b1.Opacity = 1.0;
            b2.Opacity = 0.35;
            await Task.Delay(500);
        }
    }

    private async void AnimateSOS(Border dot, Border glow)
    {
        var b1 = (SolidColorBrush)dot.Background;
        var b2 = (SolidColorBrush)glow.Background;

        while (true)
        {
            // 3 cortos
            for (int i = 0; i < 3; i++)
            {
                b1.Opacity = 1; b2.Opacity = 0.35;
                await Task.Delay(200);
                b1.Opacity = 0.2; b2.Opacity = 0.1;
                await Task.Delay(200);
            }

            // 3 largos
            for (int i = 0; i < 3; i++)
            {
                b1.Opacity = 1; b2.Opacity = 0.35;
                await Task.Delay(600);
                b1.Opacity = 0.2; b2.Opacity = 0.1;
                await Task.Delay(600);
            }

            // 3 cortos
            for (int i = 0; i < 3; i++)
            {
                b1.Opacity = 1; b2.Opacity = 0.35;
                await Task.Delay(200);
                b1.Opacity = 0.2; b2.Opacity = 0.1;
                await Task.Delay(200);
            }

            await Task.Delay(1000);
        }
    }

   
    private Image LoadImage(string uriString, int size)
    {
        var uri = new Uri(uriString);
        using var stream = AssetLoader.Open(uri);

        return new Image
        {
            Source = new Avalonia.Media.Imaging.Bitmap(stream),
            Width = size,
            Height = size
        };
    }
}
