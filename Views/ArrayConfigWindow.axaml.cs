using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
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
        
        
        private void OnSaveClicked(object? sender, RoutedEventArgs e)
        {
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

            // Performance
            _config.ResyncPriority = (int)NumResyncPriority.Value;
            _config.ResyncMaxSpeed = (int)NumResyncMaxSpeed.Value;

            // Alerts
            _config.AlertDegraded = TgAlertDegraded.IsChecked ?? false;
            _config.AlertDiskFail = TgAlertDiskFail.IsChecked ?? false;
            _config.AlertSlowResync = TgAlertSlowResync.IsChecked ?? false;

            ArrayConfigService.Save(_arrayName, _config);
            Close();
        }

        private void OnCancelClicked(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
