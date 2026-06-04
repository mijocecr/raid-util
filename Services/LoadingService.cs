using System;
using Avalonia.Controls;
using RAID_Util.Views;

namespace RAID_Util.Services;

public static class LoadingService
{
    private static LoadingDialog? _dialog;

    // ============================================================
    // ⭐ Mostrar diálogo
    // ============================================================
    public static IDisposable Show(string message, Window? owner = null)
    {
        if (_dialog == null)
        {
            _dialog = new LoadingDialog(message);

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

    // ============================================================
    // ⭐ Actualizar mensaje sin cerrar el diálogo
    // ============================================================
    public static void Update(string message)
    {
        if (_dialog != null)
            _dialog.SetMessage(message);
    }

    // ============================================================
    // ⭐ Cerrar manualmente el diálogo
    // ============================================================
    public static void Hide()
    {
        if (_dialog != null)
        {
            _dialog.Close();
            _dialog = null;
        }
    }
}