using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Tests;

[Collection("Environment")]
public class SafeDiskGuardTests : EnvironmentIsolation
{
    public SafeDiskGuardTests()
    {
        // SystemDiskDetector no debe encontrar particiones ni UUID críticos.
        WriteFakeTool("lsblk", "#!/bin/sh\necho '{\"blockdevices\":[]}'\n", executable: true);
        WriteFakeTool("blkid", "#!/bin/sh\nexit 0\n", executable: true);
    }

    [Fact]
    public void FilesystemBlocksCreationButNotWipe()
    {
        var disk = new RaidDiskInfo { Name = "sdb", FsType = "ext4" };

        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Modify));
        Assert.True(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Wipe));
    }

    [Fact]
    public void PartitionsBlockCreationButNotMemberRemoval()
    {
        var disk = new RaidDiskInfo { Name = "sdb", Children = new List<string> { "sdb1" } };

        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Modify));
        Assert.True(SafeDiskGuard.CanModify(disk, SafeDiskOperation.RemoveMember));
    }

    [Fact]
    public void SystemDiskIsAlwaysBlocked()
    {
        var disk = new RaidDiskInfo { Name = "sda", IsSystemDisk = true };

        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Modify));
        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Wipe));
        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.RemoveMember));
    }

    [Fact]
    public void MountedDiskIsAlwaysBlocked()
    {
        var disk = new RaidDiskInfo { Name = "sdb", MountPath = "/mnt/data" };

        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Modify));
        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Wipe));
        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.RemoveMember));
    }

    [Fact]
    public void RaidMemberBlocksWipeButNotRemoval()
    {
        var disk = new RaidDiskInfo
        {
            Name = "sdb",
            RaidMembership = RaidMembership.Active
        };

        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Modify));
        Assert.False(SafeDiskGuard.CanModify(disk, SafeDiskOperation.Wipe));
        Assert.True(SafeDiskGuard.CanModify(disk, SafeDiskOperation.RemoveMember));
    }
}
