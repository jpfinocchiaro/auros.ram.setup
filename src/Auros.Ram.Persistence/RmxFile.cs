using System.Reflection;
using System.Security.Cryptography;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Microsoft.Data.Sqlite;

namespace Auros.Ram.Persistence;

public static class AppInfo
{
    /// <summary>Versión semántica de la aplicación (RNF-60), sin el sufijo de commit.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];
}

public sealed class RmxOpenResult(RiskMatrixDocument document, FileKey key, RmxHeader header, string? backupPath)
{
    public RiskMatrixDocument Document { get; } = document;
    public FileKey Key { get; } = key;
    public RmxHeader Header { get; } = header;

    /// <summary>Si el archivo se migró a un esquema nuevo, ruta de la copia del original (RNF-27).</summary>
    public string? BackupPath { get; } = backupPath;
}

/// <summary>
/// Abre y guarda archivos de matriz y de plantilla (RNF-11 a RNF-14, RNF-24, RNF-26, RNF-30, RNF-37).
/// La base SQLCipher solo existe en disco cifrada: en el archivo final y, mientras se arma o se lee,
/// en un archivo de trabajo en la carpeta de datos del usuario que se borra al terminar.
/// Excepción: un archivo creado sin contraseña (<see cref="KdfAlgorithm.None"/>) guarda la base sin cifrar.
/// </summary>
public static class RmxFile
{
    public const string MatrixExtension = ".rmx";
    public const string TemplateExtension = ".rmt";

    private const int SqliteNotADatabase = 26;

    static RmxFile() => SQLitePCL.Batteries_V2.Init();

    public static int CurrentSchemaVersion => Schema.CurrentVersion;

    public static void Save(RiskMatrixDocument doc, string path, FileKey key, RmxFileKind kind = RmxFileKind.Matrix)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(key);
        var work = AppPaths.NewWorkFile();
        try
        {
            using (var c = OpenEncrypted(work, key))
            {
                Schema.Migrate(c, 0);
                DocumentStore.Write(c, doc);
            }

            var payload = File.ReadAllBytes(work);
            var header = new RmxHeader(kind, key.Parameters, Schema.CurrentVersion, AppInfo.Version, payload.Length, SHA256.HashData(payload));
            AtomicFile.Write(path, header.ToBytes(), payload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RmxFileException(RmxErrorKind.IoError, ex);
        }
        finally
        {
            AppPaths.DeleteWorkFile(work);
        }
    }

    /// <summary>Lee solo la cabecera (sin contraseña), p. ej. para mostrar la versión del archivo.</summary>
    public static RmxHeader ReadHeader(string path)
    {
        var buffer = new byte[RmxHeader.Size];
        try
        {
            using var fs = File.OpenRead(path);
            fs.ReadExactly(buffer);
        }
        catch (EndOfStreamException)
        {
            throw new RmxFileException(RmxErrorKind.NotRmx);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RmxFileException(RmxErrorKind.IoError, ex);
        }

        var header = RmxHeader.Parse(buffer);
        EnsureSupported(header);
        return header;
    }

    private static void EnsureSupported(RmxHeader header)
    {
        if (header.SchemaVersion > Schema.CurrentVersion)
        {
            throw new RmxFileException(RmxErrorKind.NewerVersion);
        }

        if (header.SchemaVersion < Schema.OldestSupportedVersion)
        {
            throw new RmxFileException(RmxErrorKind.ObsoleteVersion);
        }
    }

    /// <summary>
    /// Verifica, descifra y carga el archivo. Si es de un esquema anterior, guarda una copia del original
    /// y lo migra en el lugar (RNF-14, RNF-27). Un error nunca modifica el archivo (RNF-30).
    /// Si el archivo no tiene contraseña, <paramref name="password"/> se ignora.
    /// </summary>
    public static RmxOpenResult Open(string path, string? password, RmxFileKind expected = RmxFileKind.Matrix)
    {
        var (header, payload) = ReadVerified(path, expected);
        if (header.IsProtected && string.IsNullOrEmpty(password))
        {
            throw new RmxFileException(RmxErrorKind.WrongPassword);
        }

        var key = FileKey.Derive(password ?? "", header.Kdf);
        try
        {
            var doc = Load(payload, key, out var fromVersion);
            string? backup = null;
            if (fromVersion < Schema.CurrentVersion)
            {
                backup = BackupPathFor(path, fromVersion);
                File.Copy(path, backup, overwrite: false);
                Save(doc, path, key, expected);
                header = ReadHeader(path);
            }

            return new RmxOpenResult(doc, key, header, backup);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Abre con una clave ya derivada (archivos de recuperación de la misma sesión).</summary>
    public static RiskMatrixDocument OpenWithKey(string path, FileKey key, RmxFileKind expected = RmxFileKind.Matrix)
    {
        var (header, payload) = ReadVerified(path, expected);
        if (!header.Kdf.Salt.AsSpan().SequenceEqual(key.Parameters.Salt) || header.Kdf.Algorithm != key.Parameters.Algorithm)
        {
            throw new RmxFileException(RmxErrorKind.WrongPassword);
        }

        return Load(payload, key, out _);
    }

    // ---- Plantillas (RF-06, RNF-17) -----------------------------------------

    public static void SaveTemplate(IEnumerable<Scale> scales, string path, FileKey key)
    {
        var doc = new RiskMatrixDocument(new ProjectInfo { Name = Path.GetFileNameWithoutExtension(path) }, scales.Select(s => s.Clone()), []);
        Save(doc, path, key, RmxFileKind.Template);
    }

    public static IReadOnlyList<Scale> OpenTemplate(string path, string? password)
    {
        var result = Open(path, password, RmxFileKind.Template);
        result.Key.Dispose();
        return result.Document.Scales;
    }

    // ---- Interno -----------------------------------------------------------

    private static (RmxHeader Header, byte[] Payload) ReadVerified(string path, RmxFileKind expected)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RmxFileException(RmxErrorKind.IoError, ex);
        }

        var header = RmxHeader.Parse(data);
        EnsureSupported(header);

        if (header.Kind != expected)
        {
            throw new RmxFileException(RmxErrorKind.WrongKind);
        }

        var payload = data[RmxHeader.Size..];
        if (payload.Length != header.PayloadLength ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), header.PayloadHash))
        {
            throw new RmxFileException(RmxErrorKind.Corrupted);
        }

        return (header, payload);
    }

    private static RiskMatrixDocument Load(byte[] payload, FileKey key, out int fromVersion)
    {
        var work = AppPaths.NewWorkFile();
        try
        {
            File.WriteAllBytes(work, payload);
            using var c = OpenEncrypted(work, key);
            try
            {
                Sql.Scalar(c, "SELECT count(*) FROM sqlite_master;");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNotADatabase)
            {
                throw new RmxFileException(RmxErrorKind.WrongPassword, ex);
            }

            VerifyIntegrity(c, key.IsProtected);
            fromVersion = Schema.GetVersion(c);
            if (fromVersion < Schema.CurrentVersion)
            {
                Schema.Migrate(c, fromVersion);
            }

            return DocumentStore.Read(c);
        }
        catch (SqliteException ex)
        {
            throw new RmxFileException(RmxErrorKind.Corrupted, ex);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException)
        {
            throw new RmxFileException(RmxErrorKind.Corrupted, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RmxFileException(RmxErrorKind.IoError, ex);
        }
        finally
        {
            AppPaths.DeleteWorkFile(work);
        }
    }

    private static void VerifyIntegrity(SqliteConnection c, bool encrypted)
    {
        // HMAC de cada página (SQLCipher) y consistencia estructural (SQLite). RNF-26.
        if (encrypted)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA cipher_integrity_check;";
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                throw new RmxFileException(RmxErrorKind.Corrupted);
            }
        }

        if (Sql.Scalar(c, "PRAGMA integrity_check;") as string != "ok")
        {
            throw new RmxFileException(RmxErrorKind.Corrupted);
        }
    }

    private static SqliteConnection OpenEncrypted(string file, FileKey key)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var c = new SqliteConnection(cs);
        try
        {
            c.Open();
            // Clave en bruto: la derivación lenta ya se hizo con Argon2id/PBKDF2 (RNF-32).
            // Sin contraseña no se da clave y SQLCipher trabaja como SQLite común.
            if (key.IsProtected)
            {
                Sql.Execute(c, $"PRAGMA key = \"x'{Convert.ToHexString(key.Key)}'\";");
            }

            Sql.Execute(c, "PRAGMA foreign_keys = ON;");
            return c;
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }

    private static string BackupPathFor(string path, int version)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var candidate = Path.Combine(dir, $"{name}.v{version}{ext}");
        for (var i = 2; File.Exists(candidate); i++)
        {
            candidate = Path.Combine(dir, $"{name}.v{version} ({i}){ext}");
        }

        return candidate;
    }
}

/// <summary>RNF-24: se escribe un temporal junto al original y se reemplaza con un renombrado.</summary>
internal static class AtomicFile
{
    public static void Write(string path, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        var tmp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                fs.Write(header);
                fs.Write(payload);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
