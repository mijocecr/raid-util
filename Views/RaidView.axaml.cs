using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using RAID_Util.Core;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class RaidView : UserControl
{
    private List<RaidArrayInfo> _arrays = new();

    // ⭐ Flag para forzar fake data si quieres probar la UI sin backend
    private const bool FORCE_FAKE_DATA = true;

    public bool IsFakeMode => FORCE_FAKE_DATA;
    
    public RaidView()
    {
        InitializeComponent();
        BtnCreateArray.Click += OnCreateArrayClicked;
        BtnDeleteArray.Click += OnDeleteArrayClicked;
        BtnRefreshArrays.Click += OnRefreshArraysClicked;
        BtnConfigArrays.Click += OnConfigArraysClicked;
        BtnDeleteArray.IsEnabled = false;
        BtnConfigArrays.IsEnabled = false;

        
        Console.WriteLine("[RAIDVIEW] Constructor RaidView ejecutado.");

        if (FORCE_FAKE_DATA)
        {
            Console.WriteLine("[RAIDVIEW] FORCE_FAKE_DATA = true → cargando datos falsos.");
            LoadFakeData();
        }
        else
        {
            Console.WriteLine("[RAIDVIEW] Modo real: esperando datos desde MainWindow.");
            // En modo real, los datos llegan vía MainWindow.RaidViewControl.SetArrays(...)
            // No llamamos a LoadRealData() aquí para no duplicar llamadas a sudo.
        }
    }

    public void SetArrays(List<RaidArrayInfo> arrays)
    {
        Console.WriteLine($"[RAIDVIEW] SetArrays() llamado. arrays.Count = {arrays?.Count ?? -1}");

        _arrays = arrays ?? new List<RaidArrayInfo>();

        if (_arrays.Count == 0)
        {
            Console.WriteLine("[RAIDVIEW] SetArrays(): lista vacía.");
            BuildUI();
            return;
        }

        foreach (var a in _arrays)
        {
            Console.WriteLine($"[RAIDVIEW] ARRAY {a.Name} Level={a.Level} State={a.State} Disks={a.Disks?.Count ?? 0}");

            if (string.IsNullOrWhiteSpace(a.StateIcon))
                a.StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png";

            if (string.IsNullOrWhiteSpace(a.TotalSize))
                a.TotalSize = "Unknown";

            if (string.IsNullOrWhiteSpace(a.UsableSize))
                a.UsableSize = a.TotalSize;

            if (string.IsNullOrWhiteSpace(a.ParitySize))
                a.ParitySize = "N/A";

            if (string.IsNullOrWhiteSpace(a.DiskSummary))
                a.DiskSummary = $"{a.Disks?.Count ?? 0}× Disk";

            if (string.IsNullOrWhiteSpace(a.Uptime))
                a.Uptime = "Unknown";

            if (a.Disks == null)
                a.Disks = new List<RaidDiskInfo>();

            foreach (var d in a.Disks)
            {
                if (string.IsNullOrWhiteSpace(d.Icon))
                    d.Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png";

                Console.WriteLine($"[RAIDVIEW]   DISK {d.Name} Role={d.Role} State={d.State} Icon={d.Icon}");
            }
        }

        BuildUI();
    }

    // Si algún día quieres volver a cargar real desde aquí, lo tienes:
    private async void LoadRealData()
    {
        Console.WriteLine("[RAIDVIEW] LoadRealData() iniciado.");

        try
        {
            var service = new RaidService();

            _arrays = await service.GetArraysAsync();

            if (_arrays == null || _arrays.Count == 0)
            {
                Console.WriteLine("[RAIDVIEW] No se detectaron arrays. Lista vacía.");
                _arrays = new List<RaidArrayInfo>();
            }
            else
            {
                Console.WriteLine($"[RAIDVIEW] Arrays detectados: {_arrays.Count}");

                foreach (var a in _arrays)
                {
                    Console.WriteLine($"[RAIDVIEW] ARRAY {a.Name} Level={a.Level} State={a.State} Disks={a.Disks?.Count ?? 0}");

                    if (string.IsNullOrWhiteSpace(a.StateIcon))
                        a.StateIcon = "avares://RAID-Util/Assets/Icons/array-caution.png";

                    if (string.IsNullOrWhiteSpace(a.TotalSize))
                        a.TotalSize = "Unknown";

                    if (string.IsNullOrWhiteSpace(a.UsableSize))
                        a.UsableSize = a.TotalSize;

                    if (string.IsNullOrWhiteSpace(a.ParitySize))
                        a.ParitySize = "N/A";

                    if (string.IsNullOrWhiteSpace(a.DiskSummary))
                        a.DiskSummary = $"{a.Disks?.Count ?? 0}× Disk";

                    if (string.IsNullOrWhiteSpace(a.Uptime))
                        a.Uptime = "Unknown";

                    if (a.Disks == null)
                        a.Disks = new List<RaidDiskInfo>();

                    foreach (var d in a.Disks)
                    {
                        if (string.IsNullOrWhiteSpace(d.Icon))
                            d.Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png";

                        Console.WriteLine($"[RAIDVIEW]   DISK {d.Name} Role={d.Role} State={d.State} Icon={d.Icon}");
                    }
                }
            }

            BuildUI();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RAIDVIEW] Error loading real RAID data: {ex}");
        }

        Console.WriteLine("[RAIDVIEW] LoadRealData() finalizado.");
    }

    private void LoadFakeData()
    {
        Console.WriteLine("[RAIDVIEW] LoadFakeData() ejecutado.");

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

                TotalSize = "500 GB",
                UsableSize = "500 GB",
                ParitySize = "0 GB",
                AverageTemp = 34,
                DiskSummary = "2× SSD SATA",
                Uptime = "12 days",
                RebuildProgress = 0,
                RebuildETA = "",

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

                TotalSize = "3 TB",
                UsableSize = "2 TB",
                ParitySize = "1 TB",
                AverageTemp = 41,
                DiskSummary = "3× HDD 7200 RPM",
                Uptime = "3 days",
                RebuildProgress = 37,
                RebuildETA = "12 min",

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

                TotalSize = "2 TB",
                UsableSize = "2 TB",
                ParitySize = "0 TB",
                AverageTemp = 46,
                DiskSummary = "2× NVMe PCIe 4.0",
                Uptime = "7 hours",
                RebuildProgress = 82,
                RebuildETA = "3 min",

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
        Console.WriteLine("[RAIDVIEW] BuildUI() ejecutado.");

        if (ListArrays == null)
        {
            Console.WriteLine("[RAIDVIEW] ERROR: ListArrays == null (revisa RaidView.axaml, x:Name=\"ListArrays\").");
            return;
        }

        Console.WriteLine($"[RAIDVIEW] _arrays.Count = {_arrays.Count}");

        ListArrays.Children.Clear();

        foreach (var array in _arrays)
        {
            Console.WriteLine($"[RAIDVIEW] Dibujando array {array.Name} con {array.Disks?.Count ?? 0} discos.");

            var card = BuildArrayCard(array);
            ListArrays.Children.Add(card);

            if (array.IsExpanded)
            {
                var expanded = BuildExpandedCard(array);
                ListArrays.Children.Add(expanded);
            }
        }

        Console.WriteLine("[RAIDVIEW] BuildUI() completado.");
    }

    private RaidArrayInfo? _selectedArray = null;

private Border BuildArrayCard(RaidArrayInfo array)
{
    var icon = LoadImage(array.StateIcon, 150);
    icon.Margin = new Thickness(4);
    icon.VerticalAlignment = VerticalAlignment.Center;

    var name = new TextBlock
    {
        Text = $"{array.Name} ({array.Level})",
        FontSize = 22,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 0, 0, 4)
    };

    var info = new StackPanel { Spacing = 2 };

    info.Children.Add(name);

    info.Children.Add(new TextBlock
    {
        Text = $"State: {array.State}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Disks: {array.Disks.Count} ({array.Disks.Count(d => d.State == "OK")} OK, {array.Disks.Count(d => d.State == "FAULTY")} Faulty)",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Size: {array.TotalSize} (Usable {array.UsableSize}, Parity {array.ParitySize})",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Avg Temp: {array.AverageTemp}°C",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Type: {array.DiskSummary}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Uptime: {array.Uptime}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    if (array.RebuildProgress > 0)
    {
        info.Children.Add(new TextBlock
        {
            Text = $"Rebuild: {array.RebuildProgress}% (ETA {array.RebuildETA})",
            FontSize = 14,
            Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
        });
    }

    // Original grid (icon + info)
    var grid = new Grid
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Star)
        }
    };

    grid.Children.Add(icon);
    Grid.SetColumn(icon, 0);

    grid.Children.Add(info);
    Grid.SetColumn(info, 1);

    // ⭐ Overlay grid for checkbox + more button
    var overlay = new Grid
    {
        RowDefinitions =
        {
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Star)
        }
    };

    // Top-right panel
    var topRightPanel = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Spacing = 6
    };

    // ⭐ CheckBox (left of More)
    var chkSelect = new CheckBox
    {
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Right,
        IsChecked = (_selectedArray == array)
    };

    chkSelect.Checked += (_, _) =>
    {
        _selectedArray = array;
        ClearOtherSelections(array);
        BtnDeleteArray.IsEnabled = true;
        BtnConfigArrays.IsEnabled = true;
        BuildUI();
    };

    chkSelect.Unchecked += (_, _) =>
    {
        if (_selectedArray == array)
            _selectedArray = null;
        BtnDeleteArray.IsEnabled = false;
        BtnConfigArrays.IsEnabled = false;
        BuildUI();
    };

    topRightPanel.Children.Add(chkSelect);

    // ⭐ More button
    var btnMore = new Button
    {
        Content = "More",
        Classes = { "MoreButton" },
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    // ⭐ Create menu
    var menu = new ContextMenu
    {
        Items =
        {
            new MenuItem { Header = "Details", Tag = "details" },
            new MenuItem { Header = "Start array", Tag = "start" },
            new MenuItem { Header = "Stop array", Tag = "stop" },
            new MenuItem { Header = "Add disk", Tag = "add" },
            new MenuItem { Header = "Remove disk", Tag = "remove" },
            new MenuItem { Header = "Mark disk faulty", Tag = "faulty" },
            new MenuItem { Header = "Replace disk", Tag = "replace" },
            new MenuItem { Header = "Force check or resync", Tag = "check" },
            new MenuItem { Header = "View logs", Tag = "logs" }
        }
    };

// ⭐ Open menu on click
    btnMore.Click += (_, _) =>
    {
        menu.PlacementTarget = btnMore;
        menu.Open(btnMore);  
    };


// ⭐ Menu item handlers
    foreach (var item in menu.Items.OfType<MenuItem>())
        item.Click += (_, _) => OnMoreMenuClick(array, item.Tag?.ToString());

    
    topRightPanel.Children.Add(btnMore);

    // Add top-right panel
    overlay.Children.Add(topRightPanel);
    Grid.SetRow(topRightPanel, 0);

    // Add original content
    overlay.Children.Add(grid);
    Grid.SetRow(grid, 1);

    // Glow + card border
    var glowColor = GetArrayGlowColor(array.State);
    var glowBrush = new SolidColorBrush(glowColor) { Opacity = 0.35 };

    var glowBorder = new Border
    {
        Background = glowBrush,
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(0),
        Margin = new Thickness(0, 0, 0, 8)
    };

    var cardBorder = new Border
    {
        Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
        CornerRadius = new CornerRadius(10),
        Cursor = new Cursor(StandardCursorType.Hand),
        Padding = new Thickness(12),
        Child = overlay
    };

    glowBorder.Child = cardBorder;

    AnimateArrayGlow(glowBorder, glowBrush);

    cardBorder.PointerPressed += (_, _) =>
    {
        array.IsExpanded = !array.IsExpanded;
        BuildUI();
    };

    return glowBorder;
}

// ⭐ Selección única

    private void ClearOtherSelections(RaidArrayInfo selected)
    {
        foreach (var arr in _arrays)
        {
            if (arr != selected)
                arr.IsSelected = false;
        }

        BtnDeleteArray.IsEnabled = _selectedArray != null;
    }


  
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

        panel.Children.Add(BuildMountOptions(array));

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
        Console.WriteLine($"[RAIDVIEW]   BuildDiskCard() para {disk.Name}, Icon={disk.Icon}");

        var icon = LoadImage(disk.Icon, 72);
        icon.Margin = new Thickness(2);

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

        var statusDot = BuildStatusDot(disk.State);

        var manageButton = new Button
        {
            Content = "More",
            Width = 80,
            
            Height = 32,
            Classes = { "IconButton" },
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Top
        };

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

        var glow = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(color) { Opacity = 0.35 },
            Margin = new Thickness(0),
        };

        var dot = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var container = new Grid
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(8, 0, 8, 0)
        };

        container.Children.Add(glow);
        container.Children.Add(dot);

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
            for (int i = 0; i < 3; i++)
            {
                b1.Opacity = 1; b2.Opacity = 0.35;
                await Task.Delay(200);
                b1.Opacity = 0.2; b2.Opacity = 0.1;
                await Task.Delay(200);
            }

            for (int i = 0; i < 3; i++)
            {
                b1.Opacity = 1; b2.Opacity = 0.35;
                await Task.Delay(600);
                b1.Opacity = 0.2; b2.Opacity = 0.1;
                await Task.Delay(600);
            }

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
        try
        {
            if (string.IsNullOrWhiteSpace(uriString))
            {
                Console.WriteLine("[RAIDVIEW] LoadImage(): uriString vacío, usando icono por defecto.");
                uriString = "avares://RAID-Util/Assets/Icons/disk-hdd.png";
            }

            var uri = new Uri(uriString);
            using var stream = AssetLoader.Open(uri);

            return new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(stream),
                Width = size,
                Height = size
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RAIDVIEW] LoadImage() ERROR con '{uriString}': {ex.Message}. Usando icono por defecto.");

            var fallback = new Uri("avares://RAID-Util/Assets/Icons/disk-hdd.png");
            using var stream = AssetLoader.Open(fallback);

            return new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(stream),
                Width = size,
                Height = size
            };
        }
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
    
    private void LoadArrays()
    {
        // This method reloads all RAID arrays and rebuilds the UI.
        // It replaces the previous MainWindow.LoadRaidAsync call.
    }

    private void OpenCreateArrayWindow()
    {
        // Opens the Create Array window.
    }

    private void OpenDeleteArrayWindow()
    {
        // Opens the Delete Array window.
    }

    private void OpenRaidConfigWindow()
    {
        // Opens the global RAID configuration window.
    }

    
    private void OnCreateArrayClicked(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Create Array button clicked.");
        OpenCreateArrayWindow();
    }

  
    private async void OnDeleteArrayClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedArray == null)
        {
            Console.WriteLine("[DELETE] No array selected.");
            return;
        }

        var array = _selectedArray;

        // Confirmación
        var dialog = new ConfirmDialog($"Delete array {array.Name}?", "This action cannot be undone.");
        var result = await dialog.ShowDialog<bool>(GetWindow());

        if (!result)
        {
            Console.WriteLine("[DELETE] Cancelled.");
            return;
        }

        Console.WriteLine($"[DELETE] REAL delete for {array.Name}");

        // FakeData → solo eliminar de la lista
        if (IsFakeMode)
        {
            _arrays.Remove(array);
            _selectedArray = null;
            BuildUI();
            return;
        }

        // Modo real
        using (LoadingService.Show("Deleting array..."))
        {
            var service = new RaidService();
            bool ok = await Task.Run(() => service.DeleteArrayAsync(array));

            if (!ok)
            {
                Console.WriteLine("[DELETE] ERROR deleting array.");
                return;
            }
        }

        // Actualizar UI
        _arrays.Remove(array);
        _selectedArray = null;
        BuildUI();
    }

    
    
private Window GetWindow()
{
    return (Window)VisualRoot!;
}

   
    
    private async void OnRefreshArraysClicked(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Refresh button clicked.");
        await LoadRaidAsync();
    }


    private void OnConfigArraysClicked(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Config button clicked.");
        OpenRaidConfigWindow();
    }

  private async Task LoadRaidAsync()
{
    LogService.Write("[RAIDVIEW] ================= RAID LOAD START =================");
    LogService.Debug("[RAIDVIEW] LoadRaidAsync() ENTER");

    try
    {
        // ⭐ FAKE DATA MODE → NO BACKEND
        if (IsFakeMode)
        {
            LogService.Write("[RAIDVIEW] Fake mode enabled → loading fake arrays.");
            LoadFakeData();

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop1)
            {
                var main1 = desktop1.MainWindow as MainWindow;
                main1?.UpdateStatus("Fake RAID data loaded.");
            }

            LogService.Write("[RAIDVIEW] ================= RAID LOAD END (FAKE) =================");
            return;
        }

        // ⭐ REAL MODE → BACKEND
        using (LoadingService.Show("Loading RAID arrays..."))
        {
            var service = new RaidService();

            LogService.Debug("[RAIDVIEW] Calling RaidService.GetArraysAsync()...");
            var arrays = await service.GetArraysAsync();

            LogService.Debug($"[RAIDVIEW] Arrays returned: {arrays.Count}");
            foreach (var a in arrays)
                LogService.Debug($"[RAIDVIEW] ARRAY → {a.Name} | Level={a.Level} | State={a.State} | Disks={a.Disks.Count}");

            LogService.Debug("[RAIDVIEW] Calling RaidService.GetAllDisksAsync()...");
            var disks = await service.GetAllDisksAsync();

            LogService.Debug($"[RAIDVIEW] Disks returned: {disks.Count}");
            foreach (var d in disks)
                LogService.Debug($"[RAIDVIEW] DISK → {d.Name} | Array={d.ArrayName} | Role={d.Role} | State={d.State} | Rota={d.IsRotational}");

            LogService.Debug("[RAIDVIEW] Sending arrays to SetArrays()...");
            SetArrays(arrays);

            // Update MainWindow status bar
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var main = desktop.MainWindow as MainWindow;
                main?.UpdateStatus("RAID information refreshed.");
            }
        }

        LogService.Write("[RAIDVIEW] ================= RAID LOAD END =================");
    }
    catch (Exception ex)
    {
        LogService.Error("[RAIDVIEW] LoadRaidAsync() EXCEPTION:");
        LogService.Error(ex.ToString());

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = desktop.MainWindow as MainWindow;
            main?.UpdateStatus("Error loading RAID information.");
        }

        LogService.Write("[RAIDVIEW] ================= RAID LOAD FAILED =================");
    }
    finally
    {
        LogService.Debug("[RAIDVIEW] LoadRaidAsync() EXIT");
    }
}

    
   //-------------Boton More------------------//
   
   private void OnMoreMenuClick(RaidArrayInfo array, string? action)
   {
       if (action == null)
           return;

       Console.WriteLine($"[MORE] Action '{action}' on array {array.Name}");

       switch (action)
       {
           case "details":
               ShowArrayDetails(array);
               break;

           case "start":
               RunArrayCheck(array);   // placeholder
               break;

           case "stop":
               RunArrayRepair(array);  // placeholder
               break;

           case "add":
               AddSpareToArray(array); // placeholder
               break;

           case "remove":
               RemoveArray(array);     // placeholder
               break;

           case "faulty":
               Console.WriteLine($"[MORE] Marking array {array.Name} as faulty (UI only)");
               break;

           case "replace":
               Console.WriteLine($"[MORE] Replacing disk in {array.Name} (UI only)");
               break;

           case "check":
               RunArrayCheck(array);   // placeholder
               break;

           case "logs":
               Console.WriteLine($"[MORE] Viewing logs for {array.Name}");
               break;

           default:
               Console.WriteLine($"[MORE] Unknown action: {action}");
               break;
       }
   }

   
   //-------------Boton More-----------------//
    
}//Fin de Clase
