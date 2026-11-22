using System;
using System.Linq;
using Avalonia;
using RAID_Util.Helpers;

namespace RAID_Util;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h"))
        {
            Console.WriteLine("RAID-Util — gestor de RAID para Linux");
            Console.WriteLine("  --cli   Fuerza la interfaz de terminal (útil en servidores headless)");
            Console.WriteLine("  --gui   Fuerza la interfaz gráfica");
            Console.WriteLine("  --help  Muestra esta ayuda");
            return;
        }

        var forceCli = args.Any(a => a.Equals("--cli", StringComparison.OrdinalIgnoreCase));
        var forceGui = args.Any(a => a.Equals("--gui", StringComparison.OrdinalIgnoreCase));

        if (forceCli || (!forceGui && ShouldUseCli()))
        {
            CliApp.Run().GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static bool ShouldUseCli()
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var hasDisplay = !string.IsNullOrWhiteSpace(display) || !string.IsNullOrWhiteSpace(wayland);
        var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;

        return !hasDisplay && interactive;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
