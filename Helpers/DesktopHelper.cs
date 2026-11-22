using System;
using RAID_Util.Helpers;

public static class DesktopHelper
{
    public static void OpenPath(string path)
    {
        var tools = new[]
        {
            ("xdg-open", false),
            ("gio", true),
            ("kioclient5", false),
            ("kioclient", false),
            ("gvfs-open", false),
            ("sensible-browser", false)
        };

        foreach (var (tool, needsOpen) in tools)
        {
            var binary = ToolLocator.Resolve(tool);
            if (binary is null)
                continue;

            var arguments = needsOpen ? new[] { "open", path } : new[] { path };
            if (ShellHelper.Run(binary, arguments).ExitCode == 0)
                return;
        }

        // Último recurso: el terminal necesita un intérprete para "cd ...; exec bash".
        // La ruta se entrecomilla aquí; el resto de herramientas usa ArgumentList.
        var terminal = ToolLocator.Resolve("x-terminal-emulator");
        var bash = ToolLocator.Resolve("bash");
        if (terminal is null || bash is null)
            return;

        ShellHelper.Run(
            terminal,
            new[] { "-e", bash, "-c", $"cd {Quote(path)}; exec bash" });
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}
