using System;
using System.IO;
using RAID_Util.Helpers;

namespace RAID_Util.Services
{
    public static class MountService
    {
        // ============================
        // 1) DETECTAR SI ESTÁ MONTADO
        // ============================
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

        // ============================
        // 2) MONTAR (universal, blindado)
        // ============================
      
        
        public static bool Mount(string device, string mountPoint, string options = "defaults")
        {
            ShellHelper.EjecutarComoRoot($"mkdir -p \"{mountPoint}\"");

            if (IsMounted(mountPoint))
                return true;

            // Detectar filesystem
            var fsResult = ShellHelper.EjecutarComoRoot($"lsblk -no FSTYPE \"{device}\"");

            string fsType = fsResult.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]
                .Trim()
                .ToLower();

            if (string.IsNullOrWhiteSpace(fsType))
                fsType = "unknown";

            string finalOpts = options;

            // FS no POSIX → uid/gid/umask
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
                return false;

            // ⭐ FIX UNIVERSAL PARA DEBIAN Y TODOS LOS POSIX FS
            if (fsType is "ext4" or "xfs" or "btrfs" or "f2fs")
            {
                ShellHelper.EjecutarComoRoot($"chown 1000:1000 \"{mountPoint}\"");
                ShellHelper.EjecutarComoRoot($"chmod 775 \"{mountPoint}\"");
            }

            return true;
        }



        
        
        // ============================
        // 3) DESMONTAR
        // ============================
        public static bool Unmount(string mountPoint)
        {
            if (!IsMounted(mountPoint))
                return true;

            var r = ShellHelper.EjecutarComoRoot($"umount -f \"{mountPoint}\"");
            return r.ExitCode == 0;
        }
    }
}
