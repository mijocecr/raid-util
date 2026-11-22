using RAID_Util.Helpers;

namespace RAID_Util.Tests;

[Collection("Environment")]
public class ToolAndRewriterTests : EnvironmentIsolation
{
    [Fact]
    public void ToolsAreResolvedFromPath()
    {
        var fake = WriteFakeTool("mdadm", "#!/bin/sh\nexit 0\n");
        ToolLocator.Reset();

        Assert.Equal(fake, ToolLocator.Resolve("mdadm"));
        Assert.True(ToolLocator.IsAvailable("mdadm"));
        Assert.Null(ToolLocator.Resolve("missing-raid-tool"));
    }

    [Fact]
    public void FixedPathsAreRewrittenToTheLocatedTool()
    {
        var fake = WriteFakeTool("mdadm", "#!/bin/sh\nexit 0\n");
        ToolLocator.Reset();

        var rewritten = CommandRewriter.Rewrite("/usr/sbin/mdadm --detail /dev/md0");

        Assert.True(rewritten.Contains(fake, StringComparison.Ordinal));
        Assert.False(rewritten.Contains("/usr/sbin/mdadm", StringComparison.Ordinal));
    }

    [Fact]
    public void BusyBoxDdLosesTheGnuOnlyStatusOption()
    {
        WriteFakeTool("dd", "#!/bin/sh\nprintf 'BusyBox v1.36.1\\n'\n", executable: true);
        ToolLocator.Reset();
        CommandRewriter.ResetForTests();

        var rewritten = CommandRewriter.Rewrite(
            "/usr/bin/dd if=/dev/zero of=/dev/sdb bs=1M status=none");

        Assert.False(rewritten.Contains("status=none", StringComparison.Ordinal));
    }
}
