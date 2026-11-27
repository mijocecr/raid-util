using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RAID_Util.Helpers;

/// <summary>
/// Parseo de /proc/mdstat con ámbito por array. Evita atribuir a un array el
/// estado o el progreso de otro, y distingue "no hay arrays" de "no se pudo leer".
/// </summary>
public static class MdstatParser
{
    private static readonly Regex ArrayHeader =
        new(@"^(\S+)\s+:\s+", RegexOptions.Compiled);

    private static readonly Regex Progress =
        new(@"\b(resync|recovery|reshape|check|verify|rebuild)\s*=\s*(\d+(?:\.\d+)?)%",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MissingMember =
        new(@"\[[^]]*_[^]]*\]", RegexOptions.Compiled);

    public static List<string> ParseArrays(string mdstat)
    {
        var names = new List<string>();
        foreach (var line in mdstat.Split('\n'))
        {
            var match = ArrayHeader.Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups[1].Value;
            if (name.StartsWith("md", StringComparison.OrdinalIgnoreCase) && !names.Contains(name))
                names.Add(name);
        }

        return names;
    }

    public static string NormalizeName(string arrayName)
    {
        var name = arrayName.Replace('\\', '/').Split('/').Last();
        var colon = name.IndexOf(':');
        return colon > 0 ? name[..colon] : name;
    }

    /// <summary>
    /// Devuelve la cabecera del array y sus líneas indentadas (bloques,
    /// miembros faltantes, progreso), que es donde mdstat marca [U_].
    /// </summary>
    public static string GetArrayLine(string mdstat, string arrayName)
    {
        var name = NormalizeName(arrayName);
        var result = new List<string>();
        var collecting = false;

        foreach (var raw in mdstat.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var header = ArrayHeader.Match(line);

            if (header.Success)
            {
                collecting = header.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase);
                if (collecting)
                    result.Add(line);
                continue;
            }

            if (!collecting)
                continue;

            if (string.IsNullOrWhiteSpace(line))
                break;

            if (char.IsWhiteSpace(line[0]))
                result.Add(line);
        }

        return string.Join("\n", result);
    }

    /// <summary>Línea de progreso (resync/recovery/...) PERTENECIENTE a este array.</summary>
    public static string GetProgressLine(string mdstat, string arrayName)
    {
        var name = NormalizeName(arrayName);
        var inArray = false;

        foreach (var line in mdstat.Split('\n'))
        {
            var header = ArrayHeader.Match(line);
            if (header.Success)
            {
                inArray = header.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inArray || !line.TrimStart().StartsWith('['))
                continue;

            var progress = Progress.Match(line);
            if (!progress.Success)
                continue;

            if (double.TryParse(progress.Groups[2].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var percent) && percent < 100)
                return line.Trim();
        }

        return "";
    }

    public static bool IsOperationInProgress(string mdstat, string arrayName) =>
        GetProgressLine(mdstat, arrayName).Length > 0;

    public static int ParseProgressPercent(string line)
    {
        var match = Progress.Match(line);
        if (!match.Success ||
            !double.TryParse(match.Groups[2].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var percent))
            return 0;

        return (int)Math.Clamp(Math.Round(percent), 0, 100);
    }

    public static bool IsDegradedLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var text = line.ToLowerInvariant();
        return text.Contains("degraded") ||
               text.Contains("faulty") ||
               text.Contains("failed") ||
               text.Contains("inactive") ||
               text.Contains("read-only") ||
               text.Contains("broken") ||
               text.Contains("(f)") ||
               MissingMember.IsMatch(line);
    }

    public static bool HasFaultyMember(string line)
    {
        var text = line.ToLowerInvariant();
        return text.Contains("(f)") || text.Contains("faulty");
    }
}
