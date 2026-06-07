using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

public static class MountService
{
    // ============================================================
    // ⭐ Resolver PATH REAL del dispositivo (arrays y discos)
    // ============================================================
    private static async Task<string> ResolveRealDeviceAsync(string device)
    {
        if (device.StartsWith("/dev/"))
            return device;

        var arrays = await RaidService.Instance.GetArraysAsync();
        var array = arrays.FirstOrDefault(a =>
            a.Name == device ||
            a.Path.EndsWith("/" + device, StringComparison.Ordinal) ||
            a.Path.EndsWith(device, StringComparison.Ordinal));

        if (array != null)
            return array.Path;

        return "/dev/" + device;
    }

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
    // ⭐ MONTAR (versión estable y segura)
    // ============================================================
    public static bool Mount(string device, string mountPoint, string options = "defaults")
    {
        // ⭐ Resolver PATH REAL (seguro, no bloquea UI)
        device = ResolveRealDeviceAsync(device).Result;

        ShellHelper.EjecutarComoRoot($"mkdir -p \"{mountPoint}\"");

        if (IsMounted(mountPoint))
            ShellHelper.EjecutarComoRoot($"umount -f \"{mountPoint}\"");

        ShellHelper.EjecutarComoRoot("udevadm settle");

        var fsResult = ShellHelper.EjecutarComoRoot($"lsblk -no FSTYPE \"{device}\"");

        var fsType = fsResult.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLower();

        if (string.IsNullOrWhiteSpace(fsType))
        {
            LogService.Error($"[MOUNT] ERROR: El dispositivo {device} NO tiene filesystem. Abortando montaje.");
            return false;
        }

        var finalOpts = options;

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

        var r = ShellHelper.EjecutarComoRoot(
            $"mount -o {finalOpts} \"{device}\" \"{mountPoint}\""
        );

        if (r.ExitCode != 0)
        {
            LogService.Error($"[MOUNT] ERROR al montar {device}: {r.Stderr}");
            return false;
        }

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
