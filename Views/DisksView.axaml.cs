using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class DisksView : UserControl
{
    private static bool _resourcesReady;
    private static IBrush? _bmwTextBrush;
    private static IBrush? _bmwTextDimBrush;
    private static IBrush? _bmwAccentBrush;
    private static IBrush? _bmwSurfaceElevatedBrush;
    private static IBrush? _bmwBorderBrush;
    private static Thickness _cardBorderThickness;
    private static Thickness _cardPadding;
    private static Thickness _cardMargin;
    private static CornerRadius _cardCornerRadius;
    private readonly List<RaidDiskInfo> _disks = new();

    public DisksView()
    {
        InitializeComponent();
        LogService.Debug("[DISKSVIEW] Constructor ejecutado.");
        
        var btn = this.FindControl<Button>("BtnRefresh");
        if (btn != null)
        {
            btn.Click += async (_, _) =>
            {
                await RefreshDisksAsync();
            };
        }
        
        
    }

    private static void EnsureResources()
    {
        if (_resourcesReady)
            return;

        var app = Application.Current!;
        _bmwTextBrush = (IBrush)app.FindResource("BMWTextBrush")!;
        _bmwTextDimBrush = (IBrush)app.FindResource("BMWTextDimBrush")!;
        _bmwAccentBrush = (IBrush)app.FindResource("BMWAccentBrush")!;
        _bmwSurfaceElevatedBrush = (IBrush)app.FindResource("BMWSurfaceElevatedBrush")!;
        _bmwBorderBrush = (IBrush)app.FindResource("BMWBorderBrush")!;

        _cardBorderThickness = new Thickness(1);
        _cardPadding = new Thickness(12);
        _cardMargin = new Thickness(0, 0, 0, 10);
        _cardCornerRadius = new CornerRadius(10);

        _resourcesReady = true;
    }
 
    async public void Initialize(bool sudoReady, bool forceFake)
    {
        LogService.Info($"[DISKSVIEW] Initialize() sudoReady={sudoReady}, forceFake={forceFake}");

        _disks.Clear();

        if (!sudoReady)
        {
            LoadFakeData();
        }
        else
        {
            if (forceFake)
                LoadFakeData();
            else
                await LoadRealDataAsync();

        }

        RenderDisks();
    }

    public async Task RefreshDisksAsync()
    {
        LogService.Debug("[DISKSVIEW] RefreshDisksAsync() called.");

        using (LoadingService.Show("Refreshing disks..."))
        {
            await LoadRealDataAsync();   // ⬅️ async, sin Task.Run
        }

        RenderDisks();
    }



    private void LoadFakeData()
    {
        _disks.Clear();

        _disks.Add(new RaidDiskInfo
        {
            Name = "sdb",
            Model = "Samsung SSD 860 EVO",
            Size = "500GB",
            Serial = "S3Z9NX0M123456A",
            Icon = DiskIconService.GetIcon(null, "SSD SATA", false),
            IsRotational = false
        });

        _disks.Add(new RaidDiskInfo
        {
            Name = "sdc",
            Model = "Seagate Barracuda",
            Size = "2TB",
            Serial = "ZDH12X0A0001",
            Icon = DiskIconService.GetIcon(null, "HDD", true),
            IsRotational = true
        });
    }

    private async Task LoadRealDataAsync()
    {
        using (LoadingService.Show("Loading disks..."))
        {
            await Task.Run(() =>
            {
                _disks.Clear();

                try
                {
                    var list = DiskService.GetAllDisks();
                    LogService.Info($"[DISKSVIEW] Discos detectados: {list.Count}");

                    var eligible = list.Where(d =>
                        !d.IsSystemDisk &&
                        (
                            d.RaidMembership == RaidMembership.None ||
                            d.Role.Equals("removed", StringComparison.OrdinalIgnoreCase)
                        )
                    );

                    foreach (var d in eligible)
                    {
                        d.Model ??= "Unknown";
                        d.Serial ??= "Unknown";
                        d.Size ??= "Unknown";
                        d.FsType ??= "";
                        d.MountPath ??= "";
                        d.State ??= "UNKNOWN";

                        string iconHint = d.Icon;

                        if (d.IsNvme)
                            iconHint = "nvme";
                        else if (d.IsUsb)
                            iconHint = "usb";
                        else if (d.IsIscsi)
                            iconHint = "iscsi";
                        else if (d.IsVirtual)
                            iconHint = "virtual";

                        d.Icon = DiskIconService.GetIcon(iconHint, d.Model, d.IsRotational);

                        _disks.Add(d);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"[DISKSVIEW] Error cargando discos reales: {ex}");
                }
            });
        }

        RenderDisks();
    }


    private void RenderDisks()
    {
        DiskList.Children.Clear();

        foreach (var disk in _disks)
            DiskList.Children.Add(BuildDiskCard(disk));
    }

    private Border BuildDiskCard(RaidDiskInfo disk)
{
    EnsureResources();

    disk.Model ??= "Unknown";
    disk.Serial ??= "Unknown";
    disk.Size ??= "Unknown";
    disk.FsType ??= "";
    disk.MountPath ??= "";
    disk.State ??= "UNKNOWN";

    string temperature = GetTemperature(disk);

    string type =
        disk.IsNvme ? "NVMe" :
        !disk.IsRotational ? "SSD" :
        "HDD";

    // ⭐ Forzar tipo real sin cambiar firmas
    string iconHint = disk.Icon;

    if (disk.IsNvme)
        iconHint = "nvme";
    else if (disk.IsUsb)
        iconHint = "usb";
    else if (disk.IsIscsi)
        iconHint = "iscsi";
    else if (disk.IsVirtual)
        iconHint = "virtual";

    var fixedIcon = DiskIconService.GetIcon(iconHint, disk.Model, disk.IsRotational);
    var icon = DiskIconHelper.LoadImage(fixedIcon, 80);
    icon.Stretch = Stretch.Uniform;

    var iconContainer = new Border
    {
        Width = 80,
        Height = 80,
        Margin = new Thickness(0, 0, 16, 0),
        VerticalAlignment = VerticalAlignment.Top,
        Child = icon
    };

    var details = new Grid();
    for (var i = 0; i < 6; i++)
        details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

    var r = 0;

    void AddRow(Control c)
    {
        details.Children.Add(c);
        Grid.SetRow(c, r++);
    }

    AddRow(new TextBlock
    {
        Text = disk.Model,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 16,
        FontWeight = FontWeight.Bold,
        Foreground = _bmwTextBrush
    });

    AddRow(new TextBlock
    {
        Text = $"/dev/{disk.Name}  •  {disk.Size}",
        Foreground = _bmwTextDimBrush
    });

    AddRow(new TextBlock
    {
        Text = $"Type: {type}",
        Foreground = _bmwTextDimBrush
    });

    AddRow(new TextBlock
    {
        Text = $"Serial: {disk.Serial}",
        Foreground = _bmwTextDimBrush
    });

    AddRow(new TextBlock
    {
        Text = $"Temperature: {temperature}",
        Foreground = _bmwTextDimBrush
    });

    // ⭐ Nuevo: mostrar estado SMART real
    AddRow(new TextBlock
    {
        Text = $"Status: {disk.SmartStatus}",
        Foreground = _bmwAccentBrush
    });

    var btnMore = new Button
    {
        Content = "More",
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Height = 32,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top
    };
    btnMore.Classes.Add("MoreButton");

    if (!disk.IsSystemDisk)
    {
        var menu = BuildDiskContextMenu(disk);

        foreach (var item in menu.Items.OfType<MenuItem>())
            item.Click += (_, _) =>
            {
                var action = item.Tag?.ToString();
                OnMenuClick(disk, action);
            };

        btnMore.ContextMenu = menu;

        btnMore.Click += (_, _) =>
        {
            menu.PlacementTarget = btnMore;
            menu.Open(btnMore);
        };
    }
    else
    {
        btnMore.IsVisible = false;
    }

    var grid = new Grid
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Auto)
        }
    };

    grid.Children.Add(iconContainer);
    Grid.SetColumn(iconContainer, 0);
    Grid.SetRowSpan(iconContainer, r);

    grid.Children.Add(details);
    Grid.SetColumn(details, 1);

    grid.Children.Add(btnMore);
    Grid.SetColumn(btnMore, 2);
    Grid.SetRow(btnMore, 0);

    return new Border
    {
        Background = _bmwSurfaceElevatedBrush,
        BorderBrush = _bmwBorderBrush,
        BorderThickness = _cardBorderThickness,
        CornerRadius = _cardCornerRadius,
        Padding = _cardPadding,
        Margin = _cardMargin,
        Child = grid
    };
}

    
    private ContextMenu BuildDiskContextMenu(RaidDiskInfo disk)
    {
        var menu = new ContextMenu();
        var items = new List<MenuItem>();

        // Siempre disponibles
        items.Add(new MenuItem { Header = "View Info", Tag = "info" });
        items.Add(new MenuItem { Header = "SMART", Tag = "smart" });

        // 1) Discos del sistema → solo info + smart
        if (disk.IsSystemDisk || SystemDiskDetector.IsSystemDisk(disk.Name))
        {
            menu.ItemsSource = items;
            return menu;
        }

        // 2) Discos RAID activos → bloquear destructivas
        if (disk.IsUsedByRaid &&
            !disk.Role.Equals("removed", StringComparison.OrdinalIgnoreCase))
        {
            menu.ItemsSource = items;
            return menu;
        }

        // 3) Si el propio disco está montado → solo unmount
        if (disk.IsMounted)
        {
            items.Add(new MenuItem { Header = "Unmount", Tag = "unmount" });
            menu.ItemsSource = items;
            return menu;
        }

        // 4) Si alguna partición hija está montada → también bloquear destructivas
        if (HasMountedChildren(disk))
        {
            // No hay unmount directo a nivel disco, así que solo info + smart
            menu.ItemsSource = items;
            return menu;
        }

        // 5) A partir de aquí:
        //    - No es sistema
        //    - No está en RAID activo (solo removed o fuera de RAID)
        //    - No está montado
        //    - No tiene particiones montadas
        //    → se permiten operaciones destructivas
        items.Add(new MenuItem { Header = "Wipe Disk", Tag = "wipe" });
        items.Add(new MenuItem { Header = "Zero Superblock", Tag = "zerosb" });
        items.Add(new MenuItem { Header = "Create Partition Table", Tag = "ptable" });
        items.Add(new MenuItem { Header = "Initialize", Tag = "init" });

        menu.ItemsSource = items;
        return menu;
    }


    private static bool HasMountedChildren(RaidDiskInfo disk)
    {
        try
        {
            if (disk.Children == null || disk.Children.Count == 0)
                return false;

            if (!File.Exists("/proc/mounts"))
                return false;

            var lines = File.ReadAllLines("/proc/mounts");

            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var dev = parts[0];

                // /dev/sdX1, /dev/nvme0n1p1, etc.
                foreach (var child in disk.Children)
                {
                    if (dev.Equals($"/dev/{child}", StringComparison.Ordinal))
                        return true;
                }
            }
        }
        catch
        {
            // En caso de duda, mejor ser conservador
            return true;
        }

        return false;
    }


    

    private async void OnMenuClick(RaidDiskInfo disk, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return;

        var act = action.Trim().ToLowerInvariant();

        bool IsDestructive(string a) =>
            a is "wipe" or "zerosb" or "ptable" or "init";

        if (IsDestructive(act))
        {
            if (disk.IsSystemDisk)
            {
                await ShowInfoDialog("Error", "This disk belongs to the operating system.");
                return;
            }

            if (disk.IsUsedByRaid)
            {
                await ShowInfoDialog("Error", "This disk is part of a RAID array.");
                return;
            }
        }

        if (disk.IsMounted &&
            act is not ("unmount" or "info" or "smart"))
        {
            await ShowInfoDialog("Error", "Unmount the disk first.");
            return;
        }

        switch (act)
        {
            case "info":
                await ShowInfo(disk);
                break;

            case "smart":
                await RunSmart(disk);
                break;

            case "unmount":
                await UnmountDisk(disk);
                break;

            case "wipe":
                await WipeDisk(disk);
                break;

            case "zerosb":
                await ZeroSuperblock(disk);
                break;

            case "ptable":
                await CreatePartitionTable(disk);
                break;

            case "init":
                await InitializeDisk(disk);
                break;
        }
    }

    private async Task<bool> ShowConfirm(string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);
        var owner = this.GetVisualRoot() as Window;
        return owner != null
            ? await dlg.ShowDialog<bool>(owner)
            : await dlg.ShowDialog<bool>(new Window());
    }

    private async Task ShowInfoDialog(string title, string message)
    {
        var dlg = new InfoDialog(title, message);
        var owner = this.GetVisualRoot() as Window;
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            await dlg.ShowDialog(new Window());
    }

    private async Task ShowConsoleDialog(string title, string text)
    {
        var dlg = new ConsoleDialog(title, text);
        var owner = this.GetVisualRoot() as Window;
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            await dlg.ShowDialog(new Window());
    }

    private async Task ShowInfo(RaidDiskInfo disk)
    {
        string temperature = GetTemperature(disk);

        string type =
            disk.IsNvme ? "NVMe" :
            !disk.IsRotational ? "SSD" :
            "HDD";

        var msg =
            $"Model: {disk.Model}\n" +
            $"Size: {disk.Size}\n" +
            $"Serial: {disk.Serial}\n" +
            $"Type: {type}\n" +
            $"Temperature: {temperature}\n" +
            $"Filesystem: {disk.FsType}\n" +
            $"Mount: {disk.MountPath}\n" +
            $"RAID: {(disk.IsUsedByRaid ? disk.ArrayName : "No")}";

        await ShowInfoDialog("Disk Information", msg);
    }

    private async Task RunSmart(RaidDiskInfo disk)
    {
        var r = ShellHelper.EjecutarComoRoot($"smartctl -a /dev/{disk.Name}");
        var output = (r.Stdout + "\n" + r.Stderr).Trim();
        await ShowConsoleDialog($"SMART — /dev/{disk.Name}", output);
    }

    private string GetTemperature(RaidDiskInfo disk)
    {
        try
        {
            var r = ShellHelper.EjecutarComoRoot($"smartctl -A /dev/{disk.Name}");
            var text = (r.Stdout + "\n" + r.Stderr).Trim();

            foreach (var line in text.Split('\n'))
            {
                var lower = line.ToLowerInvariant();

                if (lower.Contains("temperature_celsius") ||
                    lower.Contains("temperature_internal") ||
                    lower.Contains("current drive temperature") ||
                    lower.Contains("temperature:"))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var p in parts.Reverse())
                    {
                        if (int.TryParse(p.Replace("°C", "").Replace("C", ""), out var temp))
                            return $"{temp}°C";
                    }
                }
            }
        }
        catch
        {
        }

        return "N/A";
    }

    private async Task UnmountDisk(RaidDiskInfo disk)
    {
        if (string.IsNullOrWhiteSpace(disk.MountPath))
        {
            await ShowInfoDialog("Unmount", "This disk is not mounted.");
            return;
        }

        var ok = MountService.Unmount(disk.MountPath);

        if (!ok)
        {
            await ShowInfoDialog("Error", "Could not unmount the disk.");
            return;
        }

        await ShowInfoDialog("Unmount", "Disk unmounted successfully.");

        await LoadRealDataAsync();

        RenderDisks();
    }

    private async Task WipeDisk(RaidDiskInfo disk)
    {
        var confirm = await ShowConfirm("Wipe Disk", $"This will erase ALL signatures on /dev/{disk.Name}. Continue?");
        if (!confirm) return;

        var r = ShellHelper.EjecutarComoRoot($"wipefs -a /dev/{disk.Name}");

        if (r.ExitCode != 0)
        {
            await ShowInfoDialog("Error", "Could not wipe the disk.");
            return;
        }

        await ShowInfoDialog("Wipe Disk", "Disk wiped successfully.");

        await LoadRealDataAsync();

        RenderDisks();
    }

    private async Task ZeroSuperblock(RaidDiskInfo disk)
    {
        var confirm = await ShowConfirm("Zero Superblock", $"Remove RAID metadata from /dev/{disk.Name}?");
        if (!confirm) return;

        var r = ShellHelper.EjecutarComoRoot($"mdadm --zero-superblock /dev/{disk.Name}");

        if (r.ExitCode != 0)
        {
            await ShowInfoDialog("Error", "Could not zero the superblock.");
            return;
        }

        await ShowInfoDialog("Zero Superblock", "Superblock removed.");

        await LoadRealDataAsync();

        RenderDisks();
    }

    private async Task CreatePartitionTable(RaidDiskInfo disk)
    {
        var confirm = await ShowConfirm("Create Partition Table",
            $"Create a new GPT table on /dev/{disk.Name}? This erases ALL partitions.");
        if (!confirm) return;

        var r = ShellHelper.EjecutarComoRoot($"parted -s /dev/{disk.Name} mklabel gpt");

        if (r.ExitCode != 0)
        {
            await ShowInfoDialog("Error", "Could not create partition table.");
            return;
        }

        await ShowInfoDialog("Partition Table", "GPT partition table created.");

        await LoadRealDataAsync();

        RenderDisks();
    }

    private async Task InitializeDisk(RaidDiskInfo disk)
    {
        var dlg = new InitializeDiskDialog();
        var owner = this.GetVisualRoot() as Window;

        var result = await dlg.ShowDialog<InitializeDiskResult?>(owner);
        if (result == null)
            return;

        var label = result.Label;
        var fs = result.FileSystem;

        var part = $"/dev/{disk.Name}1";
        var mountPoint = $"/mnt/raid-util/{label}";

        using (LoadingService.Show($"Initializing /dev/{disk.Name}..."))
        {
            await Task.Run(() =>
            {
               
                ///////////////
                                var r1 = ShellHelper.EjecutarComoRoot($"parted -s /dev/{disk.Name} mklabel gpt");
                if (r1.ExitCode != 0)
                    throw new Exception("Failed to create partition table.");

                // Crear partición
                var r2 = ShellHelper.EjecutarComoRoot($"parted -s /dev/{disk.Name} mkpart primary 0% 100%");
                if (r2.ExitCode != 0)
                    throw new Exception("Failed to create partition.");

                // Notificar al kernel
                ShellHelper.EjecutarComoRoot("partprobe");
                ShellHelper.EjecutarComoRoot("udevadm settle");

                // Esperar a que /dev/sdX1 exista
                for (int i = 0; i < 20; i++)
                {
                    if (File.Exists(part))
                        break;
                    Thread.Sleep(200);
                }

                if (!File.Exists(part))
                    throw new Exception($"Partition {part} not detected.");

                // Crear filesystem
                var cmd = fs switch
                {
                    "ext4" => $"mkfs.ext4 -F -L \"{label}\" {part}",
                    "xfs" => $"mkfs.xfs -f -L \"{label}\" {part}",
                    "btrfs" => $"mkfs.btrfs -f -L \"{label}\" {part}",
                    "f2fs" => $"mkfs.f2fs -f -l \"{label}\" {part}",
                    "ntfs" => $"mkfs.ntfs -f -L \"{label}\" {part}",
                    "exfat" => $"mkfs.exfat -n \"{label}\" {part}",
                    "fat32" => $"mkfs.vfat -F 32 -n \"{label}\" {part}",
                    _ => throw new Exception("Unsupported filesystem")
                };

                var r3 = ShellHelper.EjecutarComoRoot(cmd);
                if (r3.ExitCode != 0)
                    throw new Exception("Filesystem creation failed.");

                // Crear directorio de montaje
                ShellHelper.EjecutarComoRoot($"mkdir -p \"{mountPoint}\"");

                // Montar
                MountService.Unmount(mountPoint);
                MountService.Mount(part, mountPoint);

                // Permisos
                ShellHelper.EjecutarComoRoot($"chmod 775 \"{mountPoint}\"");
            });
        }

        await ShowInfoDialog(
            "Disk Initialized",
            $"Disk /dev/{disk.Name} initialized successfully.\n\nFS: {fs}\nLabel: {label}\nMount: {mountPoint}"
        );

        await LoadRealDataAsync();

        RenderDisks();
    }
}
