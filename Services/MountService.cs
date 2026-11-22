using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Helpers;
using RAID_Util.Models;

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

        return File.ReadLines("/proc/mounts").Any(line =>
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return fields.Length >= 2 && PathEquals(Decode(fields[1]), mountPoint);
        });
    }

    public static bool IsMountedReadOnly(string mountPoint)
    {
        if (!File.Exists("/proc/mounts"))
            return false;

        return File.ReadLines("/proc/mounts").Any(line =>
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !PathEquals(Decode(fields[1]), mountPoint))
                return false;

            return fields[3].Split(',').Contains("ro", StringComparer.Ordinal);
        });
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.Ordinal);

    private static string Decode(string value) => value
        .Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal)
        .Replace("\\012", "\n", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);

    public static bool Mount(string device, string mountPoint, string options = "defaults") =>
        Mount(device, mountPoint, options, MountOptionsService.DefaultPermissions);

    /// <summary>Overload canónico: opciones y permisos salen de la configuración compartida.</summary>
    public static bool Mount(string device, string mountPoint, ArrayConfig config) =>
        Mount(device, mountPoint, MountOptionsService.BuildOptions(config),
            MountOptionsService.NormalizePermissions(config.MountPermissions));

    public static bool Mount(string device, string mountPoint, string options, string permissions)
    {
        var error = InputValidation.ValidateMountPoint(mountPoint);
        if (error is not null)
        {
            LogService.Error($"[MOUNT] {error}");
            return false;
        }

        // ⭐ Resolver PATH REAL (seguro, no bloquea UI)
        device = ResolveRealDeviceAsync(device).GetAwaiter().GetResult();

        ShellHelper.RunRoot("mkdir", "-p", mountPoint);

        if (IsMounted(mountPoint))
            ShellHelper.RunRoot("umount", "-f", mountPoint);

        SystemIntegrationService.SettleDevicesAsync().GetAwaiter().GetResult();

        var fsResult = ShellHelper.RunRoot("lsblk", "-no", "FSTYPE", device);

        var fsType = fsResult.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(fsType))
        {
            LogService.Error($"[MOUNT] ERROR: El dispositivo {device} NO tiene filesystem. Abortando montaje.");
            return false;
        }

        var finalOpts = string.IsNullOrWhiteSpace(options) ? "defaults" : options.TrimStart(',');

        if (fsType is "exfat" or "vfat" or "ntfs")
        {
            var (uid, gid) = CurrentIds();
            if (!finalOpts.Contains("uid=", StringComparison.Ordinal)) finalOpts += $",uid={uid}";
            if (!finalOpts.Contains("gid=", StringComparison.Ordinal)) finalOpts += $",gid={gid}";
            if (!finalOpts.Contains("umask=", StringComparison.Ordinal)) finalOpts += ",umask=0002";
        }

        var mountTool = ToolLocator.IsAvailable("mount")
            ? ToolLocator.ResolveOrThrow("mount")
            : "mount";
        var r = ShellHelper.RunRoot(mountTool, "-o", finalOpts, device, mountPoint);

        if (r.ExitCode != 0)
        {
            LogService.Error($"[MOUNT] ERROR al montar {device}: {r.Stderr}");
            return false;
        }

        // Permisos idénticos en GUI y CLI. En FAT/NTFS los fija el montaje;
        // en filesystems Unix se aplican aquí con el uid/gid real del usuario.
        var perms = MountOptionsService.NormalizePermissions(permissions);
        if (fsType is not ("exfat" or "vfat" or "ntfs"))
        {
            var (uid, gid) = CurrentIds();
            ShellHelper.RunRoot("chown", $"{uid}:{gid}", mountPoint);
        }
        ShellHelper.RunRoot("chmod", perms, mountPoint);

        return true;
    }

    private static (string Uid, string Gid) CurrentIds()
    {
        string Read(string option)
        {
            if (!ToolLocator.IsAvailable("id"))
                return "1000";

            var result = ShellHelper.Run("id", option);
            var value = result.Stdout.Trim();
            return result.Success && int.TryParse(value, out _) ? value : "1000";
        }

        return (Read("-u"), Read("-g"));
    }

    // ============================================================
    // ⭐ DESMONTAR
    // ============================================================
    public static bool Unmount(string mountPoint)
    {
        if (!IsMounted(mountPoint))
            return true;

        var r = ShellHelper.RunRoot("umount", "-f", mountPoint);
        return r.ExitCode == 0;
    }
}
