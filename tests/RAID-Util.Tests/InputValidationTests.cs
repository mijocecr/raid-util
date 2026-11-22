using RAID_Util.Helpers;

namespace RAID_Util.Tests;

public class InputValidationTests
{
    [Theory]
    [InlineData("data")]
    [InlineData("a.b-c_d")]
    [InlineData("ARRAY1")]
    public void ValidLabelsAreAccepted(string label) =>
        Assert.Null(InputValidation.ValidateLabel(label, allowEmpty: false));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad label")]
    [InlineData("label;rm")]
    [InlineData("12345678901234567")]
    public void InvalidLabelsAreRejected(string label) =>
        Assert.NotNull(InputValidation.ValidateLabel(label, allowEmpty: false));

    [Fact]
    public void EmptyLabelIsAllowedOnlyWhenRequested()
    {
        Assert.Null(InputValidation.ValidateLabel("", allowEmpty: true));
        Assert.NotNull(InputValidation.ValidateLabel("", allowEmpty: false));
    }

    [Theory]
    [InlineData("sda")]
    [InlineData("nvme0n1")]
    [InlineData("mmcblk0p1")]
    public void ValidDeviceNamesAreAccepted(string name) =>
        Assert.Null(InputValidation.ValidateDeviceName(name));

    [Theory]
    [InlineData("")]
    [InlineData("/dev/sda")]
    [InlineData("sda;rm -rf /")]
    [InlineData("sda$HOME")]
    public void InvalidDeviceNamesAreRejected(string name) =>
        Assert.NotNull(InputValidation.ValidateDeviceName(name));

    [Fact]
    public void DevicePathsMustStartWithDev()
    {
        Assert.Null(InputValidation.ValidateDevicePath("/dev/nvme0n1"));
        Assert.NotNull(InputValidation.ValidateDevicePath("/tmp/sda"));
    }

    [Fact]
    public void DevicePathsAreNormalizedWithoutDuplication()
    {
        Assert.Equal("/dev/sdb", InputValidation.NormalizeDevicePath("sdb"));
        Assert.Equal("/dev/sdb", InputValidation.NormalizeDevicePath("/dev/sdb"));
    }

    [Theory]
    [InlineData("/mnt/md0", false)]
    [InlineData("/run/media/user/data", false)]
    [InlineData("relative/path", true)]
    [InlineData("/mnt/with space", true)]
    [InlineData("/mnt/$HOME", true)]
    [InlineData("/mnt/`id`", true)]
    [InlineData("/mnt/\"quoted\"", true)]
    public void MountPointsAreChecked(string path, bool invalid)
    {
        var error = InputValidation.ValidateMountPoint(path);
        Assert.Equal(invalid, error is not null);
    }

    [Theory]
    [InlineData("sda", 1, "/dev/sda1")]
    [InlineData("nvme0n1", 2, "/dev/nvme0n1p2")]
    [InlineData("mmcblk0", 3, "/dev/mmcblk0p3")]
    [InlineData("vdaf", 1, "/dev/vdaf1")]
    public void PartitionPathsUseTheRightSuffix(string disk, int number, string expected) =>
        Assert.Equal(expected, InputValidation.PartitionPath(disk, number));
}
