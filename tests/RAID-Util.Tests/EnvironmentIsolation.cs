using RAID_Util.Helpers;

namespace RAID_Util.Tests;

/// <summary>Serializa los tests que cambian PATH o estado estático global.</summary>
[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;

/// <summary>
/// Aísla PATH y el estado estático de los helpers durante un test. Todo lo que
/// cambia aquí se restaura en Dispose, incluso si el test falla.
/// </summary>
public abstract class EnvironmentIsolation : IDisposable
{
    private readonly string _originalPath;

    protected string TempDirectory { get; }

    protected EnvironmentIsolation()
    {
        _originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        TempDirectory = Path.Combine(
            Path.GetTempPath(), "raid-util-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);

        Environment.SetEnvironmentVariable("PATH", TempDirectory + Path.PathSeparator + _originalPath);
        ResetState();
    }

    protected string WriteFakeTool(string name, string body, bool executable = false)
    {
        var path = Path.Combine(TempDirectory, name);
        File.WriteAllText(path, body);
        if (executable && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static void ResetState()
    {
        ToolLocator.Reset();
        CommandRewriter.ResetForTests();
        PrivilegeSession.ResetForTests();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        ResetState();
        try
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Un proceso hijo podría seguir usando el directorio; no es grave.
        }
    }
}
