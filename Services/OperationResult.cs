namespace RAID_Util.Services;

/// <summary>Resultado de una operación compartida entre GUI y CLI.</summary>
public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}
