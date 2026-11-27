using System;
using System.Diagnostics;

namespace RAID_Util.Helpers;

/// <summary>
///     Sends desktop notifications to Linux using notify-send.
///     Supports duration, urgency, and icons.
///     Falls back to console output if notify-send is not available.
/// </summary>
public static class NotificadorLinux
{
    /// <summary>
    ///     Sends a desktop notification.
    /// </summary>
    /// <param name="mensaje">Message to display.</param>
    /// <param name="duracionMs">Duration in milliseconds (default: 5000).</param>
    /// <param name="urgencia">Urgency level: low, normal, critical.</param>
    /// <param name="icono">Optional icon name or path.</param>
    public static void Enviar(
        string mensaje,
        int duracionMs = 5000,
        string urgencia = "normal",
        string? icono = "raid-util")
    {
        try
        {
            if (!NotifySendDisponible())
            {
                DebugConsole.WriteLine($"[NOTIFICACIÓN] {mensaje}");
                return;
            }

            var start = new ProcessStartInfo(ToolLocator.ResolveOrThrow("notify-send"))
            {
                UseShellExecute = false
            };

            // ArgumentList evita que comillas o $ en el mensaje rompan el comando.
            start.ArgumentList.Add("-t");
            start.ArgumentList.Add(duracionMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add(urgencia);
            if (!string.IsNullOrWhiteSpace(icono))
            {
                start.ArgumentList.Add("-i");
                start.ArgumentList.Add(icono);
            }
            start.ArgumentList.Add("RAID Management");
            start.ArgumentList.Add(mensaje);

            Process.Start(start);
        }
        catch
        {
            DebugConsole.WriteLine($"[NOTIFICACIÓN] {mensaje}");
        }
    }

    /// <summary>
    ///     Checks if notify-send is available in the system.
    /// </summary>
    private static bool NotifySendDisponible() => ToolLocator.Resolve("notify-send") is not null;
}