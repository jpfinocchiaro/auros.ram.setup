using System.Security.Cryptography;
using System.Text;
using Auros.Ram.Core;

namespace Auros.Ram.Persistence;

/// <summary>
/// Autoguardado (RNF-25): copia cifrada con la misma clave del archivo, en la carpeta de datos
/// del usuario. Si existe y es más nueva que el archivo, hubo un cierre anormal.
/// </summary>
public static class RecoveryStore
{
    public static string PathFor(string originalPath)
    {
        var full = Path.GetFullPath(originalPath);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            full = full.ToUpperInvariant();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
        return Path.Combine(AppPaths.RecoveryDirectory, hash + RmxFile.MatrixExtension);
    }

    public static void Save(RiskMatrixDocument doc, string originalPath, FileKey key) =>
        RmxFile.Save(doc, PathFor(originalPath), key);

    public static bool HasRecovery(string originalPath)
    {
        var recovery = PathFor(originalPath);
        return File.Exists(recovery) &&
               (!File.Exists(originalPath) || File.GetLastWriteTimeUtc(recovery) > File.GetLastWriteTimeUtc(originalPath));
    }

    /// <summary>Carga la recuperación; devuelve null si no corresponde a esta clave o está dañada.</summary>
    public static RiskMatrixDocument? TryLoad(string originalPath, FileKey key)
    {
        try
        {
            return RmxFile.OpenWithKey(PathFor(originalPath), key);
        }
        catch (RmxFileException)
        {
            return null;
        }
    }

    public static void Delete(string originalPath)
    {
        try
        {
            File.Delete(PathFor(originalPath));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
