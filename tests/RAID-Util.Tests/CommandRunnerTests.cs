using RAID_Util.Helpers;

namespace RAID_Util.Tests;

public class CommandRunnerTests
{
    private static string Shell => ToolLocator.ResolveOrThrow("sh");

    [Fact]
    public async Task CapturesStdoutStderrAndExitCode()
    {
        var result = await CommandRunner.RunAsync(
            Shell, new[] { "-c", "printf hello; printf warning >&2; exit 3" });

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Success);
        Assert.Equal("hello", result.Stdout);
        Assert.Equal("warning", result.Stderr);
    }

    [Fact]
    public async Task ArgumentsAreNotReinterpretedByAShell()
    {
        const string argument = "a b $HOME `id` 'quoted'";
        var result = await CommandRunner.RunAsync(
            Shell, new[] { "-c", "printf '%s' \"$1\"", "sh", argument });

        Assert.True(result.Success, result.Stderr);
        Assert.Equal(argument, result.Stdout);
    }

    [Fact]
    public async Task ForcesEnglishLocaleForParsableOutput()
    {
        var result = await CommandRunner.RunAsync(
            Shell, new[] { "-c", "printf '%s' \"$LC_ALL\"" });

        Assert.True(result.Success, result.Stderr);
        Assert.Equal("C", result.Stdout);
    }

    [Fact]
    public async Task TimeoutKillsTheProcess()
    {
        var result = await CommandRunner.RunAsync(
            Shell, new[] { "-c", "while :; do :; done" }, timeout: TimeSpan.FromMilliseconds(250));

        Assert.False(result.Success);
        Assert.Contains("Timeout", result.Stderr);
    }

    [Fact]
    public async Task CancellationRethrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CommandRunner.RunAsync(Shell, new[] { "-c", "while :; do :; done" }, cts.Token));
    }
}
