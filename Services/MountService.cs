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

            var lines = File.ReadAllLines("/proc/mounts");
            foreach (var line in lines)
            {
                if (line.Contains($" {mountPoint} "))
                    return true;
            }
            return false;
        }

        public static bool Mount(string device, string mountPoint, string options = "defaults")
        {
            // Crear carpeta con permisos root
            ShellHelper.EjecutarComoRoot($"mkdir -p {mountPoint}");

            // Ejecutar mount como root
            var r = ShellHelper.EjecutarComoRoot(
                $"mount -o {options} {device} {mountPoint}"
            );

            return r.ExitCode == 0;
        }

        public static bool Unmount(string mountPoint)
        {
            if (!IsMounted(mountPoint))
                return true;

            var r = ShellHelper.EjecutarComoRoot($"umount {mountPoint}");
            return r.ExitCode == 0;
        }

        public static void AddToFstab(string device, string mountPoint, string fsType = "ext4", string options = "defaults")
        {
            // Eliminar entradas previas
            ShellHelper.EjecutarComoRoot($"sed -i '/{mountPoint}/d' /etc/fstab");

            // Añadir nueva entrada
            ShellHelper.EjecutarComoRoot(
                $"bash -c \"echo '{device} {mountPoint} {fsType} {options} 0 0' >> /etc/fstab\""
            );
        }

        public static void RemoveFromFstab(string mountPoint)
        {
            ShellHelper.EjecutarComoRoot($"sed -i '/{mountPoint}/d' /etc/fstab");
        }
    }
}