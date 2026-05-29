using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services
{
    public static class DiskService
    {
        // ============================================================
        // OBTENER TODOS LOS DISCOS FÍSICOS REALES
        // ============================================================
        public static List<RaidDiskInfo> GetAllDisks()
        {
            var disks = new List<RaidDiskInfo>();

            // 1) Ejecutar lsblk en JSON
            string json = ShellHelper.EjecutarSinRoot(
                "lsblk -J -o NAME,TYPE,SIZE,MODEL,SERIAL,ROTA,MOUNTPOINT,FSTYPE"
            ).Stdout;

            if (string.IsNullOrWhiteSpace(json))
                return disks;

            var parsed = ParseLsblk(json);

            foreach (var d in parsed)
            {
                if (d.Type != "disk")
                    continue;

                bool isRot =
                    d.Rotational == "1" ||
                    d.Rotational.Equals("true", StringComparison.OrdinalIgnoreCase);

                var info = new RaidDiskInfo
                {
                    Name = d.Name,
                    Model = d.Model,
                    Size = d.Size,
                    Serial = d.Serial,
                    Filesystem = d.FsType,
                    MountPoint = d.MountPoint,
                    IsMounted = !string.IsNullOrWhiteSpace(d.MountPoint),
                    IsRotational = isRot,
                    Type = GetDiskType(d),
                    Temperature = GetTemperature(d.Name),

                    // ⭐ ICONO CORRECTO USANDO DiskIconService (con nombre y modelo)
                    Icon = DiskIconService.GetIcon(d.Name, d.Model, isRot)
                };

                // 2) Detectar si pertenece a un array RAID
                info.IsUsedByRaid = MdadmService_IsDiskInArray(d.Name, out string arrayName);
                info.ArrayName = arrayName;

                // 3) Detectar si es disco del sistema
                info.IsSystemDisk = SystemDiskDetector.IsSystemDisk(d.Name);

                // ⭐ 4) STATUS REAL
                info.Status = GetDiskStatus(info);

                disks.Add(info);
            }

            return disks;
        }

        // ============================================================
        // PARSEAR JSON DE LSBKL
        // ============================================================
        private static List<LsblkEntry> ParseLsblk(string json)
        {
            var list = new List<LsblkEntry>();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.GetProperty("blockdevices");

            foreach (var dev in root.EnumerateArray())
            {
                list.Add(new LsblkEntry
                {
                    Name = dev.GetProperty("name").GetString() ?? "",
                    Type = dev.GetProperty("type").GetString() ?? "",
                    Size = dev.GetProperty("size").GetString() ?? "",
                    Model = dev.GetProperty("model").GetString() ?? "",
                    Serial = dev.GetProperty("serial").GetString() ?? "",
                    Rotational = dev.GetProperty("rota").GetRawText(),
                    MountPoint = dev.TryGetProperty("mountpoint", out var mp) ? mp.GetString() ?? "" : "",
                    FsType = dev.TryGetProperty("fstype", out var fs) ? fs.GetString() ?? "" : ""
                });
            }

            return list;
        }

        // ============================================================
        // DETECTAR TIPO DE DISCO (NVMe CORREGIDO)
        // ============================================================
        private static string GetDiskType(LsblkEntry d)
        {
            string model = d.Model.ToLower();
            string name = d.Name.ToLower();

            if (name.StartsWith("nvme"))
                return "NVMe";

            if (model.Contains("usb"))
                return "USB";

            if (model.Contains("fileio") ||
                model.Contains("virtual") ||
                model.Contains("vmware") ||
                model.Contains("qemu") ||
                model.Contains("vbox") ||
                model.Contains("loop") ||
                model.Contains("mapper") ||
                model.Contains("nbd") ||
                model.Contains("zram"))
                return "Virtual";

            if (d.Rotational == "1" || d.Rotational.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "HDD";

            return "SSD";
        }

        // ============================================================
        // TEMPERATURA REAL (SMARTCTL)
        // ============================================================
        
        private static string GetTemperature(string name)
        {
            var r = ShellHelper.EjecutarComoRoot($"smartctl -A /dev/{name}");

            if (string.IsNullOrWhiteSpace(r.Stdout))
                return "N/A";

            var lines = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // ============================
            // 1) NVMe: "Temperature: 42 Celsius"
            // ============================
            foreach (var line in lines)
            {
                if (line.Contains("Temperature:", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Celsius", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("Sensor", StringComparison.OrdinalIgnoreCase)) // evitar "Temperature Sensor 1"
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (int.TryParse(p, out int temp))
                            return $"{temp}°C";
                    }
                }
            }

            // ============================
            // 2) HDD/SSD: atributo 194
            // ============================
            foreach (var line in lines)
            {
                // Aceptamos línea que empiece por 194 o contenga "Temperature_Celsius"
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("194") && !trimmed.Contains("Temperature_Celsius", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Ejemplo:
                // 194 Temperature_Celsius ... 29 (Min/Max 18/52)
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Estrategia simple y segura:
                // buscar el ÚLTIMO número ANTES de un token que empiece por '('
                int lastNumberBeforeParen = -1;
                foreach (var p in parts)
                {
                    if (p.StartsWith("("))
                        break;

                    if (int.TryParse(p, out int val))
                        lastNumberBeforeParen = val;
                }

                if (lastNumberBeforeParen >= 0)
                    return $"{lastNumberBeforeParen}°C";
            }

            return "N/A";
        }



        // ============================================================
        // STATUS REAL DEL DISCO
        // ============================================================
        private static string GetDiskStatus(RaidDiskInfo info)
        {
            // 1) Si está en RAID → estado definido por mdadm
            if (info.IsUsedByRaid)
                return "In RAID";

            // 2) Leer estado SMART
            var r = ShellHelper.EjecutarComoRoot($"smartctl -H /dev/{info.Name}");

            if (string.IsNullOrWhiteSpace(r.Stdout))
                return "Unknown";

            string output = r.Stdout.ToLower();

            if (output.Contains("passed"))
                return "Healthy";

            if (output.Contains("warning") || output.Contains("prefail"))
                return "Warning";

            if (output.Contains("failed"))
                return "Failed";

            return "Unknown";
        }

        // ============================================================
        // DETECTAR SI UN DISCO ESTÁ EN UN ARRAY RAID
        // ============================================================
        private static bool MdadmService_IsDiskInArray(string diskName, out string arrayName)
        {
            arrayName = "";

            var r = ShellHelper.EjecutarComoRoot($"mdadm --examine /dev/{diskName}");
            string output = (r.Stdout + r.Stderr).ToLower();

            if (output.Contains("no md superblock"))
                return false;

            foreach (var line in output.Split('\n'))
            {
                if (line.Trim().StartsWith("name"))
                {
                    var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        arrayName = parts.Last().Trim();
                        return true;
                    }
                }
            }

            return false;
        }
    }

    // ============================================================
    // MODELO INTERNO PARA LSBKL
    // ============================================================
    public class LsblkEntry
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Size { get; set; } = "";
        public string Model { get; set; } = "";
        public string Serial { get; set; } = "";
        public string Rotational { get; set; } = "";
        public string MountPoint { get; set; } = "";
        public string FsType { get; set; } = "";
    }
}
