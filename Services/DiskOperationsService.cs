using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

/// <summary>
/// Operaciones destructivas de disco compartidas por GUI y CLI. Antes había dos
/// implementaciones con quoting, nombres de partición y semántica de éxito distintos.
/// </summary>
public static class DiskOperationsService
{
    public static async Task<OperationResult> DeepCleanAsync(
        string diskName, CancellationToken cancellationToken = default)
    {
        var validation = InputValidation.ValidateDeviceName(diskName);
        if (validation is not null)
            return OperationResult.Fail(validation);

        if (!ToolLocator.IsAvailable("mdadm"))
            return OperationResult.Fail("mdadm no está disponible en este sistema.");

        var device = $"/dev/{diskName}";
        var steps = new List<(string Tool, string[] Arguments, bool Required)>
        {
            ("mdadm", new[] { "--zero-superblock", "--force", device }, true),
            ("wipefs", new[] { "-a", "-f", device }, false),
            ("sgdisk", new[] { "--zap-all", device }, false)
        };

        var failures = new List<string>();
        foreach (var (tool, arguments, required) in steps)
        {
            if (!ToolLocator.IsAvailable(tool))
            {
                failures.Add($"{tool}: no disponible");
                continue;
            }

            var result = await ShellHelper.RunRootAsync(tool, arguments, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
                failures.Add($"{tool}: {result.Stderr.Trim()}");
        }

        // dd con BusyBox no acepta status=none; el reescritor ya lo elimina,
        // pero aquí se usa la forma portable directamente.
        if (ToolLocator.IsAvailable("dd"))
        {
            var dd = await ShellHelper.RunRootAsync(
                "dd", new[] { "if=/dev/zero", $"of={device}", "bs=1M", "count=10", "conv=fdatasync" },
                cancellationToken).ConfigureAwait(false);
            if (!dd.Success)
                failures.Add($"dd: {dd.Stderr.Trim()}");
        }

        if (ToolLocator.IsAvailable("blockdev"))
        {
            var reread = await ShellHelper.RunRootAsync(
                "blockdev", new[] { "--rereadpt", device }, cancellationToken).ConfigureAwait(false);
            if (!reread.Success)
                failures.Add($"blockdev: {reread.Stderr.Trim()}");
        }

        if (ToolLocator.IsAvailable("partprobe"))
        {
            var probe = await ShellHelper.RunRootAsync(
                "partprobe", new[] { device }, cancellationToken).ConfigureAwait(false);
            if (!probe.Success)
                failures.Add($"partprobe: {probe.Stderr.Trim()}");
        }

        await SystemIntegrationService.SettleDevicesAsync(cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0 && steps.Any(s => s.Required && failures.Any(f => f.StartsWith(s.Tool, StringComparison.Ordinal))))
            return OperationResult.Fail($"No se pudo limpiar {device}: {string.Join("; ", failures)}");

        var message = failures.Count == 0
            ? $"Disk {device} has been thoroughly wiped and cleaned."
            : $"Disk {device} cleaned with warnings: {string.Join("; ", failures)}";

        LogService.Info($"[DISK-OPS] {message}");
        return OperationResult.Ok(message);
    }

    public static async Task<OperationResult> InitializeDiskAsync(
        string diskName,
        string fileSystem,
        string label,
        string? mountPoint = null,
        CancellationToken cancellationToken = default)
    {
        var validation = InputValidation.ValidateDeviceName(diskName);
        if (validation is not null)
            return OperationResult.Fail(validation);

        validation = InputValidation.ValidateLabel(label, allowEmpty: false);
        if (validation is not null)
            return OperationResult.Fail(validation);

        if (!ToolLocator.IsAvailable("parted"))
            return OperationResult.Fail("parted no está disponible en este sistema.");

        var device = $"/dev/{diskName}";
        var partition = InputValidation.PartitionPath(diskName, 1);
        mountPoint ??= MountOptionsService.DefaultDiskMountPoint(label);

        validation = InputValidation.ValidateMountPoint(mountPoint);
        if (validation is not null)
            return OperationResult.Fail(validation);

        var mkfs = FormatCommandBuilder.Build(fileSystem, partition, label);

        var labelResult = await ShellHelper.RunRootAsync(
            "parted", new[] { "-s", device, "mklabel", "gpt" }, cancellationToken).ConfigureAwait(false);
        if (!labelResult.Success)
            return OperationResult.Fail($"No se pudo crear la tabla GPT: {labelResult.Stderr.Trim()}");

        var partResult = await ShellHelper.RunRootAsync(
            "parted", new[] { "-s", device, "mkpart", "primary", "0%", "100%" }, cancellationToken)
            .ConfigureAwait(false);
        if (!partResult.Success)
            return OperationResult.Fail($"No se pudo crear la partición: {partResult.Stderr.Trim()}");

        if (ToolLocator.IsAvailable("partprobe"))
            await ShellHelper.RunRootAsync("partprobe", new[] { device }, cancellationToken).ConfigureAwait(false);
        await SystemIntegrationService.SettleDevicesAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; attempt < 20 && !File.Exists(partition); attempt++)
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);

        if (!File.Exists(partition))
            return OperationResult.Fail($"La partición {partition} no apareció.");

        var format = await ShellHelper.RunRootAsync(mkfs.Tool, mkfs.Arguments, cancellationToken)
            .ConfigureAwait(false);
        if (!format.Success)
            return OperationResult.Fail($"Falló el formateo: {format.Stderr.Trim()}");

        await ShellHelper.RunRootAsync("mkdir", new[] { "-p", mountPoint }, cancellationToken)
            .ConfigureAwait(false);

        MountService.Unmount(mountPoint);
        if (!MountService.Mount(partition, mountPoint, "defaults", "775"))
            return OperationResult.Fail($"No se pudo montar {partition} en {mountPoint}.");

        return OperationResult.Ok(
            $"Disk initialized and mounted at {mountPoint}.\n\nFS: {fileSystem}\nLabel: {label}\nMount: {mountPoint}");
    }
}
