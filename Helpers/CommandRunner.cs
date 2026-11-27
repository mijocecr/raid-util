using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RAID_Util.Helpers;

/// <summary>
/// Ejecución de procesos sin intérprete de comandos implícito, con salida leída
/// concurrentemente, timeout y cancelación. Base común para GUI y CLI.
/// </summary>
public static class CommandRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ejecuta un programa con argumentos separados (sin <c>bash -c</c>).
    /// </summary>
    public static async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        string? standardInput = null)
    {
        var callId = Interlocked.Increment(ref _callCount);
        var stopwatch = Stopwatch.StartNew();
        var effectiveTimeout = timeout ?? DefaultTimeout;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        // Salida determinista: el parseo de mdadm/smartctl/blkid está en inglés.
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";
        psi.Environment["LANGUAGE"] = "C";
        psi.Environment["PATH"] = ExpandedPath();

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            using var registration = cancellationToken.Register(() => KillSafely(process));

            // Hay que empezar a leer ANTES de esperar: si stderr llena el pipe,
            // WaitForExit + ReadToEnd secuenciales se bloquean (deadlock clásico).
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var completion = Task.WhenAll(stdoutTask, stderrTask);

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var delay = Task.Delay(effectiveTimeout, cancellationToken);
            var finished = await Task.WhenAny(completion, delay).ConfigureAwait(false);
            if (finished != completion)
            {
                cancellationToken.ThrowIfCancellationRequested();
                KillSafely(process);
                try
                {
                    await completion.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // El árbol de procesos puede haber dejado pipes abiertos.
                }

                return new CommandResult(1, stdoutTask.Status == TaskStatus.RanToCompletion ? stdoutTask.Result : "",
                    $"Timeout tras {effectiveTimeout.TotalSeconds:0} s ejecutando {fileName}");
            }

            await completion.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var result = new CommandResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
            Log(callId, fileName, string.Join(' ', arguments), result, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
        {
            KillSafely(process);
            throw;
        }
        catch (Exception ex)
        {
            return new CommandResult(1, "", ex.Message);
        }
    }

    /// <summary>
    /// Ejecuta una línea de comandos con un intérprete. Se mantiene para las rutas
    /// heredadas que aún usan tuberías/redirecciones; el intérprete se resuelve por
    /// PATH y la línea se pasa como UN solo argumento (sin doble escapado).
    /// </summary>
    public static Task<CommandResult> RunShellAsync(
        string command,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var shell = ToolLocator.Resolve("bash")
                    ?? ToolLocator.Resolve("sh")
                    ?? throw new InvalidOperationException("No se encontró bash ni sh en PATH.");

        return RunAsync(shell, new[] { "-c", CommandRewriter.Rewrite(command) }, cancellationToken, timeout);
    }

    private static long _callCount;

    private static string ExpandedPath()
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        var parts = new List<string>(current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var directory in new[] { "/usr/local/sbin", "/usr/sbin", "/sbin" })
            if (!parts.Contains(directory, StringComparer.Ordinal))
                parts.Add(directory);
        return string.Join(Path.PathSeparator, parts);
    }

    private static void KillSafely(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // El proceso ya terminó o no es matable desde este usuario.
        }
    }

    private static void Log(long callId, string fileName, string arguments, CommandResult result, long elapsedMs)
    {
        DebugConsole.WriteLine($"[CMD] #{callId} {fileName} {arguments}");
        DebugConsole.WriteLine($"[CMD] #{callId} EXIT={result.ExitCode} ({elapsedMs} ms)");
        if (!string.IsNullOrWhiteSpace(result.Stdout))
            DebugConsole.WriteLine($"[CMD] #{callId} STDOUT:\n{result.Stdout}");
        if (!string.IsNullOrWhiteSpace(result.Stderr))
            DebugConsole.WriteLine($"[CMD] #{callId} STDERR:\n{result.Stderr}");
    }
}
