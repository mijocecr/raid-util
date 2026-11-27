using RAID_Util.Helpers;

namespace RAID_Util.Tests;

public class MdstatParserTests
{
    private const string TwoArrays = """
        Personalities : [raid1]
        md0 : active raid1 sda1[0] sdb1[1]
              976630336 blocks super 1.2 [2/2] [UU]

        md1 : active raid1 sdc1[0] sdd1[1]
              976630336 blocks super 1.2 [2/1] [U_]
              resync=DELAYED

        unused devices: <none>
        """;

    [Fact]
    public void MissingMdstatIsNotReportedAsDegraded()
    {
        Assert.Equal("", MdstatParser.GetArrayLine("", "md0"));
        Assert.False(MdstatParser.IsDegradedLine(MdstatParser.GetArrayLine("", "md0")));
        Assert.False(MdstatParser.IsOperationInProgress("", "md0"));
    }

    [Fact]
    public void ArrayNameWithUnderscoreIsNotDegradedByItsName()
    {
        // md_d0 era marcado como degradado solo por contener '_'.
        const string mdstat = "md_d0 : active raid1 sda1[0] sdb1[1]\n      976630336 blocks super 1.2 [2/2] [UU]\n";

        var line = MdstatParser.GetArrayLine(mdstat, "md_d0");

        Assert.NotEmpty(line);
        Assert.False(MdstatParser.IsDegradedLine(line));
    }

    [Fact]
    public void DegradedMemberIsDetectedInItsOwnArray()
    {
        var line = MdstatParser.GetArrayLine(TwoArrays, "md1");

        Assert.Contains("md1 :", line);
        Assert.True(MdstatParser.IsDegradedLine(line));
    }

    [Fact]
    public void HealthyArrayIsNotAffectedBySiblingArray()
    {
        var md0 = MdstatParser.GetArrayLine(TwoArrays, "md0");
        var md1 = MdstatParser.GetArrayLine(TwoArrays, "md1");

        Assert.False(MdstatParser.IsDegradedLine(md0));
        Assert.True(MdstatParser.IsDegradedLine(md1));
    }

    [Fact]
    public void ResyncOfAnotherArrayDoesNotMarkThisArrayAsResyncing()
    {
        Assert.False(MdstatParser.IsOperationInProgress(TwoArrays, "md0"));
    }

    [Fact]
    public void RealProgressLineIsScopedAndParsed()
    {
        const string mdstat = """
            md0 : active raid1 sda1[0] sdb1[1]
                  976630336 blocks super 1.2 [2/2] [UU]
                  [=>...................]  resync =  9.4% (91872320/976630336) finish=88.2min speed=17362K/sec

            md1 : active raid1 sdc1[0] sdd1[1]
                  976630336 blocks super 1.2 [2/2] [UU]
            """;

        var line = MdstatParser.GetProgressLine(mdstat, "md0");

        Assert.Contains("resync =", line);
        Assert.Equal(9, MdstatParser.ParseProgressPercent(line));
        Assert.False(MdstatParser.IsOperationInProgress(mdstat, "md1"));
    }

    [Fact]
    public void ResyncAt99PercentIsStillInProgress()
    {
        const string mdstat = """
            md0 : active raid1 sda1[0] sdb1[1]
                  976630336 blocks super 1.2 [2/2] [UU]
                  [===================.]  resync = 99.9% (976532735/976630336) finish=0.0min speed=200000K/sec
            """;

        // Antes se normalizaba a "terminado" solo por tener progreso < 100.
        Assert.True(MdstatParser.IsOperationInProgress(mdstat, "md0"));
        Assert.Equal(100, MdstatParser.ParseProgressPercent(MdstatParser.GetProgressLine(mdstat, "md0")));
    }

    [Fact]
    public void ResyncAt100PercentIsNoLongerInProgress()
    {
        const string mdstat = """
            md0 : active raid1 sda1[0] sdb1[1]
                  976630336 blocks super 1.2 [2/2] [UU]
                  [====================]  resync = 100% (976630336/976630336) finish=0.0min speed=200000K/sec
            """;

        Assert.False(MdstatParser.IsOperationInProgress(mdstat, "md0"));
    }

    [Fact]
    public void ArrayNamesAreParsedFromHeadersOnly()
    {
        var names = MdstatParser.ParseArrays(TwoArrays);

        Assert.Equal(new[] { "md0", "md1" }, names);
    }

    [Fact]
    public void NormalizeNameAcceptsDevPathAndUuidSuffix()
    {
        Assert.Equal("md0", MdstatParser.NormalizeName("/dev/md0"));
        Assert.Equal("md0", MdstatParser.NormalizeName("md0:0"));
    }
}
