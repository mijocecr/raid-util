using System;
using System.Linq;
using RAID_Util.Core;
using RAID_Util.Models;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

/// <summary>
/// Tipo de operación destructiva. Determina qué comprobaciones del guard son
/// aplicables: limpiar un disco implica borrar filesystem/particiones, y quitar
/// un miembro implica sacarlo de un array, así que esos estados no se bloquean.
/// </summary>
public enum SafeDiskOperation
{
    /// <summary>El disco debe estar vacío (crear array). Comprobación completa.</summary>
    Modify,

    /// <summary>Limpiar/inicializar un disco: se permite filesystem y particiones.</summary>
    Wipe,

    /// <summary>Quitar un miembro de un array: se permite que esté en RAID.</summary>
    RemoveMember
}

public static class SafeDiskGuard
{
    private const int ERROR_TIMEOUT = 8000;
    private const int WARNING_TIMEOUT = 6000;
    private const int INFO_TIMEOUT = 4000;

    // ============================================================
    // MÉTODO PRINCIPAL
    // ============================================================
    public static bool CanModify(RaidDiskInfo disk) =>
        CanModify(disk, SafeDiskOperation.Modify);

    /// <summary>
    /// Variante para operaciones que solo reciben el nombre del dispositivo.
    /// Si no se puede inspeccionar el disco, falla cerrado.
    /// </summary>
    public static bool CanModifyDevice(string diskName, SafeDiskOperation operation)
    {
        var name = diskName.StartsWith("/dev/", StringComparison.Ordinal) ? diskName[5..] : diskName;
        RaidDiskInfo? disk = null;

        try
        {
            disk = DiskService.GetAllDisks().FirstOrDefault(d => d.Name == name);
        }
        catch (Exception ex)
        {
            LogService.Warn($"[GUARD] No se pudo inspeccionar /dev/{name}: {ex.Message}");
        }

        if (disk is null)
        {
            Deny($"No se pudo inspeccionar /dev/{name}; operación bloqueada por seguridad.", true);
            return false;
        }

        return CanModify(disk, operation);
    }

    public static bool CanModify(RaidDiskInfo disk, SafeDiskOperation operation)
    {
        if (disk is null)
            return false;

        // -------------------------------
        // 1. DISCO DEL SISTEMA (siempre)
        // El detector es la fuente única: evita depender de que el modelo haya
        // sido rellenado por DiskService o por el parser de mdadm.
        // -------------------------------
        if (disk.IsSystemDisk || SystemDiskDetector.IsSystemDisk(disk.Name))
        {
            Deny("This disk contains system partitions (/, /boot or EFI). Destructive operations are blocked for safety.", true);
            return false;
        }

        // -------------------------------
        // 2. DISCO MONTADO (siempre)
        // -------------------------------
        if (disk.IsMounted)
        {
            Deny("This disk or one of its partitions is currently mounted. Unmount all partitions before performing this operation.", true);
            return false;
        }

        // -------------------------------
        // 3. DISCO CON FILESYSTEM (solo para Modify)
        // -------------------------------
        if (operation == SafeDiskOperation.Modify && !string.IsNullOrWhiteSpace(disk.FsType))
        {
            Deny("This disk contains a valid filesystem. Wipe the disk before using it for RAID or JBOD.", false);
            return false;
        }

        // -------------------------------
        // 4. DISCO CON PARTICIONES (solo para Modify)
        // -------------------------------
        if (operation == SafeDiskOperation.Modify && disk.Children.Count > 0)
        {
            Deny("This disk contains one or more partitions. Wipe the disk before using it for RAID or JBOD.", false);
            return false;
        }

        // -------------------------------
        // 5. DISCO USADO POR RAID (Modify y Wipe)
        // Al quitar un miembro o borrar un array, el disco está en RAID por
        // definición: esa operación se gestiona desde la sección de arrays.
        // -------------------------------
        if (operation != SafeDiskOperation.RemoveMember && disk.IsUsedByRaid)
        {
            Deny("This disk is part of an active RAID array and cannot be modified. Use the RAID Arrays section to manage this device.", true);
            return false;
        }

        // -------------------------------
        // 6. DISCO ISCSI (limitado)
        // -------------------------------
        if (disk.IsIscsi)
        {
            Deny("This is an iSCSI disk. Only non-destructive operations are allowed unless the disk is empty.", false);

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
            Deny("This is a USB disk. Using USB devices for RAID is not recommended.", false, WARNING_TIMEOUT);

        // -------------------------------
        // 8. DISCO VIRTUAL (advertencia)
        // -------------------------------
        if (disk.IsVirtual)
            Deny("This is a virtual disk. Some operations may behave differently depending on the environment.", false, WARNING_TIMEOUT);

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

    private static void Deny(string message, bool critical, int? timeout = null) =>
        NotificadorLinux.Enviar(
            message,
            timeout ?? (critical ? ERROR_TIMEOUT : WARNING_TIMEOUT),
            critical ? "critical" : "normal",
            "raid-util"
        );
}
