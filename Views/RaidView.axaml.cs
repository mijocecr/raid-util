using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    // ⭐ Flag para forzar fake data si quieres probar la UI sin backend
    private const bool FORCE_FAKE_DATA = false;


    // ⭐ Constantes optimizadas para tarjetas de disco
    private static readonly Thickness DiskCardPadding = new(12);
    private static readonly Thickness DiskCardMargin = new(0, 0, 0, 10);
    private static readonly CornerRadius DiskCardRadius = new(8);

// ⭐ Menú contextual reutilizable para discos
    private static readonly ContextMenu DiskMenu = new()
    {
        Items =
        {
            new MenuItem { Header = "SMART Info", Tag = "smart" },
            new MenuItem { Header = "Mark as Faulty", Tag = "faulty" },
            new MenuItem { Header = "Set as Spare", Tag = "spare" },
            new MenuItem { Header = "Remove from Array", Tag = "remove" }
        }
    };


    // ⭐ Constantes optimizadas (no cambian nada visual)
    private static readonly Thickness CardPadding = new(12);
    private static readonly Thickness CardMargin = new(0, 0, 0, 8);
    private static readonly CornerRadius CardRadius = new(10);
    private static readonly CornerRadius GlowRadius = new(14);


    // ⭐ Constantes optimizadas (no cambian nada visual)
    private static readonly Thickness ExpandedPadding = new(16);
    private static readonly Thickness ExpandedMargin = new(0, 0, 0, 16);
    private static readonly CornerRadius ExpandedRadius = new(10);


// ⭐ Menú contextual reutilizable (antes se creaba uno por tarjeta)
    private static readonly ContextMenu ArrayMenu = new()
    {
        Items =
        {
            new MenuItem { Header = "Start resync/rebuild", Tag = "resync" },
            new MenuItem { Header = "Force check", Tag = "check" },
            new MenuItem { Header = "Force repair", Tag = "repair" },
            new MenuItem { Header = "Stop array", Tag = "stop" },
            new MenuItem { Header = "Details", Tag = "details" },
            new MenuItem { Header = "Add disk to array", Tag = "add_disk" }
        }
    };


    // ⭐ Preparado para cache de iconos (se activará en Parte 2)
    private static readonly Dictionary<string, IImage> _iconCache = new();
    private List<RaidArrayInfo> _arrays = new();
    private RaidArrayInfo? _currentArray;


    // Variables globales necesarias:
    private RaidDiskInfo? _currentDisk;
    private bool _monitorBlinkState;

    private DispatcherTimer? _monitorBlinkTimer;
    private string? _monitoringArrayName;
    private Border? _monitoringBorder;


    private RaidArrayInfo? _selectedArray;

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

        foreach (var item in DiskMenu.Items.OfType<MenuItem>())
            item.Click += OnDiskMenuItemClick;


        Console.WriteLine("[RAIDVIEW] Constructor RaidView ejecutado.");

        if (FORCE_FAKE_DATA)
        {
            Console.WriteLine("[RAIDVIEW] FORCE_FAKE_DATA = true → cargando datos falsos.");
            LoadFakeData();
        }

        Console.WriteLine("[RAIDVIEW] Modo real: esperando datos desde MainWindow.");
        // En modo real, los datos llegan vía MainWindow.RaidViewControl.SetArrays(...)
        // No llamamos a LoadRealData() aquí para no duplicar llamadas a sudo.
    }


    public bool IsFakeMode => FORCE_FAKE_DATA;


    private async void OnAssembleArraysClicked(object? sender, RoutedEventArgs e)
    {
        var service = new RaidService();
        var parent = GetWindow();

        // 1) Mostrar diálogo de carga
        var loading = new LoadingDialog("Assembling arrays...");
        loading.Show(parent);

        await Task.Delay(50); // Permite renderizar el diálogo

        // 2) Ejecutar AutoAssemble en segundo plano
        var ok = await Task.Run(() => service.AutoAssemble());

        // 3) Cerrar diálogo
        loading.Close();

        // 4) Mostrar resultado
        if (!ok)
        {
            await ShowInfo("Error", "Could not assemble stopped arrays.");
            return;
        }

        await ShowInfo("Success", "Arrays assembled correctly.");

        // 5) Refrescar arrays
        BtnRefreshArrays.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }


    private async void OnInitializeClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedArray == null)
        {
            await ShowInfo("Error", "Select an array first.");
            return;
        }

        var array = _selectedArray;

        // 1) Diálogo BMW para elegir FS y label
        var dlg = new FormatArrayDialog(array.Name);
        var owner = this.GetVisualRoot() as Window;
        var result = await dlg.ShowDialog<FormatArrayResult?>(owner);

        if (result == null)
            return;

        // 2) Mostrar loading dialog
        var loading = new LoadingDialog($"Initializing {array.Name}...");
        loading.Show(owner);

        var ok = false;

        try
        {
            // 3) Ejecutar la operación pesada en segundo plano
            ok = await Task.Run(async () =>
            {
                var service = new RaidService();
                return await service.InitializeArrayAsync(
                    array.Name,
                    result.Filesystem,
                    result.Label
                );
            });
        }
        finally
        {
            // 4) Cerrar loading dialog
            loading.Close();
        }

        // 5) Mostrar resultado
        if (!ok)
        {
            await ShowInfo("Error", "Could not initialize the array.");
            return;
        }

        await ShowInfo("Success", $"The array {array.Name} was initialized.");

        // 6) Refrescar UI
        BtnRefreshArrays.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }


    private async Task<bool> ShowConfirm(string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);

        var owner = this.GetVisualRoot() as Window;
        if (owner != null)
            return await dlg.ShowDialog<bool>(owner);

        // fallback
        return await dlg.ShowDialog<bool>(new Window());
    }

    private async Task<bool> ShowInfo(string title, string message)
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
        _arrays = arrays ?? new List<RaidArrayInfo>();

        foreach (var a in _arrays)
        {
            a.StateIcon ??= "avares://RAID-Util/Assets/Icons/array-caution.png";
            a.TotalSize ??= "Unknown";
            a.UsableSize ??= a.TotalSize;
            a.ParitySize ??= "N/A";
            a.DiskSummary ??= $"{a.Disks?.Count ?? 0}× Disk";
            a.Uptime ??= "Unknown";

            if (a.Disks == null)
                a.Disks = new List<RaidDiskInfo>();

            foreach (var d in a.Disks)
                if (string.IsNullOrWhiteSpace(d.Icon) || !d.Icon.Contains("avares://"))
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

        BuildUI();
    }


    private void LoadFakeData()
    {
        Console.WriteLine("[RAIDVIEW] LoadFakeData() ejecutado.");

        _arrays = new List<RaidArrayInfo>
        {
            new()
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
                    new()
                    {
                        Name = "sda1",
                        Model = "Samsung SSD 860 EVO",
                        Size = "500G",
                        Role = "active",
                        State = "OK",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-ssd.png",
                        ArrayName = "md0"
                    },
                    new()
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

            new()
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
                    new()
                    {
                        Name = "sdc1",
                        Model = "WD Blue 1TB",
                        Size = "1T",
                        Role = "active",
                        State = "OK",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                        ArrayName = "md1"
                    },
                    new()
                    {
                        Name = "sdd1",
                        Model = "WD Blue 1TB",
                        Size = "1T",
                        Role = "active",
                        State = "OK",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                        ArrayName = "md1"
                    },
                    new()
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

            new()
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
                    new()
                    {
                        Name = "nvme0n1p1",
                        Model = "Samsung 980 PRO",
                        Size = "1T",
                        Role = "rebuilding",
                        State = "WARN",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-nvme.png",
                        ArrayName = "md2"
                    },
                    new()
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
        var isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;

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

        // Si no hay arrays, limpiar y salir
        if (_arrays == null || _arrays.Count == 0)
        {
            ListArrays.Children.Clear();
            Console.WriteLine("[RAIDVIEW] No hay arrays, UI vacía.");
            return;
        }

        Console.WriteLine($"[RAIDVIEW] _arrays.Count = {_arrays.Count}");

        // Limpiar contenedor
        ListArrays.Children.Clear();

        // Dibujar arrays
        foreach (var array in _arrays)
        {
            Console.WriteLine($"[RAIDVIEW] Dibujando array {array.Name} con {array.Disks?.Count ?? 0} discos.");

            // Tarjeta principal
            var card = BuildArrayCard(array);
            ListArrays.Children.Add(card);

            // Tarjeta expandida
            if (array.IsExpanded)
            {
                var expanded = BuildExpandedCard(array);
                ListArrays.Children.Add(expanded);
            }
        }

        // ⭐ Restaurar parpadeo si había un array en monitoreo
        if (_monitoringArrayName != null)
            foreach (var border in ListArrays.Children.OfType<Border>())
                // border.Child es el cardBorder
                if (border.Child is Border cardBorder)
                    // cardBorder.Child es el Grid overlay
                    if (cardBorder.Child is Grid overlay)
                        // Buscar TextBlocks dentro del overlay
                        foreach (var tb in overlay.GetVisualDescendants().OfType<TextBlock>())
                            if (tb.Text != null && tb.Text.StartsWith(_monitoringArrayName))
                            {
                                _monitoringBorder = cardBorder;
                                RestartBlinking();
                                goto END_MONITOR_SEARCH;
                            }

        END_MONITOR_SEARCH:

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

    private Border BuildArrayCard(RaidArrayInfo array)
    {
        // ⭐ Icono (cacheado automáticamente)
        var icon = LoadImage(array.StateIcon, 150);
        icon.Margin = new Thickness(4);
        icon.VerticalAlignment = VerticalAlignment.Center;

        // ⭐ Nombre + nivel
        var name = new TextBlock
        {
            Text = $"{array.Name} ({array.Level})",
            FontSize = 22,
            Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        // ⭐ Panel de información
        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(name);

        var dimBrush = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!;

        info.Children.Add(new TextBlock { Text = $"State: {array.State}", FontSize = 14, Foreground = dimBrush });
        info.Children.Add(new TextBlock
        {
            Text =
                $"Disks: {array.Disks.Count} ({array.Disks.Count(d => d.State == "OK")} OK, {array.Disks.Count(d => d.State == "FAULTY")} Faulty)",
            FontSize = 14,
            Foreground = dimBrush
        });
        info.Children.Add(new TextBlock { Text = $"Size: {array.TotalSize}", FontSize = 14, Foreground = dimBrush });
        info.Children.Add(new TextBlock { Text = $"Path: {array.Path}", FontSize = 14, Foreground = dimBrush });
        info.Children.Add(new TextBlock
        {
            Text = $"Persist Mount: {(array.PersistMount ? "YES" : "NO")}  Parity: {array.ParitySize}",
            FontSize = 14,
            Foreground = dimBrush
        });

        if (array.RebuildProgress > 0)
            info.Children.Add(new TextBlock
            {
                Text = $"Rebuild: {array.RebuildProgress}% (ETA {array.RebuildETA})",
                FontSize = 14,
                Foreground = dimBrush
            });

        // ⭐ Grid principal (icono + info)
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

        // ⭐ Overlay (checkbox + botón More)
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

        // ⭐ Checkbox de selección
        var chkSelect = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsChecked = _selectedArray == array
        };

        chkSelect.Checked += (_, _) =>
        {
            _selectedArray = array;
            ClearOtherSelections(array);
            BtnDeleteArray.IsEnabled = true;
            BtnConfigArrays.IsEnabled = true;
            BtnInitialize.IsEnabled = true;
        };

        chkSelect.Unchecked += (_, _) =>
        {
            if (_selectedArray == array)
                _selectedArray = null;

            BtnDeleteArray.IsEnabled = false;
            BtnConfigArrays.IsEnabled = false;
            BtnInitialize.IsEnabled = false;
        };

        topRightPanel.Children.Add(chkSelect);

        // ⭐ Botón More
        var btnMore = new Button
        {
            Content = "More",
            Classes = { "MoreButton" },
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        btnMore.Click += (_, _) =>
        {
            ArrayMenu.PlacementTarget = btnMore;
            ArrayMenu.Open(btnMore);
        };

        foreach (var item in ArrayMenu.Items.OfType<MenuItem>())
            item.Click += (_, _) => OnMoreMenuClick(array, item.Tag?.ToString());

        topRightPanel.Children.Add(btnMore);

        overlay.Children.Add(topRightPanel);
        Grid.SetRow(topRightPanel, 0);

        overlay.Children.Add(grid);
        Grid.SetRow(grid, 1);

        // ⭐ Glow + tarjeta
        var glowColor = GetArrayGlowColor(array.State);
        var glowBrush = new SolidColorBrush(glowColor) { Opacity = 0.35 };

        var glowBorder = new Border
        {
            Background = glowBrush,
            CornerRadius = GlowRadius,
            Padding = new Thickness(0),
            Margin = CardMargin
        };

        var cardBorder = new Border
        {
            Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
            CornerRadius = CardRadius,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = CardPadding,
            Child = overlay
        };

        glowBorder.Child = cardBorder;

        // ⭐ Animación
        AnimateArrayGlow(glowBorder, glowBrush);

        // ⭐ EXPANDIR / COLAPSAR — VERSIÓN FUNCIONAL
        cardBorder.PointerPressed += (_, _) =>
        {
            array.IsExpanded = !array.IsExpanded;

            var parent = ListArrays;
            var index = parent.Children.IndexOf(glowBorder);

            if (array.IsExpanded)
            {
                NotificadorLinux.Enviar($"Monitorizing: {array.Name}\n Started");
                StartMonitoringArray(array, cardBorder);

                var expanded = BuildExpandedCard(array);
                expanded.Tag = $"expanded:{array.Name}";
                parent.Children.Insert(index + 1, expanded);
            }
            else
            {
                StopMonitoringArray();
                NotificadorLinux.Enviar($"Monitorizing: {array.Name}\n Stopped");

                foreach (var child in parent.Children.ToList())
                    if (child is Border b &&
                        b.Tag is string tag &&
                        tag == $"expanded:{array.Name}")
                    {
                        parent.Children.Remove(b);
                        break;
                    }
            }
        };

        return glowBorder;
    }


// ⭐ Selección única

    private void ClearOtherSelections(RaidArrayInfo selected)
    {
        foreach (var arr in _arrays)
            if (arr != selected)
                arr.IsSelected = false;

        BtnDeleteArray.IsEnabled = _selectedArray != null;
    }

    private Border BuildExpandedCard(RaidArrayInfo array)
    {
        // ⭐ Panel principal
        var panel = new StackPanel { Spacing = 14 };

        var textBrush = (IBrush)Application.Current!.FindResource("BMWTextBrush")!;
        var accentBrush = (IBrush)Application.Current!.FindResource("BMWAccentBrush")!;

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
            Foreground = textBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Estado
        panel.Children.Add(new TextBlock
        {
            Text = $"State: {array.State}",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontSize = 13,
            Foreground = accentBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // ============================================================
        // 2) OPCIONES DE MONTAJE
        // ============================================================
        panel.Children.Add(BuildMountOptions(array));

        // ============================================================
        // 3) TARJETAS DE DISCOS
        // ============================================================
        foreach (var disk in array.Disks)
            panel.Children.Add(BuildDiskCard(array, disk));

        // ============================================================
        // 4) TARJETA FINAL (optimizada)
        // ============================================================
        return new Border
        {
            Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
            CornerRadius = ExpandedRadius, // ⭐ antes: new CornerRadius(10)
            Padding = ExpandedPadding, // ⭐ antes: new Thickness(16)
            Margin = ExpandedMargin, // ⭐ antes: new Thickness(0,0,0,16)
            Child = panel
        };
    }


    private Control BuildMountOptions(RaidArrayInfo array)
    {
        var cfg = ArrayConfigService.Load(array.Name);

        var mountPoint = string.IsNullOrWhiteSpace(cfg.MountPoint)
            ? $"/mnt/{array.Name}"
            : cfg.MountPoint;

        var isMounted = MountService.IsMounted(mountPoint);

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

                var mountOpts = opts.Count == 0
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

        if (state == "FAULTY")
            // ⭐ Patrón SOS (rojo)
            // · · · — — — · · ·
            while (true)
            {
                // 3 cortos
                for (var i = 0; i < 3; i++)
                {
                    glow.Opacity = 0.20;
                    dot.Opacity = 0.20;
                    await Task.Delay(150);

                    glow.Opacity = 0.80;
                    dot.Opacity = 0.80;
                    await Task.Delay(150);
                }

                // 3 largos
                for (var i = 0; i < 3; i++)
                {
                    glow.Opacity = 0.20;
                    dot.Opacity = 0.20;
                    await Task.Delay(300);

                    glow.Opacity = 0.80;
                    dot.Opacity = 0.80;
                    await Task.Delay(300);
                }

                // 3 cortos
                for (var i = 0; i < 3; i++)
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

// Handler global (fuera de BuildDiskCard)
    private async void OnDiskMenuItemClick(object? sender, EventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag && _currentDisk != null && _currentArray != null)
            await OnDiskMenuClick(_currentArray, _currentDisk, tag);
    }

    private Border BuildDiskCard(RaidArrayInfo array, RaidDiskInfo disk)
    {
        Console.WriteLine($"[RAIDVIEW]   BuildDiskCard() para {disk.Name}, Icon={disk.Icon}");

        // ⭐ Icono cacheado
        var icon = LoadImage(disk.Icon, 72);
        icon.Margin = new Thickness(2);

        // ⭐ Brushes reutilizados
        var textBrush = (IBrush)Application.Current!.FindResource("BMWTextBrush")!;
        var dimBrush = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!;

        // ⭐ Nombre
        var name = new TextBlock
        {
            Text = disk.Name,
            FontSize = 17,
            Foreground = textBrush
        };

        // ⭐ Modelo
        var model = new TextBlock
        {
            Text = $"Model: {disk.Model}",
            FontSize = 14,
            Foreground = dimBrush
        };

        // ⭐ Tamaño
        var size = new TextBlock
        {
            Text = $"Size: {disk.Size}",
            FontSize = 14,
            Foreground = dimBrush
        };

        // ⭐ Rol RAID
        var role = new TextBlock
        {
            Text = $"RAID Role: {disk.Role}",
            FontSize = 14,
            Foreground = dimBrush
        };

        // ⭐ Estado SMART
        var smart = new TextBlock
        {
            Text = $"SMART: {disk.State}",
            FontSize = 14,
            Foreground = dimBrush
        };

        // ⭐ Stack de texto
        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { name, model, size, role, smart }
        };

        // ⭐ LED SMART
        var statusDot = BuildStatusDot(disk.State);

        // ⭐ Botón More
        var manageButton = new Button
        {
            Content = "More",
            Width = 80,
            Height = 32,
            Margin = new Thickness(10),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Classes = { "IconButton" }
        };

        // ⭐ Crear menú contextual por-disco (NO estático)
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "SMART Info", Tag = "smart" },
                new MenuItem { Header = "Mark as Faulty", Tag = "faulty" },
                new MenuItem { Header = "Set as Spare", Tag = "spare" },
                new MenuItem { Header = "Remove from Array", Tag = "remove" }
            }
        };

        // ⭐ Handlers DIRECTOS (sin variables globales)
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.Click += async (_, _) => { await OnDiskMenuClick(array, disk, item.Tag?.ToString()); };

        // ⭐ Abrir menú contextual
        manageButton.Click += (_, _) =>
        {
            menu.PlacementTarget = manageButton;
            menu.Open(manageButton);
        };

        // ⭐ Grid principal
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

        // ⭐ Tarjeta final optimizada
        return new Border
        {
            Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
            BorderBrush = (IBrush)Application.Current!.FindResource("BMWBorderBrush")!,
            BorderThickness = new Thickness(1),
            CornerRadius = DiskCardRadius,
            Padding = DiskCardPadding,
            Margin = DiskCardMargin,
            Child = grid
        };
    }


    private async Task OnDiskMenuClick(RaidArrayInfo array, RaidDiskInfo disk, string? action)
    {
        switch (action)
        {
            case "smart":
            {
                var result = ShellHelper.EjecutarComoRoot($"smartctl -a /dev/{disk.Name}");
                var info = result.Stdout + "\n" + result.Stderr;

                var dlg = new ConsoleDialog($"SMART — {disk.Name}", info);
                var owner = this.GetVisualRoot() as Window;
                await dlg.ShowDialog(owner ?? new Window());
                break;
            }

            case "faulty":
            {
                ShellHelper.EjecutarComoRoot($"mdadm /dev/{array.Name} --fail /dev/{disk.Name}");
                await LoadRaidAsync();
                break;
            }

            case "spare":
            {
                ShellHelper.EjecutarComoRoot($"mdadm /dev/{array.Name} --set-spare /dev/{disk.Name}");
                await LoadRaidAsync();
                break;
            }

            case "remove":
            {
                await RemoveDiskFromArrayUI(array, disk.Name);
                break;
            }
        }
    }


    private Border BuildStatusDot(string smartState)
    {
        var color = smartState switch
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
            Dispatcher.UIThread.Post(() => { AnimateSmartDot(glow, dot, smartState); });

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

        RaidAlertService.StartMonitoring(array.Name, cfg, msg => { NotificadorLinux.Enviar(msg, 6000, "critical"); });

        // ============================
        // PARPADEO EN NARANJA
        // ============================
        _monitorBlinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
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
            for (var i = 0; i < 3; i++)
            {
                b1.Opacity = 1;
                b2.Opacity = 0.35;
                await Task.Delay(200);
                b1.Opacity = 0.2;
                b2.Opacity = 0.1;
                await Task.Delay(200);
            }

            for (var i = 0; i < 3; i++)
            {
                b1.Opacity = 1;
                b2.Opacity = 0.35;
                await Task.Delay(600);
                b1.Opacity = 0.2;
                b2.Opacity = 0.1;
                await Task.Delay(600);
            }

            for (var i = 0; i < 3; i++)
            {
                b1.Opacity = 1;
                b2.Opacity = 0.35;
                await Task.Delay(200);
                b1.Opacity = 0.2;
                b2.Opacity = 0.1;
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
                uriString = "avares://RAID-Util/Assets/Icons/disk-hdd.png";

            // ⭐ CACHE ACTIVADA
            if (!_iconCache.TryGetValue(uriString, out var cached))
            {
                var uri = new Uri(uriString);
                using var stream = AssetLoader.Open(uri);

                cached = new Bitmap(stream);
                _iconCache[uriString] = cached;
            }

            return new Image
            {
                Source = cached,
                Width = size,
                Height = size
            };
        }
        catch
        {
            // fallback
            const string fallback = "avares://RAID-Util/Assets/Icons/disk-hdd.png";

            if (!_iconCache.TryGetValue(fallback, out var cached))
            {
                var uri = new Uri(fallback);
                using var stream = AssetLoader.Open(uri);

                cached = new Bitmap(stream);
                _iconCache[fallback] = cached;
            }

            return new Image
            {
                Source = cached,
                Width = size,
                Height = size
            };
        }
    }


    private async void ShowArrayDetails(RaidArrayInfo array)
    {
        Console.WriteLine($"[RAIDVIEW] Mostrando detalles de {array.Name}");

        var cmd = $"/usr/sbin/mdadm --detail /dev/{array.Name}";
        var output = await ShellHelper.RunCleanAsync(cmd);

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

        // 1) Obtener todos los discos detectados por el sistema
        var allDisks = await service.GetAllDisksAsync();

        // 2) Obtener nodos completos (discos + particiones)
        var nodes = RaidService.Nodes;

        // 3) FILTRO UNIVERSAL (sin excepciones, sin quemar información)
        var freeDisks = allDisks
            .Where(d => !d.IsUsedByRaid) // no pertenece a RAID
            .Where(d => !d.HasRaidMetadata) // no tiene metadata RAID
            .Where(d => !d.IsMounted) // disco no montado
            .Where(d => d.Children.All(child =>
            {
                // Buscar partición en nodes, no en allDisks
                if (!nodes.TryGetValue(child, out var part))
                    return true; // si no existe, no bloquea

                string mp = part.mountpoint ?? "";
                return string.IsNullOrWhiteSpace(mp);
            }))
            .ToList();

        LogService.Write($"[CREATE ARRAY] Discos elegibles: {freeDisks.Count}");

        // 4) Si no hay discos libres → mensaje
        if (freeDisks.Count == 0)
        {
            new ConfirmDialog("No disks", "No free disks available to create a RAID array.")
                .ShowDialog(parent);
            return;
        }

        // 5) Mostrar diálogo de creación
        var dialog = new CreateArrayDialog(freeDisks);
        var result = await dialog.ShowDialog<CreateArrayResult?>(parent);

        if (result == null)
            return;

        // 6) Mostrar diálogo de carga
        var loading = new LoadingDialog("Creating RAID array...");
        loading.Show(parent);

        await Task.Delay(50);

        bool ok;

        // 7) Crear array real o fake
        if (IsFakeMode)
            ok = await Task.Run(() =>
            {
                CreateFakeArray(result);
                return true;
            });
        else
            ok = await CreateRealArray(result);

        loading.Close();

        // 8) Error al crear
        if (!ok)
        {
            new ConfirmDialog("Error", "Failed to create RAID array.")
                .ShowDialog(parent);
            return;
        }

        // 9) Recargar datos reales
        await LoadRaidAsync();
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
            "RAID10" => $"{count / 2 * 100} GB",
            _ => $"{count * 100} GB"
        };
    }

    private string EstimateFakeParity(string level, int count)
    {
        return level switch
        {
            "RAID5" => "100 GB",
            "RAID6" => "200 GB",
            "RAID10" => $"{count / 2 * 100} GB (mirrored)",
            _ => "0 GB"
        };
    }

    private async Task<bool> CreateRealArray(CreateArrayResult result)
    {
        var service = new RaidService();

        // 1) Crear array en background
        var ok = await Task.Run(() =>
            service.CreateArray(result.Level, result.Disks, result.FriendlyName)
        );

        if (!ok)
            return false;

        // 2) Esperar a que /dev/mdX exista realmente
        var ready = await Task.Run(() =>
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
        var persisted = await Task.Run(() =>
            service.PersistArrayToMdadmConf()
        );

        if (!persisted)
            new ConfirmDialog("Warning",
                    "Array created, but could not update mdadm.conf. Check logs.")
                .ShowDialog(GetWindow());

        // 4) Recargar arrays reales
        await LoadRaidAsync();

        return true;
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
            var ok = await Task.Run(() => service.DeleteArrayAsync(array));

            if (!ok)
            {
                Console.WriteLine("[DELETE] ERROR deleting array.");
                return;
            }
        }

        // Actualizar UI
        _arrays.Remove(array);
        _selectedArray = null;
       await LoadRaidAsync();
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

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop1)
                {
                    var main1 = desktop1.MainWindow as MainWindow;
                    main1?.UpdateStatus("Fake RAID data loaded.");
                }

                LogService.Write("[RAIDVIEW] ================= RAID LOAD END (FAKE) =================");
                return;
            }

            using (LoadingService.Show("Loading RAID arrays..."))
            {
                // ⭐ EJECUTAR EL TRABAJO PESADO EN BACKGROUND
                var result = await Task.Run(async () =>
                {
                    var service = new RaidService();

                    if (afterCreate)
                        await Task.Delay(150);

                    var arraysTask = service.GetArraysAsync();
                    var disksTask = service.GetAllDisksAsync();

                    await Task.WhenAll(arraysTask, disksTask);

                    return (arraysTask.Result, disksTask.Result);
                });

                var arrays = result.Item1 ?? new List<RaidArrayInfo>();
                var disks = result.Item2 ?? new List<RaidDiskInfo>();

                LogService.Debug($"[RAIDVIEW] Arrays returned: {arrays.Count}");
                foreach (var a in arrays)
                    LogService.Debug(
                        $"[RAIDVIEW] ARRAY → {a.Name} | Level={a.Level} | State={a.State} | Disks={a.Disks.Count}");

                LogService.Debug($"[RAIDVIEW] Disks returned: {disks.Count}");
                foreach (var d in disks)
                    LogService.Debug(
                        $"[RAIDVIEW] DISK → {d.Name} | Array={d.ArrayName} | Role={d.Role} | State={d.State} | Rota={d.IsRotational}");

                // ⭐ Asignar discos a arrays SOLO si vienen vacíos
                foreach (var array in arrays)
                    if (array.Disks == null || array.Disks.Count == 0)
                        array.Disks = disks
                            .Where(d => d.ArrayName == array.Name)
                            .ToList();

                // ⭐ Actualizar UI (solo en hilo UI)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SetArrays(arrays);

                    if (afterCreate && arrays.Count > 0)
                    {
                        var last = arrays.Last();
                        last.IsExpanded = true;
                        BuildUI();
                    }

                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var main = desktop.MainWindow as MainWindow;
                        main?.UpdateStatus("RAID information refreshed.");
                    }
                });
            }

            LogService.Write("[RAIDVIEW] ================= RAID LOAD END =================");
        }
        catch (Exception ex)
        {
            LogService.Error("[RAIDVIEW] LoadRaidAsync() EXCEPTION:");
            LogService.Error(ex.ToString());

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
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
        var act = action?.Trim().ToLowerInvariant() ?? "";

        // No permitir acciones si el array está fallado
        if (array.State == "Failed" && act != "details")
        {
            await ShowConfirm("Error", "This array is in FAILED state. Only details are available.");
            return;
        }

        // ============================================================
        // DETECCIÓN DE NIVELES
        // ============================================================

        var level = array.Level.ToUpperInvariant();

        var IsRaid0 = level == "RAID0";
        var IsRaid1 = level == "RAID1";
        var IsRaid5 = level == "RAID5";
        var IsRaid6 = level == "RAID6";
        var IsRaid10 = level == "RAID10";

        // NUEVO: soporte real para LINEAR / JBOD
        var IsLinear = level.Contains("LINEAR") || level.Contains("JBOD");

        // ============================================================
        // RESTRICCIONES POR TIPO
        // ============================================================

        // RAID0 → solo stop y details
        if (IsRaid0)
            if (act != "stop" && act != "details")
            {
                await ShowConfirm("Error", "RAID0 does not support this action.");
                return;
            }

        // LINEAR/JBOD → solo stop y details
        if (IsLinear)
            if (act != "stop" && act != "details")
            {
                await ShowConfirm("Error", "Linear/JBOD arrays do not support this action.");
                return;
            }

        // ============================================================
        // VALIDACIÓN DE MONTAJE READ-ONLY
        // ============================================================

        var IsMountedReadOnly = false;

        if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
        {
            var mountInfo = ShellHelper.EjecutarComoRoot($"mount | grep ' {array.MountPath} '");

            if (mountInfo.ExitCode == 0)
            {
                var line = mountInfo.Stdout.ToLowerInvariant();
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

                var (ok, msg) = await service.StopArraySafeAsync(array.Name);

                if (!ok)
                {
                    await ShowConfirm("Error", msg);
                    return;
                }

                await ShowConfirm("Success", msg);
                await LoadRaidAsync();
                break;


            case "add_disk":
                await AddDiskToArrayUI(array);
                break;


            case "details":
            {
                var detail = await service.GetArrayDetailsAsync(array.Name);

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

    private async Task RemoveDiskFromArrayUI(RaidArrayInfo array, string diskName)
    {
        var owner = this.GetVisualRoot() as Window;

        var confirm = new InfoDialog(
            "Remove Disk",
            $"Are you sure you want to remove /dev/{diskName} from {array.Name}?"
        );

        var ok = await confirm.ShowDialog<bool>(owner ?? new Window());
        if (!ok)
            return;

        var loading = new LoadingDialog($"Removing /dev/{diskName}...");
        loading.Show(owner);

        try
        {
            var service = new RaidService();
            var result = await service.RemoveDiskFromArrayAsync(array.Name, diskName);

            if (!result)
            {
                await ShowConfirm("Error",
                    $"Could not remove /dev/{diskName} from {array.Name}.\nCheck logs for details.");
                return;
            }
        }
        finally
        {
            loading.Close();
        }

        await LoadRaidAsync();

        NotificadorLinux.Enviar(
            $"Disk removed:\n/dev/{diskName} removed from {array.Name}"
        );
    }


    private async Task AddDiskToArrayUI(RaidArrayInfo array)
    {
        LogService.Write($"[RAIDVIEW] AddDiskToArrayUI → {array.Name}");

        var service = new RaidService();

        var allDisks = await service.GetAllDisksAsync();

        var candidates = allDisks
            .Where(d =>
                !d.IsSystemDisk &&
                !d.IsMounted &&
                string.IsNullOrWhiteSpace(d.Filesystem) &&
                !d.IsUsedByRaid &&
                string.IsNullOrWhiteSpace(d.ArrayName) &&
                d.Status != "FAULTY" &&
                d.Role != "faulty" &&
                d.Role != "removed" &&
                !string.Equals(d.Type, "Virtual", StringComparison.OrdinalIgnoreCase) &&
                !d.Name.Any(char.IsDigit)
            )
            .ToList();

        if (candidates.Count == 0)
        {
            await ShowConfirm("No disks available",
                "There are no valid free disks to add.");
            return;
        }

        var dlg = new SelectDiskDialog(candidates);
        var owner = this.GetVisualRoot() as Window;

        var selectedDisk = await dlg.ShowDialog<string?>(owner ?? new Window());

        if (string.IsNullOrWhiteSpace(selectedDisk))
        {
            LogService.Write("[RAIDVIEW] AddDiskToArrayUI → cancelado por el usuario.");
            return;
        }

        NotificadorLinux.Enviar($"Adding /dev/{selectedDisk} to {array.Name}…");

        var ok = await service.AddDiskToArrayAsync(array.Name, selectedDisk);

        if (!ok)
        {
            await ShowConfirm("Error", $"Could not add /dev/{selectedDisk} to {array.Name}.");
            NotificadorLinux.Enviar($"Failed to add /dev/{selectedDisk}.", 5000, "critical");
            return;
        }

        await ShowConfirm("Success", $"/dev/{selectedDisk} added to {array.Name}.");
        NotificadorLinux.Enviar($"Disk added: /dev/{selectedDisk} → {array.Name}");

        var grow = await ShowConfirm(
            "Expand RAID Array",
            $"The disk /dev/{selectedDisk} was added as a spare.\n\n" +
            $"Do you want to expand {array.Name} to use this disk?"
        );

        if (grow)
        {
            NotificadorLinux.Enviar($"Expanding array {array.Name}…");
            await ExpandArrayAndResize(array.Name, array.Disks.Count + 1);
        }

        await LoadRaidAsync();
    }


    private async Task ExpandArrayAndResize(string arrayName, int newDeviceCount)
    {
        var owner = this.GetVisualRoot() as Window;
        var service = new RaidService();

        using (LoadingService.Show("Expanding RAID array...", owner))
        {
            var ok = await Task.Run(() =>
                service.ExpandArrayAndResizeAsync(arrayName, newDeviceCount)
            );

            if (!ok)
            {
                await ShowConfirm("Error",
                    $"Could not expand and resize {arrayName}.\nCheck logs for details.");
                return;
            }
        }

        await ShowConfirm("Success",
            $"The array {arrayName} has been expanded and resized.");

        await LoadRaidAsync();
    }
} //Fin de Clase