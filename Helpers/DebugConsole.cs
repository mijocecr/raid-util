using System;
using RAID_Util.Services;

namespace RAID_Util.Helpers;

/// <summary>
/// Salida de diagnóstico de servicios y vistas.
/// Históricamente estos mensajes se escribían en Console y aparecían en medio
/// de los menús de la CLI (incluso con Console.Clear() que borraba la pantalla).
/// Ahora van al log de la aplicación, con rotación y nivel configurable.
/// </summary>
public static class DebugConsole
{
    public static void WriteLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var text = message.TrimEnd();
        var upper = text.ToUpperInvariant();

        // Los errores deben quedar visibles con el nivel de log por defecto.
        if (upper.Contains("ERROR") || upper.Contains("EXCEPTION") ||
            upper.Contains("FALLO") || upper.Contains("WARNING"))
            LogService.Error(text);
        else
            LogService.Debug(text);
    }

    public static void WriteLine(object? value) => WriteLine(value?.ToString());

    // Console.Clear() se usaba en una ruta de GUI y vaciaba el menú de la CLI.
    public static void Clear()
    {
        // Intencionadamente vacío.
    }
}
