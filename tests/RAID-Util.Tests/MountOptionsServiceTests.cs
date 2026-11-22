using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Tests;

public class MountOptionsServiceTests
{
    [Theory]
    [InlineData("md0", "md0")]
    [InlineData("/dev/md0", "md0")]
    [InlineData("/dev/md/host:md0", "md0")]
    [InlineData("/dev/md/imsm0", "imsm0")]
    public void ArrayKeyIgnoresDevPathAndContainerName(string input, string expected) =>
        Assert.Equal(expected, MountOptionsService.ArrayKey(input));

    [Fact]
    public void ArrayMountPointUsesTheCanonicalBaseName()
    {
        Assert.Equal("/mnt/md0", MountOptionsService.DefaultArrayMountPoint("/dev/md/host:md0"));
        Assert.Equal("/mnt/md0", MountOptionsService.EffectiveArrayMountPoint("md0", null));
        Assert.Equal("/srv/raid", MountOptionsService.EffectiveArrayMountPoint("md0", " /srv/raid "));
    }

    [Fact]
    public void DiskMountPointUsesCurrentUser()
    {
        var mountPoint = MountOptionsService.DefaultDiskMountPoint("data");
        Assert.Equal($"/run/media/{Environment.UserName}/data", mountPoint);
    }

    [Fact]
    public void BuildOptionsReflectsConfiguration()
    {
        var config = new ArrayConfig();
        Assert.Equal("defaults", MountOptionsService.BuildOptions(config));

        config.Mount_NoAtime = true;
        config.Mount_Discard = true;
        config.Mount_ReadOnly = true;
        Assert.Equal("noatime,discard,ro", MountOptionsService.BuildOptions(config));

        config.Mount_NoAtime = false;
        config.Mount_Discard = false;
        config.Mount_ReadOnly = false;
        config.Mount_NoDirAtime = true;
        config.Mount_Sync = true;
        Assert.Equal("nodiratime,sync", MountOptionsService.BuildOptions(config));
    }

    [Theory]
    [InlineData("755", "755")]
    [InlineData(" 700 ", "700")]
    [InlineData("99", "755")]
    [InlineData("888", "755")]
    [InlineData(null, "755")]
    public void PermissionsFallBackToTheDefault(string? value, string expected) =>
        Assert.Equal(expected, MountOptionsService.NormalizePermissions(value));

    [Fact]
    public void MountPointValidationIsShared()
    {
        Assert.Null(MountOptionsService.ValidateMountPoint("/mnt/md0"));
        Assert.NotNull(MountOptionsService.ValidateMountPoint("/mnt/bad name"));
    }
}
