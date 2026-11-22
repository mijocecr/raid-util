using RAID_Util.Services;

namespace RAID_Util.Tests;

public class FstabServiceTests
{
    [Theory]
    [InlineData("md0", "/dev/md0")]
    [InlineData("/dev/md0", "/dev/md0")]
    [InlineData("sda1", "/dev/sda1")]
    [InlineData("nvme0n1p1", "/dev/nvme0n1p1")]
    [InlineData("/srv/data", "/srv/data")]
    public void DevicesAreNormalized(string input, string expected) =>
        Assert.Equal(expected, FstabService.NormalizeDevice(input));

    [Theory]
    [InlineData("/dev/md0", "md0")]
    [InlineData("/dev/md/host:md0", "md/host:md0")]
    [InlineData("md0", "md0")]
    public void BaseNameStripsDevPrefix(string input, string expected) =>
        Assert.Equal(expected, FstabService.BaseName(input));
}
