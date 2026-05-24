using System.Collections.Generic;
using System.Threading.Tasks;

namespace RAID_Util.Services
{
    public class RaidService
    {
        // ============================================================
        //  MODELOS SIMPLES
        // ============================================================

        public class RaidDiskInfo
        {
            public string Device { get; set; } = string.Empty;   // /dev/sdX
            public string Model { get; set; } = string.Empty;
            public string Size { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;   // Free / In Array / Faulty / Spare
        }

        // ============================================================
        //  CONSULTA DE DISCOS
        // ============================================================

        /// <summary>
        /// Devuelve todos los discos visibles para RAID (sin filtrar aún).
        /// </summary>
        public async Task<List<RaidDiskInfo>> GetAllDisksAsync()
        {
            
            await Task.CompletedTask;

            return new List<RaidDiskInfo>
            {
                // Placeholder de ejemplo, se eliminará al implementar la lógica real
                new RaidDiskInfo { Device = "/dev/sdb", Model = "Placeholder Disk 1", Size = "100G", Status = "Free" },
                new RaidDiskInfo { Device = "/dev/sdc", Model = "Placeholder Disk 2", Size = "100G", Status = "Free" }
            };
        }

        /// <summary>
        /// Devuelve los discos que pertenecen a un array RAID concreto.
        /// </summary>
        public async Task<List<string>> GetDisksInArrayAsync(string arrayName)
        {
           
            await Task.CompletedTask;

            return new List<string>
            {
                // Placeholder, se reemplaza luego
                "/dev/sdb",
                "/dev/sdc"
            };
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
    }
}
