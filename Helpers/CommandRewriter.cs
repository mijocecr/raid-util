using System;
using System.Collections.Generic;

namespace RAID_Util.Helpers;

/// <summary>
/// Normaliza las líneas de comando heredadas antes de ejecutarlas: sustituye
/// rutas FHS fijas por la ubicación real de la herramienta y evita opciones
/// GNU-only cuando el sistema no las soporta (p. ej. dd de BusyBox).
/// </summary>
public static class CommandRewriter
{
    private static readonly string[] Tools =
    {
        "mdadm", "wipefs", "e2label", "sgdisk", "parted", "udevadm", "blkid", "lsblk",
        "smartctl", "mkfs.f2fs", "mkfs.ext4", "mkfs.xfs", "mkfs.btrfs", "mkfs.vfat",
        "mkfs.exfat", "mkfs.ntfs", "mkswap", "findmnt", "fuser", "resize2fs", "xfs_growfs",
        "systemctl", "update-initramfs", "mkinitcpio", "dracut", "dd", "sed"
    };

    private static readonly string[] Prefixes =
    {
        "/usr/sbin/", "/usr/bin/", "/sbin/", "/bin/"
    };

    private static readonly Lazy<bool> HasGnuDd = new(DetectGnuDd);

    public static string Rewrite(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return command;

        foreach (var tool in Tools)
        {
            var resolved = ToolLocator.Resolve(tool);
            if (resolved is null)
                continue;

            foreach (var prefix in Prefixes)
                command = command.Replace(prefix + tool, resolved, StringComparison.Ordinal);
        }

        // BusyBox dd no entiende --status=none; sin él la limpieza profunda falla.
        if (!HasGnuDd.Value)
            command = command.Replace(" status=none", "", StringComparison.Ordinal);

        return command;
    }

    private static bool DetectGnuDd()
    {
        try
        {
            var dd = ToolLocator.Resolve("dd");
            if (dd is null)
                return true;

            var result = CommandRunner.RunAsync(dd, new[] { "--version" }, timeout: TimeSpan.FromSeconds(5))
                .GetAwaiter().GetResult();
            return result.Stdout.Contains("GNU coreutils", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }
}
