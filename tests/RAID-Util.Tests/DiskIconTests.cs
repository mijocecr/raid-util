using System.Text.Json;
using RAID_Util.Helpers;
using RAID_Util.Services;

namespace RAID_Util.Tests;

public class DiskIconTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("\"1\"", true)]
    [InlineData("\"true\"", true)]
    [InlineData("\"0\"", false)]
    [InlineData("null", false)]
    [InlineData("2", true)]
    public void RotaIsParsedRegardlessOfItsJsonType(string json, bool expected)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(expected, DiskTypeHelper.ParseRotational(doc.RootElement));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public void RotaTokenFromJsonNetIsParsed(object token, bool expected) =>
        Assert.Equal(expected, DiskTypeHelper.ParseRotational(token));

    [Fact]
    public void ToshibaHddWithBooleanRotaIsNotClassifiedAsSsd()
    {
        // Salida real de lsblk (util-linux 2.42.4): rota es booleano, no "1".
        const string json = """
        {"blockdevices":[{"name":"sda","type":"disk","size":"3,6T",
          "model":"TOSHIBA MQ04ABB400","serial":"X","rota":true,
          "fstype":null,"mountpoint":null,"pkname":null,"pttype":"gpt",
          "tran":"sata","children":null}]}
        """;

        var entry = Assert.Single(DiskService.ParseLsblk(json));

        Assert.True(entry.Rotational);
        Assert.Equal("sata", entry.Transport);
        Assert.EndsWith("disk-hdd.png", DiskIconService.GetIcon(null, entry.Model, entry.Rotational));
    }

    [Fact]
    public void LegacyStringRotaIsStillAccepted()
    {
        const string json = """
        {"blockdevices":[{"name":"sdb","type":"disk","size":"2T",
          "model":"TOSHIBA DT01ACA200","serial":"Y","rota":"1",
          "fstype":null,"mountpoint":null,"pkname":null,"pttype":"gpt",
          "tran":"sata","children":null}]}
        """;

        var entry = Assert.Single(DiskService.ParseLsblk(json));

        Assert.True(entry.Rotational);
        Assert.EndsWith("disk-hdd.png", DiskIconService.GetIcon(null, entry.Model, entry.Rotational));
    }

    [Theory]
    [InlineData(null, "TOSHIBA MQ04ABB400", true, "disk-hdd.png")]
    [InlineData(null, "Samsung SSD 860 EVO", false, "disk-ssd.png")]
    [InlineData("nvme", "SAMSUNG MZVLQ256HBJD-00BH1", false, "disk-nvme.png")]
    [InlineData("usb", "Kingston DataTraveler", true, "disk-virtual.png")]
    [InlineData("virtual", "QEMU HARDDISK", true, "disk-virtual.png")]
    public void IconMatchesTheDeviceType(string? hint, string model, bool rotational, string expected)
    {
        var uri = DiskIconService.GetIcon(hint, model, rotational);

        Assert.EndsWith(expected, uri);
        Assert.StartsWith("avares://RAID-Util/Assets/Icons/", uri);
    }
}
