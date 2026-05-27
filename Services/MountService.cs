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

            var lines = File.ReadAllLines("/proc/mounts");

            foreach (var line in lines)
            {
                // Coincidencia exacta del punto de montaje
                var parts = line.Split(' ');
                if (parts.Length >= 2 && parts[1] == mountPoint)
                    return true;
            }

            return false;
        }

        // ============================
        // 2) MONTAR
        // ============================
        public static bool Mount(string device, string mountPoint, string options = "defaults")
        {
            // Crear carpeta si no existe
            ShellHelper.EjecutarComoRoot($"mkdir -p \"{mountPoint}\"");

            // Evitar montar dos veces
            if (IsMounted(mountPoint))
                return true;

            var r = ShellHelper.EjecutarComoRoot(
                $"mount -o {options} \"{device}\" \"{mountPoint}\""
            );

            return r.ExitCode == 0;
        }

        // ============================
        // 3) DESMONTAR
        // ============================
        public static bool Unmount(string mountPoint)
        {
            if (!IsMounted(mountPoint))
                return true;

            // -f para arrays RAID o dispositivos ocupados
            var r = ShellHelper.EjecutarComoRoot($"umount -f \"{mountPoint}\"");
            return r.ExitCode == 0;
        }
    }
}