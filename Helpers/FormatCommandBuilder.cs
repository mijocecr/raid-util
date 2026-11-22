using System;
using System.Collections.Generic;

namespace RAID_Util.Helpers;

/// <summary>
/// Construye comandos mkfs/mkswap como lista de argumentos: la etiqueta nunca
/// se interpola en una cadena de shell, con lo que desaparece la inyección root.
/// </summary>
public static class FormatCommandBuilder
{
    public static (string Tool, string[] Arguments) Build(string fileSystem, string device, string? label)
    {
        var error = InputValidation.ValidateDeviceName(device.StartsWith("/dev/") ? device[5..] : device);
        if (error is not null)
            throw new ArgumentException(error, nameof(device));

        error = InputValidation.ValidateLabel(label);
        if (error is not null)
            throw new ArgumentException(error, nameof(label));

        var args = new List<string>();
        string tool;

        switch (fileSystem.ToLowerInvariant())
        {
            case "ext4":
                tool = ToolLocator.ResolveOrThrow("mkfs.ext4");
                args.Add("-F");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-L"); args.Add(label); }
                break;
            case "xfs":
                tool = ToolLocator.ResolveOrThrow("mkfs.xfs");
                args.Add("-f");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-L"); args.Add(label); }
                break;
            case "btrfs":
                tool = ToolLocator.ResolveOrThrow("mkfs.btrfs");
                args.Add("-f");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-L"); args.Add(label); }
                break;
            case "f2fs":
                tool = ToolLocator.ResolveOrThrow("mkfs.f2fs");
                args.Add("-f");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-l"); args.Add(label); }
                break;
            case "vfat":
            case "fat32":
                tool = ToolLocator.ResolveOrThrow("mkfs.vfat");
                args.Add("-F"); args.Add("32");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-n"); args.Add(label); }
                break;
            case "exfat":
                tool = ToolLocator.ResolveOrThrow("mkfs.exfat");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-n"); args.Add(label); }
                break;
            case "ntfs":
                tool = ToolLocator.ResolveOrThrow("mkfs.ntfs");
                args.Add("-f");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-L"); args.Add(label); }
                break;
            case "swap":
                tool = ToolLocator.ResolveOrThrow("mkswap");
                if (!string.IsNullOrWhiteSpace(label)) { args.Add("-L"); args.Add(label); }
                break;
            default:
                throw new ArgumentException($"Filesystem no soportado: {fileSystem}", nameof(fileSystem));
        }

        args.Add(device);
        return (tool, args.ToArray());
    }

    /// <summary>
    /// Filesystems ofrecidos solo si la herramienta existe en esta distro.
    /// </summary>
    public static IEnumerable<string> AvailableFileSystems()
    {
        yield return "ext4";
        yield return "xfs";
        yield return "btrfs";
        yield return "swap";
        if (ToolLocator.IsAvailable("mkfs.f2fs")) yield return "f2fs";
        if (ToolLocator.IsAvailable("mkfs.vfat")) yield return "fat32";
        if (ToolLocator.IsAvailable("mkfs.exfat")) yield return "exfat";
        if (ToolLocator.IsAvailable("mkfs.ntfs")) yield return "ntfs";
    }
}
