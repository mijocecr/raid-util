using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RAID_Util.Helpers;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Views
{
    public partial class ArrayConfigWindow : Window
    {
        private readonly string _arrayName;
        private readonly ArrayConfig _config;

        public ArrayConfigWindow(string arrayName)
        {
            InitializeComponent();

            _arrayName = arrayName;
            _config = ArrayConfigService.Load(arrayName);

            LoadUI();
            HookEvents();
        }

        private void LoadUI()
        {
            // Identity
            TxtName.Text = _config.Name;
            TxtFsLabel.Text = _config.FsLabel;

            // Mount
            TxtMountPoint.Text = _config.MountPoint;

            TgNoAtime.IsChecked = _config.Mount_NoAtime;
            TgNoDirAtime.IsChecked = _config.Mount_NoDirAtime;
            TgDiscard.IsChecked = _config.Mount_Discard;
            TgSync.IsChecked = _config.Mount_Sync;
            TgReadOnly.IsChecked = _config.Mount_ReadOnly;

            TgAutoMount.IsChecked = _config.AutoMount;

            // ⭐ NEW: Mount permissions
            TxtMountPermissions.Text = _config.MountPermissions;

            // Performance
            NumResyncPriority.Value = _config.ResyncPriority;
            NumResyncMaxSpeed.Value = _config.ResyncMaxSpeed;

            // Alerts
            TgAlertDegraded.IsChecked = _config.AlertDegraded;
            TgAlertDiskFail.IsChecked = _config.AlertDiskFail;
            TgAlertSlowResync.IsChecked = _config.AlertSlowResync;
        }

        private void HookEvents()
        {
            BtnSave.Click += OnSaveClicked;
            BtnCancel.Click += OnCancelClicked;
            BtnPickMountPoint.Click += OnPickMountPointClicked;
        }

        private async Task ShowError(string message)
        {
            var dlg = new Window
            {
                Width = 380,
                Height = 140,
                Background = this.Background,
                Foreground = this.Foreground,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "Validation Error",
                CanResize = false
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12
            };

            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = this.Foreground,
                TextWrapping = TextWrapping.Wrap
            });

            var btn = new Button
            {
                Content = "OK",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            btn.Click += (_, _) => dlg.Close();

            panel.Children.Add(btn);
            dlg.Content = panel;

            await dlg.ShowDialog(this);
        }

        private string BuildMountOptions()
        {
            var opts = new List<string>();

            // ⭐ Permitir desmontar sin sudo (cualquier usuario)
            opts.Add("users");

            if (TgNoAtime.IsChecked == true) opts.Add("noatime");
            if (TgNoDirAtime.IsChecked == true) opts.Add("nodiratime");
            if (TgDiscard.IsChecked == true) opts.Add("discard");
            if (TgSync.IsChecked == true) opts.Add("sync");
            if (TgReadOnly.IsChecked == true) opts.Add("ro");

            // Si solo está "users", añadimos defaults
            if (opts.Count == 1)
                opts.Add("defaults");

            return string.Join(",", opts);
        }


        private async void OnPickMountPointClicked(object? sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select mount point"
            };

            var result = await dialog.ShowAsync(this);

            if (!string.IsNullOrWhiteSpace(result))
                TxtMountPoint.Text = result;
        }

        private async void OnSaveClicked(object? sender, RoutedEventArgs e)
        {
            // ============================
            // 1) VALIDACIÓN DE NOMBRE
            // ============================
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                await ShowError("Array name cannot be empty.");
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(TxtName.Text, @"^[a-zA-Z0-9\-_]+$"))
            {
                await ShowError("Array name contains invalid characters.");
                return;
            }

            // ============================
            // 2) VALIDACIÓN DE MOUNT POINT
            // ============================
            if (TgAutoMount.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(TxtMountPoint.Text))
                {
                    await ShowError("Mount point is required when AutoMount is enabled.");
                    return;
                }

                if (!TxtMountPoint.Text.StartsWith("/"))
                {
                    await ShowError("Mount point must be an absolute path.");
                    return;
                }

                if (TxtMountPoint.Text == "/" ||
                    TxtMountPoint.Text == "/home" ||
                    TxtMountPoint.Text == "/usr" ||
                    TxtMountPoint.Text == "/root")
                {
                    await ShowError("This mount point is not allowed.");
                    return;
                }
            }

            // ============================
            // 3) OPCIONES INCOMPATIBLES
            // ============================
            if (TgReadOnly.IsChecked == true && TgDiscard.IsChecked == true)
            {
                await ShowError("discard cannot be used on read-only mounts.");
                return;
            }

            // ============================
            // 4) VALIDACIÓN DE PERFORMANCE
            // ============================
            if (NumResyncPriority.Value is null ||
                NumResyncPriority.Value < 1 || NumResyncPriority.Value > 200000)
            {
                await ShowError("Resync priority is out of range (1–200000).");
                return;
            }

            if (NumResyncMaxSpeed.Value is null ||
                NumResyncMaxSpeed.Value < 100 || NumResyncMaxSpeed.Value > 500000)
            {
                await ShowError("Resync max speed is out of range (100–500000).");
                return;
            }

            // ============================
            // ⭐ 5) VALIDACIÓN DE PERMISOS
            // ============================
            if (string.IsNullOrWhiteSpace(TxtMountPermissions.Text))
            {
                await ShowError("Permissions cannot be empty.");
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(TxtMountPermissions.Text, @"^[0-7]{3}$"))
            {
                await ShowError("Permissions must be a 3‑digit octal value (e.g., 755, 775, 777).");
                return;
            }

            // ============================
            // 6) SI  ES VÁLIDO → GUARDAR CONFIG
            // ============================

            // Identity
            _config.Name = TxtName.Text ?? "";
            _config.FsLabel = TxtFsLabel.Text ?? "";

            // Mount
            _config.MountPoint = TxtMountPoint.Text ?? "";
            _config.Mount_NoAtime = TgNoAtime.IsChecked ?? false;
            _config.Mount_NoDirAtime = TgNoDirAtime.IsChecked ?? false;
            _config.Mount_Discard = TgDiscard.IsChecked ?? false;
            _config.Mount_Sync = TgSync.IsChecked ?? false;
            _config.Mount_ReadOnly = TgReadOnly.IsChecked ?? false;
            _config.AutoMount = TgAutoMount.IsChecked ?? false;

            // ⭐ NEW: Mount permissions
            _config.MountPermissions = TxtMountPermissions.Text.Trim();

            // Performance
            _config.ResyncPriority = (int)NumResyncPriority.Value!;
            _config.ResyncMaxSpeed = (int)NumResyncMaxSpeed.Value!;

            // Alerts
            _config.AlertDegraded = TgAlertDegraded.IsChecked ?? false;
            _config.AlertDiskFail = TgAlertDiskFail.IsChecked ?? false;
            _config.AlertSlowResync = TgAlertSlowResync.IsChecked ?? false;

            ArrayConfigService.Save(_arrayName, _config);

            // ============================
            // 7) MONTAJE / DESMONTAJE REAL
            // ============================

            string device = $"/dev/{_arrayName}";
            string mountPoint = _config.MountPoint;
            string options = BuildMountOptions();

            if (_config.AutoMount)
            {
                if (!System.IO.File.Exists(device))
                {
                    await ShowError($"Device {device} does not exist.");
                    return;
                }

                bool ok = MountService.Mount(device, mountPoint, options);
                if (!ok)
                {
                    await ShowError("Failed to mount the array.");
                    return;
                }

                // ⭐ Apply permissions immediately
                ShellHelper.EjecutarComoRoot($"chmod {_config.MountPermissions} {mountPoint}");
            }
            else
            {
                MountService.Unmount(mountPoint);
            }

            // ============================
            // 8) ESCRITURA EN /etc/fstab
            // ============================

            try
            {
                string fs = FstabService.DetectFilesystem(device);

                if (string.IsNullOrWhiteSpace(fs))
                {
                    await ShowError("Could not detect filesystem type.");
                    return;
                }

                if (_config.AutoMount)
                {
                    FstabService.WriteEntry(device, mountPoint, fs, options);
                }
                else
                {
                    FstabService.RemoveEntry(device);
                }
            }
            catch (Exception ex)
            {
                await ShowError("Failed to update /etc/fstab:\n" + ex.Message);
                return;
            }

            // ============================
            // 9) APLICAR CONFIG RAID REAL
            // ============================
            try
            {
                MdadmService.ApplyConfig(_arrayName, _config);
            }
            catch (Exception ex)
            {
                await ShowError("Failed to apply RAID settings:\n" + ex.Message);
                return;
            }

            Close();
        }

        private void OnCancelClicked(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
