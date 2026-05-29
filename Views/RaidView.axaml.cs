using System;
using System.Collections.Generic;
using System.IO;
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
using Avalonia.Threading;
using Avalonia.VisualTree;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class RaidView : UserControl
{
    private List<RaidArrayInfo> _arrays = new();

    // ⭐ Flag para forzar fake data si quieres probar la UI sin backend
    private const bool FORCE_FAKE_DATA = false;

    private DispatcherTimer? _monitorBlinkTimer = null;
    private bool _monitorBlinkState = false;
    private Border? _monitoringBorder = null;
    private string? _monitoringArrayName = null;

    
    public bool IsFakeMode => FORCE_FAKE_DATA;
    
    public RaidView()
    {
        InitializeComponent();
        BtnCreateArray.Click += OnCreateArrayClicked;
        BtnDeleteArray.Click += OnDeleteArrayClicked;
        BtnRefreshArrays.Click += OnRefreshArraysClicked;
        BtnConfigArrays.Click += OnConfigArraysClicked;
        BtnAssembleArrays.Click += OnAssembleArraysClicked;
        BtnInitialize.Click += OnInitializeClicked;

        BtnDeleteArray.IsEnabled = false;
        BtnConfigArrays.IsEnabled = false;
        BtnInitialize.IsEnabled = false;


        
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
    
    
    private async void OnAssembleArraysClicked(object? sender, RoutedEventArgs e)
    {
        var service = new RaidService();
        var parent = GetWindow();

        // 1) Mostrar diálogo de carga
        var loading = new LoadingDialog("Assembling arrays...");
        loading.Show(parent);

        await Task.Delay(50); // Permite renderizar el diálogo

        // 2) Ejecutar AutoAssemble en segundo plano
        bool ok = await Task.Run(() => service.AutoAssemble());

        // 3) Cerrar diálogo
        loading.Close();

        // 4) Mostrar resultado
        if (!ok)
        {
            await ShowConfirm("Error", "Could not assemble stopped arrays.");
            return;
        }

        await ShowConfirm("Success", "Arrays assembled correctly.");

        // 5) Refrescar arrays
        BtnRefreshArrays.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }


    
    private async void OnInitializeClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedArray == null)
        {
            await ShowConfirm("Error", "Select an array first.");
            return;
        }

        var array = _selectedArray;

        // ⭐ Abrir ventana BMW en modal
        var dlg = new FormatArrayDialog(array.Name);
        var owner = this.GetVisualRoot() as Window;
        var result = await dlg.ShowDialog<FormatArrayResult?>(owner);

        // Usuario canceló
        if (result == null)
            return;

        var service = new RaidService();

        // ⭐ Llamada correcta con los 3 parámetros
        bool ok = await service.InitializeArrayAsync(
            array.Name,
            result.Filesystem,
            result.Label
        );

        if (!ok)
        {
            await ShowConfirm("Error", "Could not initialize the array.");
            return;
        }

        await ShowConfirm("Success", $"The array {array.Name} was initialized.");

        // Refrescar
        BtnRefreshArrays.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }


    
    
    
    private async Task<bool> ShowConfirm(string title, string message)
    {
        var dlg = new InfoDialog(title, message);

        var owner = this.GetVisualRoot() as Window;
        if (owner != null)
            return await dlg.ShowDialog<bool>(owner);

        // fallback
        return await dlg.ShowDialog<bool>(new Window());
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
                if (string.IsNullOrWhiteSpace(d.Icon) || !d.Icon.Contains("avares://"))
                {
                    d.Icon = d.Icon switch
                    {
                        "hdd" => "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                        "ssd" => "avares://RAID-Util/Assets/Icons/disk-ssd.png",
                        "nvme" => "avares://RAID-Util/Assets/Icons/disk-nvme.png",
                        "usb" => "avares://RAID-Util/Assets/Icons/disk-usb.png",
                        "virtual" => "avares://RAID-Util/Assets/Icons/disk-virtual.png",
                        _ => "avares://RAID-Util/Assets/Icons/disk-hdd.png"
                    };
                }

                Console.WriteLine($"[RAIDVIEW]   DISK {d.Name} Role={d.Role} State={d.State} Icon={d.Icon}");
            }
        }

        BuildUI();
    }

    // Si algún día quieres volver a cargar real desde aquí, lo tienes:
    private async Task LoadRealData()

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
        
        // ⭐ Restaurar parpadeo si había un array en monitoreo
        if (_monitoringArrayName != null)
        {
            foreach (var glow in ListArrays.Children.OfType<Border>())
            {
                if (glow.Child is Border card)
                {
                    if (card.Child is Grid overlay)
                    {
                        var textBlocks = overlay.GetVisualDescendants().OfType<TextBlock>();

                        if (textBlocks.Any(t => t.Text.StartsWith(_monitoringArrayName)))
                        {
                            _monitoringBorder = card;
                            RestartBlinking();
                            break;
                        }
                    }
                }
            }
        }



        Console.WriteLine("[RAIDVIEW] BuildUI() completado.");
    }
    
    private void RestartBlinking()
    {
        if (_monitorBlinkTimer != null)
            _monitorBlinkTimer.Stop();

        _monitorBlinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _monitorBlinkTimer.Tick += (_, _) =>
        {
            if (_monitoringBorder == null)
                return;

            _monitorBlinkState = !_monitorBlinkState;

            _monitoringBorder.BorderBrush = _monitorBlinkState
                ? Brushes.Orange
                : Brushes.Transparent;

            _monitoringBorder.BorderThickness = new Thickness(3);
        };

        _monitorBlinkTimer.Start();
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
        Text = $"Size: {array.TotalSize}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Path: {array.Path}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });
    
    info.Children.Add(new TextBlock
    {
        Text = $"Persist Mount: {(array.PersistMount ? "YES" : "NO")}  Parity: {array.ParitySize}",
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

    // Grid principal (icono + info)
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

    // Overlay para checkbox + botón More
    var overlay = new Grid
    {
        RowDefinitions =
        {
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Star)
        }
    };

    var topRightPanel = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Spacing = 6
    };

    // Checkbox de selección
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
        BtnInitialize.IsEnabled = true;

        BuildUI();
    };

    chkSelect.Unchecked += (_, _) =>
    {
        if (_selectedArray == array)
            _selectedArray = null;
        BtnDeleteArray.IsEnabled = false;
        BtnConfigArrays.IsEnabled = false;
        BtnInitialize.IsEnabled = false;

        BuildUI();
    };

    topRightPanel.Children.Add(chkSelect);

    // Botón More
    var btnMore = new Button
    {
        Content = "More",
        Classes = { "MoreButton" },
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    // Menú contextual del array (solo acciones RAID reales)
    var menu = new ContextMenu
    {
        Items =
        {
            new MenuItem { Header = "Start resync/rebuild", Tag = "resync" },
            new MenuItem { Header = "Force check", Tag = "check" },
            new MenuItem { Header = "Force repair", Tag = "repair" },
            new MenuItem { Header = "Stop array", Tag = "stop" },
            new MenuItem { Header = "Details", Tag = "details" }
        }
    };

    btnMore.Click += (_, _) =>
    {
        menu.PlacementTarget = btnMore;
        menu.Open(btnMore);
    };

    foreach (var item in menu.Items.OfType<MenuItem>())
        item.Click += (_, _) => OnMoreMenuClick(array, item.Tag?.ToString());

    topRightPanel.Children.Add(btnMore);

    overlay.Children.Add(topRightPanel);
    Grid.SetRow(topRightPanel, 0);

    overlay.Children.Add(grid);
    Grid.SetRow(grid, 1);

    // Glow + tarjeta
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

        if (array.IsExpanded)
            StartMonitoringArray(array, cardBorder);
        else
            StopMonitoringArray();

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
        var panel = new StackPanel { Spacing = 14 };

        // ============================================================
        // 1) TÍTULO DEL ARRAY
        // ============================================================
        panel.Children.Add(new TextBlock
        {
            Text = $"{array.Name}  •  {array.Level}",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Estado
        panel.Children.Add(new TextBlock
        {
            Text = $"State: {array.State}",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontSize = 13,
            Foreground = (IBrush)Application.Current!.FindResource("BMWAccentBrush")!,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // ============================================================
        // 2) OPCIONES DE MONTAJE (PersistMount + botones)
        // ============================================================
        panel.Children.Add(BuildMountOptions(array));

        // ============================================================
        // 3) TARJETAS DE DISCOS
        // ============================================================
        foreach (var disk in array.Disks)
            panel.Children.Add(BuildDiskCard(disk));

        // ============================================================
        // 4) TARJETA FINAL
        // ============================================================
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
    var cfg = ArrayConfigService.Load(array.Name);

    string mountPoint = string.IsNullOrWhiteSpace(cfg.MountPoint)
        ? $"/mnt/{array.Name}"
        : cfg.MountPoint;

    bool isMounted = MountService.IsMounted(mountPoint);

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
        Content = isMounted ? "Unmount" : "Mount",
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Classes = { "PrimaryButton" },
        Width = 100
    };

    btnMount.Click += (_, _) =>
    {
        if (MountService.IsMounted(mountPoint))
        {
            MountService.Unmount(mountPoint);
        }
        else
        {
            // SOLO flags del usuario
            var opts = new List<string>();

            if (cfg.Mount_NoAtime) opts.Add("noatime");
            if (cfg.Mount_NoDirAtime) opts.Add("nodiratime");
            if (cfg.Mount_Discard) opts.Add("discard");
            if (cfg.Mount_Sync) opts.Add("sync");
            if (cfg.Mount_ReadOnly) opts.Add("ro");

            string mountOpts = opts.Count == 0
                ? "defaults"
                : string.Join(",", opts);

            MountService.Mount($"/dev/{array.Name}", mountPoint, mountOpts);

            // chmod solo afecta a FS POSIX (ext4/xfs/btrfs/f2fs)
            ShellHelper.EjecutarComoRoot($"chmod {cfg.MountPermissions} \"{mountPoint}\"");
        }

        BuildUI();
    };

    var btnOpen = new Button
    {
        Content = "Open",
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Classes = { "SecondaryButton" },
        Width = 90,
        IsEnabled = isMounted
    };

    btnOpen.Click += (_, _) =>
    {
        if (MountService.IsMounted(mountPoint))
            DesktopHelper.OpenPath(mountPoint);
    };

    buttonsRow.Children.Add(btnMount);
    buttonsRow.Children.Add(btnOpen);

    panel.Children.Add(buttonsRow);

    panel.Children.Add(new TextBlock
    {
        Text = $"Mount Path: {mountPoint}",
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Left,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });

    return panel;
}





   
private async void AnimateSmartDot(Border glow, Border dot, string state)
{
    if (state == "WARN")
    {
        // ⭐ Parpadeo normal (amarillo)
        while (true)
        {
            glow.Opacity = 0.25;
            dot.Opacity = 0.25;
            await Task.Delay(400);

            glow.Opacity = 0.55;
            dot.Opacity = 0.55;
            await Task.Delay(400);
        }
    }

    if (state == "FAULTY")
    {
        // ⭐ Patrón SOS (rojo)
        // · · · — — — · · ·
        while (true)
        {
            // 3 cortos
            for (int i = 0; i < 3; i++)
            {
                glow.Opacity = 0.20;
                dot.Opacity = 0.20;
                await Task.Delay(150);

                glow.Opacity = 0.80;
                dot.Opacity = 0.80;
                await Task.Delay(150);
            }

            // 3 largos
            for (int i = 0; i < 3; i++)
            {
                glow.Opacity = 0.20;
                dot.Opacity = 0.20;
                await Task.Delay(300);

                glow.Opacity = 0.80;
                dot.Opacity = 0.80;
                await Task.Delay(300);
            }

            // 3 cortos
            for (int i = 0; i < 3; i++)
            {
                glow.Opacity = 0.20;
                dot.Opacity = 0.20;
                await Task.Delay(150);

                glow.Opacity = 0.80;
                dot.Opacity = 0.80;
                await Task.Delay(150);
            }

            // Pausa entre SOS
            await Task.Delay(600);
        }
    }
}


   
   
   
    
   private Border BuildDiskCard(RaidDiskInfo disk)
{
    Console.WriteLine($"[RAIDVIEW]   BuildDiskCard() para {disk.Name}, Icon={disk.Icon}");

    // Icono del disco
    var icon = LoadImage(disk.Icon, 72);
    icon.Margin = new Thickness(2);

    // Nombre del disco
    var name = new TextBlock
    {
        Text = disk.Name,
        FontSize = 17,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!
    };

    // Modelo
    var model = new TextBlock
    {
        Text = $"Model: {disk.Model}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    // Tamaño
    var size = new TextBlock
    {
        Text = $"Size: {disk.Size}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    // Rol RAID
    var role = new TextBlock
    {
        Text = $"RAID Role: {disk.Role}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    // Estado SMART
    var smart = new TextBlock
    {
        Text = $"SMART: {disk.State}",
        FontSize = 14,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    };

    // Stack de texto
    var textStack = new StackPanel
    {
        Spacing = 2,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { name, model, size, role, smart }
    };

   

    // Botón More
    var manageButton = new Button
    {
        Content = "More",
        Width = 80,
        Height = 32,
        Margin =  new Thickness(10),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Classes = { "IconButton" }
    };
    
    
    // LED SMART
    var statusDot = BuildStatusDot(disk.State);

    manageButton.Click += (_, _) =>
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "SMART Info", Tag = "smart" },
                new MenuItem { Header = "Mark as Faulty", Tag = "faulty" },
                new MenuItem { Header = "Set as Spare", Tag = "spare" },
                new MenuItem { Header = "Remove from Array", Tag = "remove" },
                
            }
        };

        foreach (var item in menu.Items.OfType<MenuItem>())
            item.Click += (_, _) => OnDiskMenuClick(disk, item.Tag?.ToString());

        menu.Open(manageButton);
    };

    // Grid principal
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

    // Tarjeta final
    return new Border
    {
        Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
        BorderBrush = (IBrush)Application.Current!.FindResource("BMWBorderBrush")!,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 0, 10),
        Child = grid
    };
}

   
   private async void OnDiskMenuClick(RaidDiskInfo disk, string? action)
{
    var service = new RaidService();

    // ============================================================
    // VALIDACIONES BÁSICAS
    // ============================================================

    if (disk == null)
    {
        await ShowConfirm("Error", "Disk not found.");
        return;
    }

    if (string.IsNullOrWhiteSpace(disk.ArrayName))
    {
        await ShowConfirm("Error", "This disk does not belong to any array.");
        return;
    }

    // Buscar el array al que pertenece este disco
    var array = _arrays.FirstOrDefault(a => a.Name == disk.ArrayName);

    if (array == null)
    {
        await ShowConfirm("Error", $"Array {disk.ArrayName} not found.");
        return;
    }

    string level = array.Level.ToUpperInvariant();

    bool IsRaid0 = level == "RAID0";
    bool IsRaid1 = level == "RAID1";
    bool IsRaid5 = level == "RAID5";
    bool IsRaid6 = level == "RAID6";
    bool IsRaid10 = level == "RAID10";

    // RAID0 → no soporta ninguna acción RAID sobre discos
    if (IsRaid0 && action != "smart" && action != "details")
    {
        await ShowConfirm("Error", "RAID0 does not support disk operations.");
        return;
    }

    // No permitir acciones si el array está fallado
    if (array.State == "Failed" && action != "smart" && action != "details")
    {
        await ShowConfirm("Error", "This array is FAILED. Only SMART and details are available.");
        return;
    }

    // ============================================================
    // ACCIONES
    // ============================================================

    switch (action)
    {
        // --------------------------------------------------------
        // SMART INFO
        // --------------------------------------------------------
        case "smart":
        {
            string info = await service.GetSmartInfoAsync(disk.Name);

            var dlg = new ConsoleDialog($"SMART — {disk.Name}", info);
            var owner = this.GetVisualRoot() as Window;

            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                await dlg.ShowDialog(new Window());

            break;
        }

        // --------------------------------------------------------
        // MARK AS FAULTY
        // --------------------------------------------------------
        case "faulty":

            // No marcar faulty si ya lo está
            if (disk.State == "FAULTY")
            {
                await ShowConfirm("Info", "This disk is already marked as faulty.");
                return;
            }

            // No marcar faulty si el array está reconstruyendo
            if (array.State == "Rebuilding")
            {
                await ShowConfirm("Error", "Cannot mark disk as faulty while array is rebuilding.");
                return;
            }

            // No marcar faulty si es el último disco activo en RAID1
            if (IsRaid1 && array.Disks.Count(d => d.Role == "active") <= 1)
            {
                await ShowConfirm("Error", "Cannot mark the last active disk as faulty in RAID1.");
                return;
            }

            service.MarkDiskAsFaulty(disk.ArrayName, disk.Name);
            await ShowConfirm("Success", $"{disk.Name} marked as faulty.");
            break;

        // --------------------------------------------------------
        // SET AS SPARE
        // --------------------------------------------------------
        case "spare":

            // No convertir en spare si ya está en el array como activo
            if (disk.Role == "active")
            {
                await ShowConfirm("Error", "Active disks cannot be converted to spare.");
                return;
            }

            // No convertir en spare si ya es spare
            if (disk.Role == "spare")
            {
                await ShowConfirm("Info", "This disk is already a spare.");
                return;
            }

            // No convertir en spare si está faulty
            if (disk.State == "FAULTY")
            {
                await ShowConfirm("Error", "Faulty disks cannot be set as spare.");
                return;
            }

            service.SetDiskAsSpare(disk.ArrayName, disk.Name);
            await ShowConfirm("Success", $"{disk.Name} added as spare.");
            break;

        // --------------------------------------------------------
        // REMOVE DISK FROM ARRAY
        // --------------------------------------------------------
        case "remove":

            // Solo se puede remover si está faulty o spare
            if (disk.Role != "faulty" && disk.Role != "spare")
            {
                await ShowConfirm("Error", "Only faulty or spare disks can be removed.");
                return;
            }

            // No remover si el array está reconstruyendo
            if (array.State == "Rebuilding")
            {
                await ShowConfirm("Error", "Cannot remove disk while array is rebuilding.");
                return;
            }

            // No remover si es el último disco activo en RAID1
            if (IsRaid1 && disk.Role == "active" &&
                array.Disks.Count(d => d.Role == "active") <= 1)
            {
                await ShowConfirm("Error", "Cannot remove the last active disk in RAID1.");
                return;
            }

            service.RemoveDiskFromArray(disk.ArrayName, disk.Name);
            await ShowConfirm("Success", $"{disk.Name} removed from array.");
            break;

        // --------------------------------------------------------
        // DEFAULT
        // --------------------------------------------------------
        default:
            await ShowConfirm("Error", "Unknown action.");
            break;
    }

    BuildUI();
}



private Border BuildStatusDot(string smartState)
{
    Color color = smartState switch
    {
        "OK" => Color.FromRgb(0, 200, 0),
        "WARN" => Color.FromRgb(255, 200, 0),
        "FAULTY" => Color.FromRgb(220, 0, 0),
        _ => Color.FromRgb(255, 200, 0)
    };

    var glow = new Border
    {
        Width = 28,
        Height = 28,
        CornerRadius = new CornerRadius(14),
        Background = new SolidColorBrush(color) { Opacity = 0.35 },
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
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
        Height = 28
    };

    container.Children.Add(glow);
    container.Children.Add(dot);

    // ⭐ Restaurar parpadeo original
    if (smartState == "WARN" || smartState == "FAULTY")
    {
        Dispatcher.UIThread.Post(() =>
        {
            AnimateSmartDot(glow, dot, smartState);
        });
    }

    return new Border
    {
        Child = container,
        Background = Brushes.Transparent
    };
}


    
    private void StartMonitoringArray(RaidArrayInfo array, Border cardBorder)

    {
        // Detener monitorización previa si existía
        StopMonitoringArray();

        _monitoringArrayName = array.Name;
        _monitoringBorder = cardBorder;


        // ============================
        // INICIAR MONITORIZACIÓN REAL
        // ============================
        var cfg = ArrayConfigService.Load(array.Name);

        RaidAlertService.StartMonitoring(array.Name, cfg, msg =>
        {
            NotificadorLinux.Enviar(msg, 6000, "critical", "raid-util");
        });

        // ============================
        // PARPADEO EN NARANJA
        // ============================
        _monitorBlinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _monitorBlinkTimer.Tick += (_, _) =>
        {
            if (_monitoringBorder == null)
                return;

            _monitorBlinkState = !_monitorBlinkState;

            _monitoringBorder.BorderBrush = _monitorBlinkState
                ? Brushes.Orange
                : Brushes.Transparent;

            _monitoringBorder.BorderThickness = new Thickness(3);
        };

        _monitorBlinkTimer.Start();
    }

    private void StopMonitoringArray()
    {
        RaidAlertService.StopMonitoring();

        if (_monitorBlinkTimer != null)
        {
            _monitorBlinkTimer.Stop();
            _monitorBlinkTimer = null;
        }

        if (_monitoringBorder != null)
        {
            _monitoringBorder.BorderBrush = Brushes.Transparent;
            _monitoringBorder.BorderThickness = new Thickness(0);
        }

        _monitoringBorder = null;
        _monitoringArrayName = null;
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
            if (string.IsNullOrWhiteSpace(uriString) ||
                !uriString.Contains("avares://"))
            {
                Console.WriteLine($"[RAIDVIEW] LoadImage(): Icono inválido '{uriString}', usando icono por defecto.");
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

            // fallback final
            var fallbackUri = new Uri("avares://RAID-Util/Assets/Icons/disk-hdd.png");
            using var stream = AssetLoader.Open(fallbackUri);

            return new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(stream),
                Width = size,
                Height = size
            };
        }
    }


    private async void ShowArrayDetails(RaidArrayInfo array)
    {
        Console.WriteLine($"[RAIDVIEW] Mostrando detalles de {array.Name}");

        string cmd = $"/usr/sbin/mdadm --detail /dev/{array.Name}";
        string output = await ShellHelper.RunCleanAsync(cmd);

        var dialog = new Window
        {
            Title = $"Array Details — {array.Name}",
            Width = 600,
            Height = 500,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = output,
                    FontFamily = "Consolas",
                    FontSize = 13,
                    Foreground = Brushes.White
                }
            }
        };

        dialog.Show();
    }



    private void OpenArrayConfigWindow()
    {
        if (_selectedArray == null)
        {
            Console.WriteLine("[RAIDVIEW] No array selected → cannot open config window.");
            return;
        }

        Console.WriteLine($"[RAIDVIEW] Opening ArrayConfigWindow for {_selectedArray.Name}");

        var win = new ArrayConfigWindow(_selectedArray.Name);
        win.ShowDialog(GetWindow());
    }


    private async void OnCreateArrayClicked(object? sender, RoutedEventArgs e)
    {
        var parent = GetWindow();
        var service = new RaidService();

        // Obtener discos reales
        var allDisks = await service.GetAllDisksAsync();

        var freeDisks = allDisks
            .Where(d => d.State == "Free")
            .Where(d => !IsDiskMounted(d.Name))
            .ToList();

        if (freeDisks.Count == 0)
        {
            new ConfirmDialog("No disks", "No free disks available to create a RAID array.")
                .ShowDialog(parent);
            return;
        }

        // Diálogo de creación
        var dialog = new CreateArrayDialog(freeDisks);
        var result = await dialog.ShowDialog<CreateArrayResult?>(parent);

        if (result == null)
            return;

        // Mostrar diálogo de carga (UI thread)
        var loading = new LoadingDialog("Creating RAID array...");
        loading.Show(parent);

        await Task.Delay(50); // permite renderizar

        bool ok;

        if (IsFakeMode)
        {
            // Fake mode: solo ejecutar
            ok = await Task.Run(() => { CreateFakeArray(result); return true; });
        }
        else
        {
            // Real mode: CreateRealArray ahora es async
            ok = await CreateRealArray(result);
        }

        loading.Close();

        if (!ok)
        {
            new ConfirmDialog("Error", "Failed to create RAID array.")
                .ShowDialog(parent);
            return;
        }

        // Recargar arrays reales
        await LoadRealData();
    }


    
    
    private bool IsDiskMounted(string diskName)
    {
        try
        {
            string mounts = File.ReadAllText("/proc/mounts");

            // Si el disco tiene particiones, lsblk las mostrará como sdc1, sdc2...
            // pero sdc sin particiones no aparecerá en mounts → OK
            return mounts.Contains($"/dev/{diskName}");
        }
        catch
        {
            return false;
        }
    }

    
  
    private void CreateFakeArray(CreateArrayResult result)
    {
        // Crear un array RAID falso para pruebas
        var fakeArray = new RaidArrayInfo
        {
            Name = result.FriendlyName,
            Level = result.Level,
            State = "Healthy",
            StateIcon = "avares://RAID-Util/Assets/Icons/array-ok.png",

            Disks = result.Disks,

            DiskSummary = $"{result.Disks.Count}× Disk",
            TotalSize = $"{result.Disks.Count * 100} GB",
            UsableSize = EstimateFakeUsableSize(result.Level, result.Disks.Count),
            ParitySize = EstimateFakeParity(result.Level, result.Disks.Count),

            Uptime = "0 min"
        };

        _arrays.Add(fakeArray);

        BuildUI();
    }

    
    private string EstimateFakeUsableSize(string level, int count)
    {
        return level switch
        {
            "RAID1" => "100 GB",
            "RAID5" => $"{(count - 1) * 100} GB",
            "RAID6" => $"{(count - 2) * 100} GB",
            "RAID10" => $"{(count / 2) * 100} GB",
            _ => $"{count * 100} GB"
        };
    }

    private string EstimateFakeParity(string level, int count)
    {
        return level switch
        {
            "RAID5" => "100 GB",
            "RAID6" => "200 GB",
            "RAID10" => $"{(count / 2) * 100} GB (mirrored)",
            _ => "0 GB"
        };
    }

    private async Task<bool> CreateRealArray(CreateArrayResult result)
    {
        var service = new RaidService();

        // 1) Crear array en background
        bool ok = await Task.Run(() =>
            service.CreateArray(result.Level, result.Disks, result.FriendlyName)
        );

        if (!ok)
            return false;

        // 2) Esperar a que /dev/mdX exista realmente
        bool ready = await Task.Run(() =>
            service.WaitForArray(service.LastCreatedMdName)
        );

        if (!ready)
        {
            new ConfirmDialog("Warning",
                    $"Array created, but /dev/{service.LastCreatedMdName} did not appear in time.")
                .ShowDialog(GetWindow());
            return false;
        }

        // 3) Persistir en mdadm.conf
        bool persisted = await Task.Run(() =>
            service.PersistArrayToMdadmConf()
        );

        if (!persisted)
        {
            new ConfirmDialog("Warning",
                    "Array created, but could not update mdadm.conf. Check logs.")
                .ShowDialog(GetWindow());
        }

        // 4) Recargar arrays reales
        await LoadRaidAsync();

        return true;
    }



    private async void OpenFormatArrayDialog(RaidArrayInfo array)
    {
        var dialog = new FormatArrayDialog(array.Name);
        var result = await dialog.ShowDialog<FormatArrayResult?>(GetWindow());

        if (result == null)
            return;

        using (LoadingService.Show("Formatting array..."))
        {
            var service = new RaidService();

            bool ok = service.FormatArray(array.Name, result.Filesystem, result.Label);

            if (!ok)
            {
                new ConfirmDialog("Error", "Failed to format array. Check logs.")
                    .ShowDialog(GetWindow());
                return;
            }

            _ = LoadRaidAsync();
        }
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
        
        BtnDeleteArray.IsEnabled = false;
        BtnConfigArrays.IsEnabled = false;
        BtnInitialize.IsEnabled = false;
        
        
        Console.WriteLine("Refresh button clicked.");
        await LoadRaidAsync();
    }


    private void OnConfigArraysClicked(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Config button clicked.");
        OpenArrayConfigWindow();
    }

  private async Task LoadRaidAsync(bool afterCreate = false)
{
    LogService.Write("[RAIDVIEW] ================= RAID LOAD START =================");
    LogService.Debug("[RAIDVIEW] LoadRaidAsync() ENTER");

    try
    {
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

        using (LoadingService.Show("Loading RAID arrays..."))
        {
            var service = new RaidService();

            // ⭐ Esperar un poco si venimos de crear un array
            if (afterCreate)
                await Task.Delay(150);

            // ⭐ Ejecutar en paralelo
            var arraysTask = service.GetArraysAsync();
            var disksTask = service.GetAllDisksAsync();

            await Task.WhenAll(arraysTask, disksTask);

            var arrays = arraysTask.Result;
            var disks = disksTask.Result;

            if (arrays == null)
            {
                LogService.Error("[RAIDVIEW] GetArraysAsync returned null.");
                return;
            }

            LogService.Debug($"[RAIDVIEW] Arrays returned: {arrays.Count}");
            foreach (var a in arrays)
                LogService.Debug($"[RAIDVIEW] ARRAY → {a.Name} | Level={a.Level} | State={a.State} | Disks={a.Disks.Count}");

            LogService.Debug($"[RAIDVIEW] Disks returned: {disks.Count}");
            foreach (var d in disks)
                LogService.Debug($"[RAIDVIEW] DISK → {d.Name} | Array={d.ArrayName} | Role={d.Role} | State={d.State} | Rota={d.IsRotational}");

            SetArrays(arrays);
            

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
   
   
   private async void OnMoreMenuClick(RaidArrayInfo array, string? action)
{
    var service = new RaidService();

    // ============================================================
    // VALIDACIONES BÁSICAS
    // ============================================================

    if (array == null)
    {
        await ShowConfirm("Error", "Array not found.");
        return;
    }

    if (string.IsNullOrWhiteSpace(array.Level))
    {
        await ShowConfirm("Error", "Invalid RAID level.");
        return;
    }

    if (array.Disks == null || array.Disks.Count == 0)
    {
        await ShowConfirm("Error", "This array has no disks.");
        return;
    }

    // Normalizamos acción
    string act = action?.Trim().ToLowerInvariant() ?? "";

    // No permitir acciones si el array está fallado
    if (array.State == "Failed" && act != "details")
    {
        await ShowConfirm("Error", "This array is in FAILED state. Only details are available.");
        return;
    }

    // ============================================================
    // DETECCIÓN DE NIVELES
    // ============================================================

    string level = array.Level.ToUpperInvariant();

    bool IsRaid0  = level == "RAID0";
    bool IsRaid1  = level == "RAID1";
    bool IsRaid5  = level == "RAID5";
    bool IsRaid6  = level == "RAID6";
    bool IsRaid10 = level == "RAID10";

    // NUEVO: soporte real para LINEAR / JBOD
    bool IsLinear = level.Contains("LINEAR") || level.Contains("JBOD");

    // ============================================================
    // RESTRICCIONES POR TIPO
    // ============================================================

    // RAID0 → solo stop y details
    if (IsRaid0)
    {
        if (act != "stop" && act != "details")
        {
            await ShowConfirm("Error", "RAID0 does not support this action.");
            return;
        }
    }

    // LINEAR/JBOD → solo stop y details
    if (IsLinear)
    {
        if (act != "stop" && act != "details")
        {
            await ShowConfirm("Error", "Linear/JBOD arrays do not support this action.");
            return;
        }
    }

    // ============================================================
    // VALIDACIÓN DE MONTAJE READ-ONLY
    // ============================================================

    bool IsMountedReadOnly = false;

    if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
    {
        var mountInfo = ShellHelper.EjecutarComoRoot($"mount | grep ' {array.MountPath} '");

        if (mountInfo.ExitCode == 0)
        {
            string line = mountInfo.Stdout.ToLowerInvariant();
            if (line.Contains("(ro,") || line.Contains(" ro,"))
                IsMountedReadOnly = true;
        }
    }

    // ============================================================
    // ACCIONES
    // ============================================================

    switch (act)
    {
        case "resync":

            if (IsRaid0 || IsLinear)
            {
                await ShowConfirm("Error", "This RAID level does not support resync.");
                return;
            }

            if (array.State == "Rebuilding")
            {
                await ShowConfirm("Info", "This array is already rebuilding.");
                return;
            }

            if (array.State == "Degraded" &&
                !array.Disks.Any(d => d.Role == "spare"))
            {
                await ShowConfirm("Error", "Cannot rebuild: no spare disk available.");
                return;
            }

            if (IsMountedReadOnly)
            {
                await ShowConfirm("Error", "Array is mounted read-only. Cannot start resync.");
                return;
            }

            await service.StartArrayResyncAsync(array.Name);
            await ShowConfirm("Success", "Resync started.");
            break;

        case "check":

            if (IsRaid0 || IsLinear)
            {
                await ShowConfirm("Error", "This RAID level does not support consistency checks.");
                return;
            }

            if (!array.IsMounted)
            {
                await ShowConfirm("Error", "Array must be mounted to perform a check.");
                return;
            }

            if (array.State == "Rebuilding")
            {
                await ShowConfirm("Error", "Cannot check while rebuilding.");
                return;
            }

            await service.ForceArrayCheckAsync(array.Name);
            await ShowConfirm("Success", "Check started.");
            break;

        case "repair":

            if (IsRaid0 || IsLinear)
            {
                await ShowConfirm("Error", "This RAID level does not support repair.");
                return;
            }

            if (array.State != "Degraded")
            {
                await ShowConfirm("Error", "Repair is only available for degraded arrays.");
                return;
            }

            if (!array.Disks.Any(d => d.Role == "spare"))
            {
                await ShowConfirm("Error", "No spare disk available for repair.");
                return;
            }

            if (array.State == "Rebuilding")
            {
                await ShowConfirm("Error", "Cannot repair while rebuilding.");
                return;
            }

            await service.ForceArrayRepairAsync(array.Name);
            await ShowConfirm("Success", "Repair started.");
            break;

        case "stop":

            // 1) Si está montado → desmontar
            if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
            {
                var um = ShellHelper.EjecutarComoRoot($"umount -f \"{array.MountPath}\"");

                if (um.ExitCode != 0)
                {
                    await ShowConfirm("Error",
                        $"Failed to unmount array.\n\n{um.Stdout}\n{um.Stderr}");
                    return;
                }
            }

            // 2) Intentar detener
            var result = await service.StopArrayAsync(array.Name);

            if (result.ExitCode != 0)
            {
                await ShowConfirm("Error",
                    $"Failed to stop array.\n\n{result.Stdout}\n{result.Stderr}");
                return;
            }

            await ShowConfirm("Success", "Array stopped.");
            await LoadRaidAsync();
            
            break;




        case "details":
        {
            string detail = await service.GetArrayDetailsAsync(array.Name);

            var dlg = new ConsoleDialog($"Array Details — {array.Name}", detail);
            var owner = this.GetVisualRoot() as Window;

            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                await dlg.ShowDialog(new Window());

            break;
        }

        default:
            await ShowConfirm("Error", $"Unknown action '{action}'.");
            break;
    }

    
}


   
  
    
   private async void StartArrayResync(RaidArrayInfo array)
   {
       Console.WriteLine($"[RAIDVIEW] Iniciando resync en {array.Name}");

       string cmd = $"/usr/sbin/mdadm --readwrite /dev/{array.Name}";
       var result = ShellHelper.EjecutarComoRoot(cmd);

       if (result.ExitCode != 0)
           Console.WriteLine($"[RAIDVIEW] ERROR resync: {result.Stderr}");
       else
           Console.WriteLine($"[RAIDVIEW] Resync iniciado correctamente");

       BuildUI();
   }


   private async void ForceArrayCheck(RaidArrayInfo array)
   {
       Console.WriteLine($"[RAIDVIEW] Ejecutando check en {array.Name}");

       string cmd = $"/usr/sbin/mdadm --action=check /dev/{array.Name}";
       var result = ShellHelper.EjecutarComoRoot(cmd);

       if (result.ExitCode != 0)
           Console.WriteLine($"[RAIDVIEW] ERROR check: {result.Stderr}");
       else
           Console.WriteLine($"[RAIDVIEW] Check iniciado correctamente");

       BuildUI();
   }

   private async void ForceArrayRepair(RaidArrayInfo array)
   {
       Console.WriteLine($"[RAIDVIEW] Ejecutando repair en {array.Name}");

       string cmd = $"/usr/sbin/mdadm --action=repair /dev/{array.Name}";
       var result = ShellHelper.EjecutarComoRoot(cmd);

       if (result.ExitCode != 0)
           Console.WriteLine($"[RAIDVIEW] ERROR repair: {result.Stderr}");
       else
           Console.WriteLine($"[RAIDVIEW] Repair iniciado correctamente");

       BuildUI();
   }

   

   private async void StopArray(RaidArrayInfo array)
   {
       Console.WriteLine($"[RAIDVIEW] Deteniendo array {array.Name}");

       string cmd = $"/usr/sbin/mdadm --stop /dev/{array.Name}";
       var result = ShellHelper.EjecutarComoRoot(cmd);

       if (result.ExitCode != 0)
           Console.WriteLine($"[RAIDVIEW] ERROR stop: {result.Stderr}");
       else
           Console.WriteLine($"[RAIDVIEW] Array detenido correctamente");

       BuildUI();
   }


  

   
   private List<RaidDiskInfo> GetSafeDisks()
   {
       return _arrays
           .SelectMany(a => a.Disks)
           .Where(d =>
                   !d.IsSystemDisk &&          // marcado por el backend
                   !d.IsMounted &&             // no montado
                   !d.IsBoot &&                // no contiene /boot o EFI
                   !d.IsRoot &&                // no contiene /
                   !d.IsHome &&                // no contiene /home
                   !d.IsSwap &&                // no contiene swap
                   !d.IsUsedByRaid &&          // no pertenece a otro array
                   !d.IsUsbSystemSource        // si RAID-util corre desde USB
           )
           .ToList();
   }

   
   
}//Fin de Clase
