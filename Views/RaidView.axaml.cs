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
using RAID_Util.Core;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class RaidView : UserControl
{
    private List<RaidArrayInfo> _arrays = new();

    public RaidView()
    {
        InitializeComponent();
        //LoadRealData();
        LoadFakeData();
        BuildUI();
    }

    
    private async void LoadRealData()
    {
        try
        {
            var service = new RaidService();

            // Obtener arrays reales del sistema
            _arrays = await service.GetArraysAsync();

            // Si no hay arrays, mostrar UI vacía
            if (_arrays == null || _arrays.Count == 0)
            {
                _arrays = new List<RaidArrayInfo>();
            }

            BuildUI();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading real RAID data: {ex.Message}");
        }
    }

    
    private void LoadFakeData()
{
    _arrays = new List<RaidArrayInfo>
    {
        new RaidArrayInfo
        {
            Name = "md0",
            Level = "RAID1",
            State = "Healthy",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-ok.png",
            IsExpanded = false,
            IsMounted = true,
            PersistMount = true,
            MountPath = "/mnt/md0",
            Disks = new List<RaidDiskInfo>
            {
                new RaidDiskInfo
                {
                    Name = "sda1",
                    Model = "Samsung SSD 860 EVO",
                    Size = "500G",
                    Role = "active",
                    State = "OK",
                    Icon = "avares://RAID-Util/Assets/Icons/disk-ssd.png",
                    ArrayName = "md0"
                },
                new RaidDiskInfo
                {
                    Name = "sdb1",
                    Model = "Samsung SSD 860 EVO",
                    Size = "500G",
                    Role = "active",
                    State = "OK",
                    Icon = "avares://RAID-Util/Assets/Icons/disk-ssd.png",
                    ArrayName = "md0"
                }
            }
        },

        new RaidArrayInfo
        {
            Name = "md1",
            Level = "RAID5",
            State = "Degraded",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png",
            IsExpanded = true,
            IsMounted = false,
            PersistMount = false,
            MountPath = "/mnt/md1",
            Disks = new List<RaidDiskInfo>
            {
                new RaidDiskInfo
                {
                    Name = "sdc1",
                    Model = "WD Blue 1TB",
                    Size = "1T",
                    Role = "active",
                    State = "OK",
                    Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                    ArrayName = "md1"
                },
                new RaidDiskInfo
                {
                    Name = "sdd1",
                    Model = "WD Blue 1TB",
                    Size = "1T",
                    Role = "active",
                    State = "OK",
                    Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                    ArrayName = "md1"
                },
                new RaidDiskInfo
                {
                    Name = "sde1",
                    Model = "WD Blue 1TB",
                    Size = "1T",
                    Role = "faulty",
                    State = "FAULTY",
                    Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                    ArrayName = "md1"
                }
            }
        },

        new RaidArrayInfo
        {
            Name = "md2",
            Level = "RAID0",
            State = "Rebuilding",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png",
            IsExpanded = false,
            IsMounted = false,
            PersistMount = false,
            MountPath = "/mnt/md2",
            Disks = new List<RaidDiskInfo>
            {
                new RaidDiskInfo
                {
                    Name = "nvme0n1p1",
                    Model = "Samsung 980 PRO",
                    Size = "1T",
                    Role = "rebuilding",
                    State = "WARN",
                    Icon = "avares://RAID-Util/Assets/Icons/disk-nvme.png",
                    ArrayName = "md2"
                },
                new RaidDiskInfo
                {
                    Name = "nvme1n1p1",
                    Model = "Samsung 980 PRO",
                    Size = "1T",
                    Role = "active",
                    State = "OK",
                    Icon = "avares://RAID-Util/Assets/Icons/disk-nvme.png",
                    ArrayName = "md2"
                }
            }
        }
    };

    BuildUI();
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

    // BOTÓN ⋮ BMW
    var btnMenu = new Button
    {
        Content = "⋮",
        Width = 32,
        Height = 32,
        Classes = { "IconButton" },
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    // MENÚ BMW + LÓGICA REAL
    btnMenu.Click += (_, _) =>
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem
                {
                    Header = "Details",
                    Command = new LambdaCommand(() => ShowArrayDetails(array))
                },
                new MenuItem
                {
                    Header = "Check Array",
                    Command = new LambdaCommand(() => RunArrayCheck(array))
                },
                new MenuItem
                {
                    Header = "Repair Array",
                    Command = new LambdaCommand(() => RunArrayRepair(array))
                },
                new MenuItem
                {
                    Header = "Add Spare",
                    Command = new LambdaCommand(() => AddSpareToArray(array))
                },
                new MenuItem
                {
                    Header = "Remove Array",
                    Command = new LambdaCommand(() => RemoveArray(array))
                }
            }
        };

        menu.Open(btnMenu);
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

    // MAIN GRID (solo 1 fila ahora)
    var grid = new Grid
    {
        RowDefinitions =
        {
            new RowDefinition(GridLength.Auto)
        },
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Auto),   // icon
            new ColumnDefinition(GridLength.Star),   // name + info
            new ColumnDefinition(GridLength.Auto)    // menu
        }
    };

    // ICON
    grid.Children.Add(icon);
    Grid.SetRow(icon, 0);
    Grid.SetColumn(icon, 0);

    // NAME + INFO
    grid.Children.Add(nameAndInfo);
    Grid.SetRow(nameAndInfo, 0);
    Grid.SetColumn(nameAndInfo, 1);

    // MENU BUTTON
    grid.Children.Add(btnMenu);
    Grid.SetRow(btnMenu, 0);
    Grid.SetColumn(btnMenu, 2);

    // -------------------------------
    // GLOW EXTERIOR BMW
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

    // Tarjeta interior BMW
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
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        FontSize = 20,
        FontWeight = FontWeight.Bold,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!,
        Margin = new Thickness(0, 0, 0, 8)
    });

    // Opciones de montaje
    panel.Children.Add(BuildMountOptions(array));

    // Discos
    foreach (var disk in array.Disks)
        panel.Children.Add(BuildDiskCard(disk));

    return new Border
    {
        Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16),
        Margin = new Thickness(0, 0, 0, 16),
        Child = panel
    };
}

private Control BuildMountOptions(RaidArrayInfo array)
{
    var panel = new StackPanel
    {
        Spacing = 12,
        Margin = new Thickness(0, 0, 0, 10)
    };

    // 🔵 BOTONES CENTRADOS
    var buttonsRow = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        
        HorizontalAlignment = HorizontalAlignment.Right
    };

    var btnMount = new Button
    {
        Content = array.IsMounted ? "Unmount" : "Mount",
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Classes = { "PrimaryButton" },
        Width = 100
    };

    btnMount.Click += (_, _) =>
    {
        array.IsMounted = !array.IsMounted;
        BuildUI();
    };

    var btnOpen = new Button
    {
        Content = "Open",
        Classes = { "SecondaryButton" },
        Width = 90
    };

    buttonsRow.Children.Add(btnMount);
    buttonsRow.Children.Add(btnOpen);

    panel.Children.Add(buttonsRow);

    // 🔵 TOGGLE ÚNICO (CENTRADO)
    var togglesRow = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 30,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    var chkPersist = new CheckBox
    {
        Content = "Persist Mount",
        IsChecked = array.PersistMount,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!
    };

    chkPersist.Checked += (_, _) => array.PersistMount = true;
    chkPersist.Unchecked += (_, _) => array.PersistMount = false;

    togglesRow.Children.Add(chkPersist);

    panel.Children.Add(togglesRow);

    // 🔵 RUTA DE MONTAJE (IZQUIERDA)
    panel.Children.Add(new TextBlock
    {
        Text = $"Mount Path: {array.MountPath ?? "/mnt/" + array.Name}",
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Left,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    return panel;
}

   
  private Border BuildDiskCard(RaidDiskInfo disk)
{
    var icon = LoadImage(disk.Icon, 72);
    icon.Margin = new Thickness(2);

    // TEXTOS BMW
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

    // DOT con glow BMW
    var statusDot = BuildStatusDot(disk.State);

    // BOTÓN ⋮ BMW
    var manageButton = new Button
    {
        Content = "⋮",
        Width = 32,
        Height = 32,
        Classes = { "IconButton" },
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    // MENÚ BMW + LÓGICA REAL
    manageButton.Click += (_, _) =>
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem
                {
                    Header = "Details",
                    Command = new LambdaCommand(() => ShowDiskDetails(disk))
                },
                new MenuItem
                {
                    Header = "Check",
                    Command = new LambdaCommand(() => RunDiskCheck(disk))
                },
                new MenuItem
                {
                    Header = "Repair",
                    Command = new LambdaCommand(() => RunDiskRepair(disk))
                },
                new MenuItem
                {
                    Header = "SMART Info",
                    Command = new LambdaCommand(() => ShowSmartInfo(disk))
                },
                new MenuItem
                {
                    Header = "Mark as Faulty",
                    Command = new LambdaCommand(() => MarkDiskAsFaulty(disk))
                }
            }
        };

        menu.Open(manageButton);
    };

    // GRID
    var grid = new Grid
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto)
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

    // TARJETA BMW
    return new Border
    {
        Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
        BorderBrush = (IBrush)Application.Current!.FindResource("BMWBorderBrush")!,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
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
    
    
    private void ShowDiskDetails(RaidDiskInfo disk)
    {
        Console.WriteLine($"Details for {disk.Name}");
    }

    private void RunDiskCheck(RaidDiskInfo disk)
    {
        Console.WriteLine($"Checking {disk.Name}");
    }

    private void RunDiskRepair(RaidDiskInfo disk)
    {
        Console.WriteLine($"Repairing {disk.Name}");
    }

    private void ShowSmartInfo(RaidDiskInfo disk)
    {
        Console.WriteLine($"SMART info for {disk.Name}");
    }

    private void MarkDiskAsFaulty(RaidDiskInfo disk)
    {
        Console.WriteLine($"Marking {disk.Name} as faulty");
    }

    
    private void ShowArrayDetails(RaidArrayInfo array)
    {
        Console.WriteLine($"Array details: {array.Name}");
    }

    private void RunArrayCheck(RaidArrayInfo array)
    {
        Console.WriteLine($"Checking array {array.Name}");
    }

    private void RunArrayRepair(RaidArrayInfo array)
    {
        Console.WriteLine($"Repairing array {array.Name}");
    }

    private void AddSpareToArray(RaidArrayInfo array)
    {
        Console.WriteLine($"Adding spare to array {array.Name}");
    }

    private void RemoveArray(RaidArrayInfo array)
    {
        Console.WriteLine($"Removing array {array.Name}");
    }

  
    
    
}
