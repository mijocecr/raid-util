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
    
    
    
    // ============================================================
    // SINGLETON UNIVERSAL
    // ============================================================
    private static readonly Lazy<RaidService> _instance =
        new Lazy<RaidService>(() => new RaidService());

    public static RaidService Instance => _instance.Value;

    // Constructor privado → evita múltiples instancias
    private RaidService()
    {
        // Si necesitas inicializar algo, ponlo aquí
    }
    
    public static Dictionary<string, dynamic> Nodes { get; private set; } = new();

    public string LastCreatedMdName { get; private set; } = "";

    public Task<List<RaidDiskInfo>> GetAllDisksAsync()
    {
        // ⭐ Blindaje: NO permitir escaneo de discos antes de validar sudo
        if (!Credentials.AllowRaidCalls)
        {
            LogService.Debug("[RAID] GetAllDisksAsync blocked → AllowRaidCalls = false");
            return Task.FromResult(new List<RaidDiskInfo>());
        }

        return Task.FromResult(DiskService.GetAllDisks());
    }


    // ============================================================
    // ADD DISK TO ARRAY
    // ============================================================
  public async Task<bool> AddDiskToArrayAsync(string arrayName, string diskName)
{
    try
    {
        if (!await EnsureArraySafeForModification(arrayName))
            return false;

        NotificadorLinux.Enviar($"Preparando para añadir {diskName} a {arrayName}…");

        LogService.Info($"[RAID] AddDiskToArray START → array={arrayName}, disk={diskName}");


        NotificadorLinux.Enviar("Esperando a que mdadm esté libre…");

        if (!await WaitForMdadmIdleAsync())
        {
            NotificadorLinux.Enviar("mdadm está ocupado. Operación cancelada.", 5000, "critical");
            return false;
        }

        NotificadorLinux.Enviar("Validando array…");

        var arrays = await GetArraysAsync();
        var array = arrays.FirstOrDefault(a =>
            a.Name == arrayName ||
            a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
            a.Path.EndsWith(arrayName, StringComparison.Ordinal));

        if (array == null)
        {
            NotificadorLinux.Enviar($"Array {arrayName} no encontrado.", 5000, "critical");
            return false;
        }

        var arrayPath = array.Path;

        if (array.Disks.Any(d => d.Name == diskName))
        {
            NotificadorLinux.Enviar($"El disco {diskName} ya pertenece al array.", 5000, "critical");
            return false;
        }

        NotificadorLinux.Enviar("Validando disco…");

        var errors = await ValidateDiskForRaidAsync(diskName);
        if (errors.Count > 0)
        {
            foreach (var e in errors)
                LogService.Error("[VALIDATION] " + e);

            NotificadorLinux.Enviar("El disco no es válido para RAID.", 5000, "critical");
            return false;
        }

        NotificadorLinux.Enviar($"Añadiendo /dev/{diskName} al array…");

        var cmd = $"/usr/sbin/mdadm {arrayPath} --add /dev/{diskName}";
        var result = ShellHelper.EjecutarComoRoot(cmd);

        if (result.ExitCode != 0)
        {
            NotificadorLinux.Enviar("Error al añadir el disco al array.", 5000, "critical");
            return false;
        }

        NotificadorLinux.Enviar("Finalizando operación…");

        ShellHelper.EjecutarComoRoot("udevadm settle");
        await WaitForMdadmIdleAsync();

        // ⭐ NUEVO: persistir configuración y actualizar initramfs
        PersistArrayToMdadmConf();
        ShellHelper.EjecutarComoRoot("update-initramfs -u");

        NotificadorLinux.Enviar("Esperando a que el array esté healthy…");

        var healthy = await WaitForArrayHealthy(arrayName);

        if (!healthy)
            NotificadorLinux.Enviar("El disco fue añadido, pero el array sigue degradado.", 5000, "warning");
        else
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


    public async Task<bool> WaitForArrayHealthy(string arrayName)
    {
        string devPath;

        if (arrayName.StartsWith("/dev/"))
        {
            devPath = arrayName;
        }
        else
        {
            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a =>
                a.Name == arrayName ||
                a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
                a.Path.EndsWith(arrayName, StringComparison.Ordinal));

            devPath = array != null ? array.Path : $"/dev/{arrayName}";
        }

        for (int i = 0; i < 300; i++)
        {
            var r = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail {devPath}");
            if (r.ExitCode != 0)
            {
                await Task.Delay(200);
                continue;
            }

            var text = r.Stdout.ToLowerInvariant();

            bool isClean = text.Contains("state : clean");
            bool isDegraded = text.Contains("degraded");

            if (isClean && !isDegraded)
                return true;

            await Task.Delay(200);
        }

        return false;
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

        var mount = await ShellHelper.RunCleanAsync($"lsblk -no MOUNTPOINT /dev/{diskName}");
        if (!string.IsNullOrWhiteSpace(mount))
            errors.Add($"Disk /dev/{diskName} is mounted at {mount}.");

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
    //  LECTURA DE ARRAYS
    // ============================================================
    

public async Task<List<RaidArrayInfo>> GetArraysAsync()
{
    var arrays = new List<RaidArrayInfo>();

    // ⭐ Blindaje: no ejecutar nada de RAID si aún no se ha validado sudo
    if (!Credentials.AllowRaidCalls)
    {
        LogService.Debug("[RAID] GetArraysAsync blocked → AllowRaidCalls = false");
        return arrays;
    }

    LogService.Debug("[RAID] GetArraysAsync ENTER");

    string mdadmPath = string.Empty;

    // ============================================================
    // 1) UNIVERSAL: which mdadm SIN ROOT (evita bloqueo)
    // ============================================================
    LogService.Debug("[RAID] Buscando mdadm con 'which mdadm' (EjecutarSinRoot)...");
    var which = ShellHelper.EjecutarSinRoot("which mdadm");
    LogService.Debug($"[RAID] which mdadm → exit={which.ExitCode}, stdout='{which.Stdout.Trim()}', stderr='{which.Stderr.Trim()}'");

    if (which.ExitCode == 0 && !string.IsNullOrWhiteSpace(which.Stdout))
        mdadmPath = which.Stdout.Trim();

    // 🔹 2) Intento 2: rutas conocidas
    if (string.IsNullOrWhiteSpace(mdadmPath))
    {
        LogService.Debug("[RAID] which mdadm no devolvió ruta, probando rutas conocidas...");
        if (File.Exists("/usr/sbin/mdadm"))
            mdadmPath = "/usr/sbin/mdadm";
        else if (File.Exists("/sbin/mdadm"))
            mdadmPath = "/sbin/mdadm";
    }

    if (string.IsNullOrWhiteSpace(mdadmPath))
    {
        LogService.Error("[RAID] mdadm no encontrado. Abortando GetArraysAsync.");
        return arrays;
    }

    LogService.Debug($"[RAID] mdadm encontrado en: {mdadmPath}");

    // ============================================================
    // 2) ESCANEO mdadm --detail --scan
    // ============================================================
    LogService.Debug("[RAID] Ejecutando mdadm --detail --scan...");
    var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot($"{mdadmPath} --detail --scan");
    var scan = (stdout + "\n" + stderr).Trim();

    LogService.Debug($"[RAID] mdadm --detail --scan exit={exit}");
    LogService.Debug("[RAID] mdadm --detail --scan OUTPUT:");
    LogService.Debug(scan);

    if (!string.IsNullOrWhiteSpace(scan))
    {
        foreach (var raw in scan.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("ARRAY"))
                continue;

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var arrayPathOriginal = tokens[1];
            LogService.Debug($"[RAID] ARRAY line → path original='{arrayPathOriginal}'");

            // ⭐ Normalización universal
            var arrayPath = NormalizeDev(arrayPathOriginal);
            LogService.Debug($"[RAID] ARRAY path normalizado='{arrayPath}'");

            var rawName = Path.GetFileName(arrayPath);
            var arrayName = rawName.Contains(':')
                ? rawName.Split(':').Last()
                : rawName;

            // ============================================================
            // 3) DETALLE DEL ARRAY
            // ============================================================
            LogService.Debug($"[RAID] Ejecutando mdadm --detail {arrayPath}...");
            var detail = await RunMdadmAsync($"--detail {arrayPath}");

            if (string.IsNullOrWhiteSpace(detail) ||
                detail.Contains("No such file") ||
                detail.Contains("cannot open") ||
                detail.Contains("not enough devices"))
            {
                LogService.Error($"[RAID] Ignorando array inválido: {arrayPath}");
                continue;
            }

            var state = ParseArrayStateEnum(detail);
            var level = ParseLevel(detail);

            if (state == RaidArrayState.Failed)
            {
                LogService.Debug($"[RAID] Ignorando array FAILED: {arrayName}");
                continue;
            }

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
                DiskSummary = "0× Disk",
                Uptime = ParseUptime(detail),
                RebuildProgress = ParseRebuildProgress(detail),
                RebuildETA = ParseRebuildEta(detail)
            };

            var lines = detail.Split('\n')
                              .Where(l => Regex.IsMatch(l, @"(active|faulty|spare|removed|rebuild|sync|recover|inactive)"))
                              .ToList();

            var processed = new HashSet<string>();

            foreach (var l in lines)
            {
                var m = Regex.Match(l, @"\/dev\/(sd[a-z]\d*|nvme\d+n\d+p\d+)");
                if (!m.Success)
                    continue;

                var devName = m.Groups[1].Value;

                if (processed.Contains(devName))
                    continue;

                processed.Add(devName);

                var diskInfo = await GetDiskInfo(devName);

                if (l.Contains("faulty"))
                    diskInfo.Role = "faulty";
                else if (l.Contains("spare"))
                    diskInfo.Role = "spare";
                else if (l.Contains("active"))
                    diskInfo.Role = "active";
                else if (l.Contains("removed"))
                    diskInfo.Role = "removed";
                else if (l.Contains("rebuild"))
                    diskInfo.Role = "rebuilding";
                else if (l.Contains("sync"))
                    diskInfo.Role = "syncing";
                else if (l.Contains("recover"))
                    diskInfo.Role = "recovering";
                else if (l.Contains("inactive"))
                    diskInfo.Role = "inactive";
                else
                    diskInfo.Role = "unknown";

                diskInfo.RaidMembership = ParseMembershipFromRole(diskInfo.Role);

                if (diskInfo.RaidMembership == RaidMembership.None &&
                    diskInfo.Role == "removed")
                    continue;

                diskInfo.State = ParseDiskStateFromRole(diskInfo.Role);
                diskInfo.ArrayName = arrayName;

                info.Disks.Add(diskInfo);
            }

            if (info.Disks.Count == 0)
            {
                LogService.Debug($"[RAID] Ignorando array sin discos: {info.Name}");
                continue;
            }

            if (info.TotalSize == "Unknown" ||
                info.TotalSize == "0" ||
                info.TotalSize == "0B")
            {
                LogService.Debug($"[RAID] Ignorando array con tamaño inválido: {info.Name}");
                continue;
            }

            info.DiskSummary = $"{info.Disks.Count}× Disk";

            try
            {
                var lsblk = await ShellHelper.RunCleanAsync($"lsblk -J {arrayPath}");
                dynamic blk = JsonConvert.DeserializeObject(lsblk)!;

                var mount = "";

                try
                {
                    if (blk.blockdevices[0].mountpoints != null)
                    {
                        var mps = blk.blockdevices[0].mountpoints;
                        if (mps.Count > 0)
                            mount = mps[0] ?? "";
                    }
                    else
                    {
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

            arrays.Add(info);
        }
    }

    // ============================================================
    // Fallback /proc/mdstat
    // ============================================================
    if (!arrays.Any(a => a.Disks.Count > 0) && File.Exists("/proc/mdstat"))
    {
        LogService.Debug("[RAID] Fallback: detectando desde /proc/mdstat");

        var md = File.ReadAllText("/proc/mdstat");
        arrays.Clear();

        foreach (var raw in md.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var mArray = Regex.Match(line, @"^(md\d+)\s*:");
            if (!mArray.Success)
                continue;

            var arrayName = mArray.Groups[1].Value;

            if (line.Contains("inactive") || line.Contains("0 blocks"))
            {
                LogService.Debug($"[RAID] Ignorando array fantasma en /proc/mdstat: {arrayName}");
                continue;
            }

            var diskMatches = Regex.Matches(line, @"(sd[a-z]\d*|nvme\d+n\d+p\d+)");
            var disks = new List<RaidDiskInfo>();

            foreach (Match dm in diskMatches)
            {
                var devName = dm.Value;

                var diskInfo = await GetDiskInfo(devName);
                diskInfo.ArrayName = arrayName;
                diskInfo.Role = "active";
                diskInfo.RaidMembership = RaidMembership.Active;
                diskInfo.State = "OK";

                disks.Add(diskInfo);
            }

            if (disks.Count == 0)
            {
                LogService.Debug($"[RAID] Ignorando array sin discos en fallback: {arrayName}");
                continue;
            }

            arrays.Add(new RaidArrayInfo
            {
                Name = arrayName,
                Path = $"/dev/{arrayName}",
                Level = "Unknown",
                State = RaidArrayState.Rebuilding,
                StateIcon = GetStateIcon(RaidArrayState.Rebuilding),
                Disks = disks,
                MountPath = "",
                IsMounted = false,
                TotalSize = "Unknown",
                UsableSize = "Unknown",
                ParitySize = "N/A",
                AverageTemp = 0,
                DiskSummary = $"{disks.Count}× Disk",
                Uptime = "Unknown",
                RebuildProgress = 0,
                RebuildETA = "Unknown"
            });
        }
    }

    LogService.Debug($"[RAID] GetArraysAsync EXIT → {arrays.Count} arrays");
    return arrays;
}





    
    
    private RaidMembership ParseMembershipFromRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return RaidMembership.None;

        role = role.ToLower().Trim();

        return role switch
        {
            "active"      => RaidMembership.Active,
            "spare"       => RaidMembership.Spare,
            "faulty"      => RaidMembership.Faulty,
            "rebuilding"  => RaidMembership.Rebuilding,
            "syncing"     => RaidMembership.Syncing,
            "sync"        => RaidMembership.Syncing,
            "recovering"  => RaidMembership.Rebuilding,   // mdadm lo usa como recovery
            "recover"     => RaidMembership.Rebuilding,
            "removed"     => RaidMembership.None,
            "inactive"    => RaidMembership.None,         // no existe en tu enum → None
            _             => RaidMembership.None
        };
    }

    
private RaidArrayState ParseArrayStateEnum(string detail)
{
    if (string.IsNullOrWhiteSpace(detail))
        return RaidArrayState.Unknown;

    var lower = detail.ToLowerInvariant();

    // ============================================================
    // 1) Detectar nombre del array automáticamente
    // ============================================================
    string mdName = null;

    // Ejemplos de líneas:
    // "md0 : active raid1 ..."
    // "Array : /dev/md0"
    foreach (var token in lower.Split(' ', '\n', '\r', '\t'))
    {
        if (token.StartsWith("md") && token.Length <= 5)
        {
            mdName = token.Trim();
            break;
        }
    }

    // ============================================================
    // 2) Leer /proc/mdstat para detectar rebuild/resync/reshape
    // ============================================================
    try
    {
        var mdstat = File.ReadAllText("/proc/mdstat").ToLowerInvariant();

        if (mdName != null && mdstat.Contains(mdName))
        {
            if (mdstat.Contains("resync") ||
                mdstat.Contains("recovery") ||
                mdstat.Contains("rebuild") ||
                mdstat.Contains("reshape"))
            {
                return RaidArrayState.Rebuilding;
            }
        }
    }
    catch { }

    // ============================================================
    // 3) Estados críticos
    // ============================================================
    if (lower.Contains("state : faulty") ||
        lower.Contains("state : failed") ||
        lower.Contains("state : inactive") ||
        lower.Contains("state : stopped"))
        return RaidArrayState.Failed;

    // ============================================================
    // 4) Degraded
    // ============================================================
    if (lower.Contains("clean, degraded") ||
        lower.Contains("active, degraded") ||
        lower.Contains(" degraded"))
        return RaidArrayState.Degraded;

    // ============================================================
    // 5) Read-only
    // ============================================================
    if (lower.Contains("read-only"))
        return RaidArrayState.ReadOnly;

    // ============================================================
    // 6) Clean / Active
    // ============================================================
    if (lower.Contains("state : clean"))
        return RaidArrayState.Clean;

    if (lower.Contains("state : active"))
        return RaidArrayState.Active;

    return RaidArrayState.Unknown;
}



    

    public bool AutoAssemble()
    {
        Console.WriteLine("[RAID] Ejecutando AutoAssemble()");

        var result = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --assemble --scan");

        Console.WriteLine($"[RAID] AutoAssemble EXIT={result.ExitCode}");
        Console.WriteLine($"[RAID] AutoAssemble STDOUT:\n{result.Stdout}");
        Console.WriteLine($"[RAID] AutoAssemble STDERR:\n{result.Stderr}");

        return result.ExitCode == 0;
    }

   public async Task<bool> InitializeArrayAsync(string arrayName, string fsType, string label)
{
    try
    {
        if (!await EnsureArraySafeForModification(arrayName, allowFstab: true))
            return false;

        string devPath;

        if (arrayName.StartsWith("/dev/"))
        {
            devPath = arrayName;
        }
        else
        {
            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a =>
                a.Name == arrayName ||
                a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
                a.Path.EndsWith(arrayName, StringComparison.Ordinal));

            devPath = array != null ? array.Path : $"/dev/{arrayName}";
        }

        var cfg = ArrayConfigService.Load(arrayName);

        var mountPath = string.IsNullOrWhiteSpace(cfg.MountPoint)
            ? $"/mnt/{arrayName}"
            : cfg.MountPoint;

        Console.WriteLine($"[RAID] Inicializando {devPath} con FS={fsType}, label='{label}', mount={mountPath}");

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

            // ⭐ limpiar posibles entradas antiguas por devPath
            ShellHelper.EjecutarComoRoot($"sed -i '\\#{devPath}#d' /etc/fstab");
        }

        var ls = ShellHelper.EjecutarComoRoot($"ls {devPath}");
        if (ls.ExitCode != 0)
        {
            Console.WriteLine($"[RAID] ERROR: {devPath} no existe.");
            return false;
        }

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
            "swap" => string.IsNullOrWhiteSpace(label)
                ? $"mkswap {devPath}"
                : $"mkswap -L \"{label}\" {devPath}",
            _ => throw new Exception("Filesystem no soportado")
        };

        var mkfs = ShellHelper.EjecutarComoRoot(mkfsCmd);
        if (mkfs.ExitCode != 0)
        {
            Console.WriteLine($"[RAID] ERROR formateando: {mkfs.Stderr}");
            return false;
        }

        // ⭐ tras mkfs en array, persistir mdadm y actualizar initramfs
        PersistArrayToMdadmConf();
        ShellHelper.EjecutarComoRoot("update-initramfs -u");

        if (fsType == "swap")
        {
            ShellHelper.EjecutarComoRoot($"swapon {devPath}");
            return true;
        }

        ShellHelper.EjecutarComoRoot($"mkdir -p {mountPath}");

        List<string> opts = new();

        opts.Add("users");

        if (cfg.Mount_NoAtime) opts.Add("noatime");
        if (cfg.Mount_NoDirAtime) opts.Add("nodiratime");
        if (cfg.Mount_Discard) opts.Add("discard");
        if (cfg.Mount_Sync) opts.Add("sync");
        if (cfg.Mount_ReadOnly) opts.Add("ro");

        if (opts.Count == 1)
            opts.Add("defaults");

        var mountOpts = string.Join(",", opts);

        if (cfg.PersistMount)
        {
            var mount = ShellHelper.EjecutarComoRoot($"mount -o {mountOpts} {devPath} {mountPath}");
            if (mount.ExitCode != 0)
            {
                Console.WriteLine($"[RAID] ERROR montando: {mount.Stderr}");
                return false;
            }

            var perms = string.IsNullOrWhiteSpace(cfg.MountPermissions)
                ? "755"
                : cfg.MountPermissions;

            ShellHelper.EjecutarComoRoot($"chmod {perms} {mountPath}");

            var fs = NormalizeFs(fsType);

            // ⭐ usar UUID si existe
            var uuidResult = ShellHelper.EjecutarComoRoot($"blkid -s UUID -o value {devPath}");
            var uuid = uuidResult.ExitCode == 0 ? uuidResult.Stdout.Trim() : "";

            string firstField = !string.IsNullOrWhiteSpace(uuid)
                ? $"UUID={uuid}"
                : devPath;

            var entry = $"{firstField} {mountPath} {fs} {mountOpts} 0 0";

            // limpiar entradas previas por devPath o UUID
            if (!string.IsNullOrWhiteSpace(uuid))
                ShellHelper.EjecutarComoRoot($"sed -i '\\#UUID={uuid}#d' /etc/fstab");
            ShellHelper.EjecutarComoRoot($"sed -i '\\#{devPath}#d' /etc/fstab");

            ShellHelper.EjecutarComoRoot($"bash -c \"echo '{entry}' >> /etc/fstab\"");
            ShellHelper.EjecutarComoRoot("systemctl daemon-reload");
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

            if (line.StartsWith("raid level") || line.StartsWith("level"))
            {
                var idx = line.IndexOf(':');
                if (idx < 0)
                    continue;

                var level = line[(idx + 1)..].Trim();

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

    private string ParseDiskStateFromRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "UNKNOWN";

        role = role.Trim().ToLowerInvariant();

        return role switch
        {
            "faulty"        => "FAULTY",
            "failed"        => "FAULTY",
            "error"         => "FAULTY",
            "blocked"       => "FAULTY",
            "removed"       => "MISSING",
            "missing"       => "MISSING",
            "rebuilding"    => "REBUILDING",
            "recovering"    => "REBUILDING",
            "resync"        => "SYNCING",
            "sync"          => "SYNCING",
            "write-mostly"  => "WARN",
            "active"        => "OK",
            "in-sync"       => "OK",
            "clean"         => "OK",
            "spare"         => "OK",
            _               => "UNKNOWN"
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

            var idx = line.IndexOf('(');
            if (idx > 0)
            {
                var inside = line[(idx + 1)..];
                var end = inside.IndexOf(')');
                if (end > 0)
                {
                    var human = inside[..end].Trim();
                    if (!string.IsNullOrWhiteSpace(human))
                        return human;
                }
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var last = tokens.LastOrDefault();

            if (long.TryParse(last, out var sectors))
            {
                var bytes = sectors * 512.0;
                var gib = bytes / (1024 * 1024 * 1024);
                return $"{gib:F2} GiB";
            }
        }

        return "Unknown";
    }

    private string ParseUptime(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "Unknown";

        foreach (var raw in detail.Split('\n'))
        {
            var line = raw.Trim().ToLowerInvariant();

            if (line.StartsWith("update time"))
            {
                var idx = line.IndexOf(':');
                if (idx > 0)
                {
                    var val = line[(idx + 1)..].Trim();
                    return string.IsNullOrWhiteSpace(val) ? "Unknown" : val;
                }
            }
        }

        return "Unknown";
    }

    private int ParseRebuildProgress(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return 0;

        var m = Regex.Match(detail, @"(\d+)%");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var p))
            return Math.Clamp(p, 0, 100);

        return 0;
    }


    private string ParseRebuildEta(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "N/A";

        // mdadm usa "finish=123.4min"
        var m = Regex.Match(detail, @"finish=(\S+)");
        if (m.Success)
            return m.Groups[1].Value;

        return "N/A";
    }


    

    

    


    private async Task<RaidDiskInfo> GetDiskInfo(string device)
{
    // Normalizar nombre
    var name = device.Replace("/dev/", "").Trim();

    // sda1 → sda
    if (Regex.IsMatch(name, @"^sd[a-z][0-9]+$"))
        name = name.Substring(0, 3);

    // nvme0n1p1 → nvme0n1
    if (Regex.IsMatch(name, @"^nvme[0-9]+n[0-9]+p[0-9]+$"))
        name = name.Split('p')[0];

    // Obtener JSON de lsblk
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

    // Campos básicos
    string model = dev.model ?? "Unknown";
    string size = dev.size ?? "Unknown";
    bool isRotational = ParseRota(dev.rota);

    // Filesystem y montaje
    string fstype = dev.fstype ?? "";
    string mountPath = dev.mountpoint ?? "";

    // Hijos (particiones)
    List<string> children = new();
    string arrayName = "";

    if (dev.children != null)
    {
        foreach (var c in dev.children)
        {
            string childName = c.name ?? "";
            children.Add(childName);

            // Si una partición pertenece a mdX → arrayName
            if (childName.StartsWith("md"))
                arrayName = childName;
        }
    }

    // Icono
    var icon = DiskIconService.GetIcon(name, model, isRotational);

    // Transporte
    string tran = (dev.tran ?? "").ToString().ToLowerInvariant();

    return new RaidDiskInfo
    {
        // Identidad
        Name = name,
        Model = model,
        Size = size,
        Icon = icon,

        // Tipo físico
        IsRotational = isRotational,
        IsNvme = name.StartsWith("nvme", StringComparison.OrdinalIgnoreCase),
        IsUsb = tran == "usb",
        IsIscsi = tran == "iscsi",
        IsVirtual =
            model.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("QEMU", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("VBOX", StringComparison.OrdinalIgnoreCase),

        // Sistema de archivos
        FsType = fstype,

        // Montaje
        MountPath = mountPath,

        // Hijos
        Children = children,

        // RAID (se completará en RaidService)
        ArrayName = arrayName,
        RaidMembership = RaidMembership.None,
        State = "UNKNOWN",

        // Sistema
        IsSystemDisk = SystemDiskDetector.IsSystemDisk(name)
    };
}


    private RaidDiskInfo CreateUnknownDisk(string name)
    {
        return new RaidDiskInfo
        {
            // Identidad
            Name = name,
            Model = "Unknown",
            Size = "Unknown",

            // Icono genérico
            Icon = DiskIconService.GetIcon(name, "Unknown", false),

            // Tipo físico (desconocido)
            IsRotational = false,
            IsNvme = false,
            IsUsb = false,
            IsVirtual = false,
            IsIscsi = false,

            // Sistema de archivos
            FsType = "",

            // Montaje
            MountPath = "",

            // Hijos
            Children = new List<string>(),

            // RAID
            ArrayName = "",
            RaidMembership = RaidMembership.None,
            State = "UNKNOWN",

            // Sistema
            IsSystemDisk = false
        };
    }


    private string GetStateIcon(RaidArrayState state)
    {
        return state switch
        {
            RaidArrayState.Active     => "avares://RAID-Util/Assets/Icons/array-ok.png",
            RaidArrayState.Clean      => "avares://RAID-Util/Assets/Icons/array-ok.png",
            RaidArrayState.Degraded   => "avares://RAID-Util/Assets/Icons/array-caution.png",
            RaidArrayState.Rebuilding => "avares://RAID-Util/Assets/Icons/array-caution.png",
            RaidArrayState.Recovering => "avares://RAID-Util/Assets/Icons/array-caution.png",
            RaidArrayState.Resync     => "avares://RAID-Util/Assets/Icons/array-caution.png",
            RaidArrayState.ReadOnly   => "avares://RAID-Util/Assets/Icons/array-readonly.png",
            RaidArrayState.Failed     => "avares://RAID-Util/Assets/Icons/array-error.png",
            _                         => "avares://RAID-Util/Assets/Icons/array-caution.png"
        };
    }

    private async Task<string> RunMdadmAsync(string arguments)
    {
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
        var arrayPath = array.Path;
        var name = array.Name;

        LogService.Info($"[RAID] DELETE START → {name} ({arrayPath})");


        NotificadorLinux.Enviar($"Deleting RAID array {name}… This may take a moment.");

        if (!await EnsureArraySafeForModification(arrayPath))
        {
            var msg = "Array cannot be deleted because it is not in a safe state.";
            NotificadorLinux.Enviar(msg, 5000, "critical");
            LogService.Error("[RAID] DELETE ABORTED: EnsureArraySafeForModification failed.");
            return false;
        }

        // ⭐ limpiar fstab por si acaso
        RemoveArrayFromFstab(arrayPath);

        NotificadorLinux.Enviar($"Stopping array {name}…");
        LogService.Info($"[RAID] Stopping array {arrayPath}");


        var stop = ShellHelper.EjecutarComoRoot($"mdadm --stop {arrayPath}");
        if (stop.ExitCode != 0)
        {
            var msg = $"Failed to stop array {name}.";
            NotificadorLinux.Enviar(msg, 5000, "critical");
            LogService.Error($"[RAID] STOP FAILED: {stop.Stderr}");
            return false;
        }

        NotificadorLinux.Enviar($"Removing array {name}…");
        LogService.Info($"[RAID] Removing array {arrayPath}");


        var remove = ShellHelper.EjecutarComoRoot($"mdadm --remove {arrayPath}");

        if (remove.ExitCode != 0)
        {
            if (remove.Stderr.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Info("[RAID] REMOVE skipped: md device already removed after --stop.");

            }
            else
            {
                var msg = $"Failed to remove array {name}.";
                NotificadorLinux.Enviar(msg, 5000, "critical");
                LogService.Error($"[RAID] REMOVE FAILED: {remove.Stderr}");
                return false;
            }
        }

        NotificadorLinux.Enviar("Cleaning RAID metadata from member disks…");

        foreach (var d in array.Disks)
        {
            LogService.Info($"[RAID] Wiping superblock on {d.Name}");


            // ⭐ no tocar discos marcados como sistema
            if (d.IsSystemDisk)
            {
                LogService.Error($"[RAID] ZERO-SB SKIPPED on system disk {d.Name}");
                continue;
            }

            var wipe = ShellHelper.EjecutarComoRoot($"mdadm --zero-superblock /dev/{d.Name}");

            if (wipe.ExitCode != 0)
            {
                var msg = $"Failed to wipe RAID metadata on disk {d.Name}.";
                NotificadorLinux.Enviar(msg, 5000, "warning");
                LogService.Error($"[RAID] ZERO-SB FAILED on {d.Name}: {wipe.Stderr}");
            }
        }

        NotificadorLinux.Enviar("Updating mdadm configuration…");
        LogService.Info("[RAID] Updating mdadm.conf");


        // ⭐ usar método de persistencia y actualizar initramfs
        PersistArrayToMdadmConf();
        ShellHelper.EjecutarComoRoot("sync");
        ShellHelper.EjecutarComoRoot("update-initramfs -u");

        var success = $"Array {name} has been successfully deleted.";
        NotificadorLinux.Enviar(success, 5000, "info");

        LogService.Info($"[RAID] DELETE OK → {name}");

        return true;
    }
    catch (Exception ex)
    {
        var msg = "Unexpected error while deleting the RAID array.";
        NotificadorLinux.Enviar(msg, 5000, "critical");

        LogService.Error("[RAID] DELETE FAILED:");
        LogService.Error(ex.ToString());
        return false;
    }
}


    public bool CreateArray(string level, List<RaidDiskInfo> disks, string? friendlyName = null)
{
    try
    {
        foreach (var d in disks)
        {
            if (d.IsSystemDisk)
            {
                LogService.Error($"[CREATE] ERROR: /dev/{d.Name} es disco del sistema. Operación bloqueada.");
                return false;
            }
        }

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

        LogService.Info($"[CREATE] Ejecutando: {cmd}");

        var result = ShellHelper.EjecutarComoRoot(cmd);

        if (result.ExitCode != 0)
        {
            LogService.Error("[CREATE] mdadm falló:");
            LogService.Error(result.Stderr);
            return false;
        }

        ShellHelper.EjecutarComoRoot("udevadm settle");

        // ⭐ persistir arrays y actualizar initramfs
        PersistArrayToMdadmConf();
        ShellHelper.EjecutarComoRoot("update-initramfs -u");

        LogService.Info($"[CREATE] Array creado correctamente → {arrayPath}");
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
        for (var i = 0; i < 128; i++)
        {
            var md = $"md{i}";
            var path = $"/dev/{md}";

            if (!File.Exists(path))
                return md;

            var result = ShellHelper.EjecutarComoRoot($"mdadm --detail {path}");
            if (result.ExitCode != 0)
                return md;
        }

        throw new Exception("No hay dispositivos mdX libres disponibles.");
    }

    public static string NormalizeDev(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        name = name.Trim();

        // Si ya es /dev/md0, /dev/md1, etc → OK
        if (Regex.IsMatch(name, @"^/dev/md\d+$"))
            return name;

        // Si viene como /dev/md/md0 → corregir
        if (name.StartsWith("/dev/md/"))
        {
            var last = Path.GetFileName(name);

            // Caso: /dev/md/hostname:md0
            if (last.Contains(":"))
            {
                var clean = last.Split(':').Last();
                if (Regex.IsMatch(clean, @"^md\d+$"))
                    return "/dev/" + clean;
            }

            // Caso: /dev/md/md0
            if (Regex.IsMatch(last, @"^md\d+$"))
                return "/dev/" + last;
        }

        // Si viene como hostname:md0
        if (name.Contains(":"))
        {
            var clean = name.Split(':').Last();
            if (Regex.IsMatch(clean, @"^md\d+$"))
                return "/dev/" + clean;
        }

        // Si viene como md0
        if (Regex.IsMatch(name, @"^md\d+$"))
            return "/dev/" + name;

        // Último fallback: devolver /dev/name
        return "/dev/" + name;
    }


    public async Task<bool> RemoveDiskFromArrayAsync(string arrayName, string diskName)
{
    try
    {
        string arrayPath;

        if (arrayName.StartsWith("/dev/"))
        {
            arrayPath = arrayName;
        }
        else
        {
            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a =>
                a.Name == arrayName ||
                a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
                a.Path.EndsWith(arrayName, StringComparison.Ordinal));

            arrayPath = array != null ? array.Path : $"/dev/{arrayName}";
        }

        diskName = NormalizeDev(diskName);
        var shortName = Path.GetFileName(diskName);

        if (!await EnsureArraySafeForModification(arrayPath))
            return false;

        LogService.Info($"[RAID] RemoveDiskFromArrayAsync START → array={arrayPath}, disk={diskName}");

        var detailExec = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail {arrayPath}");
        var raidLevel = "unknown";

        if (detailExec.ExitCode == 0)
        {
            foreach (var line in detailExec.Stdout.Split('\n'))
            {
                if (line.Trim().StartsWith("Raid Level"))
                {
                    raidLevel = line.Split(':')[1].Trim().ToLower();
                    break;
                }
            }
        }

        var supportsFail = raidLevel switch
        {
            "raid0"      => false,
            "linear"     => false,
            "multipath"  => false,
            _            => true
        };

        var detailText = detailExec.Stdout;

        if (supportsFail &&
            !detailText.Contains($"faulty   {diskName}") &&
            !detailText.Contains($"faulty   /dev/{shortName}"))
        {
            LogService.Info($"[RAID] Marking disk as faulty: {diskName}");
            ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm {arrayPath} --fail {diskName}");
            ShellHelper.EjecutarComoRoot("udevadm settle");
            await Task.Delay(300);
        }

        detailExec = ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --detail {arrayPath}");
        detailText = detailExec.Stdout;

        var spare = DetectSpareDevice(detailText);
        if (!string.IsNullOrWhiteSpace(spare) && NormalizeDev(spare) != diskName)
        {
            var spareDev = NormalizeDev(spare);
            LogService.Info($"[RAID] Spare detected ({spareDev}) → removing spare first.");
            ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm {arrayPath} --remove {spareDev}");
            ShellHelper.EjecutarComoRoot("udevadm settle");
        }

        bool hasFaultyLine =
            detailText.Contains($"faulty   {diskName}") ||
            detailText.Contains($"faulty   /dev/{shortName}");

        bool hasRemovedSlot = detailText.Contains(" removed");

        string removeCmd;

        if (hasFaultyLine && hasRemovedSlot)
        {
            LogService.Error("[RAID] Detected faulty device with removed slot → using '--remove failed'.");
            removeCmd = $"/usr/sbin/mdadm {arrayPath} --remove failed";
        }
        else
        {
            LogService.Info("[RAID] Using normal remove by device path.");
            removeCmd = $"/usr/sbin/mdadm {arrayPath} --remove {diskName}";
        }

        LogService.Info($"[RAID] Attempting remove: {removeCmd}");
        var remove1 = ShellHelper.EjecutarComoRoot(removeCmd);

        if (remove1.ExitCode != 0)
        {
            LogService.Error($"[RAID] First remove FAILED (code={remove1.ExitCode}) → checking lsblk children.");

            if (DiskStillAttached(shortName))
            {
                LogService.Debug("[RAID] Disk still attached → forcing kernel release.");

                // ⭐ no eliminar del kernel si es disco de sistema
                if (!SystemDiskDetector.IsSystemDisk(shortName))
                {
                    ShellHelper.EjecutarComoRoot($"echo 1 > /sys/block/{shortName}/device/delete");
                    ShellHelper.EjecutarComoRoot("udevadm settle");

                    for (int host = 0; host < 10; host++)
                    {
                        string path = $"/sys/class/scsi_host/host{host}/scan";
                        if (File.Exists(path))
                            ShellHelper.EjecutarComoRoot($"echo \"- - -\" > {path}");
                    }

                    ShellHelper.EjecutarComoRoot("udevadm settle");
                }
                else
                {
                    LogService.Error($"[RAID] SKIP device/delete on system disk {shortName}");
                }
            }

            LogService.Info("[RAID] Retrying remove after kernel release...");

            var remove2 = ShellHelper.EjecutarComoRoot(removeCmd);

            if (remove2.ExitCode != 0)
            {
                LogService.Error($"[RAID] RemoveDiskFromArrayAsync FAILED: {remove2.Stderr}");
                return false;
            }
        }

        LogService.Info($"[RAID] Cleaning metadata on {diskName}");

        if (!SystemDiskDetector.IsSystemDisk(shortName))
        {
            ShellHelper.EjecutarComoRoot($"/usr/sbin/mdadm --zero-superblock {diskName}");
            ShellHelper.EjecutarComoRoot($"/usr/sbin/wipefs -a {diskName}");
            ShellHelper.EjecutarComoRoot("udevadm settle");
        }
        else
        {
            LogService.Error($"[RAID] SKIP zero-superblock/wipefs on system disk {shortName}");
        }

        LogService.Info("[RAID] RemoveDiskFromArrayAsync OK → disk cleaned and removed.");
        return true;
    }
    catch (Exception ex)
    {
        LogService.Error("[RAID] RemoveDiskFromArrayAsync EXCEPTION:");
        LogService.Error(ex.ToString());
        return false;
    }
}


    // =====================================================================================
    // ⭐ FUNCIONES AUXILIARES
    // =====================================================================================
    private string DetectSpareDevice(string mdadmDetail)
    {
        foreach (var line in mdadmDetail.Split('\n'))
        {
            if (line.Contains(" spare ") && line.Contains("/dev/"))
            {
                var parts = line.Trim().Split(' ');
                return parts[^1].Replace("/dev/", "").Trim();
            }
        }
        return "";
    }

    private bool DiskStillAttached(string diskName)
    {
        var lsblk = ShellHelper.RunCleanAsync($"lsblk -J /dev/{diskName}").Result;
        return lsblk.Contains("\"children\"");
    }

    public async Task<bool> StartArrayResyncAsync(string arrayName)
    {
        string arrayPath;

        if (arrayName.StartsWith("/dev/"))
        {
            arrayPath = arrayName;
        }
        else
        {
            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a =>
                a.Name == arrayName ||
                a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
                a.Path.EndsWith(arrayName, StringComparison.Ordinal));

            arrayPath = array != null ? array.Path : $"/dev/{arrayName}";
        }

        var cmd = $"/usr/sbin/mdadm --readwrite {arrayPath}";
        var result = ShellHelper.EjecutarComoRoot(cmd);

        return result.ExitCode == 0;
    }

    public async Task<bool> ForceArrayCheckAsync(string arrayName)
{
    // ============================================================
    // 1. Resolver PATH REAL del array
    // ============================================================
    string arrayPath;

    if (arrayName.StartsWith("/dev/"))
    {
        arrayPath = arrayName;
    }
    else
    {
        var arrays = await GetArraysAsync();
        var array = arrays.FirstOrDefault(a =>
            a.Name == arrayName ||
            a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
            a.Path.EndsWith(arrayName, StringComparison.Ordinal));

        arrayPath = array != null ? array.Path : $"/dev/{arrayName}";
    }

    // ============================================================
    // 2. Obtener información real del array
    // ============================================================
    var arrays2 = await GetArraysAsync();
    var arrayInfo = arrays2.FirstOrDefault(a =>
        a.Path == arrayPath ||
        a.Name == arrayName);

    if (arrayInfo == null)
    {
        LogService.Error($"[CHECK] Array '{arrayName}' no encontrado.");
        return false;
    }

    // ============================================================
    // 3. Validaciones de seguridad
    // ============================================================

    // RAID0 → NO soporta check
    if (arrayInfo.Level == "raid0")
    {
        LogService.Error("[CHECK] RAID0 no soporta verificación (--action=check).");
        return false;
    }

    // Si está en resync/recovery → NO permitir
    if (arrayInfo.IsResyncing || arrayInfo.IsRecovering)
    {
        LogService.Error("[CHECK] El array está en proceso de resync/recovery. No se puede ejecutar check.");
        return false;
    }

    // Si está degradado → check no es apropiado
    if (arrayInfo.IsDegraded)
    {
        LogService.Error("[CHECK] El array está degradado. Use reparación en lugar de check.");
        return false;
    }

    // ============================================================
    // 4. Ejecutar check
    // ============================================================
    var cmd = $"/usr/sbin/mdadm --action=check {arrayPath}";
    var result = ShellHelper.EjecutarComoRoot(cmd);

    if (result.ExitCode != 0)
    {
        LogService.Error($"[CHECK] Error ejecutando check: {result.Stderr}");
        return false;
    }

    LogService.Info("[CHECK] Verificación iniciada correctamente.");
    return true;
}


    public async Task<bool> ForceArrayRepairAsync(string arrayName)
{
    // ============================================================
    // 1. Resolver PATH REAL del array
    // ============================================================
    string arrayPath;

    if (arrayName.StartsWith("/dev/"))
    {
        arrayPath = arrayName;
    }
    else
    {
        var arrays = await GetArraysAsync();
        var array = arrays.FirstOrDefault(a =>
            a.Name == arrayName ||
            a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
            a.Path.EndsWith(arrayName, StringComparison.Ordinal));

        arrayPath = array != null ? array.Path : $"/dev/{arrayName}";
    }

    // ============================================================
    // 2. Obtener información real del array
    // ============================================================
    var arrays2 = await GetArraysAsync();
    var arrayInfo = arrays2.FirstOrDefault(a =>
        a.Path == arrayPath ||
        a.Name == arrayName);

    if (arrayInfo == null)
    {
        LogService.Error($"[REPAIR] Array '{arrayName}' no encontrado.");
        return false;
    }

    // ============================================================
    // 3. Validaciones de seguridad
    // ============================================================

    // RAID0 → NO se puede reparar
    if (arrayInfo.Level == "raid0")
    {
        LogService.Error("[REPAIR] RAID0 no soporta reparación.");
        return false;
    }

    // Si no está degradado → no hay nada que reparar
    if (!arrayInfo.IsDegraded)
    {
        LogService.Debug("[REPAIR] El array no está degradado. No se requiere reparación.");
        return true;
    }

    // ============================================================
    // 4. Identificar discos faulty y spare
    // ============================================================
    var faulty = arrayInfo.Disks.Where(d => d.State == "faulty").ToList();
    var spare  = arrayInfo.Disks.Where(d => d.State == "spare").ToList();

    if (faulty.Count == 0)
    {
        LogService.Error("[REPAIR] No hay discos faulty para reparar.");
        return false;
    }

    if (spare.Count == 0)
    {
        LogService.Error("[REPAIR] No hay discos spare disponibles para la reparación.");
        return false;
    }

    var faultyDisk = faulty.First();
    var spareDisk  = spare.First();

    LogService.Info($"[REPAIR] Faulty: {faultyDisk.Name}  →  Spare: {spareDisk.Name}");

    // ============================================================
    // 5. Ejecutar flujo real de reparación mdadm
    // ============================================================

    // 5.1 Marcar faulty como failed
    ShellHelper.EjecutarComoRoot($"mdadm {arrayPath} --fail /dev/{faultyDisk.Name}");

    // 5.2 Remover faulty
    ShellHelper.EjecutarComoRoot($"mdadm {arrayPath} --remove /dev/{faultyDisk.Name}");

    // 5.3 Añadir spare
    var addResult = ShellHelper.EjecutarComoRoot($"mdadm {arrayPath} --add /dev/{spareDisk.Name}");

    if (addResult.ExitCode != 0)
    {
        LogService.Error($"[REPAIR] Error añadiendo spare: {addResult.Stderr}");
        return false;
    }

    LogService.Info("[REPAIR] Reparación iniciada. mdadm comenzará el proceso de recovery.");

    return true;
}

public async Task<(bool Ok, string Message)> StopArraySafeAsync(string arrayName)
{
    try
    {
        LogService.Info($"[STOP] StopArraySafeAsync → {arrayName}");

        var arrays = await GetArraysAsync();
        var array = arrays.FirstOrDefault(a =>
            a.Name == arrayName ||
            a.Path.EndsWith(arrayName, StringComparison.Ordinal));

        if (array == null)
            return (false, $"Array {arrayName} not found.");

        var arrayPath = array.Path;

        if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
        {
            LogService.Error("[STOP] Array is mounted.");
            return (false, $"Array is mounted at {array.MountPath}. Unmount first.");
        }

        if (array.IsDegraded)
        {
            LogService.Error("[STOP] Array is degraded. Stopping is unsafe.");
            return (false, "Array is degraded. Stopping it may cause data loss.");
        }

        if (array.IsResyncing || array.IsRecovering || array.IsChecking || array.IsRepairing)
        {
            LogService.Error("[STOP] Array is busy (resync/recovery/check/repair).");
            return (false, "Array is busy (resync/recovery/check/repair). Try again later.");
        }

        if (!array.IsActive)
        {
            LogService.Error("[STOP] Array is not active.");
            return (false, "Array is not active. Cannot stop.");
        }

        // ⭐ limpiar fstab antes de parar
        RemoveArrayFromFstab(arrayPath);

        if (array.IsMounted && !string.IsNullOrWhiteSpace(array.MountPath))
        {
            LogService.Info($"[STOP] Unmounting {array.MountPath}...");
            var um = ShellHelper.EjecutarComoRoot($"umount -f \"{array.MountPath}\"");

            if (um.ExitCode != 0)
            {
                LogService.Error($"[STOP] Unmount failed: {um.Stderr}");
                return (false, $"Failed to unmount {array.MountPath}:\n{um.Stderr}");
            }
        }

        if (!await WaitForMdadmIdleAsync())
            return (false, "mdadm is busy, cannot stop array.");

        var stop = ShellHelper.EjecutarComoRoot($"mdadm --stop {arrayPath}");

        if (stop.ExitCode != 0)
        {
            LogService.Error($"[STOP] mdadm --stop failed: {stop.Stderr}");
            return (false, $"Failed to stop array:\n{stop.Stderr}");
        }

        // ⭐ actualizar mdadm.conf e initramfs tras parar
        PersistArrayToMdadmConf();
        ShellHelper.EjecutarComoRoot("update-initramfs -u");

        LogService.Info("[STOP] StopArraySafeAsync OK");
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
        string arrayPath;

        if (arrayName.StartsWith("/dev/"))
        {
            arrayPath = arrayName;
        }
        else
        {
            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a =>
                a.Name == arrayName ||
                a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
                a.Path.EndsWith(arrayName, StringComparison.Ordinal));

            arrayPath = array != null ? array.Path : $"/dev/{arrayName}";
        }

        var cmd = $"/usr/sbin/mdadm --detail {arrayPath}";
        var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot(cmd);

        return (stdout + "\n" + stderr).Trim();
    }

    public bool PersistArrayToMdadmConf()
{
    try
    {
        LogService.Info("========== MDADM PERSIST START ==========");

        var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --detail --scan");

        LogService.Info($"[MDADM] exit={exit}");
        LogService.Info("[MDADM] STDOUT:");
        LogService.Info(stdout ?? "<null>");
        LogService.Info("[MDADM] STDERR:");
        LogService.Info(stderr ?? "<null>");

        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            LogService.Error("[MDADM] No se pudo obtener la salida de mdadm --detail --scan.");
            return false;
        }

        string[] possiblePaths =
        {
            "/etc/mdadm/mdadm.conf",
            "/etc/mdadm.conf"
        };

        var confPath = possiblePaths.FirstOrDefault(File.Exists)
                       ?? possiblePaths[0];

        if (!File.Exists(confPath))
        {
            LogService.Info("[MDADM] Archivo no existe. Creándolo...");
            File.WriteAllText(confPath,
                "DEVICE partitions\n" +
                "MAILADDR root\n\n"
            );
        }

        var tempFile = "/tmp/mdadm.conf.new";
        File.WriteAllText(tempFile,
            "DEVICE partitions\n" +
            "MAILADDR root\n\n" +
            stdout.Trim() + Environment.NewLine);

        var copy = ShellHelper.EjecutarComoRoot($"cp {tempFile} {confPath}");

        LogService.Info($"[MDADM] cp exit={copy.ExitCode}");
        LogService.Info("[MDADM] cp STDERR:");
        LogService.Info(copy.Stderr ?? "<null>");

        if (copy.ExitCode != 0)
        {
            LogService.Error("[MDADM] Error al escribir en mdadm.conf:");
            LogService.Error(copy.Stderr);
            return false;
        }

        LogService.Info("[MDADM] mdadm.conf actualizado correctamente");
        LogService.Info("========== MDADM PERSIST END (OK) ==========");
        return true;
    }
    catch (Exception ex)
    {
        LogService.Error("[MDADM] EXCEPCIÓN:");
        LogService.Error(ex.ToString());
        LogService.Info("========== MDADM PERSIST END (EXCEPTION) ==========");
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

    public bool RemoveArrayFromFstab(string arrayPath)
{
    try
    {
        var fstab = "/etc/fstab";

        if (!File.Exists(fstab))
        {
            LogService.Info("[FSTAB] No existe /etc/fstab, nada que limpiar.");
            return true;
        }

        var lines = File.ReadAllLines(fstab);

        var dev = arrayPath.Trim();

        // ⭐ obtener UUID real del dispositivo, si existe
        var uuidResult = ShellHelper.EjecutarComoRoot($"blkid -s UUID -o value {dev}");
        var uuid = uuidResult.ExitCode == 0 ? uuidResult.Stdout.Trim() : "";

        var newLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
            {
                newLines.Add(line);
                continue;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                newLines.Add(line);
                continue;
            }

            var firstField = parts[0];

            bool match = false;

            if (string.Equals(firstField, dev, StringComparison.Ordinal))
                match = true;

            if (!match && !string.IsNullOrWhiteSpace(uuid) &&
                string.Equals(firstField, $"UUID={uuid}", StringComparison.OrdinalIgnoreCase))
                match = true;

            if (!match)
                newLines.Add(line);
        }

        if (newLines.Count == lines.Length)
        {
            LogService.Info("[FSTAB] No había entrada para este array.");
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

        ShellHelper.EjecutarComoRoot("systemctl daemon-reload");

        LogService.Info("[FSTAB] Entrada eliminada correctamente.");
        return true;
    }
    catch (Exception ex)
    {
        LogService.Error("[FSTAB] EXCEPCIÓN:");
        LogService.Error(ex.ToString());
        return false;
    }
}

    
    
    public async Task<bool> EnsureArraySafeForModification(string arrayName, bool allowFstab = false)
    {
        string devPath;

        if (arrayName.StartsWith("/dev/"))
        {
            devPath = arrayName;
        }
        else
        {
            var arrays = await GetArraysAsync();
            var array = arrays.FirstOrDefault(a =>
                a.Name == arrayName ||
                a.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
                a.Path.EndsWith(arrayName, StringComparison.Ordinal));

            devPath = array != null ? array.Path : $"/dev/{arrayName}";
        }

        var mounts = await ShellHelper.RunCleanAsync(
            $"grep -E '^{devPath}\\b' /proc/mounts || true");

        if (!string.IsNullOrWhiteSpace(mounts))
        {
            NotificadorLinux.Enviar(
                $"{devPath} está montado. Debe desmontarse antes de modificar el array.",
                7000, "critical");
            return false;
        }

        // ⭐ también comprobar por UUID en fstab
        var uuidResult = ShellHelper.RunCleanAsync($"blkid -s UUID -o value {devPath}");
        var uuid = (await uuidResult)?.Trim() ?? "";

        string fstab = "";
        if (!allowFstab)
        {
            fstab = await ShellHelper.RunCleanAsync(
                $"grep -E '^{devPath}\\b' /etc/fstab || true");

            if (string.IsNullOrWhiteSpace(fstab) && !string.IsNullOrWhiteSpace(uuid))
            {
                fstab = await ShellHelper.RunCleanAsync(
                    $"grep -E '^UUID={uuid}\\b' /etc/fstab || true");
            }
        }

        if (!allowFstab && !string.IsNullOrWhiteSpace(fstab))
        {
            NotificadorLinux.Enviar(
                $"{devPath} (o su UUID) está en /etc/fstab. Debe eliminarse antes de modificar el array.",
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

            // Evitar duplicados
            if (!result.Contains(devPath))
                result.Add(devPath);
        }

        return result;
    }
}
