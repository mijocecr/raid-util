using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RAID_Util.Helpers;
using RAID_Util.Models;

namespace RAID_Util.Services;

/// <summary>
/// Operaciones sobre miembros y tamaño de un array, compartidas por GUI y CLI.
/// Antes existían dos implementaciones: la GUI ignoraba los exit codes y hacía
/// resize automático; la CLI comprobaba el resultado pero no redimensionaba.
/// </summary>
public static class ArrayOperationsService
{
    private static readonly TimeSpan ReshapeTimeout = TimeSpan.FromMinutes(30);

    public static bool CanExpand(RaidArrayInfo array)
    {
        if (array is null)
            return false;

        var level = array.Level?.Replace(" ", "").Replace("-", "").ToUpperInvariant() ?? "";
        if (level is not ("RAID5" or "RAID6" or "RAID10"))
            return false;

        if (array.IsResyncing || array.IsRecovering || array.IsChecking || array.IsRepairing)
            return false;

        if (!array.IsActive && !array.IsClean)
            return false;

        return array.RebuildProgress <= 0;
    }

    public static Task<OperationResult> MarkFaultyAsync(
        RaidArrayInfo array, string diskName, CancellationToken cancellationToken = default)
    {
        return MdadmMemberAsync(
            array,
            diskName,
            array.IsLinear || array.IsRaid0 ? "--remove" : "--fail",
            $"/dev/{diskName} marcado como faulty en {array.Path}.",
            cancellationToken);
    }

    public static Task<OperationResult> SetSpareAsync(
        RaidArrayInfo array, string diskName, CancellationToken cancellationToken = default)
    {
        return MdadmMemberAsync(
            array,
            diskName,
            "--set-spare",
            $"/dev/{diskName} configurado como spare en {array.Path}.",
            cancellationToken);
    }

    public static async Task<OperationResult> RecoverFaultyAsync(
        RaidArrayInfo array, string diskName)
    {
        var validation = Validate(array, diskName);
        if (validation is not null)
            return OperationResult.Fail(validation);

        if (!SafeDiskGuard.CanModifyDevice(diskName, SafeDiskOperation.RemoveMember))
            return OperationResult.Fail($"Operación bloqueada por SafeDiskGuard para /dev/{diskName}.");

        var ok = await RaidService.Instance.AddDiskToArrayAsync(array.Name, diskName).ConfigureAwait(false);
        return ok
            ? OperationResult.Ok($"Disk recovery started: /dev/{diskName} re-added to {array.Name}.")
            : OperationResult.Fail($"No se pudo recuperar /dev/{diskName} en {array.Name}.");
    }

    public static Task<OperationResult> ActivateSpareAsync(
        RaidArrayInfo array, int deviceCount, CancellationToken cancellationToken = default) =>
        GrowCoreAsync(array, deviceCount, waitAndResize: false, cancellationToken);

    public static Task<OperationResult> GrowAsync(
        RaidArrayInfo array, int deviceCount, CancellationToken cancellationToken = default) =>
        GrowCoreAsync(array, deviceCount, waitAndResize: true, cancellationToken);

    private static async Task<OperationResult> MdadmMemberAsync(
        RaidArrayInfo array,
        string diskName,
        string action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var validation = Validate(array, diskName);
        if (validation is not null)
            return OperationResult.Fail(validation);

        if (!SafeDiskGuard.CanModifyDevice(diskName, SafeDiskOperation.RemoveMember))
            return OperationResult.Fail($"Operación bloqueada por SafeDiskGuard para /dev/{diskName}.");

        var result = await ShellHelper.RunRootAsync(
            ToolLocator.ResolveOrThrow("mdadm"),
            new[] { array.Path, action, InputValidation.NormalizeDevicePath(diskName) },
            cancellationToken).ConfigureAwait(false);

        return result.Success
            ? OperationResult.Ok(successMessage)
            : OperationResult.Fail($"mdadm {action} falló: {result.Stderr.Trim()}");
    }

    private static async Task<OperationResult> GrowCoreAsync(
        RaidArrayInfo array,
        int deviceCount,
        bool waitAndResize,
        CancellationToken cancellationToken)
    {
        var validation = Validate(array);
        if (validation is not null)
            return OperationResult.Fail(validation);

        if (deviceCount < 2)
            return OperationResult.Fail("El número de dispositivos debe ser al menos 2.");

        var result = await ShellHelper.RunRootAsync(
            ToolLocator.ResolveOrThrow("mdadm"),
            new[] { "--grow", array.Path, $"--raid-devices={deviceCount}" },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return OperationResult.Fail($"No se pudo iniciar el reshape: {result.Stderr.Trim()}");

        var message = $"The array {array.Name} is now reshaping to {deviceCount} devices.";

        if (!waitAndResize)
            return OperationResult.Ok(message);

        try
        {
            await WaitForReshapeAsync(array.Path, cancellationToken).ConfigureAwait(false);

            var resize = await ResizeFileSystemAsync(array.Path, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resize))
                message += "\n" + resize;
        }
        catch (OperationCanceledException)
        {
            message += "\nEl reshape sigue en curso; el filesystem se redimensionará cuando termine.";
        }
        catch (Exception ex)
        {
            LogService.Warn($"[RESHAPE] No se pudo redimensionar el filesystem: {ex.Message}");
            message += $"\nNo se pudo redimensionar el filesystem: {ex.Message}";
        }

        return OperationResult.Ok(message);
    }

    private static async Task WaitForReshapeAsync(string arrayPath, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(arrayPath.TrimEnd('/'));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReshapeTimeout);

        while (File.Exists("/proc/mdstat"))
        {
            var content = await File.ReadAllTextAsync("/proc/mdstat", timeout.Token).ConfigureAwait(false);
            var isReshaping = content.Split('\n').Any(line =>
                line.Contains(name, StringComparison.Ordinal) &&
                line.Contains("reshape", StringComparison.OrdinalIgnoreCase));

            if (!isReshaping)
                return;

            await Task.Delay(2000, timeout.Token).ConfigureAwait(false);
        }
    }

    private static async Task<string> ResizeFileSystemAsync(string arrayPath, CancellationToken cancellationToken)
    {
        var blkid = await ShellHelper.RunRootAsync(
            ToolLocator.ResolveOrThrow("blkid"),
            new[] { "-s", "TYPE", "-o", "value", arrayPath },
            cancellationToken).ConfigureAwait(false);

        var fs = blkid.Stdout.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(fs))
            return "No se detectó filesystem; omitiendo el redimensionado.";

        if (fs.StartsWith("ext", StringComparison.Ordinal))
        {
            if (!ToolLocator.IsAvailable("resize2fs"))
                return "resize2fs no está disponible; redimensiona el filesystem manualmente.";

            var result = await ShellHelper.RunRootAsync(
                ToolLocator.ResolveOrThrow("resize2fs"), new[] { arrayPath }, cancellationToken)
                .ConfigureAwait(false);
            return result.Success ? "Filesystem redimensionado." : $"No se pudo redimensionar: {result.Stderr.Trim()}";
        }

        if (fs is "xfs" or "btrfs")
        {
            var mountPoint = FindMountPoint(arrayPath);
            if (mountPoint is null)
                return $"{fs} no está montado; se redimensionará en el próximo montaje.";

            if (fs == "xfs" && ToolLocator.IsAvailable("xfs_growfs"))
            {
                var result = await ShellHelper.RunRootAsync(
                    ToolLocator.ResolveOrThrow("xfs_growfs"), new[] { mountPoint }, cancellationToken)
                    .ConfigureAwait(false);
                return result.Success ? "Filesystem XFS redimensionado." : $"No se pudo redimensionar: {result.Stderr.Trim()}";
            }

            if (fs == "btrfs" && ToolLocator.IsAvailable("btrfs"))
            {
                var result = await ShellHelper.RunRootAsync(
                    ToolLocator.ResolveOrThrow("btrfs"),
                    new[] { "filesystem", "resize", "max", mountPoint },
                    cancellationToken).ConfigureAwait(false);
                return result.Success ? "Filesystem btrfs redimensionado." : $"No se pudo redimensionar: {result.Stderr.Trim()}";
            }

            return $"Herramienta para {fs} no disponible; redimensiona manualmente.";
        }

        return $"Filesystem '{fs}' no soporta redimensionado automático.";
    }

    private static string? FindMountPoint(string device)
    {
        if (!File.Exists("/proc/mounts"))
            return null;

        var wanted = InputValidation.NormalizeDevicePath(device);
        foreach (var line in File.ReadLines("/proc/mounts"))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
                continue;

            if (Decode(fields[0]) == wanted || Decode(fields[0]).EndsWith("/" + Path.GetFileName(wanted), StringComparison.Ordinal))
                return Decode(fields[1]);
        }

        return null;
    }

    private static string Decode(string value) => value
        .Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal)
        .Replace("\\012", "\n", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);

    private static string? Validate(RaidArrayInfo array, string? diskName = null)
    {
        if (array is null)
            return "No se seleccionó ningún array.";

        var error = InputValidation.ValidateDevicePath(array.Path);
        if (error is not null)
            return error;

        if (diskName is null)
            return null;

        return InputValidation.ValidateDeviceName(diskName.StartsWith("/dev/") ? diskName[5..] : diskName);
    }
}
