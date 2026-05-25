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
        //  CONSULTA DE DISCOS
        // ============================================================

        /// <summary>
        /// Devuelve todos los discos visibles para RAID (sin filtrar aún).
        /// </summary>
      
        public async Task<List<DiskInfo>> GetAllDisksAsync()
        {
            var result = new List<DiskInfo>();

            // 1) Obtener JSON real de lsblk
            string json = await ShellHelper.RunCleanAsync(
                "lsblk -J -o NAME,MODEL,SIZE,ROTA,TYPE"
            );

            if (string.IsNullOrWhiteSpace(json))
                return result;

            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;

            foreach (var dev in data.blockdevices)
            {
                // Solo discos reales (TYPE=disk), pero también detectamos loop/lvm
                string type = dev.type ?? "unknown";

                string name = (string)dev.name;
                string model = dev.model ?? "Unknown";
                string size = dev.size ?? "Unknown";
                bool isRotational = dev.rota == "1";

                var disk = new DiskInfo
                {
                    Name = name,
                    Size = size,
                    State = "Unknown",
                    Role = "Unknown",
                    Type = type,
                    IsRotational = isRotational
                };

                // 2) Determinar si pertenece a un array RAID
                string mdadmInfo = await ShellHelper.RunCleanAsync(
                    $"mdadm --examine /dev/{name}"
                );

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

        
        /// <summary>
        /// Devuelve los discos que pertenecen a un array RAID concreto.
        /// </summary>
        public async Task<List<string>> GetDisksInArrayAsync(string arrayName)
        {
            var result = new List<string>();

            // Ejecutar mdadm --detail
            string output = await ShellHelper.RunCleanAsync(
                $"mdadm --detail /dev/{arrayName}"
            );

            if (string.IsNullOrWhiteSpace(output))
                return result;

            foreach (var line in output.Split('\n'))
            {
                // Las líneas que contienen discos tienen /dev/sdX o /dev/nvmeXnY
                if (line.Contains("/dev/"))
                {
                    // Ejemplo de línea:
                    // "   0     8    16      0   active sync   /dev/sdb"
                    string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    // El último elemento suele ser el dispositivo
                    string dev = parts[^1];

                    if (dev.StartsWith("/dev/"))
                        result.Add(dev);
                }
            }

            return result;
        }

        // ============================================================
        //  OPERACIONES RAID (SOLO FIRMA, SIN LÓGICA AÚN)
        // ============================================================

        public async Task CreateArrayAsync(
            string name,
            string level,
            int chunkSizeKb,
            List<string> devices)
        {
    
            await Task.CompletedTask;
        }

        public async Task DeleteArrayAsync(string name)
        {
            
            await Task.CompletedTask;
        }

        public async Task AddDiskAsync(string arrayName, string device)
        {
            
            await Task.CompletedTask;
        }

        public async Task RemoveDiskAsync(string arrayName, string device)
        {
            
            await Task.CompletedTask;
        }

        public async Task MarkDiskFaultyAsync(string arrayName, string device)
        {
            
            await Task.CompletedTask;
        }

        public async Task GrowArrayAsync(
            string arrayName,
            string? newLevel = null,
            int? newDevicesCount = null,
            int? newChunkSizeKb = null)
        {
           
            await Task.CompletedTask;
        }
        
        public async Task<List<RaidArrayInfo>> GetArraysAsync()
        {
            var arrays = new List<RaidArrayInfo>();

            string mdstat = await ShellHelper.RunCleanAsync("cat /proc/mdstat");

            if (string.IsNullOrWhiteSpace(mdstat))
                return arrays;

            foreach (var line in mdstat.Split('\n'))
            {
                if (!line.StartsWith("md"))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 4)
                    continue;

                string arrayName = parts[0];
                string level = parts[3].ToUpper();

                string detail = await ShellHelper.RunCleanAsync(
                    $"mdadm --detail /dev/{arrayName}"
                );

                string realState = ParseArrayState(detail);

                var diskNames = await GetDisksInArrayAsync(arrayName);

                var info = new RaidArrayInfo
                {
                    Name = arrayName,
                    Level = level,
                    State = realState,
                    Disks = new List<RaidDiskInfo>()
                };

                foreach (var dev in diskNames)
                {
                    DiskInfo diskInfo = await GetDiskInfo(dev);

                    info.Disks.Add(new RaidDiskInfo
                    {
                        Name = dev,
                        Role = ParseDiskRole(detail, dev),
                        State = ParseDiskState(detail, dev),
                        Icon = diskInfo.Icon,
                        Size = diskInfo.Size,
                        Model = diskInfo.Model
                    });
                }

                arrays.Add(info);
            }

            return arrays;
        }

       
        
        private string ParseArrayState(string detail)
        {
            if (detail.Contains("State : clean", StringComparison.OrdinalIgnoreCase))
                return "Healthy";

            if (detail.Contains("State : active", StringComparison.OrdinalIgnoreCase))
                return "Healthy";

            if (detail.Contains("State : degraded", StringComparison.OrdinalIgnoreCase))
                return "Degraded";

            if (detail.Contains("recovery", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("rebuild", StringComparison.OrdinalIgnoreCase))
                return "Rebuilding";

            if (detail.Contains("State : read-only", StringComparison.OrdinalIgnoreCase))
                return "Read-Only";

            if (detail.Contains("State : inactive", StringComparison.OrdinalIgnoreCase))
                return "Failed";

            return "Unknown";
        }

        
        private string ParseDiskRole(string detail, string device)
        {
            foreach (var line in detail.Split('\n'))
            {
                if (!line.Contains(device))
                    continue;

                if (line.Contains("active"))
                    return "active";

                if (line.Contains("spare"))
                    return "spare";

                if (line.Contains("faulty"))
                    return "faulty";

                if (line.Contains("rebuilding"))
                    return "rebuilding";
            }

            return "unknown";
        }

        
        private string ParseDiskState(string detail, string device)
        {
            foreach (var line in detail.Split('\n'))
            {
                if (!line.Contains(device))
                    continue;

                if (line.Contains("faulty"))
                    return "FAULTY";

                if (line.Contains("spare"))
                    return "OK";

                if (line.Contains("active"))
                    return "OK";

                if (line.Contains("rebuilding"))
                    return "WARN";
            }

            return "UNKNOWN";
        }

        
        private async Task<DiskInfo> GetDiskInfo(string devicePath)
        {
            string name = devicePath.Replace("/dev/", "");

            string json = await ShellHelper.RunCleanAsync(
                $"lsblk -J -o NAME,MODEL,SIZE,ROTA,TYPE /dev/{name}"
            );

            if (string.IsNullOrWhiteSpace(json))
            {
                return new DiskInfo
                {
                    Name = name,
                    Model = "Unknown",
                    Size = "Unknown",
                    Type = "unknown",
                    IsRotational = false
                };
            }

            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;

            // lsblk devuelve blockdevices como array
            var dev = data.blockdevices[0];

            return new DiskInfo
            {
                Name = name,
                Model = dev.model ?? "Unknown",
                Size = dev.size ?? "Unknown",
                Type = dev.type ?? "unknown",
                IsRotational = dev.rota == "1"
            };
        }

        
        
        
    }//Fin de Clase
    
    
    
    
    
}






