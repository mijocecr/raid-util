using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RAID_Util.Helpers;

internal enum PrivilegeToolKind
{
    None,
    Sudo,
    Doas
}

internal sealed record PrivilegeTool(PrivilegeToolKind Kind, string Path);

/// <summary>
/// Una sola sesión de privilegios para GUI y CLI: sondea `sudo -n` sin pedir
/// contraseña, la solicita una única vez cuando hace falta y ejecuta el resto de
/// comandos en modo no interactivo. Serializa además los comandos root para
/// evitar carreras sobre /etc/fstab y sobre el propio timestamp de sudo.
/// </summary>
public static class PrivilegeSession
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Lazy<PrivilegeTool> Tool = new(DetectTool);

    private static bool _validated;
    private static Func<Task<string?>>? _passwordPrompt;

    public static bool IsRoot => Environment.UserName == "root";

    /// <summary>
    /// Registra cómo pedir la contraseña (diálogo en GUI, lectura oculta en CLI).
    /// </summary>
    public static void SetPasswordPrompt(Func<Task<string?>> prompt) => _passwordPrompt = prompt;

    public static bool IsConfigured => IsRoot || _validated || _passwordPrompt is not null;

    public static void Invalidate()
    {
        _validated = false;
    }

    /// <summary>
    /// Elimina la referencia a la contraseña compartida cuando ya no se necesite.
    /// </summary>
    public static void ForgetPassword()
    {
        Credentials.AdminPassword = string.Empty;
        _validated = false;
    }

    public static async Task<CommandResult> RunRootAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureAsync(cancellationToken).ConfigureAwait(false);

            var tool = Tool.Value;
            var argv = arguments.ToArray();
            if (argv.Length == 0)
                return new CommandResult(1, "", "No se especificó ningún comando root.");

            if (tool.Kind == PrivilegeToolKind.None)
                return await CommandRunner.RunAsync(argv[0], argv.Skip(1), cancellationToken, timeout)
                    .ConfigureAwait(false);

            // -n nunca bloquea: si el timestamp caducó falla al instante y se reintenta
            // tras una única solicitud de contraseña.
            var result = await RunThroughToolAsync(tool, argv, cancellationToken, timeout)
                .ConfigureAwait(false);

            if (!result.Success && LooksLikePasswordRequired(result.Stderr))
            {
                Invalidate();
                await EnsureAsync(cancellationToken, forcePrompt: true).ConfigureAwait(false);
                result = await RunThroughToolAsync(tool, argv, cancellationToken, timeout)
                    .ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task<CommandResult> RunRootShellAsync(
        string command,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var shell = ToolLocator.Resolve("bash")
                    ?? ToolLocator.Resolve("sh")
                    ?? throw new InvalidOperationException("No se encontró bash ni sh en PATH.");

        return RunRootAsync(new[] { shell, "-c", CommandRewriter.Rewrite(command) }, cancellationToken, timeout);
    }

    private static Task<CommandResult> RunThroughToolAsync(
        PrivilegeTool tool,
        IReadOnlyList<string> argv,
        CancellationToken cancellationToken,
        TimeSpan? timeout)
    {
        // sudo admite -n (nunca pide contraseña); doas no tiene ese flag.
        // Con doas solo se llega aquí después de verificar NOPASSWD en EnsureAsync.
        var argvWithTool = tool.Kind == PrivilegeToolKind.Sudo
            ? new[] { "-n" }.Concat(argv)
            : argv;

        return CommandRunner.RunAsync(tool.Path, argvWithTool, cancellationToken, timeout);
    }

    private static async Task EnsureAsync(CancellationToken cancellationToken, bool forcePrompt = false)
    {
        if (IsRoot)
            return;

        var tool = Tool.Value;
        if (tool.Kind == PrivilegeToolKind.None)
            throw new InvalidOperationException("Se requieren privilegios de root.");

        if (_validated && !forcePrompt)
            return;

        if (!forcePrompt)
        {
            var probeArgs = tool.Kind == PrivilegeToolKind.Sudo
                ? new[] { "-n", "-v" }
                : new[] { "true" };

            // stdin vacío y timeout corto: si doas quiere contraseña, falla o se
            // cancela enseguida en vez de dejar un prompt colgado en segundo plano.
            var probe = await CommandRunner.RunAsync(
                tool.Path, probeArgs, cancellationToken, TimeSpan.FromSeconds(5),
                tool.Kind == PrivilegeToolKind.Doas ? string.Empty : null).ConfigureAwait(false);

            if (probe.Success)
            {
                _validated = true;
                return;
            }
        }

        if (tool.Kind != PrivilegeToolKind.Sudo)
            throw new InvalidOperationException(
                "sudo no está disponible. doas solo puede usarse con NOPASSWD para las " +
                "herramientas de RAID: no acepta contraseña por stdin y un prompt " +
                "interactivo bloquearía la GUI y la CLI. Configura doas con NOPASSWD o instala sudo.");

        var prompt = _passwordPrompt
                     ?? (() => Task.FromResult<string?>(Credentials.AdminPassword));

        var password = await prompt().ConfigureAwait(false);
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("No se proporcionó la contraseña de sudo.");

        try
        {
            var validation = await CommandRunner.RunAsync(
                tool.Path,
                new[] { "-S", "-v" },
                cancellationToken,
                TimeSpan.FromSeconds(20),
                password + "\n").ConfigureAwait(false);

            if (!validation.Success)
                throw new InvalidOperationException("La contraseña de sudo no es válida.");

            _validated = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            // La cadena es inmutable: al soltar la referencia se acorta su vida útil.
            password = null;
        }
    }

    private static bool LooksLikePasswordRequired(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return false;

        return stderr.Contains("password is required", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("a password is required", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("incorrect password", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("authentication failure", StringComparison.OrdinalIgnoreCase);
    }

    private static PrivilegeTool DetectTool()
    {
        if (IsRoot)
            return new PrivilegeTool(PrivilegeToolKind.None, string.Empty);

        var sudo = ToolLocator.Resolve("sudo");
        if (sudo is not null)
            return new PrivilegeTool(PrivilegeToolKind.Sudo, sudo);

        var doas = ToolLocator.Resolve("doas");
        if (doas is not null)
            return new PrivilegeTool(PrivilegeToolKind.Doas, doas);

        throw new InvalidOperationException(
            "No se encontró sudo ni doas. Instala sudo o doas para ejecutar operaciones de RAID.");
    }
}
