using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auros.Ram.Core.Resources;
using Auros.Ram.Core.Services;

namespace Auros.Ram.Core.Licensing;

/// <summary>
/// Licencia de la versión paga: a nombre de un cliente y atada a un equipo (su código de equipo).
/// La clave va firmada con ECDSA P-256: solo la puede generar quien tiene la clave privada (la herramienta de
/// licencias, que no se distribuye); la aplicación la verifica con la clave pública, sin conexión a internet.
/// Una clave copiada a otro equipo no sirve, porque el código de equipo no coincide.
/// </summary>
public sealed record License(string Customer, string MachineCode, DateOnly IssuedOn)
{
    private const string Prefix = "AURAM1-";

    /// <summary>Genera la clave de licencia (solo la herramienta de licencias, con la clave privada).</summary>
    public static string Issue(License license, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(privateKey);
        var normalized = license with { Customer = license.Customer.Trim(), MachineCode = NormalizeCode(license.MachineCode) };
        if (normalized.Customer.Length == 0 || normalized.MachineCode.Length == 0)
        {
            throw new ArgumentException("Faltan el cliente o el código de equipo.", nameof(license));
        }

        var payload = Payload(normalized);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256);
        return Prefix + Base64Url(payload) + "." + Base64Url(signature);
    }

    /// <summary>
    /// Verifica una clave de licencia para este equipo. Si no es válida o es de otro equipo, lanza
    /// <see cref="DomainException"/> con un mensaje para el usuario.
    /// </summary>
    /// <param name="publicKey">Clave pública (SubjectPublicKeyInfo) con la que se firman las licencias.</param>
    public static License Verify(string? key, string machineCode, byte[] publicKey)
    {
        var text = new string((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!text.StartsWith(Prefix, StringComparison.Ordinal) || text[Prefix.Length..].Split('.') is not [var body, var sig])
        {
            throw new DomainException(Messages.License_Invalid);
        }

        License license;
        try
        {
            var payload = FromBase64Url(body);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (!ecdsa.VerifyData(payload, FromBase64Url(sig), HashAlgorithmName.SHA256))
            {
                throw new DomainException(Messages.License_Invalid);
            }

            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            license = new License(
                root.GetProperty("c").GetString() ?? "",
                root.GetProperty("m").GetString() ?? "",
                DateOnly.ParseExact(root.GetProperty("d").GetString() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or KeyNotFoundException or InvalidOperationException)
        {
            throw new DomainException(Messages.License_Invalid);
        }

        if (NormalizeCode(license.MachineCode) != NormalizeCode(machineCode))
        {
            throw new DomainException(Messages.License_OtherMachine);
        }

        return license;
    }

    /// <summary>
    /// Código de equipo que se muestra al usuario (XXXX-XXXX-XXXX-XXXX), a partir del identificador del sistema operativo.
    /// Se guarda solo un resumen (hash): el identificador original no se puede reconstruir.
    /// </summary>
    public static string MachineCodeFrom(string machineId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("auros.ram|" + machineId.Trim().ToUpperInvariant()));
        var code = Base32(hash.AsSpan(0, 10));
        return string.Join('-', Enumerable.Range(0, 4).Select(i => code.Substring(i * 4, 4)));
    }

    private static string NormalizeCode(string code) =>
        new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static byte[] Payload(License license)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("c", license.Customer);
            writer.WriteString("m", license.MachineCode);
            writer.WriteString("d", license.IssuedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }

    /// <summary>Base32 sin letras que se confunden (sin I, L, O, U): fácil de dictar o copiar a mano.</summary>
    private static string Base32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        return sb.ToString();
    }
}
