using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
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
    private const bool FORCE_FAKE_DATA = false;

    private  RaidStateService? _stateService;
    

    
    private static readonly Thickness DiskCardPadding = new(12);
    private static readonly Thickness DiskCardMargin = new(0, 0, 0, 10);
    private static readonly CornerRadius DiskCardRadius = new(8);

    private static readonly Thickness CardPadding = new(12);
    private static readonly Thickness CardMargin = new(0, 0, 0, 8);
    private static readonly CornerRadius CardRadius = new(10);
    private static readonly CornerRadius GlowRadius = new(14);

    private static readonly Thickness ExpandedPadding = new(16);
    private static readonly Thickness ExpandedMargin = new(0, 0, 0, 16);
    private static readonly CornerRadius ExpandedRadius = new(10);

    private static readonly ContextMenu ArrayMenu = new()
    {
        Items =
        {
            new MenuItem { Header = "Start resync/rebuild", Tag = "resync" },
            new MenuItem { Header = "Force check", Tag = "check" },
            new MenuItem { Header = "Force repair", Tag = "repair" },
            new MenuItem { Header = "Stop array", Tag = "stop" },
            new MenuItem { Header = "Details", Tag = "details" },
            new MenuItem { Header = "Add disk to array", Tag = "add_disk" },
            new MenuItem { Header = "Reshape Array (Expand)", Tag = "reshape" }
        }
    };

    private static readonly Dictionary<string, IImage> _iconCache = new();
    private List<RaidArrayInfo> _arrays = new();
    private RaidArrayInfo? _currentArray;

    private bool _monitorBlinkState;
    private DispatcherTimer? _monitorBlinkTimer;
    private string? _monitoringArrayName;
    private Border? _monitoringBorder;

    private RaidArrayInfo? _selectedArray;
    private bool _isBuildingUI = false;

    
    public RaidView(RaidStateService stateService)
        : this() // reutiliza el constructor actual
    {
        _stateService = stateService;
        _stateService.StateChanged += OnRaidStateChanged;

        // Carga inicial desde el estado en memoria
        LoadFromState();
    }

    //-----------------------------------
    


// ⭐ Conectar RaidView al RaidStateService desde MainWindow
public void AttachStateService(RaidStateService stateService)
{
    _stateService = stateService;

    // Asumimos que StateChanged es un Action o similar sin parámetros
    _stateService.StateChanged += OnRaidStateChanged;

    // Carga inicial desde el estado en memoria
    LoadFromState();
}

// ⭐ Se llama automáticamente cuando cambia el estado en memoria
private void OnRaidStateChanged()
{
    if (FORCE_FAKE_DATA)
        return;

    LogService.Debug("[RAIDVIEW] StateChanged recibido.");
    // No bloqueamos, no tocamos mdadm, solo refrescamos desde el estado
    Dispatcher.UIThread.Post(LoadFromState);
}

// ⭐ Carga arrays desde RaidStateService y reutiliza tu lógica actual
private void LoadFromState()
{
    if (_stateService == null)
        return;

    var arrays = _stateService.Arrays ?? new List<RaidArrayInfo>();

    // Reutilizamos SetArrays tal cual, para no romper nada
    SetArrays(arrays);
    LogService.Debug($"[RAIDVIEW] LoadFromState() → {_stateService?.Arrays?.Count} arrays");

}

// ⭐ SetArrays completo, tal como debe quedar


public void SetArrays(List<RaidArrayInfo> arrays)
{
    // ⭐ 1) Guardar estado anterior
    var expanded = _arrays
        .Where(a => a.IsExpanded)
        .Select(a => a.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var selected = _arrays
        .Where(a => a.IsSelected)
        .Select(a => a.Name)
        .FirstOrDefault();

    var monitoring = _monitoringArrayName;

    // ⭐ 2) Reemplazar arrays
    _arrays = arrays ?? new List<RaidArrayInfo>();

    // ⭐ 3) Restaurar estado + fallbacks
    foreach (var a in _arrays)
    {
        a.IsExpanded = expanded.Contains(a.Name);

        a.IsSelected = selected != null &&
                       a.Name.Equals(selected, StringComparison.OrdinalIgnoreCase);

        if (monitoring != null &&
            a.Name.Equals(monitoring, StringComparison.OrdinalIgnoreCase))
        {
            _monitoringArrayName = a.Name;
        }

        if (!Enum.IsDefined(typeof(RaidArrayState), a.State))
            a.State = RaidArrayState.Unknown;

        a.StateIcon = string.IsNullOrWhiteSpace(a.StateIcon)
            ? GetStateIcon(a.State)
            : a.StateIcon;

        a.Level = string.IsNullOrWhiteSpace(a.Level) ? "N/A" : a.Level;
        a.TotalSize = string.IsNullOrWhiteSpace(a.TotalSize) ? "Unknown" : a.TotalSize;
        a.UsableSize = string.IsNullOrWhiteSpace(a.UsableSize) ? a.TotalSize : a.UsableSize;
        a.ParitySize = string.IsNullOrWhiteSpace(a.ParitySize) ? "N/A" : a.ParitySize;
        a.Path = string.IsNullOrWhiteSpace(a.Path) ? "/dev/unknown" : a.Path;
        a.Uptime = string.IsNullOrWhiteSpace(a.Uptime) ? "Unknown" : a.Uptime;

        if (a.Disks == null)
            a.Disks = new List<RaidDiskInfo>();

        foreach (var d in a.Disks)
        {
            d.Model = string.IsNullOrWhiteSpace(d.Model) ? "Unknown" : d.Model;
            d.Serial = string.IsNullOrWhiteSpace(d.Serial) ? "Unknown" : d.Serial;
            d.Size = string.IsNullOrWhiteSpace(d.Size) ? "Unknown" : d.Size;

            if (string.IsNullOrWhiteSpace(d.Icon) || !d.Icon.Contains("avares://"))
                d.Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png";
        }

        a.DiskSummary = $"{a.Disks.Count}× Disk";

        if (a.RebuildProgress < 0 || a.RebuildProgress > 100)
            a.RebuildProgress = 0;

        if (string.IsNullOrWhiteSpace(a.RebuildETA))
            a.RebuildETA = "N/A";
    }

    // ⭐ IMPORTANTE:
    // NO llamar a BuildUI() aquí.
}





    
    
    //------------------------------------
    
    
    
    
   

    // ⭐ Permite conectar RaidView al RaidStateService sin romper nada
    

    
    
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

        foreach (var item in ArrayMenu.Items.OfType<MenuItem>())
        {
            item.Click += (_, _) =>
            {
                if (_selectedArray != null)
                    OnMoreMenuClick(_selectedArray, item.Tag?.ToString());
            };
        }

        if (FORCE_FAKE_DATA)
            LoadFakeData();
    }

    public bool IsFakeMode => FORCE_FAKE_DATA;

    
    

    
    
    
    private async void OnAssembleArraysClicked(object? sender, RoutedEventArgs e)
    {
        // ⭐ FIX: usar Singleton
        var service = RaidService.Instance;
        var parent = GetWindow();

        var loading = new LoadingDialog("Assembling arrays...");
        loading.Show(parent);

        await Task.Delay(50);

        var ok = await Task.Run(() => service.AutoAssemble());

        loading.Close();

        if (!ok)
        {
            await ShowInfo("Error", "Could not assemble stopped arrays.");
            return;
        }

        await ShowInfo("Success", "Arrays assembled correctly.");
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

        // ⭐ Abrir diálogo de formato
        var dlg = new FormatArrayDialog(array.Name);
        var owner = this.GetVisualRoot() as Window;
        var result = await dlg.ShowDialog<FormatArrayResult?>(owner);

        if (result == null)
            return;

        var loading = new LoadingDialog($"Initializing {array.Name}...");
        loading.Show(owner);

        var ok = false;

        try
        {
            ok = await Task.Run(async () =>
            {
                // ⭐ FIX: usar Singleton
                var service = RaidService.Instance;

                return await service.InitializeArrayAsync(
                    array.Path,          // ⭐ Path real del array
                    result.Filesystem,   // FS elegido
                    result.Label         // Label opcional
                );
            });
        }
        finally
        {
            loading.Close();
        }

        if (!ok)
        {
            await ShowInfo("Error", "Could not initialize the array.");
            return;
        }

        await ShowInfo("Success", $"The array {array.Name} was initialized.");

        // ⭐ Refrescar arrays
        BtnRefreshArrays.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }


    private async Task<bool> ShowConfirm(string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);
        var owner = this.GetVisualRoot() as Window;
        if (owner != null)
            return await dlg.ShowDialog<bool>(owner);

        return await dlg.ShowDialog<bool>(new Window());
    }

    private async Task<bool> ShowInfo(string title, string message)
    {
        var dlg = new InfoDialog(title, message);
        var owner = this.GetVisualRoot() as Window;
        if (owner != null)
            return await dlg.ShowDialog<bool>(owner);

        return await dlg.ShowDialog<bool>(new Window());
    }

   

    private void LoadFakeData()
    {
        _arrays = new List<RaidArrayInfo>
        {
            new()
            {
                Name = "md0",
                Path = "/dev/md0",
                Level = "RAID1",
                State = RaidArrayState.Clean,
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
                        RaidMembership = RaidMembership.Active,
                        State = "OK",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-ssd.png",
                        ArrayName = "md0"
                    },
                    new()
                    {
                        Name = "sdb1",
                        Model = "Samsung SSD 860 EVO",
                        Size = "500G",
                        RaidMembership = RaidMembership.Active,
                        State = "OK",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-ssd.png",
                        ArrayName = "md0"
                    }
                }
            },
            new()
            {
                Name = "md1",
                Path = "/dev/md1",
                Level = "RAID5",
                State = RaidArrayState.Degraded,
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
                        RaidMembership = RaidMembership.Active,
                        State = "OK",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                        ArrayName = "md1"
                    },
                    new()
                    {
                        Name = "sdd1",
                        Model = "WD Blue 1TB",
                        Size = "1T",
                        RaidMembership = RaidMembership.Active,
                        State = "OK",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                        ArrayName = "md1"
                    },
                    new()
                    {
                        Name = "sde1",
                        Model = "WD Blue 1TB",
                        Size = "1T",
                        RaidMembership = RaidMembership.Faulty,
                        State = "FAULTY",
                        Icon = "avares://RAID-Util/Assets/Icons/disk-hdd.png",
                        ArrayName = "md1"
                    }
                }
            }
        };

        BuildUI();
    }

    private Color GetArrayGlowColor(RaidArrayState state)
    {
        var isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;

        return state switch
        {
            RaidArrayState.Clean or RaidArrayState.Active =>
                isDark ? Color.Parse("#1C69D4") : Color.Parse("#0A4FB3"),

            RaidArrayState.Degraded or RaidArrayState.Rebuilding or RaidArrayState.Resync =>
                isDark ? Color.Parse("#F2C94C") : Color.Parse("#D9B03F"),

            RaidArrayState.ReadOnly =>
                isDark ? Color.Parse("#A7C7FF") : Color.Parse("#7AA8FF"),

            RaidArrayState.Failed =>
                isDark ? Color.Parse("#D32F2F") : Color.Parse("#B71C1C"),

            _ =>
                isDark ? Color.Parse("#F2C94C") : Color.Parse("#D9B03F")
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
        if (_isBuildingUI)
            return;

        _isBuildingUI = true;

        if (ListArrays == null)
        {
            _isBuildingUI = false;
            Dispatcher.UIThread.Post(BuildUI);
            return;
        }

        if (_arrays == null || _arrays.Count == 0)
        {
            ListArrays.Children.Clear();
            _isBuildingUI = false;
            return;
        }

        ListArrays.Children.Clear();

        foreach (var array in _arrays)
        {
            // ⭐ FILTRO SEGURO:
            //    - NO compara con BaseName (eso rompía md0 degradado)
            //    - Solo descarta discos sin ArrayName
            //    - Nunca deja array.Disks en null
            array.Disks = array.Disks?
                              .Where(d => !string.IsNullOrWhiteSpace(d.ArrayName))
                              .ToList()
                          ?? new List<RaidDiskInfo>();

            // ⭐ Si quedó vacío, no romper la UI
            if (array.Disks.Count == 0)
            {
                Console.WriteLine($"[UI] Array {array.Name} sin discos tras filtro seguro → continuando sin romper UI.");
            }

            var card = BuildArrayCard(array);
            ListArrays.Children.Add(card);

            if (array.IsExpanded)
            {
                var expanded = BuildExpandedCard(array);
                expanded.Tag = $"expanded:{array.Name}";
                ListArrays.Children.Add(expanded);
            }
        }

        _isBuildingUI = false;
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
    // ⭐ Fallbacks seguros
    var safeName = array.Name ?? "Unknown";
    var safeLevel = array.Level ?? "N/A";

    // ⭐ State es enum → NO usar ?.ToString()
    var safeState = Enum.IsDefined(typeof(RaidArrayState), array.State)
        ? array.State.ToString()
        : "Unknown";

    var safeTotalSize = array.TotalSize ?? "Unknown";
    var safePath = array.Path ?? "/dev/unknown";
    var safeParity = array.ParitySize ?? "N/A";
    var safeETA = array.RebuildETA ?? "N/A";

    var safeDisks = array.Disks ?? new List<RaidDiskInfo>();
    var activeCount = safeDisks.Count(d => d.RaidMembership == RaidMembership.Active);
    var faultyCount = safeDisks.Count(d => d.RaidMembership == RaidMembership.Faulty);

    // ⭐ Icono seguro
    var safeIcon = string.IsNullOrWhiteSpace(array.StateIcon)
        ? GetStateIcon(array.State)
        : array.StateIcon;

    var icon = LoadImage(safeIcon, 150);
    icon.Margin = new Thickness(4);
    icon.VerticalAlignment = VerticalAlignment.Center;

    var name = new TextBlock
    {
        Text = $"{safeName} ({safeLevel})",
        FontSize = 22,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 0, 0, 4)
    };

    var info = new StackPanel { Spacing = 2 };
    info.Children.Add(name);

    var dimBrush = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!;

    info.Children.Add(new TextBlock
    {
        Text = $"State: {safeState}",
        FontSize = 14,
        Foreground = dimBrush
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Disks: {safeDisks.Count} ({activeCount} OK, {faultyCount} Faulty)",
        FontSize = 14,
        Foreground = dimBrush
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Size: {safeTotalSize}",
        FontSize = 14,
        Foreground = dimBrush
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Path: {safePath}",
        FontSize = 14,
        Foreground = dimBrush
    });

    info.Children.Add(new TextBlock
    {
        Text = $"Persist Mount: {(array.PersistMount ? "YES" : "NO")}  Parity: {safeParity}",
        FontSize = 14,
        Foreground = dimBrush
    });

    // ⭐ Indicador de operación (rebuild, resync, recover…)
    if (array.RebuildProgress > 0 && array.RebuildProgress <= 100)
    {
        var opText = new TextBlock
        {
            Text = $"{safeState}: {array.RebuildProgress}%"
                   + (safeETA != "N/A" ? $" (ETA {safeETA})" : ""),
            FontSize = 14,
            Foreground = dimBrush,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // ⭐ Barra de progreso custom (100% estable)
        var barContainer = new Border
        {
            Background = Brushes.Gray,
            Height = 10,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 2, 0, 0)
        };

        var barFill = new Border
        {
            Background = Brushes.LimeGreen,
            Width = Math.Clamp(array.RebuildProgress, 0, 100) * 2, // 100% = 200px
            Height = 10,
            CornerRadius = new CornerRadius(3)
        };

        barContainer.Child = barFill;

        info.Children.Add(opText);
        info.Children.Add(barContainer);
    }

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

    var btnMore = new Button
    {
        Content = "More",
        Classes = { "MoreButton" },
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    btnMore.Click += (_, _) =>
    {
        _selectedArray = array;
        ArrayMenu.PlacementTarget = btnMore;
        ArrayMenu.Open(btnMore);
    };

    topRightPanel.Children.Add(btnMore);

    overlay.Children.Add(topRightPanel);
    Grid.SetRow(topRightPanel, 0);

    overlay.Children.Add(grid);
    Grid.SetRow(grid, 1);

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

    AnimateArrayGlow(glowBorder, glowBrush);

    cardBorder.PointerPressed += (_, _) =>
    {
        array.IsExpanded = !array.IsExpanded;

        var parent = ListArrays;
        var index = parent.Children.IndexOf(glowBorder);

        if (array.IsExpanded)
        {
            NotificadorLinux.Enviar($"Monitorizing: {safeName}\n Started");
            StartMonitoringArray(array, cardBorder);

            var expanded = BuildExpandedCard(array);
            expanded.Tag = $"expanded:{safeName}";
            parent.Children.Insert(index + 1, expanded);
        }
        else
        {
            StopMonitoringArray();
            NotificadorLinux.Enviar($"Monitorizing: {safeName}\n Stopped");

            foreach (var child in parent.Children.ToList())
                if (child is Border b &&
                    b.Tag is string tag &&
                    tag == $"expanded:{safeName}")
                {
                    parent.Children.Remove(b);
                    break;
                }
        }
    };

    return glowBorder;
}



    private void ClearOtherSelections(RaidArrayInfo selected)
    {
        foreach (var arr in _arrays)
            if (arr != selected)
                arr.IsSelected = false;

        BtnDeleteArray.IsEnabled = _selectedArray != null;
    }

    private Border BuildExpandedCard(RaidArrayInfo array)
    {
        var panel = new StackPanel { Spacing = 14 };

        var textBrush = (IBrush)Application.Current!.FindResource("BMWTextBrush")!;
        var accentBrush = (IBrush)Application.Current!.FindResource("BMWAccentBrush")!;

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

        panel.Children.Add(new TextBlock
        {
            Text = $"State: {array.State}",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontSize = 13,
            Foreground = accentBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        panel.Children.Add(BuildMountOptions(array));

        foreach (var disk in array.Disks)
            panel.Children.Add(BuildDiskCard(array, disk));

        return new Border
        {
            Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
            CornerRadius = ExpandedRadius,
            Padding = ExpandedPadding,
            Margin = ExpandedMargin,
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
                var opts = new List<string>();

                if (cfg.Mount_NoAtime) opts.Add("noatime");
                if (cfg.Mount_NoDirAtime) opts.Add("nodiratime");
                if (cfg.Mount_Discard) opts.Add("discard");
                if (cfg.Mount_Sync) opts.Add("sync");
                if (cfg.Mount_ReadOnly) opts.Add("ro");

                var mountOpts = opts.Count == 0
                    ? "defaults"
                    : string.Join(",", opts);

                MountService.Mount(array.Path, mountPoint, mountOpts);
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
    private Border BuildDiskCard(RaidArrayInfo array, RaidDiskInfo disk)
    {
        var icon = LoadImage(disk.Icon, 72);
        icon.Margin = new Thickness(2);

        var textBrush = (IBrush)Application.Current!.FindResource("BMWTextBrush")!;
        var dimBrush = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!;

        var name = new TextBlock { Text = disk.Name, FontSize = 17, Foreground = textBrush };
        var model = new TextBlock { Text = $"Model: {disk.Model}", FontSize = 14, Foreground = dimBrush };
        var size = new TextBlock { Text = $"Size: {disk.Size}", FontSize = 14, Foreground = dimBrush };
        var role = new TextBlock
        {
            Text = $"RAID Role: {disk.RaidMembership}",
            FontSize = 14,
            Foreground = dimBrush
        };
        var smart = new TextBlock
        {
            Text = $"SMART: {disk.State}",
            FontSize = 14,
            Foreground = dimBrush
        };

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { name, model, size, role, smart }
        };

        var statusDot = BuildStatusDot(disk);

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

        var menu = BuildDiskMenu(array, disk);

        manageButton.Click += (_, _) =>
        {
            menu.PlacementTarget = manageButton;
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
            CornerRadius = DiskCardRadius,
            Padding = DiskCardPadding,
            Margin = DiskCardMargin,
            Child = grid
        };
    }

    private ContextMenu BuildDiskMenu(RaidArrayInfo array, RaidDiskInfo disk)
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem { Header = "SMART Info", Tag = "smart" });

        bool raid0_or_linear =
            array.Level.Equals("linear", StringComparison.OrdinalIgnoreCase) ||
            array.Level.Equals("raid0", StringComparison.OrdinalIgnoreCase);

        if (disk.RaidMembership != RaidMembership.Faulty &&
            disk.RaidMembership != RaidMembership.None &&
            !raid0_or_linear)
        {
            menu.Items.Add(new MenuItem { Header = "Mark as Faulty", Tag = "faulty" });
        }

        if (disk.RaidMembership == RaidMembership.Active && array.SupportsRepair)
            menu.Items.Add(new MenuItem { Header = "Set as Spare", Tag = "spare" });

        if (disk.RaidMembership == RaidMembership.Spare && array.SupportsRepair)
            menu.Items.Add(new MenuItem { Header = "Convert Spare to Active", Tag = "convert_spare" });

        if (disk.RaidMembership == RaidMembership.Faulty && array.SupportsRepair)
            menu.Items.Add(new MenuItem { Header = "Recover Faulty Disk", Tag = "recover_faulty" });

        if (disk.RaidMembership != RaidMembership.None)
            menu.Items.Add(new MenuItem { Header = "Remove from Array", Tag = "remove" });

        foreach (var item in menu.Items.OfType<MenuItem>())
            item.Click += async (_, _) => await OnDiskMenuClick(array, disk, item.Tag?.ToString());

        return menu;
    }

    private Border BuildStatusDot(RaidDiskInfo disk)
{
    // 1. Determinar color base según RAID Membership
    Color color = disk.RaidMembership switch
    {
        RaidMembership.Active      => Color.FromRgb(0, 200, 0),
        RaidMembership.Spare       => Color.FromRgb(0, 150, 255),
        RaidMembership.Rebuilding  => Color.FromRgb(255, 200, 0),
        RaidMembership.Syncing     => Color.FromRgb(255, 200, 0),
        RaidMembership.Faulty      => Color.FromRgb(220, 0, 0),
        _                          => Color.FromRgb(90, 90, 90)
    };

    // 2. Glow (círculo grande)
    var glow = new Border
    {
        Width = 28,
        Height = 28,
        CornerRadius = new CornerRadius(14),
        Background = new SolidColorBrush(color) { Opacity = 0.35 },
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    // 3. Dot (círculo pequeño)
    var dot = new Border
    {
        Width = 16,
        Height = 16,
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    // 4. Contenedor
    var container = new Grid
    {
        Width = 28,
        Height = 28
    };

    container.Children.Add(glow);
    container.Children.Add(dot);

    // 5. Animaciones según estado
    Dispatcher.UIThread.Post(() =>
    {
        string state = disk.State?.ToLowerInvariant() ?? "unknown";

        // WARNING → parpadeo suave amarillo
        if (state == "warning")
        {
            AnimateWarning(glow, dot);
            return;
        }

        // FAULTY → animación SOS
        if (state == "faulty")
        {
            AnimateSOS(glow, dot);
            return;
        }

        // REBUILD / SYNC → animación de reconstrucción
        if (disk.RaidMembership == RaidMembership.Rebuilding ||
            disk.RaidMembership == RaidMembership.Syncing)
        {
            AnimateRebuild(glow, dot);
            return;
        }

        // Estados normales → sin animación
    });

    // 6. Devolver borde contenedor
    return new Border
    {
        Child = container,
        Background = Brushes.Transparent
    };
}


    private async Task OnDiskMenuClick(RaidArrayInfo array, RaidDiskInfo disk, string? action)
{
    // ⭐ FIX: usar Singleton
    var service = RaidService.Instance;

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
            if (array.Level.Equals("linear", StringComparison.OrdinalIgnoreCase) ||
                array.Level.Equals("raid0", StringComparison.OrdinalIgnoreCase))
            {
                ShellHelper.EjecutarComoRoot($"mdadm {array.Path} --remove /dev/{disk.Name}");
            }
            else
            {
                ShellHelper.EjecutarComoRoot($"mdadm {array.Path} --fail /dev/{disk.Name}");
            }

            await LoadRaidAsync();
            break;
        }

        case "spare":
        {
            ShellHelper.EjecutarComoRoot($"mdadm {array.Path} --set-spare /dev/{disk.Name}");
            await LoadRaidAsync();
            break;
        }

        case "remove":
        {
            await RemoveDiskFromArrayUI(array, disk.Name);
            break;
        }

        case "convert_spare":
        {
            int active = array.Disks.Count(d => d.RaidMembership == RaidMembership.Active);
            int newCount = active + 1;

            bool confirm = await ShowConfirm(
                "Convert Spare to Active",
                $"Convert {disk.Name} from spare to active?\n" +
                $"This will increase RAID devices from {active} to {newCount}."
            );

            if (!confirm)
                return;

            using (LoadingService.Show("Converting spare to active...", this.GetVisualRoot() as Window))
            {
                await Task.Run(() =>
                {
                    string cmd = $"mdadm --grow {array.Path} --raid-devices={newCount}";
                    ShellHelper.EjecutarComoRoot(cmd);
                });
            }

            await ShowConfirm("Done", $"{disk.Name} is now active.");
            await LoadRaidAsync();
            break;
        }

        case "recover_faulty":
        {
            ShellHelper.EjecutarComoRoot($"mdadm {array.Path} --remove /dev/{disk.Name}");
            ShellHelper.EjecutarComoRoot($"mdadm {array.Path} --add /dev/{disk.Name}");
            await LoadRaidAsync();

            NotificadorLinux.Enviar(
                $"Disk recovered:\n/dev/{disk.Name} re-added to {array.Name}\nRebuild started."
            );

            break;
        }
    }
}


    private void StartMonitoringArray(RaidArrayInfo array, Border cardBorder)
    {
        StopMonitoringArray();

        _monitoringArrayName = array.Name;
        _monitoringBorder = cardBorder;

        var cfg = ArrayConfigService.Load(array.Name);

        RaidAlertService.StartMonitoring(array.Name, cfg, msg =>
        {
            NotificadorLinux.Enviar(msg, 6000, "critical");
        });

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

    private void AnimateWarning(Border glow, Border dot)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut()
        };

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0),
            Setters =
            {
                new Setter(Border.OpacityProperty, 0.35)
            }
        });

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0.5),
            Setters =
            {
                new Setter(Border.OpacityProperty, 0.15)
            }
        });

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(1),
            Setters =
            {
                new Setter(Border.OpacityProperty, 0.35)
            }
        });

        animation.RunAsync(glow);
    }

    private void AnimateRebuild(Border glow, Border dot)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(0.8),
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut()
        };

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0),
            Setters =
            {
                new Setter(Border.OpacityProperty, 0.35)
            }
        });

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0.5),
            Setters =
            {
                new Setter(Border.OpacityProperty, 0.05)
            }
        });

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(1),
            Setters =
            {
                new Setter(Border.OpacityProperty, 0.35)
            }
        });

        animation.RunAsync(glow);
    }

    private async void AnimateSOS(Border glow, Border dot)
    {
        while (true)
        {
            for (int i = 0; i < 3; i++)
            {
                glow.Opacity = 0.35;
                await Task.Delay(150);
                glow.Opacity = 0.05;
                await Task.Delay(150);
            }

            for (int i = 0; i < 3; i++)
            {
                glow.Opacity = 0.35;
                await Task.Delay(400);
                glow.Opacity = 0.05;
                await Task.Delay(400);
            }

            for (int i = 0; i < 3; i++)
            {
                glow.Opacity = 0.35;
                await Task.Delay(150);
                glow.Opacity = 0.05;
                await Task.Delay(150);
            }

            await Task.Delay(800);
        }
    }
    private Image LoadImage(string uriString, int size)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uriString) ||
                !uriString.Contains("avares://"))
                uriString = "avares://RAID-Util/Assets/Icons/disk-hdd.png";

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
        var cmd = $"/usr/sbin/mdadm --detail {array.Path}";
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
            return;

        var win = new ArrayConfigWindow(_selectedArray);
        win.ShowDialog(GetWindow());
    }


    private async void OnCreateArrayClicked(object? sender, RoutedEventArgs e)
    {
        var parent = GetWindow();

        var service = RaidService.Instance;

        var allDisks = await service.GetAllDisksAsync();
        var nodes = RaidService.Nodes;

        // ⭐ FILTRO UNIVERSAL
        var freeDisks = allDisks
            .Where(d =>
                // 1. No pertenece a un array (salvo removed)
                (d.RaidMembership == RaidMembership.None ||
                 d.Role.Equals("removed", StringComparison.OrdinalIgnoreCase))

                // 2. No está montado
                && !d.IsMounted

                // 3. No tiene particiones montadas
                && (d.Children == null || d.Children.Count == 0)

                // 4. No es disco del sistema
                && !d.IsSystemDisk
            )
            .ToList();

        if (freeDisks.Count == 0)
        {
            new ConfirmDialog("No disks", "No free disks available to create a RAID array.")
                .ShowDialog(parent);
            return;
        }

        var dialog = new CreateArrayDialog(freeDisks);
        var result = await dialog.ShowDialog<CreateArrayResult?>(parent);

        if (result == null)
            return;

        var loading = new LoadingDialog("Creating RAID array...");
        loading.Show(parent);

        await Task.Delay(50);

        bool ok;

        if (IsFakeMode)
            ok = await Task.Run(() =>
            {
                CreateFakeArray(result);
                return true;
            });
        else
            ok = await CreateRealArray(result);

        loading.Close();

        if (!ok)
        {
            new ConfirmDialog("Error", "Failed to create RAID array.")
                .ShowDialog(parent);
            return;
        }

        await LoadRaidAsync(true);
    }



    private void CreateFakeArray(CreateArrayResult result)
    {
        var fakeArray = new RaidArrayInfo
        {
            Name = result.FriendlyName,
            Path = $"/dev/{result.FriendlyName}",
            Level = result.Level,
            State = RaidArrayState.Clean,
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
    var parent = GetWindow();

    // ⭐ FIX: usar Singleton
    var service = RaidService.Instance;

    using (LoadingService.Show("Creating RAID array...", parent))
    {
        await Task.Delay(50);

        // ============================================================
        // 1) Crear array con mdadm
        // ============================================================
        LoadingService.Update("Executing mdadm...");

        var ok = await Task.Run(() =>
            service.CreateArray(result.Level, result.Disks, result.FriendlyName)
        );

        if (!ok)
        {
            LoadingService.Update("Error creating array.");
            await Task.Delay(1200);
            return false;
        }

        // ============================================================
        // 2) Esperar a que aparezca /dev/mdX
        // ============================================================
        LoadingService.Update("Detecting new array...");

        var mdName = service.LastCreatedMdName;

        var ready = await Task.Run(() =>
            service.WaitForArray(mdName)
        );

        if (!ready)
        {
            LoadingService.Update("Array created, but device did not appear.");
            await Task.Delay(1200);

            new ConfirmDialog("Warning",
                    $"Array created, but /dev/{mdName} did not appear in time.")
                .ShowDialog(parent);

            return false;
        }

        // ============================================================
        // 3) Persistir en mdadm.conf
        // ============================================================
        LoadingService.Update("Updating mdadm.conf...");

        var persisted = await Task.Run(() =>
            service.PersistArrayToMdadmConf()
        );

        if (!persisted)
        {
            LoadingService.Update("Array created, but mdadm.conf was not updated.");
            await Task.Delay(1200);

            new ConfirmDialog("Warning",
                    "Array created, but could not update mdadm.conf. Check logs.")
                .ShowDialog(parent);
        }

        // ============================================================
        // 4) Finalización
        // ============================================================
        LoadingService.Update("Array ready.");
        await Task.Delay(600);
    }

    // ⭐ Recargar RAID
    await LoadRaidAsync(true);
    return true;
}


    private async void OnDeleteArrayClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedArray == null)
        {
            await ShowInfo("No array selected", "Please select an array before deleting.");
            return;
        }

        var array = _selectedArray;

        var dialog = new ConfirmDialog(
            $"Delete array {array.Name}?",
            "This action cannot be undone."
        );

        var result = await dialog.ShowDialog<bool>(GetWindow());
        if (!result)
            return;

        // ============================================================
        // FAKE MODE
        // ============================================================
        if (IsFakeMode)
        {
            _arrays.Remove(array);
            _selectedArray = null;
            BuildUI();
            return;
        }

        bool ok;

        using (LoadingService.Show("Deleting array..."))
        {
            // ⭐ FIX: usar Singleton
            var service = RaidService.Instance;

            ok = await Task.Run(() =>
                service.DeleteArrayAsync(array)
            );
        }

        if (!ok)
        {
            await ShowInfo(
                "Deletion incomplete",
                "The array could not be fully deleted. Some operations failed.\n\n" +
                "Please check the logs for more details."
            );
            return;
        }

        await ShowInfo(
            "Array deleted",
            $"The array {array.Name} has been successfully deleted."
        );

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

        await LoadRaidAsync();
    }

    private void OnConfigArraysClicked(object? sender, RoutedEventArgs e)
    {
        OpenArrayConfigWindow();
    }
    
    public Task RefreshArraysAsync()
    {
        return LoadRaidAsync();
    }


    private async Task LoadRaidAsync(bool afterCreate = false)
    {
        try
        {
            if (IsFakeMode)
            {
                LoadFakeData();

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopFake)
                    (desktopFake.MainWindow as MainWindow)?.UpdateStatus("Fake RAID data loaded.");

                return;
            }

            using (LoadingService.Show("Loading RAID arrays..."))
            {
                var (arrays, disks) = await Task.Run(async () =>
                {
                    // ⭐ FIX: usar Singleton
                    var service = RaidService.Instance;

                    if (afterCreate)
                        await Task.Delay(150);

                    var arraysTask = service.GetArraysAsync();
                    var disksTask = service.GetAllDisksAsync();

                    await Task.WhenAll(arraysTask, disksTask);

                    return (arraysTask.Result ?? new List<RaidArrayInfo>(),
                        disksTask.Result ?? new List<RaidDiskInfo>());
                });

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SetArrays(arrays);
                    BuildUI();

                    if (afterCreate && arrays.Count > 0)
                    {
                        var last = arrays.Last();
                        last.IsExpanded = true;
                    }

                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        (desktop.MainWindow as MainWindow)?.UpdateStatus("RAID information refreshed.");
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Error("[RAIDVIEW] LoadRaidAsync() EXCEPTION:");
            LogService.Error(ex.ToString());

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                (desktop.MainWindow as MainWindow)?.UpdateStatus("Error loading RAID information.");
        }
    }


    private string GetStateIcon(RaidArrayState state)
    {
        return state switch
        {
            RaidArrayState.Clean or RaidArrayState.Active =>
                "avares://RAID-Util/Assets/Icons/array-ok.png",

            RaidArrayState.Degraded or RaidArrayState.Rebuilding or RaidArrayState.Resync =>
                "avares://RAID-Util/Assets/Icons/array-caution.png",

            RaidArrayState.ReadOnly =>
                "avares://RAID-Util/Assets/Icons/array-readonly.png",

            RaidArrayState.Failed =>
                "avares://RAID-Util/Assets/Icons/array-error.png",

            _ =>
                "avares://RAID-Util/Assets/Icons/array-caution.png"
        };
    }
    
    private async void OnMoreMenuClick(RaidArrayInfo array, string? action)
{
    // ⭐ FIX: usar Singleton
    var service = RaidService.Instance;

    if (array == null)
    {
        await ShowConfirm("Error", "Array not found.");
        return;
    }

    var act = action?.Trim().ToLowerInvariant() ?? "";

    bool isRaid0 = array.Level.Equals("raid0", StringComparison.OrdinalIgnoreCase);
    bool isLinear = array.Level.Equals("linear", StringComparison.OrdinalIgnoreCase);

    if ((isRaid0 || isLinear) && act is not ("stop" or "details"))
    {
        await ShowConfirm("Error", "This RAID level does not support this action.");
        return;
    }

    if (array.State == RaidArrayState.Failed && act != "details")
    {
        await ShowConfirm("Error", "This array is in FAILED state. Only details are available.");
        return;
    }

    bool isMountedRO = false;

    if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
    {
        var mountInfo = ShellHelper.EjecutarComoRoot($"mount | grep ' {array.MountPath} '");

        if (mountInfo.ExitCode == 0)
        {
            var line = mountInfo.Stdout.ToLowerInvariant();
            if (line.Contains("(ro,") || line.Contains(" ro,"))
                isMountedRO = true;
        }
    }

    switch (act)
    {
        case "resync":
            if (!array.SupportsRepair)
            {
                await ShowConfirm("Error", "This RAID level does not support resync.");
                return;
            }

            if (array.IsResyncing || array.IsRecovering)
            {
                await ShowConfirm("Info", "This array is already rebuilding.");
                return;
            }

            if (isMountedRO)
            {
                await ShowConfirm("Error", "Array is mounted read-only. Cannot start resync.");
                return;
            }

            await service.StartArrayResyncAsync(array.Name);

            var ownerrs = this.GetVisualRoot() as Window;
            var dlgrb = new RebuildDialog(array.Name);

            if (ownerrs != null)
                await dlgrb.ShowDialog(ownerrs);
            else
                await dlgrb.ShowDialog(new Window());

            break;

        case "check":
            if (!array.SupportsCheck)
            {
                await ShowConfirm("Error", "This RAID level does not support consistency checks.");
                return;
            }

            if (!array.IsMounted)
            {
                await ShowConfirm("Error", "Array must be mounted to perform a check.");
                return;
            }

            if (array.IsResyncing || array.IsRecovering)
            {
                await ShowConfirm("Error", "Cannot check while rebuilding.");
                return;
            }

            await service.ForceArrayCheckAsync(array.Name);
            await ShowConfirm("Success", "Check started.");
            break;

        case "repair":
            if (!array.SupportsRepair)
            {
                await ShowConfirm("Error", "This RAID level does not support repair.");
                return;
            }

            if (!array.IsDegraded)
            {
                await ShowConfirm("Error", "Repair is only available for degraded arrays.");
                return;
            }

            if (!array.Disks.Any(d => d.RaidMembership == RaidMembership.Spare))
            {
                await ShowConfirm("Error", "No spare disk available for repair.");
                return;
            }

            if (array.IsResyncing || array.IsRecovering)
            {
                await ShowConfirm("Error", "Cannot repair while rebuilding.");
                return;
            }

            await service.ForceArrayRepairAsync(array.Name);
            await ShowConfirm("Success", "Repair started.");
            break;

        case "stop":
        {
            var (ok, msg) = await service.StopArraySafeAsync(array.Name);

            if (!ok)
            {
                await ShowConfirm("Error", msg);
                return;
            }

            await ShowConfirm("Success", msg);
            await LoadRaidAsync();
            break;
        }

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

        case "reshape":
            await HandleReshape(array);
            break;

        default:
            await ShowConfirm("Error", $"Unknown action '{action}'.");
            break;
    }
}


    private async Task HandleReshape(RaidArrayInfo array)
    {
        if (!ArrayAllowsExpansion(array))
        {
            await ShowConfirm("Not allowed", "This array cannot be expanded.");
            return;
        }

        int active = array.Disks.Count(d => d.RaidMembership == RaidMembership.Active);
        int spares = array.Disks.Count(d => d.RaidMembership == RaidMembership.Spare);
        int newCount = active + spares;

        bool confirm = await ShowConfirm(
            "Expand RAID Array",
            $"Expand array {array.Name} from {active} to {newCount} devices?\n" +
            $"This will start a RAID reshape."
        );

        if (!confirm)
            return;

        var owner = this.GetVisualRoot() as Window;

        using (LoadingService.Show("Expanding RAID array...", owner))
        {
            await Task.Run(() =>
            {
                string cmd = $"mdadm --grow {array.Path} --raid-devices={newCount}";
                ShellHelper.EjecutarComoRoot(cmd);
            });
        }

        await ShowConfirm("Reshape started",
            $"The array {array.Name} is now reshaping.");

        await Task.Run(() =>
        {
            while (true)
            {
                var stat = File.ReadAllText("/proc/mdstat");
                if (!stat.Contains("reshape"))
                    break;

                Thread.Sleep(2000);
            }
        });

        string fs = DetectArrayFileSystem(array.Name);

        if (!string.IsNullOrWhiteSpace(fs))
            ResizeArrayFileSystem(array.Name, fs);

        await LoadRaidAsync();
    }

    private string DetectArrayFileSystem(string arrayName)
    {
        if (!arrayName.StartsWith("/dev/"))
            arrayName = "/dev/" + arrayName;

        var blkid = ShellHelper.EjecutarComoRoot($"blkid {arrayName}");
        if (blkid.ExitCode != 0)
            return null;

        foreach (var part in blkid.Stdout.Split(' '))
        {
            if (part.StartsWith("TYPE="))
            {
                return part.Replace("TYPE=", "")
                    .Replace("\"", "")
                    .Trim();
            }
        }

        return null;
    }

    private void ResizeArrayFileSystem(string arrayName, string fsType)
    {
        if (!arrayName.StartsWith("/dev/"))
            arrayName = "/dev/" + arrayName;

        switch (fsType.ToLowerInvariant())
        {
            case "ext4":
            case "ext3":
            case "ext2":
                ShellHelper.EjecutarComoRoot($"resize2fs {arrayName}");
                break;

            case "xfs":
                var mount = ShellHelper.EjecutarSinRoot($"findmnt -n -o TARGET {arrayName}");
                if (mount.ExitCode == 0 && !string.IsNullOrWhiteSpace(mount.Stdout))
                {
                    string mp = mount.Stdout.Trim();
                    ShellHelper.EjecutarComoRoot($"xfs_growfs {mp}");
                }
                break;

            case "btrfs":
                var m2 = ShellHelper.EjecutarSinRoot($"findmnt -n -o TARGET {arrayName}");
                if (m2.ExitCode == 0 && !string.IsNullOrWhiteSpace(m2.Stdout))
                {
                    string mp = m2.Stdout.Trim();
                    ShellHelper.EjecutarComoRoot($"btrfs filesystem resize max {mp}");
                }
                break;

            default:
                LogService.Write($"[RESHAPE] Filesystem '{fsType}' not supported for auto-resize.");
                break;
        }
    }
    
    private async Task RemoveDiskFromArrayUI(RaidArrayInfo array, string diskName)
{
    try
    {
        var owner = this.GetVisualRoot() as Window;

        // Normalizar el nombre recibido
        string normalized = RaidService.NormalizeDev(diskName);

        // Buscar el disco real dentro del array
        var disk = array.Disks.FirstOrDefault(d =>
            d.Name.Equals(diskName, StringComparison.OrdinalIgnoreCase) ||
            RaidService.NormalizeDev(d.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase)
        );

        // Si lo encontramos, normalizamos usando su Name real
        if (disk != null)
            normalized = RaidService.NormalizeDev(disk.Name);

        // Confirmación
        var confirm = new ConfirmDialog(
            "Remove Disk",
            $"Are you sure you want to remove {normalized} from {array.Name}?"
        );

        var ok = await confirm.ShowDialog<bool>(owner ?? new Window());
        if (!ok)
            return;

        // Loading
        var loading = new LoadingDialog($"Removing {normalized}...");
        loading.Show(owner);

        try
        {
            // ⭐ FIX: usar Singleton
            var service = RaidService.Instance;

            bool removed = await service.RemoveDiskFromArrayAsync(array.Name, normalized);

            if (!removed)
            {
                await ShowConfirm(
                    "Error removing disk",
                    $"mdadm could not remove {normalized} from {array.Name}.\n" +
                    $"Check logs for details."
                );
                return;
            }

            await LoadRaidAsync();
        }
        finally
        {
            loading.Close();
        }

        NotificadorLinux.Enviar(
            $"Disk removed and cleaned:\n{normalized} removed from {array.Name}"
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine("[UI] EXCEPTION in RemoveDiskFromArrayUI:");
        Console.WriteLine(ex.ToString());
    }
}



    private List<RaidDiskInfo> GetDisksForArray(RaidArrayInfo array, List<RaidDiskInfo> allDisks)
    {
        if (array == null)
            return new List<RaidDiskInfo>();

        var baseName = array.BaseName;

        var disks = allDisks
            .Where(d => d.ArrayName != null &&
                        d.ArrayName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return disks;
    }

    private async Task AddDiskToArrayUI(RaidArrayInfo array)
    {
        var service = RaidService.Instance;

        var allDisks = await service.GetAllDisksAsync();

        // ⭐ FILTRO UNIVERSAL
        var candidates = allDisks
            .Where(d =>
                // 1. No pertenece a un array (salvo removed)
                (d.RaidMembership == RaidMembership.None ||
                 d.Role.Equals("removed", StringComparison.OrdinalIgnoreCase))

                // 2. No está montado
                && !d.IsMounted

                // 3. No tiene particiones montadas
                && (d.Children == null || d.Children.Count == 0)

                // 4. No es disco del sistema
                && !d.IsSystemDisk
            )
            .ToList();

        if (array.IsResyncing || array.IsRecovering || array.RebuildProgress > 0)
        {
            await ShowConfirm(
                "Array is rebuilding",
                "You cannot add disks while the array is rebuilding.\n\n" +
                "Wait until the rebuild completes."
            );
            return;
        }

        if (candidates.Count == 0)
        {
            await ShowConfirm(
                "No disks available",
                "There are no valid free disks to add."
            );
            return;
        }

        var dlg = new SelectDiskDialog(candidates);
        var owner = this.GetVisualRoot() as Window;

        var selectedDisk = await dlg.ShowDialog<string?>(owner ?? new Window());
        if (string.IsNullOrWhiteSpace(selectedDisk))
            return;

        using (LoadingService.Show("Adding disk to array..."))
        {
            var ok = await service.AddDiskToArrayAsync(array.Name, selectedDisk);

            if (!ok)
            {
                await ShowConfirm("Error", "Failed to add disk to array.");
                return;
            }
        }

        await LoadRaidAsync();
    }



    private bool ArrayAllowsExpansion(RaidArrayInfo array)
    {
        if (array == null)
            return false;

        var level = array.Level?
            .Replace(" ", "")
            .Replace("-", "")
            .ToUpperInvariant() ?? "";

        if (level is not ("RAID5" or "RAID6" or "RAID10"))
            return false;

        if (array.IsResyncing || array.IsRecovering || array.IsChecking || array.IsRepairing)
            return false;

        if (!array.IsActive && !array.IsClean)
            return false;

        if (array.RebuildProgress > 0)
            return false;

        return true;
    }
}
