using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RAID_Util.Helpers;

/// <summary>
/// Resuelve ejecutables por PATH (más los directorios sbin habituales) en lugar de
/// depender de rutas absolutas propias de Debian/Ubuntu.
/// </summary>
public static class ToolLocator
{
    // En muchas distros /usr/sbin no está en el PATH del usuario, y en NixOS no
    // existen estas rutas: PATH manda, los directorios fijos son solo fallback.
    private static readonly string[] ExtraDirectories =
    {
        "/usr/local/sbin", "/usr/sbin", "/sbin", "/usr/local/bin", "/usr/bin", "/bin"
    };

    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.Ordinal);

    public static string? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return Cache.GetOrAdd(name, static n => Find(n));
    }

    public static bool IsAvailable(string name) => Resolve(name) is not null;

    public static string ResolveOrThrow(string name)
    {
        return Resolve(name)
               ?? throw new InvalidOperationException(
                   $"La herramienta requerida '{name}' no está disponible en PATH. " +
                   "Instala el paquete correspondiente para tu distribución.");
    }

    /// <summary>
    /// Reinicia la caché (útil en tests o si el PATH cambia durante la sesión).
    /// </summary>
    public static void Reset() => Cache.Clear();

    private static string? Find(string name)
    {
        // Si el nombre ya es una ruta, comprobarla directamente.
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains('/'))
            return File.Exists(name) ? name : null;

        var directories = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
            directories.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        directories.AddRange(ExtraDirectories);

        foreach (var directory in directories.Distinct(StringComparer.Ordinal))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            try
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (Exception)
            {
                // Un directorio del PATH puede ser inaccesible; se ignora.
            }
        }

        return null;
    }
}
