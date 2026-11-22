using RAID_Util.Helpers;

namespace RAID_Util.Tests;

[Collection("Environment")]
public class FormatCommandBuilderTests : EnvironmentIsolation
{
    [Theory]
    [InlineData("ext4", "mkfs.ext4", "-F")]
    [InlineData("xfs", "mkfs.xfs", "-f")]
    [InlineData("btrfs", "mkfs.btrfs", "-f")]
    [InlineData("f2fs", "mkfs.f2fs", "-f")]
    [InlineData("vfat", "mkfs.vfat", "-F")]
    [InlineData("fat32", "mkfs.vfat", "-F")]
    [InlineData("exfat", "mkfs.exfat", null)]
    [InlineData("ntfs", "mkfs.ntfs", "-f")]
    [InlineData("swap", "mkswap", null)]
    public void BuildUsesSeparateArgumentsAndTheLocatedTool(
        string fileSystem, string tool, string? forceFlag)
    {
        WriteFakeTool(tool, "#!/bin/sh\nexit 0\n");
        ToolLocator.Reset();

        var (resolvedTool, arguments) = FormatCommandBuilder.Build(fileSystem, "/dev/sdb1", "data");

        Assert.EndsWith(tool, resolvedTool, StringComparison.Ordinal);
        if (forceFlag is not null)
            Assert.Equal(forceFlag, arguments[0]);

        var labelIndex = Array.IndexOf(arguments, "data");
        Assert.True(labelIndex >= 0);
        Assert.Equal("data", arguments[labelIndex]);
        Assert.Equal("/dev/sdb1", arguments[^1]);
        Assert.All(arguments, argument => Assert.False(argument.Contains(";", StringComparison.Ordinal)));
    }

    [Fact]
    public void LabelIsPassedWithTheToolSpecificFlag()
    {
        WriteFakeTool("mkfs.ext4", "#!/bin/sh\nexit 0\n");
        ToolLocator.Reset();

        var (_, arguments) = FormatCommandBuilder.Build("ext4", "/dev/sdb1", "data");

        Assert.Equal(new[] { "-F", "-L", "data", "/dev/sdb1" }, arguments);
    }

    [Fact]
    public void InvalidInputsAreRejectedBeforeBuilding()
    {
        WriteFakeTool("mkfs.ext4", "#!/bin/sh\nexit 0\n");
        ToolLocator.Reset();

        Assert.Throws<ArgumentException>(() =>
            FormatCommandBuilder.Build("ext4", "/dev/sdb;rm", "data"));
        Assert.Throws<ArgumentException>(() =>
            FormatCommandBuilder.Build("ext4", "/dev/sdb1", "bad label"));
        Assert.Throws<ArgumentException>(() =>
            FormatCommandBuilder.Build("reiserfs", "/dev/sdb1", "data"));
    }
}
