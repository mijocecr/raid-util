using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using RAID_Util.Models;
using RAID_Util.Services;
using RAID_Util.Core;

namespace RAID_Util.Views;

public partial class ArrayConfigWindow : Window
{
    private readonly RaidArrayInfo _array;
    private readonly ArrayConfig _config;

    public ArrayConfigWindow(RaidArrayInfo array)
    {
        InitializeComponent();

        _array = array;
        _config = ArrayConfigService.Load(array.BaseName);

        LogThread("CTOR");

        LoadUI();
        HookEvents();
    }

    private void Log(string msg)
    {
        Console.WriteLine($"[CFG] {msg}");
    }

    private void LogThread(string tag)
    {
        Console.WriteLine($"[THREAD] {tag} → ManagedThreadId={Environment.CurrentManagedThreadId}");
    }

    private void LoadUI()
    {
        LogThread("LoadUI");

        TxtName.Text = _config.Name;
        TxtFsLabel.Text = _config.FsLabel;

        TxtMountPoint.Text = _config.MountPoint;

        TgNoAtime.IsChecked = _config.Mount_NoAtime;
        TgNoDirAtime.IsChecked = _config.Mount_NoDirAtime;
        TgDiscard.IsChecked = _config.Mount_Discard;
        TgSync.IsChecked = _config.Mount_Sync;
        TgReadOnly.IsChecked = _config.Mount_ReadOnly;

        TgPersistMount.IsChecked = _config.PersistMount;

        TxtMountPermissions.Text = _config.MountPermissions;

        NumResyncPriority.Value = _config.ResyncPriority;
        NumResyncMaxSpeed.Value = _config.ResyncMaxSpeed;

        TgAlertDegraded.IsChecked = _config.AlertDegraded;
        TgAlertDiskFail.IsChecked = _config.AlertDiskFail;
        TgAlertSlowResync.IsChecked = _config.AlertSlowResync;
    }

    private void HookEvents()
    {
        LogThread("HookEvents");

        BtnSave.Click += OnSaveClicked;
        BtnCancel.Click += OnCancelClicked;
        BtnPickMountPoint.Click += OnPickMountPointClicked;
    }

    private async Task ShowError(string message)
    {
        Log($"ShowError: {message}");
        LogThread("ShowError");

        var dlg = new InfoDialog("Error", message);
        await dlg.ShowDialog(this);
    }

    private async Task ShowInfo(string title, string message)
    {
        Log($"ShowInfo: {title}");
        LogThread("ShowInfo");

        var dlg = new InfoDialog(title, message);
        await dlg.ShowDialog(this);
    }

    private string BuildMountOptions()
    {
        Log("BuildMountOptions()");
        LogThread("BuildMountOptions");

        var opts = new List<string>();

        if (TgNoAtime.IsChecked == true) opts.Add("noatime");
        if (TgNoDirAtime.IsChecked == true) opts.Add("nodiratime");
        if (TgDiscard.IsChecked == true) opts.Add("discard");
        if (TgSync.IsChecked == true) opts.Add("sync");
        if (TgReadOnly.IsChecked == true) opts.Add("ro");

        return opts.Count == 0 ? "defaults" : string.Join(",", opts);
    }

    private async void OnPickMountPointClicked(object? sender, RoutedEventArgs e)
    {
        Log("OnPickMountPointClicked()");
        LogThread("OnPickMountPointClicked");

        var dialog = new OpenFolderDialog { Title = "Select mount point" };
        var result = await dialog.ShowAsync(this);

        if (!string.IsNullOrWhiteSpace(result))
            TxtMountPoint.Text = result;
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        Log("OnSaveClicked()");
        LogThread("OnSaveClicked");

        // VALIDACIONES
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            await ShowError("Array name cannot be empty.");
            return;
        }

        if (!Regex.IsMatch(TxtName.Text, @"^[a-zA-Z0-9\-_]+$"))
        {
            await ShowError("Array name contains invalid characters.");
            return;
        }

        if (TgPersistMount.IsChecked == true)
        {
            var mp = TxtMountPoint.Text ?? "";

            if (string.IsNullOrWhiteSpace(mp))
            {
                await ShowError("Mount point is required when persistence is enabled.");
                return;
            }

            if (!mp.StartsWith("/"))
            {
                await ShowError("Mount point must be an absolute path.");
                return;
            }

            var forbidden = new[]
            {
                "/", "/home", "/usr", "/root", "/etc", "/dev", "/proc", "/sys", "/run", "/tmp"
            };

            if (Array.Exists(forbidden, x => x == mp))
            {
                await ShowError("This mount point is not allowed.");
                return;
            }

            if (!Directory.Exists(mp))
            {
                await ShowError("Mount point directory does not exist.");
                return;
            }
        }

        if (TgReadOnly.IsChecked == true && TgDiscard.IsChecked == true)
        {
            await ShowError("discard cannot be used on read-only mounts.");
            return;
        }

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

        if (string.IsNullOrWhiteSpace(TxtMountPermissions.Text) ||
            !Regex.IsMatch(TxtMountPermissions.Text, @"^[0-7]{3}$"))
        {
            await ShowError("Permissions must be a 3‑digit octal value (e.g., 755).");
            return;
        }

        // GUARDAR CONFIG EN MEMORIA (UI)
        Log("Saving config to memory");
        LogThread("SaveMemory");

        _config.Name = TxtName.Text ?? "";
        _config.FsLabel = TxtFsLabel.Text ?? "";

        _config.MountPoint = TxtMountPoint.Text ?? "";
        _config.Mount_NoAtime = TgNoAtime.IsChecked ?? false;
        _config.Mount_NoDirAtime = TgNoDirAtime.IsChecked ?? false;
        _config.Mount_Discard = TgDiscard.IsChecked ?? false;
        _config.Mount_Sync = TgSync.IsChecked ?? false;
        _config.Mount_ReadOnly = TgReadOnly.IsChecked ?? false;

        _config.PersistMount = TgPersistMount.IsChecked ?? false;

        _config.MountPermissions = TxtMountPermissions.Text.Trim();

        _config.ResyncPriority = (int)NumResyncPriority.Value!;
        _config.ResyncMaxSpeed = (int)NumResyncMaxSpeed.Value!;

        _config.AlertDegraded = TgAlertDegraded.IsChecked ?? false;
        _config.AlertDiskFail = TgAlertDiskFail.IsChecked ?? false;
        _config.AlertSlowResync = TgAlertSlowResync.IsChecked ?? false;

        BtnSave.IsEnabled = false;

        // ⚠️ AQUÍ VIENE EL CAMBIO CLAVE:
        // lo que dependa de la UI se calcula ANTES del Task.Run
        var device = _array.Path;
        var mountPoint = _config.MountPoint;
        var options = BuildMountOptions(); // ← ahora en el hilo de UI

        Exception? backgroundError = null;

        await Task.Run(() =>
        {
            try
            {
                LogThread("Worker START");

                var sw = Stopwatch.StartNew();

                Log("[SAVE] ArrayConfigService.Save()");
                ArrayConfigService.Save(_array.BaseName, _config);

                Log("[FSTAB] DetectFilesystem()");
                var fs = FstabService.DetectFilesystem(device);
                if (string.IsNullOrWhiteSpace(fs))
                    throw new Exception("Could not detect filesystem type.");

                if (_config.PersistMount)
                {
                    Log("[FSTAB] WriteEntry()");
                    FstabService.WriteEntry(device, mountPoint, fs, options);
                }
                else
                {
                    Log("[FSTAB] RemoveEntry()");
                    FstabService.RemoveEntry(device);
                }

                Log("[MDADM] ApplyConfig()");
                MdadmService.ApplyConfig(_array.BaseName, _config);

                sw.Stop();
                Log($"[TIMING] Worker thread finished in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                backgroundError = ex;
            }
        });

        if (backgroundError != null)
        {
            BtnSave.IsEnabled = true;
            await ShowError("Failed to save configuration:\n" + backgroundError.Message);
            return;
        }

        await ShowInfo("Configuration Saved", "The array configuration has been successfully updated.");
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Log("OnCancelClicked()");
        Close();
    }
}
