namespace Auros.Ram.Persistence;

/// <summary>
/// Carpetas de la aplicación en el directorio de datos del usuario (RNF-16).
/// Nunca contienen datos de negocio en claro: solo configuración, logs y archivos cifrados.
/// </summary>
public static class AppPaths
{
    private static string? _root;

    public static string Root =>
        _root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "auros.ram");

    /// <summary>Cambia la carpeta raíz (pruebas o ejecución portable).</summary>
    public static void UseRoot(string root) => _root = root;

    public static string WorkDirectory => Ensure("work");
    public static string RecoveryDirectory => Ensure("recovery");
    public static string LogDirectory => Ensure("logs");
    /// <summary>Archivos de idioma agregados por el usuario.</summary>
    public static string LanguageDirectory => Ensure("idiomas");

    public static string SettingsFile => Path.Combine(Ensure(""), "settings.json");

    /// <summary>Borra archivos de trabajo que quedaron de una sesión anterior (todos están cifrados).</summary>
    public static void CleanWorkDirectory()
    {
        foreach (var file in Directory.EnumerateFiles(WorkDirectory))
        {
            TryDelete(file);
        }
    }

    internal static string NewWorkFile() => Path.Combine(WorkDirectory, $"{Guid.NewGuid():N}.db");

    internal static void DeleteWorkFile(string file)
    {
        TryDelete(file);
        TryDelete(file + "-journal");
        TryDelete(file + "-wal");
        TryDelete(file + "-shm");
    }

    private static string Ensure(string sub)
    {
        var dir = Path.Combine(Root, sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
