using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RAID_Util.Helpers;

namespace RAID_Util.Services;

/// <summary>
/// Adaptador por distribución: initramfs, udev y systemd se resuelven en tiempo
/// de ejecución. Si la herramienta no existe, la operación se registra y continúa.
/// </summary>
public static class SystemIntegrationService
{
    private static readonly (string Tool, string[] Arguments)[] InitramfsStrategies =
    {
        ("update-initramfs", new[] { "-u" }),          // Debian / Ubuntu / derivatives
        ("mkinitcpio",       new[] { "-P" }),          // Arch / Manjaro
        ("dracut",           new[] { "-f" }),          // Fedora / RHEL / openSUSE
        ("mkinitfs",         Array.Empty<string>()),   // Alpine
        ("mkinitrd",         Array.Empty<string>())    // openSUSE legado
    };

    /// <summary>
    /// Regenera el initramfs con la herramienta disponible en esta distro.
    /// Nunca lanza: un fallo aquí no debe impedir una operación de RAID ya exitosa.
    /// </summary>
    public static async Task RefreshInitramfsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (tool, arguments) in InitramfsStrategies)
        {
            if (!ToolLocator.IsAvailable(tool))
                continue;

            var result = await PrivilegeSession.RunRootAsync(Prepend(tool, arguments), cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
                LogService.Info($"[INITRAMFS] {tool} completado.");
            else
                LogService.Warn($"[INITRAMFS] {tool} falló ({result.ExitCode}): {result.Stderr.Trim()}");

            return;
        }

        LogService.Warn("[INITRAMFS] No se encontró generador de initramfs; " +
                        "si el array forma parte del root, actualízalo manualmente.");
    }

    public static async Task SettleDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (!ToolLocator.IsAvailable("udevadm"))
        {
            LogService.Debug("[UDEV] udevadm no disponible; se omite el esperado.");
            return;
        }

        var result = await PrivilegeSession.RunRootAsync(
            new[] { "udevadm", "settle" }, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        if (!result.Success)
            LogService.Warn($"[UDEV] udevadm settle falló: {result.Stderr.Trim()}");
    }

    public static async Task ReloadSystemdAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists("/run/systemd/system") || !ToolLocator.IsAvailable("systemctl"))
        {
            LogService.Debug("[SYSTEMD] systemd no está activo; se omite daemon-reload.");
            return;
        }

        var result = await PrivilegeSession.RunRootAsync(
            new[] { "systemctl", "daemon-reload" }, cancellationToken, TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);

        if (!result.Success)
            LogService.Warn($"[SYSTEMD] daemon-reload falló: {result.Stderr.Trim()}");
    }

    /// <summary>
    /// Indica si una herramienta opcional está presente, para ocultar acciones
    /// que no funcionarían en esta distro (mkfs.exfat, smartctl, sgdisk, ...).
    /// </summary>
    public static bool HasTool(string name) => ToolLocator.IsAvailable(name);

    private static IEnumerable<string> Prepend(string tool, IEnumerable<string> arguments)
    {
        yield return tool;
        foreach (var argument in arguments)
            yield return argument;
    }
}
