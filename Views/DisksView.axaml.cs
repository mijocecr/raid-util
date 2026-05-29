using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RAID_Util.Helpers;
using RAID_Util.Models;
using RAID_Util.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using RAID_Util.Core;

namespace RAID_Util.Views
{
    public partial class DisksView : UserControl
    {
        private readonly List<RaidDiskInfo> _disks = new();

        public DisksView()
        {
            InitializeComponent();
            LogService.Write("[DISKSVIEW] Constructor ejecutado.");
        }

        public void Initialize(bool sudoReady, bool forceFake)
        {
            LogService.Write($"[DISKSVIEW] Initialize() sudoReady={sudoReady}, forceFake={forceFake}");

            _disks.Clear();

            if (!sudoReady)
            {
                LogService.Write("[DISKSVIEW] sudoReady = false → usando fake data.");
                LoadFakeData();
            }
            else
            {
                if (forceFake)
                {
                    LogService.Write("[DISKSVIEW] FORCE_FAKE_DATA = true → usando fake data.");
                    LoadFakeData();
                }
                else
                {
                    LogService.Write("[DISKSVIEW] Modo real → cargando discos reales.");
                    LoadRealData();
                }
            }

            RenderDisks();
        }

        // ============================================================
        // FAKE DATA
        // ============================================================
        private void LoadFakeData()
        {
            LogService.Debug("[DISKSVIEW] LoadFakeData() iniciado.");

            _disks.Clear();

            _disks.Add(new RaidDiskInfo
            {
                Name = "sdb",
                Model = "Samsung SSD 860 EVO",
                Size = "500GB",
                Type = "SSD SATA",
                Serial = "S3Z9NX0M123456A",
                Temperature = "34°C",
                State = "Free",
                Icon = DiskIconService.GetIcon(null, "SSD SATA", false)
            });

            _disks.Add(new RaidDiskInfo
            {
                Name = "sdc",
                Model = "Seagate Barracuda",
                Size = "2TB",
                Type = "HDD 7200 RPM",
                Serial = "ZDH12X0A0001",
                Temperature = "41°C",
                State = "Free",
                Icon = DiskIconService.GetIcon(null, "HDD", true)
            });

            LogService.Debug("[DISKSVIEW] LoadFakeData() completado.");
        }

        // ============================================================
        // REAL DATA
        // ============================================================
        private void LoadRealData()
        {
            LogService.Debug("[DISKSVIEW] LoadRealData() iniciado.");

            _disks.Clear();

            try
            {
                var list = DiskService.GetAllDisks();
                LogService.Write($"[DISKSVIEW] Discos detectados: {list.Count}");

                foreach (var d in list)
                {
                    LogService.Debug($"[DISKSVIEW] Disco detectado: {d.Name} Model={d.Model} Size={d.Size} RAID={d.IsUsedByRaid} Mounted={d.IsMounted}");

                    // Normalizar estado
                    if (string.IsNullOrWhiteSpace(d.State))
                        d.State = d.IsUsedByRaid ? "Used by RAID" : "Free";

                    // ⭐ Normalizar icono con DiskIconService
                    d.Icon = DiskIconService.GetIcon(d.Icon, d.Model, d.IsRotational);

                    _disks.Add(d);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[DISKSVIEW] Error cargando discos reales: {ex}");
            }

            LogService.Debug("[DISKSVIEW] LoadRealData() completado.");
        }

        // ============================================================
        // RENDERIZAR TARJETAS
        // ============================================================
        private void RenderDisks()
        {
            LogService.Debug("[DISKSVIEW] RenderDisks() ejecutado.");

            DiskList.Children.Clear();

            foreach (var disk in _disks)
            {
                LogService.Debug($"[DISKSVIEW] Renderizando tarjeta para {disk.Name}");
                DiskList.Children.Add(BuildDiskCard(disk));
            }
        }

        // ============================================================
        // TARJETA DE DISCO
        // ============================================================
        
private Border BuildDiskCard(RaidDiskInfo disk)
{
    LogService.Debug($"[DISKSVIEW] BuildDiskCard() → {disk.Name}");

    // ⭐ Normalizar icono SIEMPRE antes de cargarlo
    string fixedIcon = DiskIconService.GetIcon(disk.Icon, disk.Model, disk.IsRotational);

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

    details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

    int r = 0;

    details.Children.Add(new TextBlock
    {
        Text = disk.Model,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 16,
        FontWeight = FontWeight.Bold,
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextBrush")!
    });
    Grid.SetRow(details.Children[^1], r++);

    details.Children.Add(new TextBlock
    {
        Text = $"/dev/{disk.Name}  •  {disk.Size}",
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });
    Grid.SetRow(details.Children[^1], r++);

    details.Children.Add(new TextBlock
    {
        Text = $"Type: {disk.Type}",
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });
    Grid.SetRow(details.Children[^1], r++);

    details.Children.Add(new TextBlock
    {
        Text = $"Serial: {disk.Serial}",
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });
    Grid.SetRow(details.Children[^1], r++);

    details.Children.Add(new TextBlock
    {
        Text = $"Temperature: {disk.Temperature}",
        Foreground = (IBrush)Application.Current!.FindResource("BMWTextDimBrush")!
    });
    Grid.SetRow(details.Children[^1], r++);

    details.Children.Add(new TextBlock
    {
        Text = $"Status: {disk.Status}",
        Foreground = (IBrush)Application.Current!.FindResource("BMWAccentBrush")!
    });
    Grid.SetRow(details.Children[^1], r++);

    var btnMore = new Button
    {
        Content = "More",
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Width = 70,
        Height = 32,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top
    };

    btnMore.Classes.Add("MoreButton");

    var menu = BuildContextMenu(disk);

    btnMore.Click += (_, _) =>
    {
        LogService.Debug($"[DISKSVIEW] Abriendo menú More para {disk.Name}");
        menu.PlacementTarget = btnMore;
        menu.Open(btnMore);
    };

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
        Background = (IBrush)Application.Current!.FindResource("BMWSurfaceElevatedBrush")!,
        BorderBrush = (IBrush)Application.Current!.FindResource("BMWBorderBrush")!,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 0, 10),
        Child = grid
    };
}

        // ============================================================
        // MENÚ MORE
        // ============================================================
        private ContextMenu BuildContextMenu(RaidDiskInfo disk)
        {
            LogService.Debug($"[DISKSVIEW] BuildContextMenu() para {disk.Name}");

            var menu = new ContextMenu();

            var items = new List<MenuItem>
            {
                new MenuItem { Header = "View Info", Tag = "info" },
                new MenuItem { Header = "SMART", Tag = "smart" },
                new MenuItem { Header = "Unmount", Tag = "unmount" },
                new MenuItem { Header = "Wipe Disk", Tag = "wipe" },
                new MenuItem { Header = "Zero Superblock", Tag = "zerosb" },
                new MenuItem { Header = "Create Partition Table", Tag = "ptable" },
                new MenuItem { Header = "Initialize", Tag = "init" }
            };

            foreach (var mi in items)
                mi.Click += (_, _) => OnMenuClick(disk, mi.Tag?.ToString());

            menu.ItemsSource = items;

            return menu;
        }

        // ============================================================
        // ACCIONES DEL MENÚ MORE
        // ============================================================
        private async void OnMenuClick(RaidDiskInfo disk, string? action)
        {
            LogService.Write($"[DISKSVIEW] Acción '{action}' solicitada sobre {disk.Name}");

            if (string.IsNullOrWhiteSpace(action))
            {
                LogService.Error("[DISKSVIEW] Acción nula recibida.");
                return;
            }

            // ============================================================
            // PROTECCIONES CRÍTICAS
            // ============================================================
            if (disk.IsSystemDisk)
            {
                LogService.Error($"[DISKSVIEW] BLOQUEADO: {disk.Name} es disco del sistema.");
                await ShowInfoDialog("Error", "This disk belongs to the operating system. Action blocked.");
                return;
            }

            if (disk.IsUsedByRaid &&
                (action == "wipe" || action == "zerosb" || action == "ptable" || action == "init"))
            {
                LogService.Error($"[DISKSVIEW] BLOQUEADO: {disk.Name} pertenece a RAID {disk.ArrayName}.");
                await ShowInfoDialog("Error", "This disk is part of a RAID array. Action blocked.");
                return;
            }

            // ============================================================
            // ACCIONES
            // ============================================================
            switch (action)
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

                default:
                    LogService.Error($"[DISKSVIEW] Acción desconocida: {action}");
                    await ShowInfoDialog("Error", $"Unknown action: {action}");
                    break;
            }
        }

        // ============================================================
        // DIÁLOGOS BMW
        // ============================================================
        private async Task<bool> ShowConfirm(string title, string message)
        {
            LogService.Debug($"[DISKSVIEW] ConfirmDialog: {title}");

            var dlg = new ConfirmDialog(title, message);
            var owner = this.GetVisualRoot() as Window;

            if (owner != null)
                return await dlg.ShowDialog<bool>(owner);

            return await dlg.ShowDialog<bool>(new Window());
        }

        private async Task ShowInfoDialog(string title, string message)
        {
            LogService.Debug($"[DISKSVIEW] InfoDialog: {title}");

            var dlg = new InfoDialog(title, message);
            var owner = this.GetVisualRoot() as Window;

            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                await dlg.ShowDialog(new Window());
        }

        // ⭐ NUEVO: DIÁLOGO TIPO CONSOLA
        private async Task ShowConsoleDialog(string title, string text)
        {
            var dlg = new ConsoleDialog(title, text);
            var owner = this.GetVisualRoot() as Window;

            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                await dlg.ShowDialog(new Window());
        }

        // ============================================================
        // ACCIONES REALES
        // ============================================================
        private async Task ShowInfo(RaidDiskInfo disk)
        {
            LogService.Write($"[DISKSVIEW] View Info → {disk.Name}");

            string msg =
                $"Model: {disk.Model}\n" +
                $"Size: {disk.Size}\n" +
                $"Serial: {disk.Serial}\n" +
                $"Type: {disk.Type}\n" +
                $"Temperature: {disk.Temperature}\n" +
                $"Filesystem: {disk.Filesystem}\n" +
                $"Mount: {disk.MountPoint}\n" +
                $"RAID: {(disk.IsUsedByRaid ? disk.ArrayName : "No")}";

            await ShowInfoDialog("Disk Information", msg);
        }

        private async Task RunSmart(RaidDiskInfo disk)
        {
            LogService.Write($"[DISKSVIEW] SMART → {disk.Name}");

            var r = ShellHelper.EjecutarComoRoot($"smartctl -a /dev/{disk.Name}");
            string output = (r.Stdout + "\n" + r.Stderr).Trim();

            await ShowConsoleDialog($"SMART — /dev/{disk.Name}", output);
        }

        private async Task UnmountDisk(RaidDiskInfo disk)
        {
            LogService.Write($"[DISKSVIEW] Unmount → {disk.Name}");

            if (string.IsNullOrWhiteSpace(disk.MountPoint))
            {
                await ShowInfoDialog("Unmount", "This disk is not mounted.");
                return;
            }

            bool ok = MountService.Unmount(disk.MountPoint);

            if (!ok)
            {
                LogService.Error($"[DISKSVIEW] Error unmounting {disk.Name}");
                await ShowInfoDialog("Error", "Could not unmount the disk.");
                return;
            }

            await ShowInfoDialog("Unmount", "Disk unmounted successfully.");

            LoadRealData();
            RenderDisks();
        }

        private async Task WipeDisk(RaidDiskInfo disk)
        {
            LogService.Write($"[DISKSVIEW] Wipe Disk → {disk.Name}");

            bool confirm = await ShowConfirm("Wipe Disk", $"This will erase ALL signatures on /dev/{disk.Name}. Continue?");
            if (!confirm) return;

            var r = ShellHelper.EjecutarComoRoot($"wipefs -a /dev/{disk.Name}");

            if (r.ExitCode != 0)
            {
                LogService.Error($"[DISKSVIEW] wipefs error: {r.Stderr}");
                await ShowInfoDialog("Error", "Could not wipe the disk.");
                return;
            }

            await ShowInfoDialog("Wipe Disk", "Disk wiped successfully.");

            LoadRealData();
            RenderDisks();
        }

        private async Task ZeroSuperblock(RaidDiskInfo disk)
        {
            LogService.Write($"[DISKSVIEW] Zero Superblock → {disk.Name}");

            bool confirm = await ShowConfirm("Zero Superblock", $"Remove RAID metadata from /dev/{disk.Name}?");
            if (!confirm) return;

            var r = ShellHelper.EjecutarComoRoot($"mdadm --zero-superblock /dev/{disk.Name}");

            if (r.ExitCode != 0)
            {
                LogService.Error($"[DISKSVIEW] zerosb error: {r.Stderr}");
                await ShowInfoDialog("Error", "Could not zero the superblock.");
                return;
            }

            await ShowInfoDialog("Zero Superblock", "Superblock removed.");

            LoadRealData();
            RenderDisks();
        }

        private async Task CreatePartitionTable(RaidDiskInfo disk)
        {
            LogService.Write($"[DISKSVIEW] Create Partition Table → {disk.Name}");

            bool confirm = await ShowConfirm("Create Partition Table", $"Create a new GPT table on /dev/{disk.Name}? This erases ALL partitions.");
            if (!confirm) return;

            var r = ShellHelper.EjecutarComoRoot($"parted -s /dev/{disk.Name} mklabel gpt");

            if (r.ExitCode != 0)
            {
                LogService.Error($"[DISKSVIEW] parted error: {r.Stderr}");
                await ShowInfoDialog("Error", "Could not create partition table.");
                return;
            }

            await ShowInfoDialog("Partition Table", "GPT partition table created.");

            LoadRealData();
            RenderDisks();
        }

        private async Task InitializeDisk(RaidDiskInfo disk)
        {
            LogService.Write($"[DISKSVIEW] Initialize Disk → {disk.Name}");

            bool confirm = await ShowConfirm("Initialize Disk", $"This will create a new partition and filesystem on /dev/{disk.Name}. Continue?");
            if (!confirm) return;

            ShellHelper.EjecutarComoRoot($"parted -s /dev/{disk.Name} mklabel gpt");
            ShellHelper.EjecutarComoRoot($"parted -s /dev/{disk.Name} mkpart primary ext4 0% 100%");
            ShellHelper.EjecutarComoRoot($"mkfs.ext4 -F /dev/{disk.Name}1");

            await ShowInfoDialog("Initialize Disk", "Disk initialized successfully.");

            LoadRealData();
            RenderDisks();
        }
    }
}
