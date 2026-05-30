using System;
using System.IO;
using RAID_Util.Helpers;

namespace RAID_Util.Services
{
    
    public static class MountService
{
    public static bool IsMounted(string mountPoint)
    {
        if (!File.Exists("/proc/mounts"))
            return false;

        foreach (var line in File.ReadAllLines("/proc/mounts"))
        {
            var parts = line.Split(' ');
            if (parts.Length >= 2 && parts[1] == mountPoint)
                return true;
        }

        return false;
    }

    // ============================================================
    // ⭐ MONTAR (versión estable post-mkfs)
    // ============================================================
    public static bool Mount(string device, string mountPoint, string options = "defaults")
    {
        // 1) Crear directorio
        ShellHelper.EjecutarComoRoot($"mkdir -p \"{mountPoint}\"");

        // 2) Si ya está montado → desmontar SIEMPRE (udev puede haber montado en RO)
        if (IsMounted(mountPoint))
            ShellHelper.EjecutarComoRoot($"umount -f \"{mountPoint}\"");

        // 3) Esperar a que el FS esté listo
        ShellHelper.EjecutarComoRoot("udevadm settle");

        // 4) Detectar filesystem real
        var fsResult = ShellHelper.EjecutarComoRoot($"lsblk -no FSTYPE \"{device}\"");

        string fsType = fsResult.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLower();

        if (string.IsNullOrWhiteSpace(fsType))
            fsType = "unknown";

        string finalOpts = options;

        // 5) FS no POSIX → uid/gid/umask
        if (fsType is "exfat" or "vfat" or "ntfs")
        {
            if (!finalOpts.Contains("uid="))
                finalOpts += ",uid=1000";

            if (!finalOpts.Contains("gid="))
                finalOpts += ",gid=1000";

            if (!finalOpts.Contains("umask="))
                finalOpts += ",umask=0002";
        }

        finalOpts = finalOpts.TrimStart(',');

        // 6) Montar
        var r = ShellHelper.EjecutarComoRoot(
            $"mount -o {finalOpts} \"{device}\" \"{mountPoint}\""
        );

        if (r.ExitCode != 0)
            return false;

        // 7) Permisos correctos para POSIX FS
        if (fsType is "ext4" or "xfs" or "btrfs" or "f2fs")
        {
            ShellHelper.EjecutarComoRoot($"chown 1000:1000 \"{mountPoint}\"");
            ShellHelper.EjecutarComoRoot($"chmod 775 \"{mountPoint}\"");
        }

        return true;
    }

    // ============================================================
    // ⭐ DESMONTAR
    // ============================================================
    public static bool Unmount(string mountPoint)
    {
        if (!IsMounted(mountPoint))
            return true;

        var r = ShellHelper.EjecutarComoRoot($"umount -f \"{mountPoint}\"");
        return r.ExitCode == 0;
    }
}

    
    
}
