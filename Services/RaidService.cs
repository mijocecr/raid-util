using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services
{
    public class RaidService
    {
        // ============================================================
        //  CONSULTA DE DISCOS (UNIFICADO A RaidDiskInfo)
        // ============================================================

        public async Task<List<RaidDiskInfo>> GetAllDisksAsync()
        {
            var result = new List<RaidDiskInfo>();

            string json = await ShellHelper.RunCleanAsync(
                "lsblk -J -o NAME,MODEL,SIZE,ROTA,TYPE"
            );

            Console.WriteLine("[RAID] lsblk RAW JSON:");
            Console.WriteLine(json);

            if (string.IsNullOrWhiteSpace(json))
                return result;

            dynamic data;
            try
            {
                data = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RAID] ERROR al parsear JSON de lsblk:");
                Console.WriteLine(ex);
                return result;
            }

            foreach (var dev in data.blockdevices)
            {
                string type = dev.type ?? "unknown";

                if (type != "disk" && type != "lvm" && type != "loop")
                    continue;

                string name = (string)dev.name;
                string model = dev.model ?? "Unknown";
                string size = dev.size ?? "Unknown";

                // ⭐ FIX CRÍTICO: rota puede ser bool, int o string
                bool isRotational = ParseRota(dev.rota);

                Console.WriteLine($"[RAID] DISK {name} rotaToken={dev.rota} → parsed={isRotational}");

                var disk = new RaidDiskInfo
                {
                    Name = name,
                    Size = size,
                    Model = model,
                    State = "Unknown",
                    Role = "Unknown",
                    ArrayName = "",
                    IsRotational = isRotational,
                    Icon = GetDiskIcon(name, model, isRotational)   // ⭐ ICONO REAL ⭐
                };

                string mdadmInfo = await RunMdadmAsync($"--examine /dev/{name}");

                if (!string.IsNullOrWhiteSpace(mdadmInfo) &&
                    mdadmInfo.Contains("Raid Level", StringComparison.OrdinalIgnoreCase))
                {
                    disk.State = "In Array";
                }
                else
                {
                    disk.State = "Free";
                }

                result.Add(disk);
            }

            return result;
        }

        // ============================================================
        //  ICONOS REALES POR TIPO DE DISCO
        // ============================================================

        private string GetDiskIcon(string name, string model, bool isRotational)
        {
            string lowerName = name.ToLowerInvariant();
            string lowerModel = model.ToLowerInvariant();

            // NVMe
            if (lowerName.StartsWith("nvme"))
                return "avares://RAID-Util/Assets/Icons/disk-nvme.png";

            // USB
            if (lowerModel.Contains("usb"))
                return "avares://RAID-Util/Assets/Icons/disk-usb.png";

            // Virtual Disk (VMware, QEMU, VirtualBox…)
            if (lowerModel.Contains("virtual") ||
                lowerModel.Contains("vmware") ||
                lowerModel.Contains("qemu") ||
                lowerModel.Contains("vbox"))
                return "avares://RAID-Util/Assets/Icons/disk-virtual.png";

            // HDD
            if (isRotational)
                return "avares://RAID-Util/Assets/Icons/disk-hdd.png";

            // SSD SATA
            return "avares://RAID-Util/Assets/Icons/disk-ssd.png";
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

            var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot("/usr/sbin/mdadm --detail --scan");
            string scan = (stdout + "\n" + stderr).Trim();

            Console.WriteLine("[RAID] mdadm --detail --scan OUTPUT:");
            Console.WriteLine(scan);

            if (string.IsNullOrWhiteSpace(scan))
                return arrays;

            foreach (var raw in scan.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("ARRAY"))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                string arrayPath = parts[1];
                string arrayName = arrayPath.Replace("/dev/", "");

                string detail = await RunMdadmAsync($"--detail {arrayPath}");

                Console.WriteLine("=== DETAIL OUTPUT ===");
                Console.WriteLine(detail);
                Console.WriteLine("=====================");

                if (string.IsNullOrWhiteSpace(detail))
                    continue;

                string state = ParseArrayState(detail);
                var diskNames = await GetDisksInArrayAsync(arrayName, detail);

                var info = new RaidArrayInfo
                {
                    Name = arrayName,
                    Level = ParseLevel(detail),
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

                foreach (var dev in diskNames)
                {
                    RaidDiskInfo diskInfo = await GetDiskInfo(dev);

                    diskInfo.Role = ParseDiskRole(detail, dev);
                    diskInfo.State = ParseDiskState(detail, dev);
                    diskInfo.ArrayName = arrayName;

                    info.Disks.Add(diskInfo);
                }

                arrays.Add(info);
            }

            Console.WriteLine("=== ARRAYS DETECTADOS ===");
            foreach (var arr in arrays)
            {
                Console.WriteLine($"ARRAY: {arr.Name}  Level={arr.Level}  State={arr.State}");
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

            if (lower.Contains("state : clean") && !lower.Contains("degraded"))
                return "Healthy";

            if (lower.Contains("state : active") && !lower.Contains("degraded"))
                return "Healthy";

            if (lower.Contains("degraded"))
                return "Degraded";

            if (lower.Contains("recover") ||
                lower.Contains("rebuild") ||
                lower.Contains("resync"))
                return "Rebuilding";

            if (lower.Contains("read-only"))
                return "Read-Only";

            if (lower.Contains("inactive") ||
                lower.Contains("failed") ||
                lower.Contains("faulty"))
                return "Failed";

            return "Unknown";
        }

        private string ParseDiskRole(string detail, string device)
        {
            foreach (var raw in detail.Split('\n'))
            {
                string line = raw.Trim();

                if (!line.Contains(device, StringComparison.Ordinal))
                    continue;

                string lower = line.ToLowerInvariant();

                if (lower.Contains("active") || lower.Contains("sync"))
                    return "active";

                if (lower.Contains("spare"))
                    return "spare";

                if (lower.Contains("faulty"))
                    return "faulty";

                if (lower.Contains("rebuild") || lower.Contains("recover"))
                    return "rebuilding";
            }

            return "unknown";
        }

        private string ParseDiskState(string detail, string device)
        {
            foreach (var raw in detail.Split('\n'))
            {
                string line = raw.Trim();

                if (!line.Contains(device, StringComparison.Ordinal))
                    continue;

                string lower = line.ToLowerInvariant();

                if (lower.Contains("faulty"))
                    return "FAULTY";

                if (lower.Contains("rebuild") || lower.Contains("recover"))
                    return "WARN";

                if (lower.Contains("active") || lower.Contains("sync"))
                    return "OK";

                if (lower.Contains("spare"))
                    return "OK";
            }

            return "UNKNOWN";
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

        private async Task<RaidDiskInfo> GetDiskInfo(string devicePath)
        {
            string raw = devicePath.Trim().TrimEnd(':');
            string name = raw.Replace("/dev/", "");

            string json = await ShellHelper.RunCleanAsync(
                $"lsblk -J -o NAME,MODEL,SIZE,ROTA,TYPE /dev/{name}"
            );

            if (string.IsNullOrWhiteSpace(json))
            {
                return new RaidDiskInfo
                {
                    Name = name,
                    Model = "Unknown",
                    Size = "Unknown",
                    Icon = "",
                    ArrayName = ""
                };
            }

            dynamic data;
            try
            {
                data = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;
            }
            catch
            {
                return new RaidDiskInfo
                {
                    Name = name,
                    Model = "Unknown",
                    Size = "Unknown",
                    Icon = "",
                    ArrayName = ""
                };
            }

            var dev = data.blockdevices[0];

            return new RaidDiskInfo
            {
                Name = name,
                Model = dev.model ?? "Unknown",
                Size = dev.size ?? "Unknown",
                Icon = "",
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

                if (!line.Contains("/dev/"))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                string dev = parts[^1];

                if (dev.StartsWith("/dev/"))
                    result.Add(dev);
            }

            return result;
        }
    }
}
