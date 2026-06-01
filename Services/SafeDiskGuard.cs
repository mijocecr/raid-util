using System;
using System.Linq;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class SafeDiskGuard
{
    // ============================================================
    //  VALIDACIONES PÚBLICAS (ARRAY-LEVEL)
    // ============================================================

    public static bool CanModifyArray(RaidArrayInfo array, out string reason)
    {
        // No se puede modificar un array montado
        if (array.IsMounted)
        {
            reason = $"El array {array.Name} está montado en {array.MountPath}. No se puede modificar.";
            return false;
        }

        // No se puede modificar un array en estado crítico
        if (array.State == "Failed")
        {
            reason = $"El array {array.Name} está en estado FAILED. No se puede modificar.";
            return false;
        }

        // Validar discos
        if (!ValidateArrayDisks(array, out reason))
            return false;

        reason = "";
        return true;
    }

    public static bool CanStopArray(RaidArrayInfo array, out string reason)
    {
        // No parar arrays en reconstrucción
        if (array.State == "Rebuilding")
        {
            reason = $"El array {array.Name} está reconstruyéndose. No se puede detener.";
            return false;
        }

        // No parar arrays en estado crítico
        if (array.State == "Failed")
        {
            reason = $"El array {array.Name} está en estado FAILED. No se puede detener.";
            return false;
        }

        // Validar discos
        if (!ValidateArrayDisks(array, out reason))
            return false;

        reason = "";
        return true;
    }

    public static bool CanFormatArray(RaidArrayInfo array, out string reason)
    {
        // No formatear si está montado
        if (array.IsMounted)
        {
            reason = $"El array {array.Name} está montado. No se puede formatear.";
            return false;
        }

        // No formatear si está degradado
        if (array.State == "Degraded")
        {
            reason = $"El array {array.Name} está degradado. No se puede formatear.";
            return false;
        }

        // Validar discos
        if (!ValidateArrayDisks(array, out reason))
            return false;

        reason = "";
        return true;
    }

    public static bool CanExpandArray(RaidArrayInfo array, out string reason)
    {
        // No expandir si está montado
        if (array.IsMounted)
        {
            reason = $"El array {array.Name} está montado. No se puede expandir.";
            return false;
        }

        // No expandir si está degradado
        if (array.State == "Degraded")
        {
            reason = $"El array {array.Name} está degradado. No se puede expandir.";
            return false;
        }

        // No expandir si está en rebuild
        if (array.State == "Rebuilding")
        {
            reason = $"El array {array.Name} está reconstruyéndose. No se puede expandir.";
            return false;
        }

        // Validar discos
        if (!ValidateArrayDisks(array, out reason))
            return false;

        reason = "";
        return true;
    }

    public static bool CanRepairArray(RaidArrayInfo array, out string reason)
    {
        // No reparar si está montado
        if (array.IsMounted)
        {
            reason = $"El array {array.Name} está montado. No se puede reparar.";
            return false;
        }

        // No reparar si está en rebuild
        if (array.State == "Rebuilding")
        {
            reason = $"El array {array.Name} ya está reconstruyéndose. No se puede reparar.";
            return false;
        }

        // Validar discos
        if (!ValidateArrayDisks(array, out reason))
            return false;

        reason = "";
        return true;
    }

    // ============================================================
    //  VALIDACIÓN INTERNA DE TODOS LOS DISCOS DEL ARRAY
    // ============================================================

    private static bool ValidateArrayDisks(RaidArrayInfo array, out string reason)
    {
        foreach (var d in array.Disks)
        {
            // No permitir discos del sistema
            if (d.IsSystemDisk)
            {
                reason = $"El disco /dev/{d.Name} es un disco del sistema. Operación bloqueada.";
                return false;
            }

            // No permitir discos NVMe internos
            if (d.IsNvme && !d.IsVirtual && !d.IsUsb && !d.IsIscsi)
            {
                reason = $"El disco /dev/{d.Name} es NVMe interno. No se permite usarlo en operaciones RAID.";
                return false;
            }

            // No permitir discos montados
            if (d.IsMounted)
            {
                reason = $"El disco /dev/{d.Name} está montado en {d.MountPoint}.";
                return false;
            }

            // No permitir discos con filesystem
            if (d.HasFileSystem)
            {
                reason = $"El disco /dev/{d.Name} contiene un filesystem ({d.Filesystem}).";
                return false;
            }

            // No permitir discos con particiones
            if (d.HasPartitions)
            {
                reason = $"El disco /dev/{d.Name} contiene particiones.";
                return false;
            }

            // No permitir discos con GPT/MBR
            if (d.HasValidPartitionTable)
            {
                reason = $"El disco /dev/{d.Name} tiene una tabla de particiones válida (GPT/MBR).";
                return false;
            }

            // No permitir discos RAID inactivos
            if (d.IsRaidInactiveMember)
            {
                reason = $"El disco /dev/{d.Name} pertenece a un array RAID inactivo.";
                return false;
            }
        }

        reason = "";
        return true;
    }
}
