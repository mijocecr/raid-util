using System;
using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

public static class DiskIconHelper
{
    private const string DefaultIcon = "avares://RAID-Util/Assets/Icons/disk-hdd.png";

    // Cache para evitar recargar imágenes
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();

    public static Image LoadImage(string? uriString, int size)
    {
        var uri = ValidateUri(uriString);

        // Obtener bitmap desde cache o cargarlo
        var bitmap = _cache.GetOrAdd(uri, LoadBitmapSafe);

        return new Image
        {
            Source = bitmap,
            Width = size,
            Height = size
        };
    }

    private static string ValidateUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("avares://"))
        {
            Console.WriteLine($"[ICONHELPER] Ruta inválida '{uri}', usando default.");
            return DefaultIcon;
        }

        return uri;
    }

    private static Bitmap LoadBitmapSafe(string uri)
    {
        try
        {
            Console.WriteLine($"[ICONHELPER] Cargando icono: {uri}");
            using var stream = AssetLoader.Open(new Uri(uri));
            return new Bitmap(stream);
        }
        catch
        {
            Console.WriteLine($"[ICONHELPER] FALLO al abrir '{uri}', usando default.");

            using var fallback = AssetLoader.Open(new Uri(DefaultIcon));
            return new Bitmap(fallback);
        }
    }
}