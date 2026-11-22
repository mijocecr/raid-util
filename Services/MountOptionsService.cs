using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

/// <summary>
/// Única fuente de verdad para el punto de montaje, las opciones y los permisos.
/// Antes la GUI usaba /run/media/&lt;user&gt;/&lt;array&gt;, la CLI y RaidService /mnt/&lt;array&gt;,
/// y cada una aplicaba permisos distintos (o ninguno).
/// </summary>
public static class MountOptionsService
{
    public const string DefaultPermissions = "755";

    /// <summary>Clave estable del array: md0, también para /dev/md/host:md0.</summary>
    public static string ArrayKey(string arrayName)
    {
        if (string.IsNullOrWhiteSpace(arrayName))
            return arrayName ?? string.Empty;

        var name = arrayName.StartsWith("/dev/", StringComparison.Ordinal)
            ? Path.GetFileName(arrayName)
            : arrayName.Trim();

        var colon = name.LastIndexOf(':');
        return colon >= 0 ? name[(colon + 1)..] : name;
    }

    public static string DefaultArrayMountPoint(string arrayName) =>
        "/mnt/" + ArrayKey(arrayName);

    public static string DefaultDiskMountPoint(string label) =>
        "/run/media/" + Environment.UserName + "/" + label;

    /// <summary>Respeta el valor guardado; si no hay valor, usa el canónico.</summary>
    public static string EffectiveArrayMountPoint(string arrayName, string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultArrayMountPoint(arrayName) : configured.Trim();

    public static string BuildOptions(ArrayConfig config)
    {
        var options = new List<string>();
        if (config.Mount_NoAtime) options.Add("noatime");
        if (config.Mount_NoDirAtime) options.Add("nodiratime");
        if (config.Mount_Discard) options.Add("discard");
        if (config.Mount_Sync) options.Add("sync");
        if (config.Mount_ReadOnly) options.Add("ro");
        return options.Count == 0 ? "defaults" : string.Join(",", options);
    }

    public static bool IsValidPermissions(string? permissions) =>
        !string.IsNullOrWhiteSpace(permissions) &&
        permissions.Trim().Length == 3 && permissions.Trim().All(c => c is >= '0' and <= '7');

    /// <summary>Devuelve permisos válidos de 3 dígitos octales o el default.</summary>
    public static string NormalizePermissions(string? permissions)
    {
        if (IsValidPermissions(permissions))
            return permissions!.Trim();

        return DefaultPermissions;
    }

    public static string? ValidateMountPoint(string? path) => InputValidation.ValidateMountPoint(path);
}
