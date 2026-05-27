using System;
using Avalonia.Controls;
using Avalonia.VisualTree;
using RAID_Util.Views;

namespace RAID_Util.Services;

public static class LoadingService
{
    private static LoadingDialog? _dialog;

    public static IDisposable Show(string message, Window? owner = null)
    {
        if (_dialog == null)
        {
            _dialog = new LoadingDialog(message);

            // ⭐ Si hay ventana principal → asignar owner
            if (owner != null)
                _dialog.Show(owner);
            else
                _dialog.Show();
        }

        _dialog.SetMessage(message);

        return new LoadingScope(() =>
        {
            _dialog?.Close();
            _dialog = null;
        });
    }
}