using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services
{
    public class RaidService
    {
        


public static Dictionary<string, dynamic> Nodes { get; private set; } = new();

public async Task<List<RaidDiskInfo>> GetAllDisksAsync()
{
    var result = new List<RaidDiskInfo>();

    // 1) Obtener arrays reales
    var arrays = await GetArraysAsync();
    var arrayMembers = arrays
        .SelectMany(a => a.Disks)
        .Select(d => d.Name)
        .ToHashSet();

    // 2) lsblk universal
    string json = await ShellHelper.RunCleanAsync(
        "lsblk -J -o NAME,TYPE,SIZE,MODEL,SERIAL,ROTA,MOUNTPOINT,FSTYPE,PKNAME"
    );

    if (string.IsNullOrWhiteSpace(json))
        return result;

    dynamic data;
    try
    {
        data = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;
    }
    catch
    {
        return result;
    }

    // 3) Indexar todos los nodos (discos + particiones)
    var nodes = new Dictionary<string, dynamic>();
    foreach (var dev in data.blockdevices)
    {
        nodes[(string)dev.name] = dev;

        if (dev.children != null)
        {
            foreach (var child in dev.children)
                nodes[(string)child.name] = child;
        }
    }

    // Exponer nodos globalmente
    Nodes = nodes;

    // 4) Procesar solo discos físicos
    foreach (var dev in data.blockdevices)
    {
        if ((string)dev.type != "disk")
            continue;

        string name = dev.name;
        string model = dev.model ?? "Unknown";
        string size = dev.size ?? "Unknown";
        string mount = dev.mountpoint ?? "";
        string fstype = dev.fstype ?? "";
        bool isRotational = ParseRota(dev.rota);

        // ⭐ Children reales del JSON
        List<string> children = new();
        if (dev.children != null)
        {
            foreach (var child in dev.children)
                children.Add((string)child.name);
        }

        // ⭐ Fallback: reconstruir children con PKNAME si no hay children
        if (children.Count == 0)
        {
            children = nodes
                .Where(kv => (string?)kv.Value.pkname == name)
                .Select(kv => (string)kv.Key)
                .Where(n => n != name)
                .ToList();
        }

        var disk = new RaidDiskInfo
        {
            Name = name,
            Size = size,
            Model = model,
            MountPoint = mount,
            Filesystem = fstype,
            IsRotational = isRotational,
            Children = children,
            Icon = DiskIconService.GetIcon(name, model, isRotational)
        };

        // ============================================================
        // ⭐ FLAGS DE SEGURIDAD
        // ============================================================

        // 1) ¿Está montado el disco completo?
        disk.IsMounted = !string.IsNullOrWhiteSpace(disk.MountPoint);

        // 2) ¿Alguna partición está montada?
        bool anyChildMounted = disk.Children.Any(childName =>
        {
            if (!nodes.TryGetValue(childName, out dynamic part))
                return false;

            string mp = part.mountpoint ?? "";
            return !string.IsNullOrWhiteSpace(mp);
        });

        if (anyChildMounted)
            disk.IsMounted = true;

        // 3) ¿Es disco del sistema?
        disk.IsSystemDisk =
            disk.MountPoint == "/" ||
            disk.MountPoint == "/boot" ||
            disk.MountPoint == "/boot/efi" ||
            disk.Children.Any(c =>
            {
                var child = nodes[c];
                string mp = child.mountpoint ?? "";
                return mp == "/" || mp == "/boot" || mp == "/boot/efi";
            });

        // 4) ¿Es swap?
        disk.IsSwap =
            disk.Filesystem == "swap" ||
            disk.Children.Any(c =>
            {
                var child = nodes[c];
                string fs = child.fstype ?? "";
                return fs == "swap";
            });

        // 5) ¿Pertenece a un array RAID real?
        disk.IsUsedByRaid = arrayMembers.Contains(name);

        // 6) ¿Tiene metadata RAID?
        string mdadmInfo = await RunMdadmAsync($"--examine /dev/{name}");
        disk.HasRaidMetadata = mdadmInfo.Contains("Raid Level", StringComparison.OrdinalIgnoreCase);

        result.Add(disk);
    }

    return result;
}




public async Task<bool> AddDiskToArrayAsync(string arrayName, string diskName)
{
    try
    {
        LogService.Write($"[RAID] AddDiskToArray START → array={arrayName}, disk={diskName}");

        // 1) Validar que el array existe
        var arrays = await GetArraysAsync();
        var array = arrays.FirstOrDefault(a => a.Name == arrayName || a.Path.EndsWith(arrayName));
        if (array == null)
        {
            LogService.Error($"[RAID] AddDiskToArray: array {arrayName} not found.");
            return false;
        }

        // 2) Validar que el disco no está ya en el array
        if (array.Disks.Any(d => d.Name == diskName))
        {
            LogService.Error($"[RAID] AddDiskToArray: disk {diskName} is already member of {arrayName}.");
            return false;
        }

        // 3) Validación previa del disco
        var errors = await ValidateDiskForRaidAsync(diskName);
        if (errors.Count > 0)
        {
            foreach (var e in errors)
                LogService.Error("[VALIDATION] " + e);

            return false;
        }


        // 4) Ejecutar mdadm --add
        string cmd = $"/usr/sbin/mdadm /dev/{arrayName} --add /dev/{diskName}";
        LogService.Write($"[RAID] Ejecutando: {cmd}");

        var result = ShellHelper.EjecutarComoRoot(cmd);

        LogService.Write($"[RAID] mdadm --add EXIT={result.ExitCode}");
        LogService.Write($"[RAID] STDOUT:\n{result.Stdout}");
        LogService.Write($"[RAID] STDERR:\n{result.Stderr}");

        if (result.ExitCode != 0)
        {
            LogService.Error($"[RAID] AddDiskToArray FAILED: {result.Stderr}");
            return false;
        }

        ShellHelper.EjecutarComoRoot("udevadm settle");

        await GetArraysAsync();

        LogService.Write($"[RAID] AddDiskToArray OK → {diskName} añadido a {arrayName}");
        return true;
    }
    catch (Exception ex)
    {
        LogService.Error("[RAID] AddDiskToArray EXCEPTION:");
        LogService.Error(ex.ToString());
        return false;
    }
}



public async Task<List<string>> ValidateDiskForRaidAsync(string diskName)
{
    var errors = new List<string>();

    // 1) ¿Está montado?
    string mount = await ShellHelper.RunCleanAsync($"lsblk -no MOUNTPOINT /dev/{diskName}");
    if (!string.IsNullOrWhiteSpace(mount))
        errors.Add($"Disk /dev/{diskName} is mounted at {mount}.");

    // 2) ¿Tiene particiones?
    string json = await ShellHelper.RunCleanAsync($"lsblk -J /dev/{diskName}");
    if (!string.IsNullOrWhiteSpace(json))
    {
        try
        {
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;

            if (data.blockdevices != null &&
                data.blockdevices.Count > 0 &&
                data.blockdevices[0].children != null)
            {
                errors.Add($"Disk /dev/{diskName} has partitions. (Must wipe first)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[RAID] ValidateDiskForRaidAsync lsblk JSON error:");
            Console.WriteLine(ex);
        }
    }

    // 3) Metadata RAID previa
    string mdadmInfo = await RunMdadmAsync($"--examine /dev/{diskName}");
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
    string fuser = await ShellHelper.RunCleanAsync($"fuser -v /dev/{diskName}");
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

                if (bool.TryParse(s, out bool parsedBool))
                    return parsedBool;

                if (int.TryParse(s, out int parsedInt))
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
    // 1) mdadm --detail --scan
    // ============================================================
    var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --detail --scan");
    string scan = (stdout + "\n" + stderr).Trim();

    Console.WriteLine("[RAID] mdadm --detail --scan OUTPUT:");
    Console.WriteLine(scan);

    if (string.IsNullOrWhiteSpace(scan))
        return arrays;

    // ============================================================
    // 2) Parsear cada ARRAY detectado
    // ============================================================
    foreach (var raw in scan.Split('\n'))
    {
        string line = raw.Trim();
        if (!line.StartsWith("ARRAY"))
            continue;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            continue;

        // Path real del array (ej: /dev/md0)
        string arrayPath = parts[1];

        // Nombre amigable (md0)
        string arrayName = arrayPath.Split('/').Last();

        // ============================================================
        // 3) Obtener detalle real del array
        // ============================================================
        string detail = await RunMdadmAsync($"--detail {arrayPath}");

        Console.WriteLine("=== DETAIL OUTPUT ===");
        Console.WriteLine(detail);
        Console.WriteLine("=====================");

        if (string.IsNullOrWhiteSpace(detail))
            continue;

        string state = ParseArrayState(detail);
        string level = ParseLevel(detail);

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
        // 5) Obtener MountPath e IsMounted desde lsblk
        // ============================================================
        try
        {
            string lsblk = await ShellHelper.RunCleanAsync(
                $"lsblk -J -o NAME,MOUNTPOINT /dev/{arrayName}"
            );

            dynamic blk = Newtonsoft.Json.JsonConvert.DeserializeObject(lsblk)!;

            string mount = "";
            try
            {
                mount = blk.blockdevices[0].mountpoint ?? "";
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
            string devName = dev.Split('/').Last();

            RaidDiskInfo diskInfo = await GetDiskInfo(devName);

            // ⭐ ROLE REAL (mdadm)
            diskInfo.Role = ParseDiskRole(detail, devName);

            // ⭐ STATE DERIVADO DEL ROLE (lógica correcta)
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
        {
            Console.WriteLine($"    - {d.Name}  Role={d.Role}  State={d.State}  Size={d.Size}  Model={d.Model}");
        }
    }
    Console.WriteLine("=========================");

    Console.WriteLine($"[RAID] Arrays detectados: {arrays.Count}");

    return arrays;
}




        // ============================================================
        //  PARSERS (SIN CAMBIOS)
        // ============================================================

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
        string devPath = $"/dev/{arrayName}";

        // 1) Cargar configuración JSON del array
        var cfg = ArrayConfigService.Load(arrayName);

        // 2) Determinar punto de montaje
        string mountPath = string.IsNullOrWhiteSpace(cfg.MountPoint)
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
        string mkfsCmd = fsType switch
        {
            "ext4"          => string.IsNullOrWhiteSpace(label) ? $"mkfs.ext4 -F {devPath}" : $"mkfs.ext4 -F -L \"{label}\" {devPath}",
            "xfs"           => string.IsNullOrWhiteSpace(label) ? $"mkfs.xfs -f {devPath}" : $"mkfs.xfs -f -L \"{label}\" {devPath}",
            "btrfs"         => string.IsNullOrWhiteSpace(label) ? $"mkfs.btrfs -f {devPath}" : $"mkfs.btrfs -f -L \"{label}\" {devPath}",
            "f2fs"          => string.IsNullOrWhiteSpace(label) ? $"mkfs.f2fs -f {devPath}" : $"mkfs.f2fs -f -l \"{label}\" {devPath}",
            "vfat (FAT32)"  => string.IsNullOrWhiteSpace(label) ? $"mkfs.vfat -F 32 {devPath}" : $"mkfs.vfat -F 32 -n \"{label}\" {devPath}",
            "exfat"         => string.IsNullOrWhiteSpace(label) ? $"mkfs.exfat {devPath}" : $"mkfs.exfat -n \"{label}\" {devPath}",
            "ntfs"          => string.IsNullOrWhiteSpace(label) ? $"mkfs.ntfs -f {devPath}" : $"mkfs.ntfs -f -L \"{label}\" {devPath}",
            "swap"          => string.IsNullOrWhiteSpace(label) ? $"mkswap {devPath}" : $"mkswap -L \"{label}\" {devPath}",
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

        string mountOpts = string.Join(",", opts);

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
            string perms = string.IsNullOrWhiteSpace(cfg.MountPermissions)
                ? "755"
                : cfg.MountPermissions;

            ShellHelper.EjecutarComoRoot($"chmod {perms} {mountPath}");

            // ⭐ 11) Escribir entrada en fstab
            string fs = NormalizeFs(fsType);
            string entry = $"{devPath} {mountPath} {fs} {mountOpts} 0 0";

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



        
private string NormalizeFs(string fs)
{
    if (string.IsNullOrWhiteSpace(fs))
        return "auto";

    fs = fs.Trim().ToLowerInvariant();

    return fs switch
    {
        "ext4"              => "ext4",
        "xfs"               => "xfs",
        "btrfs"             => "btrfs",
        "f2fs"              => "f2fs",

        // UI-friendly names → real FS
        "vfat (fat32)"      => "vfat",
        "fat32"             => "vfat",
        "vfat"              => "vfat",

        "exfat"             => "exfat",

        "ntfs"              => "ntfs",
        "ntfs (windows)"    => "ntfs",

        "swap"              => "swap",

        _                   => "auto"
    };
}



private string ParseDiskRole(string detail, string device)
{
    if (string.IsNullOrWhiteSpace(detail) || string.IsNullOrWhiteSpace(device))
        return "unknown";

    string dev = device.Replace("/dev/", "").Trim();

    foreach (var raw in detail.Split('\n'))
    {
        string line = raw.Trim();
        string lower = line.ToLowerInvariant();

        // Solo líneas que contienen el dispositivo real
        if (!lower.Contains($"/dev/{dev}"))
            continue;

        // Aceptar líneas con números o guiones en las columnas
        // Ejemplos válidos:
        // 0   8   0    0   active sync   /dev/sda
        // -   0   0    2   removed
        // 2   8   48   -   faulty        /dev/sdd
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                line,
                @"^\s*[-0-9]+\s+[-0-9]+\s+[-0-9]+\s+[-0-9]+\s+"))
        {
            continue;
        }

        // ORDEN CORRECTO (igual que mdadm)
        if (lower.Contains("faulty"))
            return "faulty";

        if (lower.Contains("removed"))
            return "removed";

        if (lower.Contains("rebuild") || lower.Contains("recover"))
            return "rebuilding";

        if (lower.Contains("spare"))
            return "spare";

        if (lower.Contains("write-mostly"))
            return "write-mostly";

        // Evitar confundir "inactive" con "active"
        if (lower.Contains(" active ") ||
            lower.Contains(" active,") ||
            lower.Contains(" active\t"))
            return "active";

        if (lower.Contains(" sync "))
            return "active";
    }

    return "unknown";
}


        
        


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

        private string ParseUptime(string detail) => "Unknown";
        private int ParseRebuildProgress(string detail) => 0;
        private string ParseRebuildEta(string detail) => "";

        // ============================================================
        //  HELPERS
        // ============================================================

        
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

        
     public async Task<bool> DeleteArrayAsync(RaidArrayInfo array)
{
    try
    {
        string arrayPath = array.Path;   // ej: /dev/md0
        string name = array.Name;

        LogService.Write($"[RAID] DELETE START → {name} ({arrayPath})");

        // 1. Unmount if mounted
        if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
        {
            LogService.Write($"[RAID] Unmounting {array.MountPath}");
            ShellHelper.EjecutarComoRoot($"umount {array.MountPath}");
        }

        // 1.5 Remove from fstab (usar path real)
        RemoveArrayFromFstab(arrayPath);

        // 2. Stop array
        LogService.Write($"[RAID] Stopping array {arrayPath}");
        var stop = ShellHelper.EjecutarComoRoot($"mdadm --stop {arrayPath}");
        if (stop.ExitCode != 0)
        {
            LogService.Error($"[RAID] STOP FAILED: {stop.Stderr}");
        }

        // 3. Remove array
        LogService.Write($"[RAID] Removing array {arrayPath}");
        var remove = ShellHelper.EjecutarComoRoot($"mdadm --remove {arrayPath}");
        if (remove.ExitCode != 0)
        {
            LogService.Error($"[RAID] REMOVE FAILED: {remove.Stderr}");
        }

        // 4. Wipe superblocks
        foreach (var d in array.Disks)
        {
            LogService.Write($"[RAID] Wiping superblock on {d.Name}");
            var wipe = ShellHelper.EjecutarComoRoot($"mdadm --zero-superblock /dev/{d.Name}");

            if (wipe.ExitCode != 0)
                LogService.Error($"[RAID] ZERO-SB FAILED on {d.Name}: {wipe.Stderr}");
        }

        // 5. Update mdadm.conf
        LogService.Write("[RAID] Updating mdadm.conf");
        ShellHelper.EjecutarComoRoot("mdadm --detail --scan > /etc/mdadm/mdadm.conf");

        // 6. Sync filesystem
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



public string LastCreatedMdName { get; private set; } = "";

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

        string mdName = GetNextFreeMdName();
        string arrayPath = $"/dev/{mdName}";
        LastCreatedMdName = mdName;

        string mdadmLevel = level.ToLower() switch
        {
            "linear" => "linear",
            "jbod" => "linear",
            "jbod (linear)" => "linear",
            _ => level.Replace("RAID", "").Trim()
        };

        string deviceList = string.Join(" ", disks.Select(d => "/dev/" + d.Name));

        string nameForMdadm = string.IsNullOrWhiteSpace(friendlyName)
            ? mdName
            : friendlyName.Trim();

        string cmd =
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





public string GetNextFreeMdName()
{
    // md0 hasta md127 (límite estándar del kernel)
    for (int i = 0; i < 128; i++)
    {
        string md = $"md{i}";
        string path = $"/dev/{md}";

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
    string cmd = $"sudo smartctl -a /dev/{diskName}";
    return await ShellHelper.RunCleanAsync(cmd);
}

public bool MarkDiskAsFaulty(string arrayName, string diskName)
{
    string cmd = $"/usr/sbin/mdadm /dev/{arrayName} --fail /dev/{diskName}";
    var result = ShellHelper.EjecutarComoRoot(cmd);
    return result.ExitCode == 0;
}

public bool SetDiskAsSpare(string arrayName, string diskName)
{
    string cmd = $"/usr/sbin/mdadm /dev/{arrayName} --add /dev/{diskName}";
    var result = ShellHelper.EjecutarComoRoot(cmd);
    return result.ExitCode == 0;
}

public bool RemoveDiskFromArray(string arrayName, string diskName)
{
    string cmd = $"/usr/sbin/mdadm /dev/{arrayName} --remove /dev/{diskName}";
    var result = ShellHelper.EjecutarComoRoot(cmd);
    return result.ExitCode == 0;
}


public async Task<bool> RemoveDiskFromArrayAsync(string arrayName, string diskName)
{
    try
    {
        LogService.Write($"[RAID] RemoveDiskFromArrayAsync START → array={arrayName}, disk={diskName}");

        // ⭐ 1) Detectar nivel RAID
        var detail = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail /dev/{arrayName}");
        string raidLevel = "unknown";

        if (detail.ExitCode == 0)
        {
            foreach (var line in detail.Stdout.Split('\n'))
            {
                if (line.Trim().StartsWith("Raid Level"))
                {
                    raidLevel = line.Split(':')[1].Trim().ToLower();
                    break;
                }
            }
        }

        LogService.Write($"[RAID] Detected RAID level: {raidLevel}");

        bool supportsFail = raidLevel switch
        {
            "raid0" => false,
            "linear" => false,
            "multipath" => false,
            _ => true // RAID1,4,5,6,10
        };

        // ⭐ 2) FAIL (si el nivel lo soporta)
        if (supportsFail)
        {
            var fail = ShellHelper.EjecutarComoRoot(
                $"/usr/sbin/mdadm /dev/{arrayName} --fail /dev/{diskName}"
            );

            LogService.Write($"[RAID] FAIL EXIT={fail.ExitCode}");
            if (!string.IsNullOrWhiteSpace(fail.Stderr))
                LogService.Write($"[RAID] FAIL STDERR: {fail.Stderr}");
        }
        else
        {
            LogService.Write($"[RAID] RAID level {raidLevel} does not support FAIL. Skipping.");
        }

        // ⭐ 3) REMOVE (todos los niveles lo soportan si el array está en estado correcto)
        var remove = ShellHelper.EjecutarComoRoot(
            $"/usr/sbin/mdadm /dev/{arrayName} --remove /dev/{diskName}"
        );

        LogService.Write($"[RAID] REMOVE EXIT={remove.ExitCode}");
        if (!string.IsNullOrWhiteSpace(remove.Stderr))
            LogService.Write($"[RAID] REMOVE STDERR: {remove.Stderr}");

        if (remove.ExitCode != 0)
        {
            LogService.Error($"[RAID] RemoveDiskFromArrayAsync FAILED: {remove.Stderr}");
            return false;
        }

        // ⭐ 4) Limpieza de metadata RAID
        LogService.Write($"[RAID] Cleaning metadata on /dev/{diskName}...");

        var zero = ShellHelper.EjecutarComoRoot(
            $"/sbin/mdadm --zero-superblock /dev/{diskName}"
        );

        LogService.Write($"[RAID] ZERO-SUPERBLOCK EXIT={zero.ExitCode}");
        if (!string.IsNullOrWhiteSpace(zero.Stderr))
            LogService.Write($"[RAID] ZERO-SUPERBLOCK STDERR: {zero.Stderr}");

        // ⭐ 5) wipefs para borrar GPT/MBR/FSTYPE
        var wipe = ShellHelper.EjecutarComoRoot(
            $"/usr/sbin/wipefs -a /dev/{diskName}"
        );

        LogService.Write($"[RAID] WIPEFS EXIT={wipe.ExitCode}");
        if (!string.IsNullOrWhiteSpace(wipe.Stderr))
            LogService.Write($"[RAID] WIPEFS STDERR: {wipe.Stderr}");

        // ⭐ 6) settle
        ShellHelper.EjecutarComoRoot("udevadm settle");

        LogService.Write($"[RAID] RemoveDiskFromArrayAsync OK → disk cleaned and removed.");
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
    string cmd = $"/usr/sbin/mdadm --readwrite /dev/{arrayName}";
    var result = ShellHelper.EjecutarComoRoot(cmd);

    return result.ExitCode == 0;
}

public async Task<bool> ForceArrayCheckAsync(string arrayName)
{
    string cmd = $"/usr/sbin/mdadm --action=check /dev/{arrayName}";
    var result = ShellHelper.EjecutarComoRoot(cmd);

    return result.ExitCode == 0;
}

public async Task<bool> ForceArrayRepairAsync(string arrayName)
{
    string cmd = $"/usr/sbin/mdadm --action=repair /dev/{arrayName}";
    var result = ShellHelper.EjecutarComoRoot(cmd);

    return result.ExitCode == 0;
}

public async Task<(int ExitCode, string Stdout, string Stderr)> StopArrayAsync(string arrayName)
{
    string cmd = $"/usr/sbin/mdadm --stop /dev/{arrayName}";
    return ShellHelper.EjecutarComoRoot(cmd);
}



public async Task<string> GetArrayDetailsAsync(string arrayName)
{
    string cmd = $"/usr/sbin/mdadm --detail /dev/{arrayName}";
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
                var match = System.Text.RegularExpressions.Regex.Match(l, @"md(\d+)");
                return match.Success ? int.Parse(match.Groups[1].Value) : -1;
            })
            .First();

        LogService.Write("[MDADM] Línea seleccionada:");
        LogService.Write(newest);

        // 4. Rutas posibles según distro
        string[] possiblePaths =
        {
            "/etc/mdadm/mdadm.conf",   // Debian/Ubuntu
            "/etc/mdadm.conf"          // Arch/Manjaro
        };

        // 5. Elegir la ruta correcta
        string confPath = possiblePaths.FirstOrDefault(File.Exists)
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
        string existing = File.ReadAllText(confPath);

        // 8. Evitar duplicados
        if (existing.Contains(newest))
        {
            LogService.Write("[MDADM] La entrada ya existe en mdadm.conf");
            return true;
        }

        // 9. Escribir entrada nueva
        string tempFile = "/tmp/mdadm.conf.append";
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
                string cmd = fs switch
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
        string fstab = "/etc/fstab";

        if (!File.Exists(fstab))
        {
            LogService.Write("[FSTAB] No existe /etc/fstab, nada que limpiar.");
            return true;
        }

        string[] lines = File.ReadAllLines(fstab);

        string dev = arrayPath.Trim();               // /dev/md0
        string name = dev.Split('/').Last();         // md0
        string nameOnly = name.Replace("md", "");    // 0 (por si acaso)

        // ⭐ Patrones que deben eliminarse
        string[] patterns =
        {
            dev,                                     // /dev/md0
            $"/dev/{name}",                           // /dev/md0
            $"/dev/md/{name}",                        // /dev/md/md0
            name,                                     // md0
            $"md{nameOnly}",                          // md0
            $"md/{name}",                             // md/md0
            $"md/{nameOnly}",                         // md/0
        };

        var newLines = new List<string>();

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            bool match = patterns.Any(p => trimmed.StartsWith(p, StringComparison.Ordinal));

            // ⭐ También eliminar entradas por UUID
            if (trimmed.StartsWith("UUID=", StringComparison.Ordinal) &&
                trimmed.Contains(name, StringComparison.Ordinal))
            {
                match = true;
            }

            // ⭐ También eliminar symlinks de mdadm
            if (trimmed.Contains("md-name", StringComparison.Ordinal) ||
                trimmed.Contains("md-uuid", StringComparison.Ordinal))
            {
                if (trimmed.Contains(name, StringComparison.Ordinal))
                    match = true;
            }

            if (!match)
                newLines.Add(line);
        }

        if (newLines.Count == lines.Length)
        {
            LogService.Write("[FSTAB] No había entrada para este array.");
            return true;
        }

        string tempFile = "/tmp/fstab.cleaned";
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


        public async Task<List<string>> GetDisksInArrayAsync(string arrayName, string? existingDetail = null)
        {
            var result = new List<string>();

            string output = existingDetail;
            if (string.IsNullOrWhiteSpace(output))
                output = await RunMdadmAsync($"--detail /dev/{arrayName}");

            if (string.IsNullOrWhiteSpace(output))
                return result;

            foreach (var raw in output.Split('\n'))
            {
                string line = raw.Trim();

                // ⭐ Solo líneas que terminan en /dev/sdX o /dev/nvmeXnY
                if (!line.Contains("/dev/"))
                    continue;

                // ⭐ Evitar líneas que NO son discos (UUID, metadata, etc.)
                if (!line.EndsWith("/dev/sd") &&
                    !line.Contains("/dev/sd") &&
                    !line.Contains("/dev/nvme"))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                string dev = parts[^1];

                if (dev.StartsWith("/dev/"))
                {
                    string devName = dev.Split('/').Last();
                    result.Add(devName);
                }
            }

            return result;
        }

        
        
    }
}
