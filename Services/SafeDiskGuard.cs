using RAID_Util.Core;
using RAID_Util.Models;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

public static class SafeDiskGuard
{
    private const int ERROR_TIMEOUT = 8000;
    private const int WARNING_TIMEOUT = 6000;
    private const int INFO_TIMEOUT = 4000;

    // ============================================================
    // MÉTODO PRINCIPAL
    // ============================================================
    public static bool CanModify(RaidDiskInfo disk)
    {
        // -------------------------------
        // 1. DISCO DEL SISTEMA
        // -------------------------------
        if (disk.IsSystemDisk)
        {
            NotificadorLinux.Enviar(
                "This disk contains system partitions (/, /boot or EFI). Destructive operations are blocked for safety.",
                ERROR_TIMEOUT,
                "critical",
                "raid-util"
            );
            return false;
        }

        // -------------------------------
        // 2. DISCO MONTADO
        // -------------------------------
        if (!string.IsNullOrWhiteSpace(disk.MountPath))
        {
            NotificadorLinux.Enviar(
                "This disk or one of its partitions is currently mounted. Unmount all partitions before performing this operation.",
                ERROR_TIMEOUT,
                "critical",
                "raid-util"
            );
            return false;
        }

        // -------------------------------
        // 3. DISCO CON FILESYSTEM
        // -------------------------------
        if (!string.IsNullOrWhiteSpace(disk.FsType))
        {
            NotificadorLinux.Enviar(
                "This disk contains a valid filesystem. Wipe the disk before using it for RAID or JBOD.",
                WARNING_TIMEOUT,
                "normal",
                "raid-util"
            );
            return false;
        }

        // -------------------------------
        // 4. DISCO CON PARTICIONES
        // -------------------------------
        if (disk.Children.Count > 0)
        {
            NotificadorLinux.Enviar(
                "This disk contains one or more partitions. Wipe the disk before using it for RAID or JBOD.",
                WARNING_TIMEOUT,
                "normal",
                "raid-util"
            );
            return false;
        }

        // -------------------------------
        // 5. DISCO USADO POR RAID
        // -------------------------------
        if (disk.IsUsedByRaid)
        {
            NotificadorLinux.Enviar(
                "This disk is part of an active RAID array and cannot be modified. Use the RAID Arrays section to manage this device.",
                ERROR_TIMEOUT,
                "critical",
                "raid-util"
            );
            return false;
        }

        // -------------------------------
        // 6. DISCO ISCSI (limitado)
        // -------------------------------
        if (disk.IsIscsi)
        {
            NotificadorLinux.Enviar(
                "This is an iSCSI disk. Only non-destructive operations are allowed unless the disk is empty.",
                WARNING_TIMEOUT,
                "normal",
                "raid-util"
            );

            if (disk.Children.Count == 0 &&
                string.IsNullOrWhiteSpace(disk.FsType) &&
                !disk.IsUsedByRaid)
            {
                return true;
            }

            return false;
        }

        // -------------------------------
        // 7. DISCO USB (advertencia)
        // -------------------------------
        if (disk.IsUsb)
        {
            NotificadorLinux.Enviar(
                "This is a USB disk. Using USB devices for RAID is not recommended.",
                WARNING_TIMEOUT,
                "normal",
                "raid-util"
            );
        }

        // -------------------------------
        // 8. DISCO VIRTUAL (advertencia)
        // -------------------------------
        if (disk.IsVirtual)
        {
            NotificadorLinux.Enviar(
                "This is a virtual disk. Some operations may behave differently depending on the environment.",
                WARNING_TIMEOUT,
                "normal",
                "raid-util"
            );
        }

        // -------------------------------
        // 9. DISCO LISTO PARA USAR
        // -------------------------------
        NotificadorLinux.Enviar(
            "This disk is ready to be used for RAID or JBOD.",
            INFO_TIMEOUT,
            "low",
            "raid-util"
        );

        return true;
    }
}
