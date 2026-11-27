using System;
using System.Globalization;
using System.Text.Json;

namespace RAID_Util.Helpers;

/// <summary>
/// Interpreta el campo ROTA de lsblk de forma uniforme.
/// Según la versión de util-linux llega como booleano JSON (true/false),
/// como número (1/0) o como texto ("1"/"0"); las tres formas significan
/// "disco giratorio" (HDD).
/// </summary>
public static class DiskTypeHelper
{
    public static bool ParseRotational(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.Number => value.TryGetInt64(out var number) && number != 0,
        JsonValueKind.String => ParseRotational(value.GetString()),
        _ => false
    };

    // Variante para el token dinámico de Json.NET usado por RaidService.
    public static bool ParseRotational(object? value)
    {
        switch (value)
        {
            case null:
                return false;
            case bool boolean:
                return boolean;
            case JsonElement element:
                return ParseRotational(element);
        }

        var text = value.ToString()?.Trim().ToLowerInvariant();
        return text switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" or "" => false,
            _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                 && number != 0
        };
    }
}
