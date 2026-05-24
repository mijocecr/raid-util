using System;
using RAID_Util.Views;


namespace RAID_Util.Services;

public static class LoadingService
{
    private static LoadingDialog? _dialog;

    public static IDisposable Show(string message)
    {
        if (_dialog == null)
        {
            _dialog = new LoadingDialog(message);
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