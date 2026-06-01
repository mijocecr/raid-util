using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public class RaidService
{
    public static Dictionary<string, dynamic> Nodes { get; private set; } = new();


    public string LastCreatedMdName { get; private set; } = "";

    public Task<List<RaidDiskInfo>> GetAllDisksAsync()
    {
        return Task.FromResult(DiskService.GetAllDisks());
    }




    public async Task<bool> AddDiskToArrayAsync(string arrayName, string diskName)
    {
        try
        {
            // ⭐ VALIDACIÓN CRÍTICA
            if (!await EnsureArraySafeForModification(arrayName))
                return false;

            NotificadorLinux.Enviar($"Preparando para añadir {diskName} a {arrayName}…");

            LogService.Write($"[RAID] AddDiskToArray START → array={arrayName}, disk={diskName}");

            // 0) Esperar a que mdadm esté libre
            NotificadorLinux.Enviar("Esperando a que mdadm esté libre…");

            if (!await WaitForMdadmIdleAsync())
            {
                NotificadorLinux.Enviar("mdadm está ocupado. Operación cancelada.", 5000, "critical");
                return false;
            }

            // 1) Validar que el array existe
            NotificadorLinux.Enviar("Validando array…");

            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a => a.Name == arrayName || a.Path.EndsWith(arrayName));
            if (array == null)
            {
                NotificadorLinux.Enviar($"Array {arrayName} no encontrado.", 5000, "critical");
                return false;
            }

            // 2) Validar que el disco no está ya en el array
            if (array.Disks.Any(d => d.Name == diskName))
            {
                NotificadorLinux.Enviar($"El disco {diskName} ya pertenece al array.", 5000, "critical");
                return false;
            }

            // 3) Validación previa del disco
            NotificadorLinux.Enviar("Validando disco…");

            var errors = await ValidateDiskForRaidAsync(diskName);
            if (errors.Count > 0)
            {
                foreach (var e in errors)
                    LogService.Error("[VALIDATION] " + e);

                NotificadorLinux.Enviar("El disco no es válido para RAID.", 5000, "critical");
                return false;
            }

            // 4) Ejecutar mdadm --add
            NotificadorLinux.Enviar($"Añadiendo /dev/{diskName} al array…");

            var cmd = $"/usr/sbin/mdadm /dev/{arrayName} --add /dev/{diskName}";
            var result = ShellHelper.EjecutarComoRoot(cmd);

            if (result.ExitCode != 0)
            {
                NotificadorLinux.Enviar("Error al añadir el disco al array.", 5000, "critical");
                return false;
            }

            // 5) Esperar a que udev y mdadm terminen
            NotificadorLinux.Enviar("Finalizando operación…");

            ShellHelper.EjecutarComoRoot("udevadm settle");
            await WaitForMdadmIdleAsync();

            // 6) Refrescar arrays
            await GetArraysAsync();

            NotificadorLinux.Enviar($"Disco {diskName} añadido correctamente a {arrayName}.");

            return true;
        }
        catch (Exception ex)
        {
            NotificadorLinux.Enviar("Error inesperado al añadir el disco.", 5000, "critical");
            LogService.Error(ex.ToString());
            return false;
        }
    }


    private async Task<bool> WaitForMdadmIdleAsync(int timeoutSeconds = 20)
    {
        var waited = 0;

        while (waited < timeoutSeconds)
        {
            var output = await ShellHelper.RunCleanAsync("cat /proc/mdstat");

            if (string.IsNullOrWhiteSpace(output))
                return false;

            if (!output.Contains("resync", StringComparison.OrdinalIgnoreCase) &&
                !output.Contains("reshape", StringComparison.OrdinalIgnoreCase) &&
                !output.Contains("recovery", StringComparison.OrdinalIgnoreCase) &&
                !output.Contains("check", StringComparison.OrdinalIgnoreCase) &&
                !output.Contains("repair", StringComparison.OrdinalIgnoreCase))
                return true;

            await Task.Delay(1000);
            waited++;
        }

        return false;
    }


    public async Task<List<string>> ValidateDiskForRaidAsync(string diskName)
    {
        var errors = new List<string>();

        // 1) ¿Está montado?
        var mount = await ShellHelper.RunCleanAsync($"lsblk -no MOUNTPOINT /dev/{diskName}");
        if (!string.IsNullOrWhiteSpace(mount))
            errors.Add($"Disk /dev/{diskName} is mounted at {mount}.");

        // 2) ¿Tiene particiones?
        var json = await ShellHelper.RunCleanAsync($"lsblk -J /dev/{diskName}");
        if (!string.IsNullOrWhiteSpace(json))
            try
            {
                dynamic data = JsonConvert.DeserializeObject(json)!;

                if (data.blockdevices != null &&
                    data.blockdevices.Count > 0 &&
                    data.blockdevices[0].children != null)
                    errors.Add($"Disk /dev/{diskName} has partitions. (Must wipe first)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RAID] ValidateDiskForRaidAsync lsblk JSON error:");
                Console.WriteLine(ex);
            }

        // 3) Metadata RAID previa
        var mdadmInfo = await RunMdadmAsync($"--examine /dev/{diskName}");
        if (!string.IsNullOrWhiteSpace(mdadmInfo))
        {
            if (mdadmInfo.Contains("Raid Level", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Disk /dev/{diskName} contains RAID metadata.");

            if (mdadmInfo.Contains("MBR Magic", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Disk /dev/{diskName} has an MBR partition table.");

            if (mdadmInfo.Contains("type ee", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Disk /dev/{diskName} has a GPT protective partition.");
        }

        // 4) ¿Está en uso por procesos?
        var fuser = await ShellHelper.RunCleanAsync($"fuser -v /dev/{diskName}");
        if (!string.IsNullOrWhiteSpace(fuser))
            errors.Add($"Disk /dev/{diskName} is in use by running processes.");

        return errors;
    }


    // ============================================================
    //  PARSER ROBUSTO PARA ROTA
    // ============================================================

    private bool ParseRota(dynamic rotaToken)
    {
        try
        {
            if (rotaToken == null)
                return false;

            if (rotaToken is bool b)
                return b;

            if (rotaToken is long l)
                return l != 0;

            string s = rotaToken.ToString().Trim().ToLowerInvariant();

            if (s == "1" || s == "true" || s == "yes")
                return true;

            if (s == "0" || s == "false" || s == "no")
                return false;

            if (bool.TryParse(s, out var parsedBool))
                return parsedBool;

            if (int.TryParse(s, out var parsedInt))
                return parsedInt != 0;

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[RAID] ParseRota() EXCEPTION:");
            Console.WriteLine(ex);
            return false;
        }
    }

    // ============================================================
    //  LECTURA DE ARRAYS (SIN CAMBIOS)
    // ============================================================


    public async Task<List<RaidArrayInfo>> GetArraysAsync()
    {
        var arrays = new List<RaidArrayInfo>();

        // ============================================================
        // 1) mdadm universal (detecta ruta automáticamente)
        // ============================================================
        var mdadmPath = await ShellHelper.RunCleanAsync("command -v mdadm");
        mdadmPath = mdadmPath.Trim();
        if (string.IsNullOrWhiteSpace(mdadmPath))
            return arrays;

        var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot($"{mdadmPath} --detail --scan");
        var scan = (stdout + "\n" + stderr).Trim();

        Console.WriteLine("[RAID] mdadm --detail --scan OUTPUT:");
        Console.WriteLine(scan);

        if (string.IsNullOrWhiteSpace(scan))
            return arrays;

        // ============================================================
        // 2) Parsear cada ARRAY detectado (formato universal)
        // ============================================================
        foreach (var raw in scan.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("ARRAY"))
                continue;

            // ARRAY /dev/md0 metadata=1.2 name=host:0 UUID=xxxx
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var arrayPath = tokens[1];
            var arrayName = arrayPath.Split('/').Last();

            // ============================================================
            // 3) Obtener detalle real del array
            // ============================================================
            var detail = await RunMdadmAsync($"--detail {arrayPath}");

            Console.WriteLine("=== DETAIL OUTPUT ===");
            Console.WriteLine(detail);
            Console.WriteLine("=====================");

            if (string.IsNullOrWhiteSpace(detail))
                continue;

            var state = ParseArrayState(detail);
            var level = ParseLevel(detail);

            // ============================================================
            // 4) Obtener discos del array
            // ============================================================
            var diskNames = await GetDisksInArrayAsync(arrayName, detail);

            var info = new RaidArrayInfo
            {
                Name = arrayName,
                Path = arrayPath,
                Level = level,
                State = state,
                StateIcon = GetStateIcon(state),
                Disks = new List<RaidDiskInfo>(),

                TotalSize = ParseTotalSize(detail),
                UsableSize = ParseTotalSize(detail),
                ParitySize = "N/A",
                AverageTemp = 0,
                DiskSummary = $"{diskNames.Count}× Disk",
                Uptime = ParseUptime(detail),
                RebuildProgress = ParseRebuildProgress(detail),
                RebuildETA = ParseRebuildEta(detail)
            };

            // ============================================================
            // 5) Obtener MountPath universal
            // ============================================================
            try
            {
                var lsblk = await ShellHelper.RunCleanAsync($"lsblk -J /dev/{arrayName}");
                dynamic blk = JsonConvert.DeserializeObject(lsblk)!;

                var mount = "";

                try
                {
                    // Fedora / Arch / openSUSE → mountpoints (array)
                    if (blk.blockdevices[0].mountpoints != null)
                    {
                        var mps = blk.blockdevices[0].mountpoints;
                        if (mps.Count > 0)
                            mount = mps[0] ?? "";
                    }
                    else
                    {
                        // Debian / Ubuntu → mountpoint (string)
                        mount = blk.blockdevices[0].mountpoint ?? "";
                    }
                }
                catch
                {
                    mount = "";
                }

                info.MountPath = mount;
                info.IsMounted = !string.IsNullOrWhiteSpace(mount);
            }
            catch
            {
                info.MountPath = "";
                info.IsMounted = false;
            }

            // ============================================================
            // 6) Obtener información detallada de cada disco
            // ============================================================
            foreach (var dev in diskNames)
            {
                var devName = dev.Split('/').Last();

                var diskInfo = await GetDiskInfo(devName);

                diskInfo.Role = ParseDiskRole(detail, devName);
                diskInfo.State = ParseDiskStateFromRole(diskInfo.Role);
                diskInfo.ArrayName = arrayName;

                info.Disks.Add(diskInfo);
            }

            arrays.Add(info);
        }

        // ============================================================
        // 7) Trazas finales
        // ============================================================
        Console.WriteLine("=== ARRAYS DETECTADOS ===");
        foreach (var arr in arrays)
        {
            Console.WriteLine($"ARRAY: {arr.Name}  Level={arr.Level}  State={arr.State}");
            Console.WriteLine($"  MountPath={arr.MountPath}  IsMounted={arr.IsMounted}");
            Console.WriteLine($"  Discos detectados: {arr.Disks.Count}");

            foreach (var d in arr.Disks)
                Console.WriteLine($"    - {d.Name}  Role={d.Role}  State={d.State}  Size={d.Size}  Model={d.Model}");
        }

        Console.WriteLine("=========================");

        Console.WriteLine($"[RAID] Arrays detectados: {arrays.Count}");

        return arrays;
    }


    // ============================================================
    //  PARSERS (SIN CAMBIOS)
    // ============================================================
/*
        private string ParseLevel(string detail)
        {
            foreach (var raw in detail.Split('\n'))
            {
                string line = raw.Trim().ToLowerInvariant();
                if (line.StartsWith("raid level"))
                {
                    return line.Replace("raid level :", "").Trim().ToUpper();
                }
            }
            return "UNKNOWN";
        }

        */
/*
        private string ParseArrayState(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return "Unknown";

            string lower = detail.ToLowerInvariant();

            // PRIMERO: degradado
            if (lower.Contains("state : clean, degraded") ||
                lower.Contains("state : degraded"))
                return "Degraded";

            // Luego: rebuild/resync/recover SOLO si aparece en el estado real
            // y NO en "Consistency Policy"
            if (lower.Contains("resync=") ||      // /proc/mdstat style
                lower.Contains("recovery =") ||
                lower.Contains("rebuild =") ||
                lower.Contains("recovering"))
                return "Rebuilding";

            // Fallos graves
            if (lower.Contains("state : inactive") ||
                lower.Contains("state : stopped") ||
                lower.Contains("state : faulty") ||
                lower.Contains("state : failed"))
                return "Failed";

            // Estados sanos
            if (lower.Contains("state : clean") ||
                lower.Contains("state : active"))
                return "Healthy";

            return "Unknown";
        }
*/

    private string ParseArrayState(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "Unknown";

        var lower = detail.ToLowerInvariant();

        // -----------------------------------------
        // 1) Estados críticos (FAILED)
        // -----------------------------------------
        if (lower.Contains("state : faulty") ||
            lower.Contains("state : failed") ||
            lower.Contains("state : inactive") ||
            lower.Contains("state : stopped"))
            return "Failed";

        // -----------------------------------------
        // 2) Estados degradados (DEGRADED)
        // -----------------------------------------
        if (lower.Contains("state : clean, degraded") ||
            lower.Contains("state : degraded") ||
            lower.Contains("degraded"))
            return "Degraded";

        // -----------------------------------------
        // 3) Estados de reconstrucción (REBUILDING)
        // -----------------------------------------
        if (lower.Contains("resync=") || // /proc/mdstat style
            lower.Contains("recovery =") ||
            lower.Contains("rebuild =") ||
            lower.Contains("recovering") ||
            lower.Contains("resyncing") ||
            lower.Contains("checking"))
            return "Rebuilding";

        // -----------------------------------------
        // 4) Estado de solo lectura (READ-ONLY)
        // -----------------------------------------
        if (lower.Contains("read-only"))
            return "Read-Only";

        // -----------------------------------------
        // 5) Estados sanos (HEALTHY)
        // -----------------------------------------
        if (lower.Contains("state : clean") ||
            lower.Contains("state : active"))
            return "Healthy";

        return "Unknown";
    }


    public bool AutoAssemble()
    {
        Console.WriteLine("[RAID] Ejecutando AutoAssemble()");

        // mdadm --assemble --scan es la forma correcta de revivir arrays detenidos
        var result = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --assemble --scan");

        Console.WriteLine($"[RAID] AutoAssemble EXIT={result.ExitCode}");
        Console.WriteLine($"[RAID] AutoAssemble STDOUT:\n{result.Stdout}");
        Console.WriteLine($"[RAID] AutoAssemble STDERR:\n{result.Stderr}");

        // mdadm devuelve 0 si ensambló algo o si ya estaba ensamblado
        return result.ExitCode == 0;
    }


    public async Task<bool> InitializeArrayAsync(string arrayName, string fsType, string label)
    {
        try
        {
            // ⭐ VALIDACIÓN CRÍTICA
            if (!await EnsureArraySafeForModification(arrayName))
                return false;

            var devPath = $"/dev/{arrayName}";

            // 1) Cargar configuración JSON del array
            var cfg = ArrayConfigService.Load(arrayName);

            // 2) Determinar punto de montaje
            var mountPath = string.IsNullOrWhiteSpace(cfg.MountPoint)
                ? $"/mnt/{arrayName}"
                : cfg.MountPoint;

            Console.WriteLine($"[RAID] Inicializando {devPath} con FS={fsType}, label='{label}', mount={mountPath}");

            // 3) Si está montado → desmontar antes de formatear
            var checkMount = ShellHelper.EjecutarComoRoot($"mount | grep -w {devPath}");
            if (checkMount.ExitCode == 0)
            {
                Console.WriteLine($"[RAID] {devPath} está montado. Desmontando...");

                var umount = ShellHelper.EjecutarComoRoot($"umount -f {devPath}");
                if (umount.ExitCode != 0)
                {
                    Console.WriteLine($"[RAID] ERROR desmontando: {umount.Stderr}");
                    return false;
                }

                // Limpiar entrada previa en fstab
                ShellHelper.EjecutarComoRoot($"sed -i '\\#{devPath}#d' /etc/fstab");
            }

            // 4) Verificar que el dispositivo existe
            var ls = ShellHelper.EjecutarComoRoot($"ls {devPath}");
            if (ls.ExitCode != 0)
            {
                Console.WriteLine($"[RAID] ERROR: {devPath} no existe.");
                return false;
            }

            // 5) Construir comando mkfs
            var mkfsCmd = fsType switch
            {
                "ext4" => string.IsNullOrWhiteSpace(label)
                    ? $"mkfs.ext4 -F {devPath}"
                    : $"mkfs.ext4 -F -L \"{label}\" {devPath}",
                "xfs" => string.IsNullOrWhiteSpace(label)
                    ? $"mkfs.xfs -f {devPath}"
                    : $"mkfs.xfs -f -L \"{label}\" {devPath}",
                "btrfs" => string.IsNullOrWhiteSpace(label)
                    ? $"mkfs.btrfs -f {devPath}"
                    : $"mkfs.btrfs -f -L \"{label}\" {devPath}",
                "f2fs" => string.IsNullOrWhiteSpace(label)
                    ? $"mkfs.f2fs -f {devPath}"
                    : $"mkfs.f2fs -f -l \"{label}\" {devPath}",
                "vfat (FAT32)" => string.IsNullOrWhiteSpace(label)
                    ? $"mkfs.vfat -F 32 {devPath}"
                    : $"mkfs.vfat -F 32 -n \"{label}\" {devPath}",
                "exfat" => string.IsNullOrWhiteSpace(label)
                    ? $"mkfs.exfat {devPath}"
                    : $"mkfs.exfat -n \"{label}\" {devPath}",
                "ntfs" => string.IsNullOrWhiteSpace(label)
                    ? $"mkfs.ntfs -f {devPath}"
                    : $"mkfs.ntfs -f -L \"{label}\" {devPath}",
                "swap" => string.IsNullOrWhiteSpace(label) ? $"mkswap {devPath}" : $"mkswap -L \"{label}\" {devPath}",
                _ => throw new Exception("Filesystem no soportado")
            };

            // 6) Formatear
            var mkfs = ShellHelper.EjecutarComoRoot(mkfsCmd);
            if (mkfs.ExitCode != 0)
            {
                Console.WriteLine($"[RAID] ERROR formateando: {mkfs.Stderr}");
                return false;
            }

            // 7) Si es swap → activar y terminar
            if (fsType == "swap")
            {
                ShellHelper.EjecutarComoRoot($"swapon {devPath}");
                return true;
            }

            // 8) Crear directorio de montaje
            ShellHelper.EjecutarComoRoot($"mkdir -p {mountPath}");

            // 9) Construir opciones de montaje desde JSON
            List<string> opts = new();

            opts.Add("users"); // permitir desmontar sin sudo

            if (cfg.Mount_NoAtime) opts.Add("noatime");
            if (cfg.Mount_NoDirAtime) opts.Add("nodiratime");
            if (cfg.Mount_Discard) opts.Add("discard");
            if (cfg.Mount_Sync) opts.Add("sync");
            if (cfg.Mount_ReadOnly) opts.Add("ro");

            if (opts.Count == 1)
                opts.Add("defaults");

            var mountOpts = string.Join(",", opts);

            // ⭐ 10) Montar SOLO si PersistMount = true
            if (cfg.PersistMount)
            {
                var mount = ShellHelper.EjecutarComoRoot($"mount -o {mountOpts} {devPath} {mountPath}");
                if (mount.ExitCode != 0)
                {
                    Console.WriteLine($"[RAID] ERROR montando: {mount.Stderr}");
                    return false;
                }

                // Aplicar permisos
                var perms = string.IsNullOrWhiteSpace(cfg.MountPermissions)
                    ? "755"
                    : cfg.MountPermissions;

                ShellHelper.EjecutarComoRoot($"chmod {perms} {mountPath}");

                // ⭐ 11) Escribir entrada en fstab
                var fs = NormalizeFs(fsType);
                var entry = $"{devPath} {mountPath} {fs} {mountOpts} 0 0";

                // eliminar entradas previas
                ShellHelper.EjecutarComoRoot($"sed -i '\\#{devPath}#d' /etc/fstab'");

                // añadir entrada nueva
                ShellHelper.EjecutarComoRoot($"bash -c \"echo '{entry}' >> /etc/fstab\"");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RAID] ERROR en InitializeArrayAsync: {ex}");
            return false;
        }
    }


    private string ParseLevel(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "UNKNOWN";

        foreach (var raw in detail.Split('\n'))
        {
            var line = raw.Trim().ToLowerInvariant();

            // Formatos válidos:
            // Raid Level : raid1
            // Level : raid10
            // Raid Level : -unknown-
            // Raid Level : container
            if (line.StartsWith("raid level") || line.StartsWith("level"))
            {
                // Extraer después de los dos puntos
                var idx = line.IndexOf(':');
                if (idx < 0)
                    continue;

                var level = line[(idx + 1)..].Trim();

                // Normalizar
                return level switch
                {
                    "raid0" => "RAID0",
                    "raid1" => "RAID1",
                    "raid4" => "RAID4",
                    "raid5" => "RAID5",
                    "raid6" => "RAID6",
                    "raid10" => "RAID10",
                    "linear" => "LINEAR",
                    "container" => "CONTAINER",
                    "multipath" => "MULTIPATH",
                    "-unknown-" => "UNKNOWN",
                    _ => level.ToUpperInvariant()
                };
            }
        }

        return "UNKNOWN";
    }


    private string NormalizeFs(string fs)
    {
        if (string.IsNullOrWhiteSpace(fs))
            return "auto";

        fs = fs.Trim().ToLowerInvariant();

        return fs switch
        {
            "ext4" => "ext4",
            "xfs" => "xfs",
            "btrfs" => "btrfs",
            "f2fs" => "f2fs",

            // UI-friendly names → real FS
            "vfat (fat32)" => "vfat",
            "fat32" => "vfat",
            "vfat" => "vfat",

            "exfat" => "exfat",

            "ntfs" => "ntfs",
            "ntfs (windows)" => "ntfs",

            "swap" => "swap",

            _ => "auto"
        };
    }


    private string ParseDiskRole(string detail, string device)
    {
        if (string.IsNullOrWhiteSpace(detail) || string.IsNullOrWhiteSpace(device))
            return "unknown";

        var dev = device.Replace("/dev/", "").Trim();

        foreach (var raw in detail.Split('\n'))
        {
            var line = raw.Trim();
            var lower = line.ToLowerInvariant();

            // Solo líneas que mencionan el dispositivo real
            if (!lower.Contains($"/dev/{dev}"))
                continue;

            // -----------------------------
            // ORDEN UNIVERSAL DE PRIORIDAD
            // -----------------------------

            // 1) faulty
            if (lower.Contains("faulty"))
                return "faulty";

            // 2) removed
            if (lower.Contains("removed"))
                return "removed";

            // 3) rebuilding / recovering / resync
            if (lower.Contains("rebuild") ||
                lower.Contains("recover") ||
                lower.Contains("resync"))
                return "rebuilding";

            // 4) write-mostly (Arch, Fedora)
            if (lower.Contains("write-mostly"))
                return "write-mostly";

            // 5) blocked (openSUSE)
            if (lower.Contains("blocked"))
                return "blocked";

            // 6) spare
            if (lower.Contains("spare"))
                return "spare";

            // 7) in-sync (openSUSE)
            if (lower.Contains("in-sync"))
                return "active";

            // 8) active (evitar confundir con inactive)
            if (lower.Contains(" active ") ||
                lower.Contains(" active,") ||
                lower.Contains(" active\t"))
                return "active";

            // 9) sync (Arch, Fedora)
            if (lower.Contains(" sync "))
                return "active";
        }

        return "unknown";
    }


/*
        private string ParseDiskStateFromRole(string role)
        {
            return role switch
            {
                "faulty"     => "FAULTY",
                "removed"    => "OFFLINE",
                "rebuilding" => "WARN",
                "spare"      => "OK",
                "active"     => "OK",
                _            => "UNKNOWN"
            };
        }
*/

    private string ParseDiskStateFromRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "UNKNOWN";

        role = role.Trim().ToLowerInvariant();

        return role switch
        {
            // Estados críticos
            "faulty" => "FAULTY",
            "removed" => "OFFLINE",
            "blocked" => "ERROR",

            // Estados de advertencia
            "rebuilding" => "WARN",
            "write-mostly" => "WARN",

            // Estados sanos
            "active" => "OK",
            "in-sync" => "OK",
            "spare" => "OK",

            // Cualquier otro → desconocido
            _ => "UNKNOWN"
        };
    }


    private string ParseTotalSize(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "Unknown";

        foreach (var raw in detail.Split('\n'))
        {
            var line = raw.Trim();

            if (!line.StartsWith("Array Size", StringComparison.OrdinalIgnoreCase))
                continue;

            // -----------------------------------------
            // 1) Si hay paréntesis → extraer tamaño humano
            // -----------------------------------------
            var idx = line.IndexOf('(');
            if (idx > 0)
            {
                var inside = line[(idx + 1)..];
                var end = inside.IndexOf(')');
                if (end > 0)
                {
                    var human = inside[..end].Trim();

                    // Ejemplos válidos:
                    // "20.0 GiB"
                    // "20.0 GiB 21474836480 bytes"
                    // "20.0 GiB 21474836480 bytes, 512K chunk"
                    var parts = human.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 2 && parts[1].ToUpper().Contains("IB"))
                        return $"{parts[0]} {parts[1]}"; // "20.0 GiB"
                }
            }

            // -----------------------------------------
            // 2) Si NO hay paréntesis → convertir sectores a GiB
            // -----------------------------------------
            // Formato:
            // Array Size : 20953088
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var last = tokens.Last();

            if (long.TryParse(last, out var sectors))
            {
                // mdadm usa sectores de 512 bytes
                var bytes = sectors * 512.0;
                var gib = bytes / (1024 * 1024 * 1024);

                return $"{gib:F2} GiB";
            }
        }

        return "Unknown";
    }


/*
        private string ParseTotalSize(string detail)
        {
            foreach (var raw in detail.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("Array Size", StringComparison.OrdinalIgnoreCase))
                {
                    int idx = line.IndexOf('(');
                    if (idx > 0)
                    {
                        string inside = line[(idx + 1)..];
                        int end = inside.IndexOf(')');
                        if (end > 0)
                            return inside[..end].Trim();
                    }
                }
            }
            return "Unknown";
        }
*/
    private string ParseUptime(string detail)
    {
        return "Unknown";
    }

    private int ParseRebuildProgress(string detail)
    {
        return 0;
    }

    private string ParseRebuildEta(string detail)
    {
        return "";
    }

    // ============================================================
    //  HELPERS
    // ============================================================


    private async Task<RaidDiskInfo> GetDiskInfo(string device)
    {
        var name = device.Replace("/dev/", "").Trim();

        // Normalizar nombres de particiones
        if (Regex.IsMatch(name, @"^sd[a-z][0-9]+$"))
            name = name.Substring(0, 3);

        if (Regex.IsMatch(name, @"^nvme[0-9]+n[0-9]+p[0-9]+$"))
            name = name.Split('p')[0];

        // -----------------------------------------
        // 1) lsblk universal (sin columnas)
        // -----------------------------------------
        var json = await ShellHelper.RunCleanAsync($"lsblk -J /dev/{name}");

        if (string.IsNullOrWhiteSpace(json))
            return CreateUnknownDisk(name);

        dynamic data;
        try
        {
            data = JsonConvert.DeserializeObject(json)!;
        }
        catch
        {
            return CreateUnknownDisk(name);
        }

        if (data.blockdevices == null || data.blockdevices.Count == 0)
            return CreateUnknownDisk(name);

        var dev = data.blockdevices[0];

        // -----------------------------------------
        // 2) Campos universales
        // -----------------------------------------
        string model = dev.model ?? "Unknown";
        string size = dev.size ?? "Unknown";
        bool isRotational = ParseRota(dev.rota);

        // -----------------------------------------
        // 3) Icono universal
        // -----------------------------------------
        var icon = DiskIconService.GetIcon(name, model, isRotational);

        // -----------------------------------------
        // 4) Crear objeto final
        // -----------------------------------------
        return new RaidDiskInfo
        {
            Name = name,
            Model = model,
            Size = size,
            Icon = icon,
            IsRotational = isRotational,
            ArrayName = ""
        };
    }


    /*
       private async Task<RaidDiskInfo> GetDiskInfo(string device)
       {
           string name = device.Replace("/dev/", "").Trim();

           if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^sd[a-z][0-9]+$"))
               name = name.Substring(0, 3);

           if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^nvme[0-9]+n[0-9]+p[0-9]+$"))
               name = name.Split('p')[0];

           string json = await ShellHelper.RunCleanAsync(
               $"lsblk -J -o NAME,MODEL,SIZE,ROTA,TYPE /dev/{name}"
           );

           if (string.IsNullOrWhiteSpace(json))
               return CreateUnknownDisk(name);

           dynamic data;
           try { data = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!; }
           catch { return CreateUnknownDisk(name); }

           if (data.blockdevices == null || data.blockdevices.Count == 0)
               return CreateUnknownDisk(name);

           var dev = data.blockdevices[0];

           string model = dev.model ?? "Unknown";
           string size = dev.size ?? "Unknown";
           bool isRotational = ParseRota(dev.rota);

           string icon = DiskIconService.GetIcon(name, model, isRotational);

           return new RaidDiskInfo
           {
               Name = name,
               Model = model,
               Size = size,
               Icon = icon,
               IsRotational = isRotational,
               ArrayName = ""
           };
       }
*/

/*
        private RaidDiskInfo CreateUnknownDisk(string name)
        {
            return new RaidDiskInfo
            {
                Name = name,
                Model = "Unknown",
                Size = "Unknown",
                Icon = DiskIconService.GetIcon(name, "Unknown", false),

                ArrayName = ""
            };
        }

        */

    private RaidDiskInfo CreateUnknownDisk(string name)
    {
        return new RaidDiskInfo
        {
            Name = name,
            Model = "Unknown",
            Size = "Unknown",
            Icon = DiskIconService.GetIcon(name, "Unknown", false),
            ArrayName = ""
        };
    }


    private string GetStateIcon(string state)
    {
        return state switch
        {
            "Healthy" => "avares://RAID-Util/Assets/Icons/array-ok.png",
            "Degraded" => "avares://RAID-Util/Assets/Icons/array-caution.png",
            "Rebuilding" => "avares://RAID-Util/Assets/Icons/array-caution.png",
            "Read-Only" => "avares://RAID-Util/Assets/Icons/array-readonly.png",
            "Failed" => "avares://RAID-Util/Assets/Icons/array-error.png",
            _ => "avares://RAID-Util/Assets/Icons/array-caution.png"
        };
    }
/*
        private async Task<string> RunMdadmAsync(string arguments)
        {
            var candidates = new[]
            {
                $"/sbin/mdadm {arguments}",
                $"/usr/sbin/mdadm {arguments}",
                $"mdadm {arguments}"
            };

            foreach (var cmd in candidates)
            {
                var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot(cmd);
                string output = (stdout + "\n" + stderr).Trim();

                if (string.IsNullOrWhiteSpace(output))
                    continue;

                if (output.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (output.Contains("must be super-user", StringComparison.OrdinalIgnoreCase))
                    continue;

                return output;
            }

            return string.Empty;
        }
*/

    private async Task<string> RunMdadmAsync(string arguments)
    {
        // Rutas posibles en TODAS las distros modernas
        string[] candidates =
        {
            "/usr/sbin/mdadm",
            "/sbin/mdadm",
            "/usr/bin/mdadm",
            "/bin/mdadm",
            "mdadm"
        };

        foreach (var path in candidates)
        {
            var cmd = $"{path} {arguments}";
            var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot(cmd);

            if (exit == 0 && (!string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr)))
                return stdout + stderr;
        }

        return "";
    }


    public async Task<bool> DeleteArrayAsync(RaidArrayInfo array)
    {
        try
        {
            var arrayPath = array.Path; // /dev/md0
            var name = array.Name;

            LogService.Write($"[RAID] DELETE START → {name} ({arrayPath})");

            // 0) Asegurar entorno seguro
            if (!await EnsureArraySafeForModification(name))
            {
                LogService.Error("[RAID] DELETE ABORTED: EnsureArraySafeForModification failed.");
                return false;
            }

            // 1. Stop array (por si sigue activo)
            LogService.Write($"[RAID] Stopping array {arrayPath}");
            var stop = ShellHelper.EjecutarComoRoot($"mdadm --stop {arrayPath}");
            if (stop.ExitCode != 0) LogService.Error($"[RAID] STOP FAILED: {stop.Stderr}");

            // 2. Remove array
            LogService.Write($"[RAID] Removing array {arrayPath}");
            var remove = ShellHelper.EjecutarComoRoot($"mdadm --remove {arrayPath}");
            if (remove.ExitCode != 0) LogService.Error($"[RAID] REMOVE FAILED: {remove.Stderr}");

            // 3. Wipe superblocks
            foreach (var d in array.Disks)
            {
                LogService.Write($"[RAID] Wiping superblock on {d.Name}");
                var wipe = ShellHelper.EjecutarComoRoot($"mdadm --zero-superblock /dev/{d.Name}");

                if (wipe.ExitCode != 0)
                    LogService.Error($"[RAID] ZERO-SB FAILED on {d.Name}: {wipe.Stderr}");
            }

            // 4. Update mdadm.conf
            LogService.Write("[RAID] Updating mdadm.conf");
            ShellHelper.EjecutarComoRoot("mdadm --detail --scan > /etc/mdadm/mdadm.conf");

            // 5. Sync filesystem
            ShellHelper.EjecutarComoRoot("sync");

            LogService.Write($"[RAID] DELETE OK → {name}");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("[RAID] DELETE FAILED:");
            LogService.Error(ex.ToString());
            return false;
        }
    }
    
    public bool CreateArray(string level, List<RaidDiskInfo> disks, string? friendlyName = null)
{
    try
    {
        // ⭐ 0) Validación crítica: NUNCA permitir discos del sistema
        foreach (var d in disks)
        {
            if (d.IsSystemDisk)
            {
                LogService.Error($"[CREATE] ERROR: /dev/{d.Name} es disco del sistema. Operación bloqueada.");
                return false;
            }
        }

        // ⭐ 1) Validación previa de todos los discos (montaje, particiones, metadata, etc.)
        foreach (var d in disks)
        {
            var errors = ValidateDiskForRaidAsync(d.Name).Result;
            if (errors.Count > 0)
            {
                LogService.Error($"[CREATE] Validation failed for /dev/{d.Name}:");
                foreach (var e in errors)
                    LogService.Error("[VALIDATION] " + e);
                return false;
            }
        }

        // ⭐ 2) Detectar mdX libre
        var mdName = GetNextFreeMdName();
        var arrayPath = $"/dev/{mdName}";
        LastCreatedMdName = mdName;

        // ⭐ 3) Normalizar nivel RAID
        var mdadmLevel = level.ToLower() switch
        {
            "linear" => "linear",
            "jbod" => "linear",
            "jbod (linear)" => "linear",
            _ => level.Replace("RAID", "").Trim()
        };

        // ⭐ 4) Lista de dispositivos
        var deviceList = string.Join(" ", disks.Select(d => "/dev/" + d.Name));

        // ⭐ 5) Nombre amigable
        var nameForMdadm = string.IsNullOrWhiteSpace(friendlyName)
            ? mdName
            : friendlyName.Trim();

        // ⭐ 6) Comando mdadm
        var cmd =
            $"/usr/sbin/mdadm --create {arrayPath} " +
            $"--verbose " +
            $"--metadata=1.2 " +
            $"--name={nameForMdadm} " +
            $"--level={mdadmLevel} " +
            $"--raid-devices={disks.Count} " +
            $"{deviceList} --force --run";

        LogService.Write($"[CREATE] Ejecutando: {cmd}");

        var result = ShellHelper.EjecutarComoRoot(cmd);

        if (result.ExitCode != 0)
        {
            LogService.Error("[CREATE] mdadm falló:");
            LogService.Error(result.Stderr);
            return false;
        }

        // ⭐ 7) Esperar a udev
        ShellHelper.EjecutarComoRoot("udevadm settle");

        LogService.Write($"[CREATE] Array creado correctamente → {arrayPath}");
        return true;
    }
    catch (Exception ex)
    {
        LogService.Error("[CREATE] EXCEPCIÓN:");
        LogService.Error(ex.ToString());
        return false;
    }
}

    
    
/*
    public bool CreateArray(string level, List<RaidDiskInfo> disks, string? friendlyName = null)
    {
        try
        {
            // 0) Validación previa de todos los discos
            foreach (var d in disks)
            {
                var errors = ValidateDiskForRaidAsync(d.Name).Result;
                if (errors.Count > 0)
                {
                    LogService.Error($"[CREATE] Validation failed for /dev/{d.Name}:");
                    foreach (var e in errors)
                        LogService.Error("[VALIDATION] " + e);
                    return false;
                }
            }

            var mdName = GetNextFreeMdName();
            var arrayPath = $"/dev/{mdName}";
            LastCreatedMdName = mdName;

            var mdadmLevel = level.ToLower() switch
            {
                "linear" => "linear",
                "jbod" => "linear",
                "jbod (linear)" => "linear",
                _ => level.Replace("RAID", "").Trim()
            };

            var deviceList = string.Join(" ", disks.Select(d => "/dev/" + d.Name));

            var nameForMdadm = string.IsNullOrWhiteSpace(friendlyName)
                ? mdName
                : friendlyName.Trim();

            var cmd =
                $"/usr/sbin/mdadm --create {arrayPath} " +
                $"--verbose " +
                $"--metadata=1.2 " +
                $"--name={nameForMdadm} " +
                $"--level={mdadmLevel} " +
                $"--raid-devices={disks.Count} " +
                $"{deviceList} --force --run";

            LogService.Write($"[CREATE] Ejecutando: {cmd}");

            var result = ShellHelper.EjecutarComoRoot(cmd);

            if (result.ExitCode != 0)
            {
                LogService.Error("[CREATE] mdadm falló:");
                LogService.Error(result.Stderr);
                return false;
            }

            ShellHelper.EjecutarComoRoot("udevadm settle");

            LogService.Write($"[CREATE] Array creado correctamente → {arrayPath}");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("[CREATE] EXCEPCIÓN:");
            LogService.Error(ex.ToString());
            return false;
        }
    }
*/

    public string GetNextFreeMdName()
    {
        // md0 hasta md127 (límite estándar del kernel)
        for (var i = 0; i < 128; i++)
        {
            var md = $"md{i}";
            var path = $"/dev/{md}";

            // Si NO existe el dispositivo → está libre
            if (!File.Exists(path))
                return md;

            // Si existe pero mdadm dice que NO es un array → también está libre
            var result = ShellHelper.EjecutarComoRoot($"mdadm --detail {path}");
            if (result.ExitCode != 0)
                return md;
        }

        // Si llegamos aquí, no hay mdX libres (muy improbable)
        throw new Exception("No hay dispositivos mdX libres disponibles.");
    }

    public async Task<string> GetSmartInfoAsync(string diskName)
    {
        var cmd = $"sudo smartctl -a /dev/{diskName}";
        return await ShellHelper.RunCleanAsync(cmd);
    }

    public bool MarkDiskAsFaulty(string arrayName, string diskName)
    {
        var cmd = $"/usr/sbin/mdadm /dev/{arrayName} --fail /dev/{diskName}";
        var result = ShellHelper.EjecutarComoRoot(cmd);
        return result.ExitCode == 0;
    }

    public bool SetDiskAsSpare(string arrayName, string diskName)
    {
        var cmd = $"/usr/sbin/mdadm /dev/{arrayName} --add /dev/{diskName}";
        var result = ShellHelper.EjecutarComoRoot(cmd);
        return result.ExitCode == 0;
    }

    public async Task<bool> RemoveDiskFromArrayAsync(string arrayName, string diskName)
    {
        try
        {
            // ⭐ VALIDACIÓN CRÍTICA
            if (!await EnsureArraySafeForModification(arrayName))
                return false;

            LogService.Write($"[RAID] RemoveDiskFromArrayAsync START → array={arrayName}, disk={diskName}");

            // 1) Detectar nivel RAID
            var detail = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail /dev/{arrayName}");
            var raidLevel = "unknown";

            if (detail.ExitCode == 0)
                foreach (var line in detail.Stdout.Split('\n'))
                    if (line.Trim().StartsWith("Raid Level"))
                    {
                        raidLevel = line.Split(':')[1].Trim().ToLower();
                        break;
                    }

            var supportsFail = raidLevel switch
            {
                "raid0" => false,
                "linear" => false,
                "multipath" => false,
                _ => true
            };

            // 2) FAIL si aplica
            if (supportsFail)
                ShellHelper.EjecutarComoRoot(
                    $"/usr/sbin/mdadm /dev/{arrayName} --fail /dev/{diskName}"
                );

            // 3) REMOVE
            var remove = ShellHelper.EjecutarComoRoot(
                $"/usr/sbin/mdadm /dev/{arrayName} --remove /dev/{diskName}"
            );

            if (remove.ExitCode != 0)
            {
                LogService.Error($"[RAID] RemoveDiskFromArrayAsync FAILED: {remove.Stderr}");
                return false;
            }

            // 4) Limpieza de metadata
            ShellHelper.EjecutarComoRoot($"/sbin/mdadm --zero-superblock /dev/{diskName}");
            ShellHelper.EjecutarComoRoot($"/usr/sbin/wipefs -a /dev/{diskName}");

            ShellHelper.EjecutarComoRoot("udevadm settle");

            LogService.Write("[RAID] RemoveDiskFromArrayAsync OK → disk cleaned and removed.");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("[RAID] RemoveDiskFromArrayAsync EXCEPTION:");
            LogService.Error(ex.ToString());
            return false;
        }
    }


    public async Task<bool> StartArrayResyncAsync(string arrayName)
    {
        var cmd = $"/usr/sbin/mdadm --readwrite /dev/{arrayName}";
        var result = ShellHelper.EjecutarComoRoot(cmd);

        return result.ExitCode == 0;
    }


    public async Task<bool> ExpandArrayAndResizeAsync(string arrayName, int newDeviceCount)
    {
        try
        {
            // ⭐ VALIDACIÓN CRÍTICA
            if (!await EnsureArraySafeForModification(arrayName))
                return false;

            var dev = $"/dev/{arrayName}";

            // 1) Expandir RAID (grow)
            var grow = ShellHelper.EjecutarComoRoot(
                $"/usr/sbin/mdadm --grow {dev} --raid-devices={newDeviceCount}"
            );

            if (grow.ExitCode != 0)
            {
                LogService.Error($"[GROW] ERROR: {grow.Stderr}");
                return false;
            }

            // 2) Esperar reshape
            while (true)
            {
                var mdstat = await ShellHelper.RunCleanAsync("cat /proc/mdstat");

                if (!mdstat.Contains("reshape"))
                    break;

                await Task.Delay(2000);
            }

            // 3) Redimensionar filesystem EXT4
            var resize = ShellHelper.EjecutarComoRoot(
                $"/sbin/resize2fs {dev}"
            );

            if (resize.ExitCode != 0)
            {
                LogService.Error($"[RESIZE] ERROR: {resize.Stderr}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("[EXPAND] EXCEPTION:");
            LogService.Error(ex.ToString());
            return false;
        }
    }


    public async Task<bool> ForceArrayCheckAsync(string arrayName)
    {
        var cmd = $"/usr/sbin/mdadm --action=check /dev/{arrayName}";
        var result = ShellHelper.EjecutarComoRoot(cmd);

        return result.ExitCode == 0;
    }

    public async Task<bool> ForceArrayRepairAsync(string arrayName)
    {
        var cmd = $"/usr/sbin/mdadm --action=repair /dev/{arrayName}";
        var result = ShellHelper.EjecutarComoRoot(cmd);

        return result.ExitCode == 0;
    }
/*
public async Task<(int ExitCode, string Stdout, string Stderr)> StopArrayAsync(string arrayName)
{
    string cmd = $"/usr/sbin/mdadm --stop /dev/{arrayName}";
    return ShellHelper.EjecutarComoRoot(cmd);
}
*/

    public async Task<(bool Ok, string Message)> StopArraySafeAsync(string arrayName)
    {
        try
        {
            LogService.Write($"[STOP] StopArraySafeAsync → {arrayName}");

            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a => a.Name == arrayName || a.Path.EndsWith(arrayName));

            if (array == null)
                return (false, $"Array {arrayName} not found.");

            var arrayPath = array.Path; // /dev/md0

            // 1) Quitar de fstab
            RemoveArrayFromFstab(arrayPath);

            // 2) Desmontar si está montado
            if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
            {
                LogService.Write($"[STOP] Unmounting {array.MountPath}...");
                var um = ShellHelper.EjecutarComoRoot($"umount -f \"{array.MountPath}\"");

                if (um.ExitCode != 0)
                {
                    LogService.Error($"[STOP] Unmount failed: {um.Stderr}");
                    return (false, $"Failed to unmount {array.MountPath}:\n{um.Stderr}");
                }
            }

            // 3) Esperar mdadm libre
            if (!await WaitForMdadmIdleAsync())
                return (false, "mdadm is busy, cannot stop array.");

            // 4) Parar array
            var stop = ShellHelper.EjecutarComoRoot($"mdadm --stop {arrayPath}");

            if (stop.ExitCode != 0)
            {
                LogService.Error($"[STOP] mdadm --stop failed: {stop.Stderr}");
                return (false, $"Failed to stop array:\n{stop.Stderr}");
            }

            LogService.Write("[STOP] StopArraySafeAsync OK");
            return (true, "Array stopped.");
        }
        catch (Exception ex)
        {
            LogService.Error("[STOP] EXCEPTION:");
            LogService.Error(ex.ToString());
            return (false, "Unexpected error stopping array. Check logs.");
        }
    }


    public async Task<string> GetArrayDetailsAsync(string arrayName)
    {
        var cmd = $"/usr/sbin/mdadm --detail /dev/{arrayName}";
        var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot(cmd);

        return (stdout + "\n" + stderr).Trim();
    }


    public bool PersistArrayToMdadmConf()
    {
        try
        {
            LogService.Write("========== MDADM PERSIST START ==========");

            // 1. Ejecutar mdadm --detail --scan
            var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --detail --scan");

            LogService.Write($"[MDADM] exit={exit}");
            LogService.Write("[MDADM] STDOUT:");
            LogService.Write(stdout ?? "<null>");
            LogService.Write("[MDADM] STDERR:");
            LogService.Write(stderr ?? "<null>");

            if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                LogService.Error("[MDADM] No se pudo obtener la salida de mdadm --detail --scan.");
                return false;
            }

            // 2. Filtrar líneas ARRAY
            var arrayLines = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("ARRAY"))
                .ToList();

            LogService.Write($"[MDADM] Líneas ARRAY detectadas: {arrayLines.Count}");

            if (arrayLines.Count == 0)
            {
                LogService.Error("[MDADM] No se detectaron líneas ARRAY.");
                return false;
            }

            // 3. Seleccionar el mdX más alto (el recién creado)
            var newest = arrayLines
                .OrderByDescending(l =>
                {
                    var match = Regex.Match(l, @"md(\d+)");
                    return match.Success ? int.Parse(match.Groups[1].Value) : -1;
                })
                .First();

            LogService.Write("[MDADM] Línea seleccionada:");
            LogService.Write(newest);

            // 4. Rutas posibles según distro
            string[] possiblePaths =
            {
                "/etc/mdadm/mdadm.conf", // Debian/Ubuntu
                "/etc/mdadm.conf" // Arch/Manjaro
            };

            // 5. Elegir la ruta correcta
            var confPath = possiblePaths.FirstOrDefault(File.Exists)
                           ?? possiblePaths[0]; // Si no existe ninguna, usar Debian-style

            LogService.Write($"[MDADM] Usando ruta de configuración: {confPath}");

            // 6. Crear archivo si no existe
            if (!File.Exists(confPath))
            {
                LogService.Write("[MDADM] Archivo no existe. Creándolo...");
                File.WriteAllText(confPath,
                    "DEVICE partitions\n" +
                    "MAILADDR root\n\n"
                );
            }

            // 7. Leer contenido actual
            var existing = File.ReadAllText(confPath);

            // 8. Evitar duplicados
            if (existing.Contains(newest))
            {
                LogService.Write("[MDADM] La entrada ya existe en mdadm.conf");
                return true;
            }

            // 9. Escribir entrada nueva
            var tempFile = "/tmp/mdadm.conf.append";
            File.WriteAllText(tempFile, newest + Environment.NewLine);

            var append = ShellHelper.EjecutarComoRoot($"cat {tempFile} >> {confPath}");

            LogService.Write($"[MDADM] append exit={append.ExitCode}");
            LogService.Write("[MDADM] append STDERR:");
            LogService.Write(append.Stderr ?? "<null>");

            if (append.ExitCode != 0)
            {
                LogService.Error("[MDADM] Error al escribir en mdadm.conf:");
                LogService.Error(append.Stderr);
                return false;
            }

            LogService.Write("[MDADM] Entrada agregada correctamente a mdadm.conf");
            LogService.Write("========== MDADM PERSIST END (OK) ==========");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("[MDADM] EXCEPCIÓN:");
            LogService.Error(ex.ToString());
            LogService.Write("========== MDADM PERSIST END (EXCEPTION) ==========");
            return false;
        }
    }


    public bool WaitForArray(string mdName, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var result = ShellHelper.EjecutarComoRoot($"ls /dev/{mdName}");

            if (result.ExitCode == 0)
                return true;

            Thread.Sleep(100);
        }

        return false;
    }


    public string GetMdadmScan()
    {
        var result = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --detail --scan");

        if (result.ExitCode != 0)
            return "";

        return result.Stdout.Trim();
    }


    public bool FormatArray(string arrayName, string fs, string label)
    {
        try
        {
            // ⭐ VALIDACIÓN CRÍTICA
            if (!EnsureArraySafeForModification(arrayName).Result)
                return false;

            var cmd = fs switch
            {
                "ext4" => string.IsNullOrWhiteSpace(label)
                    ? $"/usr/sbin/mkfs.ext4 -F /dev/{arrayName}"
                    : $"/usr/sbin/mkfs.ext4 -F -L \"{label}\" /dev/{arrayName}",

                "xfs" => string.IsNullOrWhiteSpace(label)
                    ? $"/usr/sbin/mkfs.xfs -f /dev/{arrayName}"
                    : $"/usr/sbin/mkfs.xfs -f -L \"{label}\" /dev/{arrayName}",

                "btrfs" => string.IsNullOrWhiteSpace(label)
                    ? $"/usr/sbin/mkfs.btrfs -f /dev/{arrayName}"
                    : $"/usr/sbin/mkfs.btrfs -f -L \"{label}\" /dev/{arrayName}",

                _ => throw new Exception("Unknown filesystem")
            };

            LogService.Write($"[FORMAT] Ejecutando: {cmd}");

            var result = ShellHelper.EjecutarComoRoot(cmd);

            if (result.ExitCode != 0)
            {
                LogService.Error("[FORMAT] Error:");
                LogService.Error(result.Stderr);
                return false;
            }

            LogService.Write("[FORMAT] Formateo completado.");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("[FORMAT] EXCEPCIÓN:");
            LogService.Error(ex.ToString());
            return false;
        }
    }


    public bool RemoveArrayFromFstab(string arrayPath)
    {
        try
        {
            var fstab = "/etc/fstab";

            if (!File.Exists(fstab))
            {
                LogService.Write("[FSTAB] No existe /etc/fstab, nada que limpiar.");
                return true;
            }

            var lines = File.ReadAllLines(fstab);

            var dev = arrayPath.Trim(); // /dev/md0
            var name = dev.Split('/').Last(); // md0
            var nameOnly = name.Replace("md", ""); // 0 (por si acaso)

            // ⭐ Patrones que deben eliminarse
            string[] patterns =
            {
                dev, // /dev/md0
                $"/dev/{name}", // /dev/md0
                $"/dev/md/{name}", // /dev/md/md0
                name, // md0
                $"md{nameOnly}", // md0
                $"md/{name}", // md/md0
                $"md/{nameOnly}" // md/0
            };

            var newLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                var match = patterns.Any(p => trimmed.StartsWith(p, StringComparison.Ordinal));

                // ⭐ También eliminar entradas por UUID
                if (trimmed.StartsWith("UUID=", StringComparison.Ordinal) &&
                    trimmed.Contains(name, StringComparison.Ordinal))
                    match = true;

                // ⭐ También eliminar symlinks de mdadm
                if (trimmed.Contains("md-name", StringComparison.Ordinal) ||
                    trimmed.Contains("md-uuid", StringComparison.Ordinal))
                    if (trimmed.Contains(name, StringComparison.Ordinal))
                        match = true;

                if (!match)
                    newLines.Add(line);
            }

            if (newLines.Count == lines.Length)
            {
                LogService.Write("[FSTAB] No había entrada para este array.");
                return true;
            }

            var tempFile = "/tmp/fstab.cleaned";
            File.WriteAllLines(tempFile, newLines);

            var result = ShellHelper.EjecutarComoRoot($"cp {tempFile} {fstab}");

            if (result.ExitCode != 0)
            {
                LogService.Error("[FSTAB] Error al actualizar /etc/fstab:");
                LogService.Error(result.Stderr);
                return false;
            }

            LogService.Write("[FSTAB] Entrada eliminada correctamente.");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("[FSTAB] EXCEPCIÓN:");
            LogService.Error(ex.ToString());
            return false;
        }
    }

    public async Task<bool> EnsureArraySafeForModification(string arrayName)
    {
        var dev = $"/dev/{arrayName}";

        // 1) ¿Está montado?
        var mounts = await ShellHelper.RunCleanAsync(
            $"grep -E '^/dev/{arrayName}\\b' /proc/mounts || true");

        if (!string.IsNullOrWhiteSpace(mounts))
        {
            NotificadorLinux.Enviar(
                $"{dev} está montado. No se puede modificar un array montado.",
                7000, "critical");
            return false;
        }

        // 2) ¿Está en fstab?
        var fstab = await ShellHelper.RunCleanAsync(
            $"grep -E '^/dev/{arrayName}\\b' /etc/fstab || true");

        if (!string.IsNullOrWhiteSpace(fstab))
        {
            NotificadorLinux.Enviar(
                $"{dev} está en /etc/fstab. Debe eliminarse antes de modificar el array.",
                7000, "critical");
            return false;
        }

        return true;
    }


    public async Task<List<string>> GetDisksInArrayAsync(string arrayName, string detail)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(detail))
            return result;

        // Normalizar arrayName (md0, md/0, md127, etc.)
        var normalizedArray = arrayName.Replace("/dev/", "").Trim();

        foreach (var raw in detail.Split('\n'))
        {
            var line = raw.Trim();
            var lower = line.ToLowerInvariant();

            // Saltar líneas que no contienen información de disco
            if (!lower.Contains("/dev/") &&
                !Regex.IsMatch(lower, @"\bsd[a-z]\b") &&
                !Regex.IsMatch(lower, @"nvme[0-9]+n[0-9]+"))
                continue;

            // Extraer el path del disco
            // Ejemplos válidos:
            // /dev/sda
            // /dev/sdb1
            // /dev/nvme0n1
            // /dev/nvme0n1p1
            var match = Regex.Match(
                line,
                @"(/dev/(sd[a-z][0-9]*|nvme[0-9]+n[0-9]+p?[0-9]*))"
            );

            if (!match.Success)
                continue;

            var devPath = match.Groups[1].Value;
            var devName = devPath.Replace("/dev/", "").Trim();

            // Evitar duplicados
            if (!result.Contains(devPath))
                result.Add(devPath);
        }

        return result;
    }
}