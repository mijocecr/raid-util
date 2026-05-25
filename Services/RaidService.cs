using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

            foreach (var dev in data.blockdevices)
            {
                string type = dev.type ?? "unknown";

                if (type != "disk" && type != "lvm" && type != "loop")
                    continue;

                string name = (string)dev.name;
                string model = dev.model ?? "Unknown";
                string size = dev.size ?? "Unknown";
                bool isRotational = dev.rota == "1";

                var disk = new RaidDiskInfo
                {
                    Name = name,
                    Size = size,
                    Model = model,
                    State = "Unknown",
                    Role = "Unknown",
                    Icon = "",
                    ArrayName = ""
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
        //  LECTURA DE ARRAYS (UNIFICADO A RaidDiskInfo)
        // ============================================================

        public async Task<List<RaidArrayInfo>> GetArraysAsync()
        {
            var arrays = new List<RaidArrayInfo>();

            // 🔥 YA NO USAMOS sudo AQUÍ
            string scan = await ShellHelper.RunCleanAsync("/usr/sbin/mdadm --detail --scan");
            scan = scan.Trim();

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
                    Disks = new List<RaidDiskInfo>()
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
        //  PARSERS
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
                // 🔥 YA NO USAMOS sudo AQUÍ
                string output = await ShellHelper.RunCleanAsync(cmd);
                output = output.Trim();

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
