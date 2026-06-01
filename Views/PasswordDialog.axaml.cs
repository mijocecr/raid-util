using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using RAID_Util.Services;

namespace RAID_Util.Views;

public partial class PasswordDialog : Window
{
    private bool _closeApp = true;
    private bool _returningValue;

    public PasswordDialog()
    {
        LogService.Debug("[PWD_DIALOG] Inicializando diálogo de contraseña.");

        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Closed += (s, e) =>
        {
            LogService.Debug($"[PWD_DIALOG] Closed → closeApp={_closeApp}, returningValue={_returningValue}");

            if (!_returningValue && _closeApp)
            {
                LogService.Write("[PWD_DIALOG] Cerrando aplicación por cierre del diálogo.");
                var lifetime = Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime;

                lifetime?.Shutdown();
            }
        };
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var pass = PwdBox.Text ?? "";
        pass = pass.Trim(); // 🔥 CRÍTICO

        if (string.IsNullOrWhiteSpace(pass))
        {
            LogService.Debug("[PWD_DIALOG] Contraseña vacía, mostrando error.");
            ShowError("Password cannot be empty.");
            return;
        }

        LogService.Write("[PWD_DIALOG] Contraseña aceptada por el usuario.");

        _closeApp = false;
        _returningValue = true; // 🔥 EVITA CIERRE PREMATURO
        Close(pass);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        LogService.Write("[PWD_DIALOG] Cancelado por el usuario. Cerrando aplicación.");
        _closeApp = true;
        _returningValue = true;
        Close(null);
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        LogService.Write("[PWD_DIALOG] Botón Close pulsado. Cerrando aplicación.");
        _closeApp = true;
        _returningValue = true;
        Close(null);
    }

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LogService.Debug("[PWD_DIALOG] Enter presionado → aceptar.");
            OnAccept(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            LogService.Debug("[PWD_DIALOG] Escape presionado → cancelar.");
            OnCancel(sender, e);
        }
    }

    private void ShowError(string message)
    {
        LogService.Debug($"[PWD_DIALOG] Error mostrado: {message}");

        ErrorText.Text = message;
        ErrorText.IsVisible = true;

        PwdBox.Text = "";
        PwdBox.Focus();
    }
}