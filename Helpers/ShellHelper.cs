using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RAID_Util.Helpers;

/// <summary>
/// Compatibilidad con las firmas históricas. Toda la ejecución pasa ahora por
/// <see cref="CommandRunner"/> y <see cref="PrivilegeSession"/>: sin rutas fijas,
/// sin doble escapado y sin lecturas de pipes después de WaitForExit.
/// </summary>
public static class ShellHelper
{
    public static (int ExitCode, string Stdout, string Stderr) EjecutarSinRoot(string command)
    {
        var result = CommandRunner.RunShellAsync(command).GetAwaiter().GetResult();
        return (result.ExitCode, result.Stdout, result.Stderr);
    }

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command)
    {
        var result = PrivilegeSession.RunRootShellAsync(command).GetAwaiter().GetResult();
        return (result.ExitCode, result.Stdout, result.Stderr);
    }

    public static async Task<string> RunCleanAsync(string command)
    {
        var result = await CommandRunner.RunShellAsync(command).ConfigureAwait(false);
        return (result.Stdout + "\n" + result.Stderr).Trim();
    }

    /// <summary>
    /// API preferida para código nuevo: argumentos separados, sin intérprete.
    /// </summary>
    public static CommandResult Run(string fileName, params string[] arguments) =>
        CommandRunner.RunAsync(fileName, arguments).GetAwaiter().GetResult();

    public static Task<CommandResult> RunAsync(
        string fileName, string[] arguments, CancellationToken cancellationToken = default) =>
        CommandRunner.RunAsync(fileName, arguments, cancellationToken);

    public static CommandResult RunRoot(string fileName, params string[] arguments) =>
        RunRoot(fileName, arguments, standardInput: null);

    public static CommandResult RunRoot(
        string fileName, string[] arguments, string? standardInput) =>
        PrivilegeSession.RunRootAsync(arguments.Prepend(fileName), standardInput: standardInput)
            .GetAwaiter().GetResult();

    public static Task<CommandResult> RunRootAsync(
        string fileName,
        string[] arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        string? standardInput = null) =>
        PrivilegeSession.RunRootAsync(arguments.Prepend(fileName), cancellationToken, timeout, standardInput);

    public static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var opener = ToolLocator.Resolve("xdg-open") ?? ToolLocator.Resolve("gio");
            if (opener is null)
                return;

            var arguments = Path.GetFileName(opener).Equals("gio", StringComparison.OrdinalIgnoreCase)
                ? new[] { "open", path }
                : new[] { path };

            _ = CommandRunner.RunAsync(opener, arguments, timeout: TimeSpan.FromSeconds(15));
        }
        catch (Exception)
        {
            // Abrir el visor nunca debe impedir la operación que lo originó.
        }
    }
}
