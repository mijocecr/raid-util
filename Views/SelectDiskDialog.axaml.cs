using System.Collections.Generic;
using Avalonia.Controls;
using RAID_Util.Models;

namespace RAID_Util.Views;

public partial class SelectDiskDialog : Window
{
    public SelectDiskDialog(List<RaidDiskInfo> disks)
    {
        InitializeComponent();

        var list = this.FindControl<ListBox>("DiskList");
        var btnOk = this.FindControl<Button>("BtnOK");
        var btnCancel = this.FindControl<Button>("BtnCancel");

        if (list != null)
            foreach (var d in disks)
                list.Items?.Add($"{d.Name}  ({d.Size})");

        if (btnCancel != null) btnCancel.Click += (_, _) => Close(null);

        if (btnOk != null)
            btnOk.Click += (_, _) =>
            {
                if (list == null || list.SelectedItem == null)
                {
                    Close(null);
                    return;
                }

                var selected = list.SelectedItem.ToString()?.Split(' ')[0];
                Close(selected);
            };
    }
}