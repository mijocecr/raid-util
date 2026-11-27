using System;
using System.IO;
using System.Linq;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

public static class MdadmService
{
    // ============================================================
    // RESOLVER PATH REAL DEL ARRAY
    // ============================================================
    private static string ResolveArrayPath(string arrayName)
    {
        if (arrayName.StartsWith("/dev/"))
            return arrayName;

        // ⭐ FIX: usar Singleton
        var arrays = RaidService.Instance.GetArraysAsync().Result;

        var a = arrays.FirstOrDefault(x =>
            x.Name == arrayName ||
            x.Path.EndsWith("/" + arrayName, StringComparison.Ordinal) ||
            x.Path.EndsWith(arrayName, StringComparison.Ordinal));

        if (a != null)
            return a.Path;

        return "/dev/" + arrayName;
    }

    // ============================================================
    // OBTENER DETALLE REAL DEL ARRAY
    // ============================================================
    public static string GetDetail(string arrayName)
    {
        var path = ResolveArrayPath(arrayName);

        var result = ShellHelper.RunRoot(
            ToolLocator.ResolveOrThrow("mdadm"), new[] { "--detail", path });

        // Un error de mdadm NO es información del array: mezclar stderr con un
        // detalle válido hacía que mensajes como "faulty" generaran alertas.
        return result.Success ? result.Stdout.Trim() : string.Empty;
    }

    // ============================================================
    // CAMBIAR VELOCIDAD DE RESYNC
    // ============================================================
    public static void SetResyncSpeed(int min, int max)
    {
        if (min < 0 || max < min)
            throw new ArgumentOutOfRangeException(nameof(min), "Velocidades de resincronización no válidas.");

        // tee evita el intérprete y la redirección: el valor llega por stdin como
        // un argumento más del proceso, sin ">" ni comandos compuestos.
        var tee = ToolLocator.ResolveOrThrow("tee");
        ShellHelper.RunRoot(tee, new[] { "/proc/sys/dev/raid/speed_limit_min" }, min + "\n");
        ShellHelper.RunRoot(tee, new[] { "/proc/sys/dev/raid/speed_limit_max" }, max + "\n");
    }

    // ============================================================
    // LEER /proc/mdstat
    // ============================================================
    public static string GetMdstat()
    {
        try
        {
            if (!File.Exists("/proc/mdstat"))
                return string.Empty;

            return File.ReadAllText("/proc/mdstat");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[MDADM] No se pudo leer /proc/mdstat: {ex.Message}");
            return string.Empty;
        }
    }

    // ============================================================
    // DETECTAR ARRAY DEGRADADO / ROTO
    // ============================================================
    public static bool IsDegraded(string arrayName)
    {
        var md = GetMdstat();
        if (string.IsNullOrWhiteSpace(md))
            return false; // dato no disponible: no es una alerta

        var line = MdstatParser.GetArrayLine(md, arrayName);
        if (line.Length == 0)
            return false; // array aún no listado (arranque, contenedor): no alertar

        return MdstatParser.IsDegradedLine(line);
    }

    // ============================================================
    // DETECTAR RESYNC / RECOVERY / RESHAPE
    // ============================================================
    public static bool IsResyncing(string arrayName)
    {
        var md = GetMdstat();
        if (string.IsNullOrWhiteSpace(md))
            return false;

        // Solo la línea de progreso de ESTE array: antes un resync en md0
        // marcaba también a md1 como "resyncing".
        return MdstatParser.IsOperationInProgress(md, arrayName);
    }

    // ============================================================
    // APLICAR CONFIGURACIÓN RAID REAL
    // ============================================================
    public static void ApplyConfig(string arrayName, ArrayConfig cfg)
    {
        var device = ResolveArrayPath(arrayName);

        // Velocidad de resync
        SetResyncSpeed(cfg.ResyncPriority, cfg.ResyncMaxSpeed);

        // Read-only / Read-write
        var mdadm = ToolLocator.ResolveOrThrow("mdadm");
        if (cfg.Mount_ReadOnly)
            ShellHelper.RunRoot(mdadm, new[] { "--readonly", device });
        else
            ShellHelper.RunRoot(mdadm, new[] { "--readwrite", device });

        // Label solo para EXT*
        if (!string.IsNullOrWhiteSpace(cfg.FsLabel))
        {
            var fs = FstabService.DetectFilesystem(device);

            if (fs.StartsWith("ext"))
            {
                var labelError = InputValidation.ValidateLabel(cfg.FsLabel, allowEmpty: false);
                if (labelError is not null)
                    throw new ArgumentException(labelError, nameof(cfg));

                ShellHelper.RunRoot(
                    ToolLocator.ResolveOrThrow("e2label"),
                    device,
                    cfg.FsLabel);
            }
        }
    }
}
