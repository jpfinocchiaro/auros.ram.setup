using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Auros.Ram.Persistence;

public enum RmxFileKind : byte
{
    Matrix = 1,
    Template = 2,
}

/// <summary>
/// Cabecera mínima no cifrada (RNF-13). Formato (little endian), ver docs/formato-archivo.md:
/// <code>
///   0   8  firma "AUROSRAM"
///   8   2  versión del contenedor
///  10   1  tipo de archivo (1 matriz, 2 plantilla)
///  11   1  algoritmo de derivación (1 Argon2id, 2 PBKDF2-SHA512)
///  12   4  iteraciones
///  16   4  memoria KiB
///  20   4  paralelismo
///  24  32  sal
///  56   4  versión del esquema
///  60  16  versión de la aplicación (ASCII, con relleno de ceros)
///  76   8  largo del contenido cifrado
///  84  32  SHA-256 del contenido cifrado
/// 116  32  SHA-256 de los bytes 0..115
/// 148      contenido: base SQLCipher (todo cifrado)
/// </code>
/// </summary>
public sealed record RmxHeader(
    RmxFileKind Kind,
    KdfParameters Kdf,
    int SchemaVersion,
    string AppVersion,
    long PayloadLength,
    byte[] PayloadHash)
{
    public const int Size = 148;
    public const ushort ContainerVersion = 1;
    private static readonly byte[] Magic = "AUROSRAM"u8.ToArray();

    public byte[] ToBytes()
    {
        var buffer = new byte[Size];
        var span = buffer.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], ContainerVersion);
        span[10] = (byte)Kind;
        span[11] = (byte)Kdf.Algorithm;
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], Kdf.Iterations);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], Kdf.MemoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], Kdf.Parallelism);
        Kdf.Salt.CopyTo(span[24..56]);
        BinaryPrimitives.WriteInt32LittleEndian(span[56..], SchemaVersion);
        var version = Encoding.ASCII.GetBytes(AppVersion);
        version.AsSpan(0, Math.Min(16, version.Length)).CopyTo(span[60..76]);
        BinaryPrimitives.WriteInt64LittleEndian(span[76..], PayloadLength);
        PayloadHash.CopyTo(span[84..116]);
        SHA256.HashData(span[..116], span[116..148]);
        return buffer;
    }

    /// <summary>Lee y verifica la cabecera. No deriva claves ni toca el contenido.</summary>
    public static RmxHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size || !data[..8].SequenceEqual(Magic))
        {
            throw new RmxFileException(RmxErrorKind.NotRmx);
        }

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data[..116], hash);
        if (!CryptographicOperations.FixedTimeEquals(hash, data[116..148]))
        {
            throw new RmxFileException(RmxErrorKind.Corrupted);
        }

        var container = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (container > ContainerVersion)
        {
            throw new RmxFileException(RmxErrorKind.NewerVersion);
        }

        var kind = (RmxFileKind)data[10];
        if (!Enum.IsDefined(kind))
        {
            throw new RmxFileException(RmxErrorKind.Corrupted);
        }

        var kdf = new KdfParameters(
            (KdfAlgorithm)data[11],
            BinaryPrimitives.ReadInt32LittleEndian(data[12..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[16..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[20..]),
            data[24..56].ToArray());

        var appVersion = Encoding.ASCII.GetString(data[60..76]).TrimEnd('\0');
        return new RmxHeader(
            kind,
            kdf,
            BinaryPrimitives.ReadInt32LittleEndian(data[56..]),
            appVersion,
            BinaryPrimitives.ReadInt64LittleEndian(data[76..]),
            data[84..116].ToArray());
    }
}
