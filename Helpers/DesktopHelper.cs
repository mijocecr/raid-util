using RAID_Util.Helpers;

public static class DesktopHelper
{
    public static void OpenPath(string path)
    {
        string[] cmds =
        {
            $"xdg-open \"{path}\"",
            $"gio open \"{path}\"",
            $"kioclient5 exec \"{path}\"",
            $"kioclient exec \"{path}\"",
            $"gvfs-open \"{path}\"",
            $"sensible-browser \"{path}\""
        };

        foreach (var cmd in cmds)
        {
            var r = ShellHelper.EjecutarSinRoot(cmd);
            if (r.ExitCode == 0)
                return;
        }

        // Último recurso: abrir terminal
        ShellHelper.EjecutarSinRoot(
            $"x-terminal-emulator -e 'bash -c \"cd \\\"{path}\\\"; exec bash\"'"
        );
    }
}