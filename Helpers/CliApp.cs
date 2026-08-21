using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RAID_Util.Core;
using RAID_Util.Helpers;
using RAID_Util.Models;
using RAID_Util.Services;

namespace RAID_Util.Helpers
{
    public static class CliApp
    {
        public static async Task Run()
        {
            var config = ConfigManager.Load();

            EnsureCredentials();

            while (true)
            {
                Console.Clear();
                Console.WriteLine("===== RAID-Util CLI =====\n");
                Console.WriteLine("1) Show RAID subsystem status");
                Console.WriteLine("2) List active RAID arrays");
                Console.WriteLine("3) Show detailed array information");
                Console.WriteLine("4) Mount RAID array");
                Console.WriteLine("5) Unmount RAID array");
                Console.WriteLine("6) List physical disks & partitions");
                Console.WriteLine("7) Create software RAID array (Advanced)");
                Console.WriteLine("8) Stop RAID array");
                Console.WriteLine("0) Exit");
                Console.WriteLine("==========================\n");
                Console.Write("Option: ");

                var opt = Console.ReadLine()?.Trim();

                switch (opt)
                {
                    case "1": await ShowStatus(); break;
                    case "2": await ListArrays(); break;
                    case "3": await ShowArrayDetails(); break;
                    case "4": await MountArray(); break;
                    case "5": await UnmountArray(); break;
                    case "6": await ListDisks(); break;
                    case "7": await CreateArray(); break;
                    case "8": await StopArray(); break;
                    case "0": return;
                    default:
                        Console.WriteLine("Invalid option.");
                        break;
                }

                Console.WriteLine("\nPress ENTER to continue...");
                Console.ReadLine();
            }
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

        private static async Task ShowStatus()
        {
            Console.Clear();
            Console.WriteLine("=== RAID subsystem status ===\n");

            var (mdExit, mdOut, _) = await Task.Run(() => ShellHelper.EjecutarSinRoot("which mdadm"));
            if (mdExit != 0 || string.IsNullOrWhiteSpace(mdOut))
            {
                Console.WriteLine("[-] mdadm is not installed or unavailable in PATH.");
                return;
            }

            Console.WriteLine("[+] mdadm detected.");

            var (_, mdstatOut, _) = await Task.Run(() => ShellHelper.EjecutarSinRoot("cat /proc/mdstat"));
            Console.WriteLine("\n--- /proc/mdstat ---");
            Console.WriteLine(string.IsNullOrWhiteSpace(mdstatOut) ? "No active status in /proc/mdstat" : mdstatOut.Trim());
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

        private static async Task ShowArrayDetails()
        {
            Console.Clear();
            Console.WriteLine("=== Detailed array information ===\n");

            var arrays = GetActiveArrays();
            if (arrays.Count == 0)
            {
                Console.WriteLine("No active RAID arrays detected.");
                return;
            }

            Console.WriteLine("Detected RAID devices:\n");
            for (int i = 0; i < arrays.Count; i++)
            {
                Console.WriteLine($"{i + 1}) {arrays[i]}");
            }

            Console.Write("\nSelect array index: ");
            if (!int.TryParse(Console.ReadLine(), out int idx) || idx < 1 || idx > arrays.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            var targetDevice = arrays[idx - 1];

            Console.Clear();
            Console.WriteLine($"=== Details for {targetDevice} ===\n");

            var (exit, stdout, stderr) = await Task.Run(() => ShellHelper.EjecutarComoRoot($"mdadm --detail {targetDevice}"));

            if (exit == 0)
            {
                Console.WriteLine(stdout.Trim());
            }
            else
            {
                Console.WriteLine($"Error retrieving details: {stderr.Trim()}");
            }
        }

        private static async Task MountArray()
        {
            Console.Clear();
            Console.WriteLine("=== Mount RAID array ===\n");

            var arrays = GetActiveArrays();
            if (arrays.Count == 0)
            {
                Console.WriteLine("No active RAID arrays detected.");
                return;
            }

            Console.WriteLine("Available RAID devices:\n");
            for (int i = 0; i < arrays.Count; i++)
            {
                Console.WriteLine($"{i + 1}) {arrays[i]}");
            }

            Console.Write("\nSelect array index: ");
            if (!int.TryParse(Console.ReadLine(), out int idx) || idx < 1 || idx > arrays.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            var device = arrays[idx - 1];
            var defaultMount = $"/mnt/{Path.GetFileName(device)}";

            Console.Write($"Mount point directory [{defaultMount}]: ");
            var mountPoint = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(mountPoint))
                mountPoint = defaultMount;

            Console.WriteLine($"\nMounting {device} to {mountPoint}...");

            var (exit, _, stderr) = await Task.Run(() =>
            {
                var mkdirRes = ShellHelper.EjecutarComoRoot($"mkdir -p \"{mountPoint}\"");
                if (mkdirRes.ExitCode != 0) return mkdirRes;

                var mountRes = ShellHelper.EjecutarComoRoot($"mount \"{device}\" \"{mountPoint}\"");
                if (mountRes.ExitCode == 0)
                {
                    ShellHelper.EjecutarComoRoot($"chmod 777 \"{mountPoint}\"");
                }

                return mountRes;
            });

            if (exit == 0)
            {
                Console.WriteLine($"[+] Successfully mounted {device} at {mountPoint}");
            }
            else
            {
                Console.WriteLine($"[-] Failed to mount {device}: {stderr.Trim()}");
            }
        }

        private static async Task UnmountArray()
        {
            Console.Clear();
            Console.WriteLine("=== Unmount RAID array ===\n");

            Console.Write("Enter RAID device path or mount directory (e.g. /dev/md0 or /mnt/md0): ");
            var target = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(target))
            {
                Console.WriteLine("Invalid target path.");
                return;
            }

            Console.WriteLine($"\nUnmounting {target}...");

            var (exit, _, stderr) = await Task.Run(() => ShellHelper.EjecutarComoRoot($"umount \"{target}\""));

            if (exit == 0)
            {
                Console.WriteLine($"[+] {target} unmounted successfully.");
            }
            else
            {
                Console.WriteLine($"[-] Failed to unmount {target}: {stderr.Trim()}");
            }
        }

        private static async Task ListDisks()
        {
            Console.Clear();
            Console.WriteLine("=== Physical disks and partitions ===\n");

            var (exit, stdout, stderr) = await Task.Run(() => ShellHelper.EjecutarSinRoot("lsblk -o NAME,SIZE,FSTYPE,TYPE,MOUNTPOINTS"));

            if (exit == 0)
            {
                Console.WriteLine(stdout.Trim());
            }
            else
            {
                Console.WriteLine($"Error executing lsblk: {stderr.Trim()}");
            }
        }

        private static async Task CreateArray()
        {
            Console.Clear();
            Console.WriteLine("=== Create software RAID array ===\n");

            Console.Write("RAID Name / Device (e.g. /dev/md0): ");
            var devName = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(devName)) return;

            Console.Write("RAID Level (0, 1, 5, 6, 10): ");
            var level = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(level)) return;

            Console.Write("Drives (space-separated, e.g. /dev/sdb /dev/sdc): ");
            var drives = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(drives)) return;

            var driveList = drives.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = driveList.Length;

            Console.WriteLine($"\nConfiguration Summary:");
            Console.WriteLine($"  Device:      {devName}");
            Console.WriteLine($"  RAID Level: {level}");
            Console.WriteLine($"  Devices:    {string.Join(", ", driveList)} ({count} total)");

            Console.WriteLine("\nWARNING: Target drives will be formatted and existing data erased!");
            Console.Write("Type EXACTLY 'YES' to proceed: ");

            if (Console.ReadLine()?.Trim() != "YES")
            {
                Console.WriteLine("Array creation aborted.");
                return;
            }

            var cmd = $"mdadm --create --verbose {devName} --level={level} --raid-devices={count} {drives}";
            Console.WriteLine($"\nExecuting: {cmd}\n");

            var (exit, stdout, stderr) = await Task.Run(() => ShellHelper.EjecutarComoRoot(cmd));

            if (exit == 0)
            {
                Console.WriteLine("[+] Array creation initiated successfully.");
                Console.WriteLine(stdout.Trim());
            }
            else
            {
                Console.WriteLine($"[-] Array creation failed: {stderr.Trim()}");
            }
        }

        private static async Task StopArray()
        {
            Console.Clear();
            Console.WriteLine("=== Stop RAID array ===\n");

            var arrays = GetActiveArrays();
            if (arrays.Count == 0)
            {
                Console.WriteLine("No active RAID arrays detected.");
                return;
            }

            Console.WriteLine("Active RAID devices:\n");
            for (int i = 0; i < arrays.Count; i++)
            {
                Console.WriteLine($"{i + 1}) {arrays[i]}");
            }

            Console.Write("\nSelect target array index to stop: ");
            if (!int.TryParse(Console.ReadLine(), out int idx) || idx < 1 || idx > arrays.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            var device = arrays[idx - 1];

            Console.WriteLine($"\nWARNING: Stopping {device} will make it inaccessible until re-assembled.");
            Console.Write("Type EXACTLY 'YES' to confirm: ");

            if (Console.ReadLine()?.Trim() != "YES")
            {
                Console.WriteLine("Operation cancelled.");
                return;
            }

            var (exit, _, stderr) = await Task.Run(() => ShellHelper.EjecutarComoRoot($"mdadm --stop {device}"));

            if (exit == 0)
            {
                Console.WriteLine($"[+] RAID array {device} stopped successfully.");
            }
            else
            {
                Console.WriteLine($"[-] Failed to stop array: {stderr.Trim()}");
            }
        }

        private static List<string> GetActiveArrays()
        {
            var list = new List<string>();
            var (exit, stdout, _) = ShellHelper.EjecutarComoRoot("mdadm --detail --scan");

            if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
                return list;

            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] == "ARRAY")
                {
                    list.Add(parts[1]);
                }
            }

            // Aplicar Distinct para evitar elementos duplicados en la lista de opciones
            return list.Distinct().ToList();
        }
    }
}