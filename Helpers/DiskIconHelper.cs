using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

public static class DiskIconHelper
{
    private const string DefaultIcon = "avares://RAID-Util/Assets/Icons/disk-hdd.png";

    public static Image LoadImage(string uriString, int size)
    {
        try
        {
            // Validar ruta
            if (string.IsNullOrWhiteSpace(uriString) || !uriString.StartsWith("avares://"))
            {
                Console.WriteLine($"[ICONHELPER] Ruta inválida '{uriString}', usando default.");
                uriString = DefaultIcon;
            }

            Console.WriteLine($"[ICONHELPER] Cargando icono: {uriString}");

            var uri = new Uri(uriString);

            // Intentar abrir el recurso
            try
            {
                using var stream = AssetLoader.Open(uri);

                return new Image
                {
                    Source = new Bitmap(stream),
                    Width = size,
                    Height = size
                };
            }
            catch
            {
                Console.WriteLine($"[ICONHELPER] FALLO al abrir '{uriString}', usando default.");
            }

            // Fallback final
            var fallbackUri = new Uri(DefaultIcon);
            using (var fallbackStream = AssetLoader.Open(fallbackUri))
            {
                return new Image
                {
                    Source = new Bitmap(fallbackStream),
                    Width = size,
                    Height = size
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ICONHELPER] ERROR inesperado: {ex.Message}");

            var fallbackUri = new Uri(DefaultIcon);
            using var stream = AssetLoader.Open(fallbackUri);

            return new Image
            {
                Source = new Bitmap(stream),
                Width = size,
                Height = size
            };
        }
    }
}