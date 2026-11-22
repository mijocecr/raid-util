namespace RAID_Util.Helpers;

/// <summary>
/// Resultado de una ejecución de proceso, independiente del intérprete de comandos usado.
/// </summary>
public sealed record CommandResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}
