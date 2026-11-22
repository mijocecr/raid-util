using System;
using System.Text.RegularExpressions;

namespace RAID_Util.Helpers;

/// <summary>
/// Validación común a GUI y CLI antes de construir cualquier comando root.
/// </summary>
public static class InputValidation
{
    private static readonly Regex LabelPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,15}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DeviceNamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? ValidateLabel(string? label, bool allowEmpty = true)
    {
        if (string.IsNullOrWhiteSpace(label))
            return allowEmpty ? null : "La etiqueta no puede estar vacía.";

        return LabelPattern.IsMatch(label)
            ? null
            : "La etiqueta solo puede contener letras, números, guiones, guiones bajos y puntos (máx. 16 caracteres).";
    }

    public static string? ValidateDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "El nombre de dispositivo no puede estar vacío.";

        return DeviceNamePattern.IsMatch(name)
            ? null
            : "Nombre de dispositivo no válido.";
    }

    public static string? ValidateDevicePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/dev/", StringComparison.Ordinal))
            return "La ruta del dispositivo debe empezar por /dev/.";

        return ValidateDeviceName(path[5..]);
    }

    public static string NormalizeDevicePath(string device) =>
        device.StartsWith("/dev/", StringComparison.Ordinal) ? device : "/dev/" + device;

    public static string? ValidateMountPoint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "El punto de montaje no puede estar vacío.";

        if (!path.StartsWith('/'))
            return "El punto de montaje debe ser una ruta absoluta.";

        if (path.Contains(' ') || path.Contains('\t') || path.Contains('\n'))
            return "El punto de montaje no puede contener espacios.";

        if (path.Contains("$") || path.Contains('`') || path.Contains('"') || path.Contains('\''))
            return "El punto de montaje contiene caracteres no permitidos.";

        return null;
    }

    /// <summary>
    /// NVMe y mmcblk usan sufijo 'p' para las particiones; sd/vd no.
    /// </summary>
    public static string PartitionPath(string diskName, int partitionNumber)
    {
        var suffix = diskName.StartsWith("nvme", StringComparison.OrdinalIgnoreCase)
                     || diskName.StartsWith("mmcblk", StringComparison.OrdinalIgnoreCase)
            ? "p"
            : "";

        return $"/dev/{diskName}{suffix}{partitionNumber}";
    }
}
