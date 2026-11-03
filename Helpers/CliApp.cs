using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Helpers
{
    public static class CliApp
    {
        public static async Task Run()
        {
            var config = ConfigManager.Load();
            LogService.ApplyConfig(config);

            EnsureCredentials();

            while (true)
            {
                Console.Clear();
                Console.WriteLine("===== RAID-Util CLI =====\n");
                Console.WriteLine("1) System overview");
                Console.WriteLine("2) Manage RAID arrays");
                Console.WriteLine("3) Manage physical disks");
                Console.WriteLine("4) View application logs");
                Console.WriteLine("5) Application settings");
                Console.WriteLine("0) Exit");
                Console.WriteLine("==========================\n");

                switch (ReadMenuChoice(5))
                {
                    case 1: await ShowDashboard(); break;
                    case 2: await ManageArrays(); break;
                    case 3: await ManageDisks(); break;
                    case 4: await ViewLogs(); break;
                    case 5: ManageSettings(); break;
                    case 0: return;
                }

                Pause();
            }
        }

        private static int ReadMenuChoice(int maxOption)
        {
            Console.Write($"Select an option (0-{maxOption}): ");
            return int.TryParse(Console.ReadLine(), out var choice) &&
                   choice >= 0 && choice <= maxOption
                ? choice
                : -1;
        }

        private static int? SelectIndex<T>(IReadOnlyList<T> items, Func<T, string> describe, string prompt)
        {
            for (var i = 0; i < items.Count; i++)
                Console.WriteLine($"{i + 1}) {describe(items[i])}");

            Console.WriteLine("0) Back");
            Console.Write($"{prompt}: ");

            if (!int.TryParse(Console.ReadLine(), out var selected) ||
                selected < 0 || selected > items.Count)
            {
                Console.WriteLine("Invalid selection.");
                return null;
            }

            return selected == 0 ? null : selected - 1;
        }

        private static bool Confirm(string prompt)
        {
            Console.WriteLine($"\n{prompt}");
            Console.WriteLine("1) Yes");
            Console.WriteLine("2) No");
            Console.Write("Select an option: ");
            return Console.ReadLine()?.Trim() == "1";
        }

        private static void Pause()
        {
            Console.WriteLine("\nPress ENTER to continue...");
            Console.ReadLine();
        }

        private static string ReadValue(string prompt, string currentValue)
        {
            Console.Write($"{prompt} [{currentValue}]: ");
            var value = Console.ReadLine()?.Trim();
            return string.IsNullOrEmpty(value) ? currentValue : value;
        }

        private static string DescribeArray(RaidArrayInfo array) =>
            $"{array.Path} | {array.Level} | {array.State} | {array.Disks.Count} disks | " +
            $"{array.TotalSize} | {(array.IsMounted ? $"mounted at {array.MountPath}" : "not mounted")}";

        private static string DescribeDisk(RaidDiskInfo disk) =>
            $"/dev/{disk.Name} | {disk.Size} | {disk.Model} | " +
            $"{(disk.IsRotational ? "HDD" : "SSD")}{(disk.IsSystemDisk ? " | SYSTEM DISK" : "")} | " +
            $"{(disk.IsMounted ? $"mounted at {disk.MountPath}" : "not mounted")} | RAID: {disk.RaidMembership}";

        private static async Task<List<RaidArrayInfo>> GetArraysAsync()
        {
            return await RaidService.Instance.GetArraysAsync();
        }

        private static async Task ShowDashboard()
        {
            Console.Clear();
            Console.WriteLine("=== System overview ===\n");

            var status = new StatusService();
            try
            {
                Console.WriteLine($"RAID health: {await status.GetOverallRaidHealthAsync()}");
                Console.WriteLine($"Arrays:      {await status.GetArraysSummaryAsync()}");
                Console.WriteLine($"Disks:       {await status.GetDisksSummaryAsync()}");
                Console.WriteLine($"Rebuild:     {await status.GetRebuildSummaryAsync()}");
                Console.WriteLine($"Uptime:      {status.GetSessionUptime()}");
                Console.WriteLine($"CPU:         {status.GetCpuUsage()}%");
                Console.WriteLine($"RAM:         {status.GetMemoryUsage()}%");
                Console.WriteLine($"Disk free:   {status.GetDiskFree()} GB");

                var atRisk = await status.GetArraysAtRiskAsync();
                Console.WriteLine("\nArrays at risk:");
                if (atRisk.Count == 0)
                    Console.WriteLine("  None");
                else
                    foreach (var item in atRisk)
                        Console.WriteLine($"  {item.Name}: {item.Status} — {item.Details}");

                var alerts = await status.GetDiskAlertsAsync();
                Console.WriteLine("\nDisk alerts:");
                if (alerts.Count == 0)
                    Console.WriteLine("  None");
                else
                    foreach (var item in alerts)
                        Console.WriteLine($"  {item.Device}: {item.Alert}");

                var events = await status.GetRecentEventsAsync();
                Console.WriteLine("\nRecent events:");
                foreach (var item in events)
                    Console.WriteLine($"  {item}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not load system overview: {ex.Message}");
            }
        }

        private static async Task ManageArrays()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== RAID arrays ===\n");
                Console.WriteLine("1) Select an array");
                Console.WriteLine("2) Create an array");
                Console.WriteLine("3) Assemble stopped arrays");
                Console.WriteLine("4) Mount an array");
                Console.WriteLine("5) Unmount an array");
                Console.WriteLine("6) List arrays (mdadm scan)");
                Console.WriteLine("0) Back");

                switch (ReadMenuChoice(6))
                {
                    case 1:
                    {
                        var arrays = await GetArraysAsync();
                        var index = SelectIndex(arrays, DescribeArray, "Select an array");
                        if (index.HasValue)
                            await ManageArray(arrays[index.Value]);
                        break;
                    }
                    case 2: await CreateArrayFromInventory(); break;
                    case 3:
                    {
                        var ok = await Task.Run(() => RaidService.Instance.AutoAssemble());
                        Console.WriteLine(ok ? "Arrays assembled." : "Could not assemble stopped arrays.");
                        break;
                    }
                    case 4: await MountSelectedArray(); break;
                    case 5: await UnmountSelectedArray(); break;
                    case 6: await ListArrays(); break;
                    case 0: return;
                    default: Console.WriteLine("Invalid option."); break;
                }

                Pause();
            }
        }

        private static async Task ManageArray(RaidArrayInfo array)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine($"=== RAID array: {DescribeArray(array)} ===\n");
                Console.WriteLine("1) Details");
                Console.WriteLine("2) Initialize / format");
                Console.WriteLine("3) Configure array");
                Console.WriteLine("4) Start resync / rebuild");
                Console.WriteLine("5) Force consistency check");
                Console.WriteLine("6) Force repair");
                Console.WriteLine("7) Add disk");
                Console.WriteLine("8) Manage member disks");
                Console.WriteLine("9) Expand / reshape");
                Console.WriteLine("10) Toggle persistent mount");
                Console.WriteLine("11) Stop array");
                Console.WriteLine("12) Delete array");
                Console.WriteLine("0) Back");

                switch (ReadMenuChoice(12))
                {
                    case 1:
                        Console.WriteLine(await RaidService.Instance.GetArrayDetailsAsync(array.Name));
                        break;
                    case 2: await InitializeArray(array); break;
                    case 3: ConfigureArray(array); break;
                    case 4: await StartResync(array); break;
                    case 5: await CheckArray(array); break;
                    case 6: await RepairArray(array); break;
                    case 7: await AddDiskToArray(array); break;
                    case 8: await ManageArrayDisks(array); break;
                    case 9: ExpandArray(array); break;
                    case 10: await TogglePersistentMount(array); break;
                    case 11:
                        await StopSelectedArray(array);
                        Pause();
                        return;
                    case 12:
                        await DeleteSelectedArray(array);
                        Pause();
                        return;
                    case 0: return;
                    default: Console.WriteLine("Invalid option."); break;
                }

                Pause();
            }
        }

        private static async Task CreateArrayFromInventory()
        {
            var disks = (await RaidService.Instance.GetAllDisksAsync())
                .Where(d =>
                    (d.RaidMembership == RaidMembership.None ||
                     d.Role.Equals("removed", StringComparison.OrdinalIgnoreCase)) &&
                    !d.IsMounted && d.Children.Count == 0 && !d.IsSystemDisk)
                .ToList();

            if (disks.Count == 0)
            {
                Console.WriteLine("No eligible disks are available.");
                return;
            }

            var levels = new List<string> { "RAID0", "RAID1", "RAID5", "RAID6", "RAID10" };
            if (RaidService.Instance.KernelSupportsLinear())
                levels.Insert(0, "JBOD (Linear)");

            var levelIndex = SelectIndex(levels, level => level, "Select RAID level");
            if (!levelIndex.HasValue)
                return;

            var level = levels[levelIndex.Value] == "JBOD (Linear)" ? "linear" : levels[levelIndex.Value];
            var selected = new List<RaidDiskInfo>();

            while (true)
            {
                Console.WriteLine("\nSelected disks:");
                if (selected.Count == 0)
                    Console.WriteLine("  None");
                else
                    foreach (var disk in selected)
                        Console.WriteLine($"  {DescribeDisk(disk)}");

                Console.WriteLine("\nChoose a disk to add:");
                var available = disks.Where(d => !selected.Contains(d)).ToList();
                for (var i = 0; i < available.Count; i++)
                    Console.WriteLine($"{i + 1}) {DescribeDisk(available[i])}");
                Console.WriteLine("0) Finish disk selection");
                Console.Write("Select a disk: ");

                if (!int.TryParse(Console.ReadLine(), out var choice) || choice < 0 || choice > available.Count)
                {
                    Console.WriteLine("Invalid selection.");
                    continue;
                }

                if (choice == 0)
                    break;

                selected.Add(available[choice - 1]);
            }

            var minimumDisks = level switch
            {
                "linear" or "RAID0" or "RAID1" => 2,
                "RAID5" => 3,
                "RAID6" or "RAID10" => 4,
                _ => int.MaxValue
            };

            if (selected.Count < minimumDisks ||
                (level == "RAID10" && selected.Count % 2 != 0))
            {
                Console.WriteLine(level == "RAID10"
                    ? "RAID10 requires at least four disks and an even disk count."
                    : $"{level} requires at least {minimumDisks} disks.");
                return;
            }

            var friendlyName = ReadValue("Friendly array name (optional)", "");
            if (!string.IsNullOrWhiteSpace(friendlyName) &&
                friendlyName.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            {
                Console.WriteLine("The friendly name may contain only letters, numbers, '-' and '_'.");
                return;
            }

            Console.WriteLine($"\nArray type: {level}");
            Console.WriteLine("Selected devices:");
            foreach (var disk in selected)
                Console.WriteLine($"  {DescribeDisk(disk)}");

            if (!Confirm("Creating this array writes RAID metadata to the selected disks. Continue?"))
                return;

            var created = await Task.Run(() =>
                RaidService.Instance.CreateArray(level, selected, friendlyName));
            Console.WriteLine(created
                ? $"Array created: /dev/{RaidService.Instance.LastCreatedMdName}"
                : "Array creation failed. Check the application logs.");
        }

        private static async Task MountSelectedArray()
        {
            var arrays = await GetArraysAsync();
            var index = SelectIndex(arrays, DescribeArray, "Select an array to mount");
            if (!index.HasValue)
                return;

            var array = arrays[index.Value];
            var config = ArrayConfigService.Load(array.BaseName);
            var defaultPath = string.IsNullOrWhiteSpace(config.MountPoint)
                ? $"/mnt/{array.Name}"
                : config.MountPoint;
            var mountPath = ReadValue("Mount point", defaultPath);
            if (!Path.IsPathRooted(mountPath) || mountPath.Any(c =>
                    !char.IsAsciiLetterOrDigit(c) && c is not '/' and not '.' and not '_' and not '-'))
            {
                Console.WriteLine(
                    "Mount point must be an absolute path using only letters, numbers, '/', '.', '_' and '-'.");
                return;
            }

            var mounted = await Task.Run(() =>
                MountService.Mount(array.Path, mountPath, BuildMountOptions(config)));
            Console.WriteLine(mounted
                ? $"Mounted {array.Path} at {mountPath}."
                : $"Could not mount {array.Path}. Check that it has a filesystem and review the logs.");
        }

        private static async Task UnmountSelectedArray()
        {
            var arrays = await GetArraysAsync();
            var mounted = arrays.Where(a => a.IsMounted).ToList();
            var index = SelectIndex(mounted, DescribeArray, "Select an array to unmount");
            if (!index.HasValue)
                return;

            var array = mounted[index.Value];
            if (!Confirm($"Unmount {array.Path} from {array.MountPath}?"))
                return;

            Console.WriteLine(MountService.Unmount(array.MountPath ?? "")
                ? "Array unmounted."
                : "Could not unmount the array.");
        }

        private static async Task InitializeArray(RaidArrayInfo array)
        {
            var filesystems = new List<string> { "ext4", "xfs", "btrfs", "swap" };
            if (File.Exists("/usr/bin/mkfs.f2fs") || File.Exists("/usr/sbin/mkfs.f2fs") ||
                File.Exists("/sbin/mkfs.f2fs") || File.Exists("/bin/mkfs.f2fs"))
                filesystems.Add("f2fs");

            var fsIndex = SelectIndex(filesystems, fs => fs, "Select filesystem");
            if (!fsIndex.HasValue)
                return;

            var fs = filesystems[fsIndex.Value];
            var label = ReadValue("Filesystem label (optional)", "");
            Console.WriteLine($"\nTarget: {DescribeArray(array)}");
            Console.WriteLine($"Filesystem: {fs} | Label: {(string.IsNullOrWhiteSpace(label) ? "(none)" : label)}");
            if (!Confirm("Formatting permanently destroys data on this array. Continue?"))
                return;

            var ok = await RaidService.Instance.InitializeArrayAsync(array.Path, fs, label);
            Console.WriteLine(ok ? "Array initialized." : "Array initialization failed. Check the logs.");
        }

        private static async Task StartResync(RaidArrayInfo array)
        {
            if (!array.SupportsRepair || array.State == RaidArrayState.Failed ||
                array.IsResyncing || array.IsRecovering)
            {
                Console.WriteLine("This array cannot start a resync in its current state.");
                return;
            }

            if (!Confirm($"Start resync / rebuild on {DescribeArray(array)}?"))
                return;

            Console.WriteLine(await RaidService.Instance.StartArrayResyncAsync(array.Name)
                ? "Resync / rebuild started."
                : "Could not start resync / rebuild.");
        }

        private static async Task CheckArray(RaidArrayInfo array)
        {
            if (!array.SupportsCheck || !array.IsMounted || array.IsResyncing || array.IsRecovering)
            {
                Console.WriteLine("A consistency check requires a supported, mounted array that is not rebuilding.");
                return;
            }

            if (!Confirm($"Start a consistency check on {DescribeArray(array)}?"))
                return;

            Console.WriteLine(await RaidService.Instance.ForceArrayCheckAsync(array.Name)
                ? "Consistency check started."
                : "Could not start the consistency check.");
        }

        private static async Task RepairArray(RaidArrayInfo array)
        {
            if (!array.SupportsRepair || !array.IsDegraded || array.IsResyncing || array.IsRecovering ||
                !array.Disks.Any(d => d.RaidMembership == RaidMembership.Spare))
            {
                Console.WriteLine(
                    "Repair requires a degraded supported array with a spare disk and no active rebuild.");
                return;
            }

            if (!Confirm($"Repair degraded array {DescribeArray(array)} using its spare disk?"))
                return;

            Console.WriteLine(await RaidService.Instance.ForceArrayRepairAsync(array.Name)
                ? "Repair started."
                : "Could not repair the array.");
        }

        private static async Task AddDiskToArray(RaidArrayInfo array)
        {
            var disks = (await RaidService.Instance.GetAllDisksAsync())
                .Where(d =>
                    (d.RaidMembership == RaidMembership.None ||
                     d.Role.Equals("removed", StringComparison.OrdinalIgnoreCase)) &&
                    !d.IsMounted && d.Children.Count == 0 && !d.IsSystemDisk)
                .ToList();

            var index = SelectIndex(disks, DescribeDisk, "Select a disk to add");
            if (!index.HasValue)
                return;

            var disk = disks[index.Value];
            if (!Confirm($"Add {DescribeDisk(disk)} to {DescribeArray(array)}?"))
                return;

            Console.WriteLine(await RaidService.Instance.AddDiskToArrayAsync(array.Name, disk.Name)
                ? "Disk added to the array."
                : "Could not add the disk. Check the logs.");
        }

        private static async Task ManageArrayDisks(RaidArrayInfo array)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine($"=== Member disks — {DescribeArray(array)} ===\n");
                var index = SelectIndex(array.Disks, DescribeDisk, "Select a member disk");
                if (!index.HasValue)
                    return;

                await ManageArrayDisk(array, array.Disks[index.Value]);
                Pause();
            }
        }

        private static async Task ManageArrayDisk(RaidArrayInfo array, RaidDiskInfo disk)
        {
            var actions = new List<(string Label, string Action)>
            {
                ("SMART information", "smart")
            };

            if (!array.IsRaid0 && !array.IsLinear)
            {
                if (disk.RaidMembership is RaidMembership.Active or RaidMembership.Spare)
                    actions.Add(("Mark as faulty", "faulty"));
                if (disk.RaidMembership == RaidMembership.Active)
                    actions.Add(("Set as spare", "spare"));
                if (disk.RaidMembership == RaidMembership.Spare)
                    actions.Add(("Convert spare to active", "convert"));
                if (disk.RaidMembership == RaidMembership.Faulty)
                    actions.Add(("Recover faulty disk", "recover"));
                if (array.SupportsDiskRemoval)
                    actions.Add(("Remove from array", "remove"));
            }

            Console.WriteLine($"\nSelected member: {DescribeDisk(disk)}");
            var actionIndex = SelectIndex(actions, a => a.Label, "Select an action");
            if (!actionIndex.HasValue)
                return;

            var action = actions[actionIndex.Value].Action;
            var service = RaidService.Instance;

            switch (action)
            {
                case "smart":
                    PrintCommandOutput($"smartctl -a /dev/{disk.Name}");
                    break;
                case "faulty":
                    if (!Confirm($"Mark /dev/{disk.Name} as faulty in {array.Path}?"))
                        return;
                    PrintCommandOutput($"mdadm {Quote(array.Path)} --fail {Quote($"/dev/{disk.Name}")}");
                    break;
                case "spare":
                    if (!Confirm($"Set /dev/{disk.Name} as a spare in {array.Path}?"))
                        return;
                    PrintCommandOutput($"mdadm {Quote(array.Path)} --set-spare {Quote($"/dev/{disk.Name}")}");
                    break;
                case "convert":
                {
                    var activeCount = array.Disks.Count(d => d.RaidMembership == RaidMembership.Active);
                    if (!Confirm(
                            $"Convert /dev/{disk.Name} from spare to active? Device count becomes {activeCount + 1}."))
                        return;

                    var result = ShellHelper.EjecutarComoRoot(
                        $"mdadm --grow {Quote(array.Path)} --raid-devices={activeCount + 1}");
                    PrintResult(result.ExitCode, result.Stdout, result.Stderr);
                    break;
                }
                case "recover":
                    if (!Confirm($"Add /dev/{disk.Name} back to {array.Path}?"))
                        return;
                    Console.WriteLine(await service.AddDiskToArrayAsync(array.Name, disk.Name)
                        ? "Disk recovery started."
                        : "Could not recover the disk.");
                    break;
                case "remove":
                    if (!Confirm($"Remove /dev/{disk.Name} from {DescribeArray(array)}?"))
                        return;
                    Console.WriteLine(await service.RemoveDiskFromArrayAsync(array.Name, disk.Name)
                        ? "Disk removed from the array."
                        : "Could not remove the disk. Check the logs.");
                    break;
            }
        }

        private static void ExpandArray(RaidArrayInfo array)
        {
            var level = array.Level.Replace(" ", "").Replace("-", "").ToUpperInvariant();
            if (level is not ("RAID5" or "RAID6" or "RAID10") ||
                array.IsResyncing || array.IsRecovering || array.IsChecking || array.IsRepairing ||
                (!array.IsActive && !array.IsClean) || array.RebuildProgress > 0)
            {
                Console.WriteLine("Only idle, active RAID5/6/10 arrays can be expanded.");
                return;
            }

            var active = array.Disks.Count(d => d.RaidMembership == RaidMembership.Active);
            var spare = array.Disks.Count(d => d.RaidMembership == RaidMembership.Spare);
            var newCount = active + spare;
            if (newCount <= active)
            {
                Console.WriteLine("No spare disks are available to expand this array.");
                return;
            }

            if (!Confirm($"Reshape {array.Path} from {active} to {newCount} devices?"))
                return;

            var result = ShellHelper.EjecutarComoRoot(
                $"mdadm --grow {Quote(array.Path)} --raid-devices={newCount}");
            PrintResult(result.ExitCode, result.Stdout, result.Stderr);
            if (result.ExitCode == 0)
                Console.WriteLine("Reshape started. The array status will show its progress.");
        }

        private static async Task TogglePersistentMount(RaidArrayInfo array)
        {
            var service = RaidService.Instance;
            var enable = !array.PersistMount;
            var action = enable ? "Add a persistent mount entry" : "Remove the persistent mount entry";

            if (!Confirm($"{action} for {DescribeArray(array)}?"))
                return;

            var ok = enable
                ? await service.PersistMountAsync(array)
                : await service.RemovePersistMountAsync(array);
            if (ok)
                array.PersistMount = enable;

            Console.WriteLine(ok
                ? (enable ? "Persistent mount enabled." : "Persistent mount removed.")
                : "Could not update the persistent mount. Check the logs.");
        }

        private static async Task StopSelectedArray(RaidArrayInfo array)
        {
            if (!Confirm($"Stop {DescribeArray(array)}? It will be unavailable until assembled again."))
                return;

            var (ok, message) = await RaidService.Instance.StopArraySafeAsync(array.Name);
            Console.WriteLine(message);
            if (ok)
                Console.WriteLine("Array stopped.");
        }

        private static async Task DeleteSelectedArray(RaidArrayInfo array)
        {
            Console.WriteLine($"Selected array: {DescribeArray(array)}");
            if (!Confirm("Delete this array? This cannot be undone and removes RAID metadata from its member disks."))
                return;

            Console.WriteLine(await RaidService.Instance.DeleteArrayAsync(array)
                ? "Array deleted."
                : "Array deletion failed or was incomplete. Check the logs.");
        }

        private static void ConfigureArray(RaidArrayInfo array)
        {
            var config = ArrayConfigService.Load(array.BaseName);
            Console.WriteLine($"\n=== Array configuration: {DescribeArray(array)} ===");

            config.Name = ReadValue("Friendly name", config.Name);
            config.FsLabel = ReadValue("Filesystem label", config.FsLabel);
            config.MountPoint = ReadValue("Mount point", config.MountPoint);
            config.MountPermissions = ReadValue("Mount permissions (3-digit octal)", config.MountPermissions);

            config.Mount_NoAtime = ReadBoolean("Mount option noatime", config.Mount_NoAtime);
            config.Mount_NoDirAtime = ReadBoolean("Mount option nodiratime", config.Mount_NoDirAtime);
            config.Mount_Discard = ReadBoolean("Mount option discard", config.Mount_Discard);
            config.Mount_Sync = ReadBoolean("Mount option sync", config.Mount_Sync);
            config.Mount_ReadOnly = ReadBoolean("Read-only mount", config.Mount_ReadOnly);
            config.PersistMount = ReadBoolean("Persist mount in /etc/fstab", config.PersistMount);
            config.ResyncPriority = ReadInt("Resync priority (1-200000)", config.ResyncPriority, 1, 200000);
            config.ResyncMaxSpeed = ReadInt("Resync max speed (100-500000)", config.ResyncMaxSpeed, 100, 500000);
            config.AlertDegraded = ReadBoolean("Alert when array is degraded", config.AlertDegraded);
            config.AlertDiskFail = ReadBoolean("Alert when a disk fails", config.AlertDiskFail);
            config.AlertSlowResync = ReadBoolean("Alert on slow resync", config.AlertSlowResync);

            if (string.IsNullOrWhiteSpace(config.Name) ||
                config.Name.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            {
                Console.WriteLine(
                    "Friendly name must contain only letters, numbers, '-' and '_'. Configuration not saved.");
                return;
            }

            if (config.PersistMount)
            {
                if (!Path.IsPathRooted(config.MountPoint) || config.MountPoint.Contains(' '))
                {
                    Console.WriteLine(
                        "Persistent mount point must be an absolute path without spaces. Configuration not saved.");
                    return;
                }

                if (new[] { "/", "/home", "/usr", "/root", "/etc", "/dev", "/proc", "/sys", "/tmp" }
                    .Contains(config.MountPoint, StringComparer.Ordinal))
                {
                    Console.WriteLine("This mount point is protected. Configuration not saved.");
                    return;
                }

                if (!Directory.Exists(config.MountPoint))
                {
                    Console.WriteLine("Persistent mount point must already exist. Configuration not saved.");
                    return;
                }
            }

            if (config.Mount_ReadOnly && config.Mount_Discard)
            {
                Console.WriteLine("discard cannot be combined with a read-only mount. Configuration not saved.");
                return;
            }

            if (config.MountPermissions.Length != 3 ||
                config.MountPermissions.Any(c => c is < '0' or > '7'))
            {
                Console.WriteLine("Permissions must be a 3-digit octal value. Configuration not saved.");
                return;
            }

            if (!Confirm("Save and apply these array settings?"))
                return;

            try
            {
                var fs = FstabService.DetectFilesystem(array.Path);
                if (string.IsNullOrWhiteSpace(fs) || fs == "auto")
                    throw new InvalidOperationException("Could not detect the array filesystem.");

                ArrayConfigService.Save(array.BaseName, config);
                if (config.PersistMount)
                    FstabService.WriteEntry(array.Path, config.MountPoint, fs, BuildMountOptions(config));
                else
                    FstabService.RemoveEntry(array.Path);

                MdadmService.ApplyConfig(array.BaseName, config);
                Console.WriteLine("Array configuration saved and applied.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not apply array configuration: {ex.Message}");
            }
        }

        private static bool ReadBoolean(string prompt, bool current)
        {
            Console.WriteLine($"\n{prompt} (current: {(current ? "Yes" : "No")})");
            Console.WriteLine("1) Yes");
            Console.WriteLine("2) No");
            Console.WriteLine("0) Keep current");
            Console.Write("Select an option: ");
            return Console.ReadLine()?.Trim() switch
            {
                "1" => true,
                "2" => false,
                _ => current
            };
        }

        private static int ReadInt(string prompt, int current, int min, int max)
        {
            while (true)
            {
                var value = ReadValue(prompt, current.ToString());
                if (int.TryParse(value, out var parsed) && parsed >= min && parsed <= max)
                    return parsed;
                Console.WriteLine($"Enter a number from {min} to {max}.");
            }
        }

        private static string BuildMountOptions(ArrayConfig config)
        {
            var options = new List<string>();
            if (config.Mount_NoAtime) options.Add("noatime");
            if (config.Mount_NoDirAtime) options.Add("nodiratime");
            if (config.Mount_Discard) options.Add("discard");
            if (config.Mount_Sync) options.Add("sync");
            if (config.Mount_ReadOnly) options.Add("ro");
            return options.Count == 0 ? "defaults" : string.Join(",", options);
        }

        private static async Task ManageDisks()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== Physical disks ===\n");
                Console.WriteLine("1) Select a disk");
                Console.WriteLine("2) Refresh disk list");
                Console.WriteLine("3) List disks and partitions (lsblk)");
                Console.WriteLine("0) Back");

                switch (ReadMenuChoice(3))
                {
                    case 1:
                    {
                        var disks = await RaidService.Instance.GetAllDisksAsync();
                        var index = SelectIndex(disks, DescribeDisk, "Select a disk");
                        if (index.HasValue)
                            await ManageDisk(disks[index.Value]);
                        break;
                    }
                    case 2:
                        Console.WriteLine($"Detected {(await RaidService.Instance.GetAllDisksAsync()).Count} disks.");
                        break;
                    case 3: await ListDisks(); break;
                    case 0: return;
                    default: Console.WriteLine("Invalid option."); break;
                }

                Pause();
            }
        }

        private static async Task ManageDisk(RaidDiskInfo disk)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine($"=== Disk: {DescribeDisk(disk)} ===\n");
                Console.WriteLine("1) View disk information");
                Console.WriteLine("2) SMART report");

                var actions = new List<string>();
                if (!disk.IsSystemDisk && !SystemDiskDetector.IsSystemDisk(disk.Name) && !disk.IsUsedByRaid)
                {
                    if (disk.IsMounted)
                        actions.Add("Unmount");
                    else if (!HasMountedChildren(disk))
                    {
                        actions.Add("Deep clean disk");
                        actions.Add("Initialize disk");
                    }
                }

                for (var i = 0; i < actions.Count; i++)
                    Console.WriteLine($"{i + 3}) {actions[i]}");
                Console.WriteLine("0) Back");
                Console.Write("Select an option: ");

                if (!int.TryParse(Console.ReadLine(), out var choice) || choice < 0 || choice > actions.Count + 2)
                {
                    Console.WriteLine("Invalid selection.");
                    continue;
                }

                if (choice == 0)
                    return;
                if (choice == 1)
                    ShowDiskInfo(disk);
                else if (choice == 2)
                    PrintCommandOutput($"smartctl -a /dev/{disk.Name}");
                else
                {
                    switch (actions[choice - 3])
                    {
                        case "Unmount": UnmountDisk(disk); break;
                        case "Deep clean disk": await DeepCleanDisk(disk); break;
                        case "Initialize disk": await InitializeDisk(disk); break;
                    }

                    var refreshed = await RaidService.Instance.GetAllDisksAsync();
                    disk = refreshed.FirstOrDefault(d => d.Name == disk.Name) ?? disk;
                }

                Pause();
            }
        }

        private static void ShowDiskInfo(RaidDiskInfo disk)
        {
            var type = disk.IsNvme ? "NVMe" : disk.IsRotational ? "HDD" : "SSD";
            var temperature = GetDiskTemperature(disk.Name);
            Console.WriteLine($"\nDevice:      /dev/{disk.Name}");
            Console.WriteLine($"Model:       {disk.Model}");
            Console.WriteLine($"Size:        {disk.Size}");
            Console.WriteLine($"Serial:      {disk.Serial}");
            Console.WriteLine($"Type:        {type}");
            Console.WriteLine($"Temperature: {temperature}");
            Console.WriteLine($"Filesystem:  {disk.FsType}");
            Console.WriteLine($"Mount:       {disk.MountPath}");
            Console.WriteLine($"SMART:       {disk.SmartStatus}");
            Console.WriteLine($"RAID:        {(disk.IsUsedByRaid ? disk.ArrayName : "No")}");
        }

        private static string GetDiskTemperature(string diskName)
        {
            var result = ShellHelper.EjecutarComoRoot($"smartctl -A {Quote($"/dev/{diskName}")}");
            var output = result.Stdout + "\n" + result.Stderr;

            foreach (var line in output.Split('\n'))
            {
                var lower = line.ToLowerInvariant();
                if (!lower.Contains("temperature_celsius") &&
                    !lower.Contains("temperature_internal") &&
                    !lower.Contains("current drive temperature") &&
                    !lower.Contains("temperature:"))
                    continue;

                foreach (var token in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Reverse())
                {
                    var value = token.Replace("°C", "").Replace("C", "");
                    if (int.TryParse(value, out var temperature))
                        return $"{temperature}°C";
                }
            }

            return "N/A";
        }

        private static bool HasMountedChildren(RaidDiskInfo disk)
        {
            if (disk.Children.Count == 0 || !File.Exists("/proc/mounts"))
                return false;

            var mountedDevices = File.ReadAllLines("/proc/mounts")
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length > 1)
                .Select(parts => parts[0])
                .ToHashSet(StringComparer.Ordinal);

            return disk.Children.Any(child => mountedDevices.Contains($"/dev/{child}"));
        }

        private static void UnmountDisk(RaidDiskInfo disk)
        {
            if (string.IsNullOrWhiteSpace(disk.MountPath))
            {
                Console.WriteLine("This disk is not mounted.");
                return;
            }

            if (!Confirm($"Unmount /dev/{disk.Name} from {disk.MountPath}?"))
                return;

            Console.WriteLine(MountService.Unmount(disk.MountPath)
                ? "Disk unmounted."
                : "Could not unmount the disk.");
        }

        private static async Task DeepCleanDisk(RaidDiskInfo disk)
        {
            Console.WriteLine($"Selected disk: {DescribeDisk(disk)}");
            if (!Confirm($"WARNING: this permanently destroys all data and metadata on /dev/{disk.Name}. Continue?"))
                return;

            var device = Quote($"/dev/{disk.Name}");
            var zeroResult = await Task.Run(() =>
                ShellHelper.EjecutarComoRoot($"mdadm --zero-superblock --force {device}"));
            if (zeroResult.ExitCode != 0)
                Console.WriteLine($"No RAID superblock was cleared (continuing): {zeroResult.Stderr.Trim()}");

            var commands = new[]
            {
                $"wipefs -a -f {device}",
                $"sgdisk --zap-all {device}",
                $"dd if=/dev/zero of={device} bs=1M count=10 status=none conv=fdatasync",
                $"blockdev --rereadpt {device}",
                $"partprobe {device}",
                "udevadm settle"
            };

            foreach (var command in commands)
            {
                var result = await Task.Run(() => ShellHelper.EjecutarComoRoot(command));
                if (result.ExitCode != 0)
                {
                    Console.WriteLine($"Command failed: {command}");
                    PrintResult(result.ExitCode, result.Stdout, result.Stderr);
                    return;
                }
            }

            Console.WriteLine($"Disk /dev/{disk.Name} was cleaned.");
        }

        private static async Task InitializeDisk(RaidDiskInfo disk)
        {
            Console.WriteLine($"Selected disk: {DescribeDisk(disk)}");
            var label = ReadValue("Volume label", "");
            var filesystems = new[] { "ext4", "xfs", "btrfs", "f2fs", "ntfs", "exfat", "fat32" };
            var fsIndex = SelectIndex(filesystems, fs => fs, "Select filesystem");
            if (!fsIndex.HasValue)
                return;

            var fs = filesystems[fsIndex.Value];
            if (string.IsNullOrWhiteSpace(label) ||
                label.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            {
                Console.WriteLine("The label is required and may contain only letters, numbers, '-' and '_'.");
                return;
            }

            if (!Confirm(
                    $"Initialize /dev/{disk.Name} as {fs} labeled '{label}'? All existing data will be destroyed."))
                return;

            var device = $"/dev/{disk.Name}";
            var partition = device + (disk.Name.StartsWith("nvme", StringComparison.Ordinal) ? "p1" : "1");
            var mountPoint = $"/run/media/{Environment.UserName}/{label}";

            try
            {
                await Task.Run(() =>
                {
                    RequireRootSuccess($"parted -s {Quote(device)} mklabel gpt");
                    RequireRootSuccess($"parted -s {Quote(device)} mkpart primary 0% 100%");
                    RequireRootSuccess($"partprobe {Quote(device)}");
                    RequireRootSuccess("udevadm settle");

                    for (var attempt = 0; attempt < 20 && !File.Exists(partition); attempt++)
                        System.Threading.Thread.Sleep(200);
                    if (!File.Exists(partition))
                        throw new IOException($"Partition {partition} was not detected.");

                    var formatCommand = fs switch
                    {
                        "ext4" => $"mkfs.ext4 -F -L {Quote(label)} {Quote(partition)}",
                        "xfs" => $"mkfs.xfs -f -L {Quote(label)} {Quote(partition)}",
                        "btrfs" => $"mkfs.btrfs -f -L {Quote(label)} {Quote(partition)}",
                        "f2fs" => $"mkfs.f2fs -f -l {Quote(label)} {Quote(partition)}",
                        "ntfs" => $"mkfs.ntfs -f -L {Quote(label)} {Quote(partition)}",
                        "exfat" => $"mkfs.exfat -n {Quote(label)} {Quote(partition)}",
                        "fat32" => $"mkfs.vfat -F 32 -n {Quote(label)} {Quote(partition)}",
                        _ => throw new InvalidOperationException("Unsupported filesystem.")
                    };
                    RequireRootSuccess(formatCommand);
                    RequireRootSuccess($"mkdir -p {Quote(mountPoint)}");
                    RequireRootSuccess($"mount {Quote(partition)} {Quote(mountPoint)}");
                    RequireRootSuccess($"chmod 775 {Quote(mountPoint)}");
                });

                Console.WriteLine($"Disk initialized and mounted at {mountPoint}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Disk initialization failed: {ex.Message}");
            }
        }

        private static void RequireRootSuccess(string command)
        {
            var result = ShellHelper.EjecutarComoRoot(command);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{command}\n{result.Stderr.Trim()}".Trim());
        }

        private static async Task ViewLogs()
        {
            var logPath = Path.Combine(ConfigManager.Get().LogsPath, "raid-util.log");
            while (true)
            {
                Console.Clear();
                Console.WriteLine($"=== Application log: {logPath} ===\n");
                Console.WriteLine("1) Show latest 2000 lines");
                Console.WriteLine("2) Open full log in the default editor");
                Console.WriteLine("0) Back");

                switch (ReadMenuChoice(2))
                {
                    case 1:
                        try
                        {
                            if (!File.Exists(logPath))
                            {
                                Console.WriteLine("No log file exists yet.");
                            }
                            else
                            {
                                var lines = await File.ReadAllLinesAsync(logPath);
                                Console.WriteLine(string.Join(Environment.NewLine, lines.TakeLast(2000)));
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not read log file: {ex.Message}");
                        }

                        Pause();
                        break;
                    case 2:
                        try
                        {
                            if (File.Exists(logPath))
                                ShellHelper.OpenFile(logPath);
                            else
                                Console.WriteLine("No log file exists yet.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not open log file: {ex.Message}");
                        }

                        Pause();
                        break;
                    case 0:
                        return;
                    default:
                        Console.WriteLine("Invalid option.");
                        Pause();
                        break;
                }
            }
        }

        private static void ManageSettings()
        {
            var config = ConfigManager.Get();
            Console.WriteLine("=== Application settings ===\n");
            config.LogsPath = ReadValue("Logs directory", config.LogsPath);
            Console.WriteLine($"\nLog level (current: {config.LogLevel})");
            var levels = new[] { "ERROR", "INFO", "DEBUG" };
            var index = SelectIndex(levels, level => level, "Select log level");
            if (index.HasValue)
                config.LogLevel = index.Value;

            try
            {
                if (!Path.IsPathRooted(config.LogsPath))
                {
                    config.LogsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config",
                        "raid-util",
                        config.LogsPath);
                }

                Directory.CreateDirectory(config.LogsPath);
                ConfigManager.Save(config);
                LogService.ApplyConfig(config);
                Console.WriteLine($"Settings saved. Log file: {Path.Combine(config.LogsPath, "raid-util.log")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not save settings: {ex.Message}");
            }
        }

        private static string Quote(string value) =>
            "'" + value.Replace("'", "'\"'\"'") + "'";

        private static void PrintCommandOutput(string command)
        {
            var (exit, stdout, stderr) = ShellHelper.EjecutarComoRoot(command);
            PrintResult(exit, stdout, stderr);
        }

        private static void PrintResult(int exit, string stdout, string stderr)
        {
            if (!string.IsNullOrWhiteSpace(stdout))
                Console.WriteLine(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr))
                Console.WriteLine(stderr.Trim());
            if (exit != 0)
                Console.WriteLine($"Command failed with exit code {exit}.");
        }

        private static void EnsureCredentials()
        {
            if (!string.IsNullOrEmpty(Credentials.AdminPassword) || Environment.UserName == "root")
                return;

            Console.Write("Enter sudo password: ");
            Credentials.AdminPassword = ReadPasswordHidden();
            Console.WriteLine();

            Credentials.AllowRaidCalls = true;

            var (exit, _, _) = ShellHelper.EjecutarComoRoot("true");
            if (exit != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nError: Sudo authentication failed or invalid password.");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        private static string ReadPasswordHidden()
        {
            string pass = string.Empty;
            ConsoleKeyInfo keyInfo;

            while ((keyInfo = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
            {
                if (keyInfo.Key == ConsoleKey.Backspace && pass.Length > 0)
                {
                    pass = pass[..^1];
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    pass += keyInfo.KeyChar;
                    Console.Write("*");
                }
            }

            return pass;
        }

        private static async Task ListArrays()
        {
            Console.Clear();
            Console.WriteLine("=== Active RAID arrays ===\n");

            var (exit, stdout, stderr) = await Task.Run(() => ShellHelper.EjecutarComoRoot("mdadm --detail --scan"));

            if (exit != 0)
            {
                Console.WriteLine($"Error querying mdadm: {stderr.Trim()}");
                return;
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine("No active RAID arrays configured on this system.");
                return;
            }

            // Filtrar líneas duplicadas en la salida general por si mdadm.conf tiene entradas repetidas
            var uniqueLines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Distinct();

            Console.WriteLine(string.Join(Environment.NewLine, uniqueLines));
        }

        private static async Task ListDisks()
        {
            Console.Clear();
            Console.WriteLine("=== Physical disks and partitions ===\n");

            var (exit, stdout, stderr) = await Task.Run(() =>
                ShellHelper.EjecutarSinRoot("lsblk -o NAME,SIZE,FSTYPE,TYPE,MOUNTPOINTS"));

            if (exit == 0)
            {
                Console.WriteLine(stdout.Trim());
            }
            else
            {
                Console.WriteLine($"Error executing lsblk: {stderr.Trim()}");
            }
        }
    }
}