using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using RAID_Util.Services;

namespace RAID_Util.Helpers
{
    public static class ShellHelper
    {
        private static long _callCount = 0;

        private static string FixCommandPath(string cmd)
        {
            // Si el comando empieza por "mdadm", lo reemplazamos por la ruta absoluta
            if (cmd.TrimStart().StartsWith("mdadm "))
                return cmd.Replace("mdadm", "/usr/sbin/mdadm");

            return cmd;
        }

        public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command)
        {
            var callId = ++_callCount;
            var sw = Stopwatch.StartNew();

            Console.WriteLine($"[SHELL] #{callId} EjecutarComoRoot: {command}");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = $"-S bash -c \"{command.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                if (!string.IsNullOrEmpty(Credentials.AdminPassword))
                {
                    var pass = Credentials.AdminPassword.TrimEnd('\r', '\n');
                    process.StandardInput.WriteLine(pass);
                    process.StandardInput.Flush();
                }

                process.StandardInput.Close();

                const int timeoutMs = 15000;

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    Console.WriteLine($"[SHELL] #{callId} TIMEOUT ejecutando '{command}'");
                    return (1, "", "Timeout");
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                sw.Stop();

                Console.WriteLine($"[SHELL] #{callId} EXIT={process.ExitCode} ({sw.ElapsedMilliseconds} ms)");
                Console.WriteLine($"[SHELL] #{callId} STDOUT:\n{stdout}");
                Console.WriteLine($"[SHELL] #{callId} STDERR:\n{stderr}");

                return (process.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SHELL] #{callId} EXCEPTION: {ex}");
                return (1, "", ex.Message);
            }
        }

        public static async Task<string> RunCleanAsync(string command)
        {
            var callId = ++_callCount;

            // 🔥 Asegurar que mdadm tenga ruta absoluta
            command = FixCommandPath(command);

            Console.WriteLine($"[SHELL] #{callId} RunCleanAsync: {command}");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                Console.WriteLine($"[SHELL] #{callId} STDOUT:\n{stdout}");
                Console.WriteLine($"[SHELL] #{callId} STDERR:\n{stderr}");

                return (stdout + "\n" + stderr).Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SHELL] #{callId} EXCEPTION: {ex}");
                return string.Empty;
            }
        }

        public static void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                var psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = true
                };

                Process.Start(psi);
            }
            catch { }
        }
    }
}
