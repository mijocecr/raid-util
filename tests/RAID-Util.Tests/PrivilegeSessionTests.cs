using RAID_Util.Helpers;

namespace RAID_Util.Tests;

[Collection("Environment")]
public class PrivilegeSessionTests : EnvironmentIsolation
{
    [Fact]
    public async Task PasswordIsRequestedOnlyOnce()
    {
        if (PrivilegeSession.IsRoot)
            return;

        var marker = Path.Combine(TempDirectory, "sudo-validated");
        WriteFakeTool("sudo", FakeSudo(marker), executable: true);
        ToolLocator.Reset();
        PrivilegeSession.ResetForTests();

        var prompts = 0;
        PrivilegeSession.SetPasswordPrompt(() =>
        {
            prompts++;
            return Task.FromResult<string?>("secret");
        });

        var argv = new[] { ToolLocator.ResolveOrThrow("sh"), "-c", "printf ok" };

        var first = await PrivilegeSession.RunRootAsync(argv);
        Assert.True(first.Success, first.Stderr);
        Assert.Equal("ok", first.Stdout);
        Assert.Equal(1, prompts);

        var second = await PrivilegeSession.RunRootAsync(argv);
        Assert.True(second.Success, second.Stderr);
        Assert.Equal(1, prompts);
        Assert.Equal(string.Empty, Credentials.AdminPassword);
    }

    [Fact]
    public async Task NonInteractiveSudoDoesNotPrompt()
    {
        if (PrivilegeSession.IsRoot)
            return;

        WriteFakeTool("sudo", "#!/bin/sh\nexit 0\n", executable: true);
        ToolLocator.Reset();
        PrivilegeSession.ResetForTests();

        var prompts = 0;
        PrivilegeSession.SetPasswordPrompt(() =>
        {
            prompts++;
            return Task.FromResult<string?>("secret");
        });

        var result = await PrivilegeSession.RunRootAsync(
            new[] { ToolLocator.ResolveOrThrow("true") });

        Assert.True(result.Success, result.Stderr);
        Assert.Equal(0, prompts);
    }

    private static string FakeSudo(string marker) => $"""
        #!/bin/sh
        marker='{marker}'
        if [ "$1" = "-n" ] && [ "$2" = "-v" ]; then
            [ -f "$marker" ] && exit 0
            exit 1
        fi
        if [ "$1" = "-S" ] && [ "$2" = "-v" ]; then
            IFS= read -r password
            if [ "$password" = "secret" ]; then
                : > "$marker"
                exit 0
            fi
            exit 1
        fi
        if [ "$1" = "-n" ]; then
            shift
            exec "$@"
        fi
        exit 1
        """;
}
