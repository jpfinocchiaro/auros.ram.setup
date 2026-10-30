using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Auros.Ram.Persistence;

public enum KdfAlgorithm : byte
{
    /// <summary>Archivo sin contraseña: la base se guarda sin cifrar.</summary>
    None = 0,
    Argon2id = 1,
    Pbkdf2Sha512 = 2,
}

/// <summary>
/// Parámetros de derivación de clave (RNF-32). Viajan en la cabecera del archivo
/// para poder endurecerlos en versiones futuras sin romper los archivos existentes.
/// </summary>
public sealed record KdfParameters(KdfAlgorithm Algorithm, int Iterations, int MemoryKiB, int Parallelism, byte[] Salt)
{
    public const int SaltLength = 32;
    public const int MinPbkdf2Iterations = 256_000;

    public static KdfParameters CreateDefault() =>
        new(KdfAlgorithm.Argon2id, Iterations: 3, MemoryKiB: 64 * 1024, Parallelism: 4, RandomNumberGenerator.GetBytes(SaltLength));

    /// <summary>Sin contraseña. La sal aleatoria se conserva para identificar el archivo de recuperación.</summary>
    public static KdfParameters CreateUnprotected() =>
        new(KdfAlgorithm.None, Iterations: 0, MemoryKiB: 0, Parallelism: 0, RandomNumberGenerator.GetBytes(SaltLength));

    public bool IsProtected => Algorithm != KdfAlgorithm.None;

    internal void EnsureAcceptable()
    {
        var ok = Salt.Length == SaltLength && Algorithm switch
        {
            KdfAlgorithm.Argon2id => Iterations is >= 1 and <= 64 && MemoryKiB is >= 8 * 1024 and <= 4 * 1024 * 1024 && Parallelism is >= 1 and <= 64,
            KdfAlgorithm.Pbkdf2Sha512 => Iterations is >= MinPbkdf2Iterations and <= 50_000_000,
            KdfAlgorithm.None => Iterations == 0 && MemoryKiB == 0 && Parallelism == 0,
            _ => false,
        };
        if (!ok)
        {
            throw new RmxFileException(RmxErrorKind.Corrupted);
        }
    }
}

/// <summary>
/// Clave derivada de un archivo abierto (RNF-33). La contraseña no se conserva:
/// solo la clave, que se borra de la memoria al cerrar el archivo.
/// </summary>
public sealed class FileKey : IDisposable
{
    public const int KeyLength = 32;
    private readonly byte[] _key;
    private bool _disposed;

    private FileKey(KdfParameters parameters, byte[] key)
    {
        Parameters = parameters;
        _key = key;
    }

    public KdfParameters Parameters { get; }

    /// <summary>False si el archivo no tiene contraseña (no se cifra ni se bloquea).</summary>
    public bool IsProtected => Parameters.IsProtected;

    internal ReadOnlySpan<byte> Key
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    /// <summary>Clave nueva con sal aleatoria (archivo nuevo o cambio de contraseña).</summary>
    public static FileKey CreateNew(string password) => Derive(password, KdfParameters.CreateDefault());

    /// <summary>Archivo sin contraseña.</summary>
    public static FileKey CreateUnprotected() => new(KdfParameters.CreateUnprotected(), []);

    /// <summary>Con contraseña si se escribió una; sin contraseña si está vacía.</summary>
    public static FileKey Create(string? password) =>
        string.IsNullOrEmpty(password) ? CreateUnprotected() : CreateNew(password);

    public static FileKey Derive(string password, KdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(password);
        parameters.EnsureAcceptable();
        if (!parameters.IsProtected)
        {
            return new FileKey(parameters, []);
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var key = parameters.Algorithm switch
            {
                KdfAlgorithm.Argon2id => Argon2(passwordBytes, parameters),
                _ => Rfc2898DeriveBytes.Pbkdf2(passwordBytes, parameters.Salt, parameters.Iterations, HashAlgorithmName.SHA512, KeyLength),
            };
            return new FileKey(parameters, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>Comprueba una contraseña contra esta clave (desbloqueo tras inactividad, RNF-36).</summary>
    public bool Matches(string password)
    {
        if (!IsProtected)
        {
            return true;
        }

        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        using var candidate = Derive(password, Parameters);
        return CryptographicOperations.FixedTimeEquals(candidate.Key, Key);
    }

    /// <summary>Copia independiente, para que un guardado en segundo plano no dependa de la vida de esta.</summary>
    public FileKey Clone() => new(Parameters, Key.ToArray());

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }

    private static byte[] Argon2(byte[] password, KdfParameters p)
    {
        using var argon = new Argon2id(password)
        {
            Salt = p.Salt,
            Iterations = p.Iterations,
            MemorySize = p.MemoryKiB,
            DegreeOfParallelism = p.Parallelism,
        };
        return argon.GetBytes(KeyLength);
    }
}
