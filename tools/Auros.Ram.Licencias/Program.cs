using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auros.Ram.Core.Licensing;

// Herramienta de licencias de auros.ram (no se distribuye con la aplicación).
//
//   dotnet run --project tools/Auros.Ram.Licencias -- emitir "<código de equipo>" "<cliente>"
//   dotnet run --project tools/Auros.Ram.Licencias -- clave-publica
//   dotnet run --project tools/Auros.Ram.Licencias -- crear-claves      (una sola vez: ya está hecho)
//
// La clave privada vive en %USERPROFILE%\auros.ram-licencias\clave-privada.pem (o en la carpeta de la variable
// AUROS_LICENCIAS). Hay que guardarla con una copia de seguridad: sin ella no se pueden emitir más licencias, y si
// alguien la obtiene puede emitirlas. Nunca se sube al repositorio.
Console.OutputEncoding = Encoding.UTF8;

var folder = Environment.GetEnvironmentVariable("AUROS_LICENCIAS") is { Length: > 0 } custom
    ? custom
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "auros.ram-licencias");
var privateKeyFile = Path.Combine(folder, "clave-privada.pem");
var registry = Path.Combine(folder, "registro.csv");

switch (args)
{
    case ["crear-claves"]:
        if (File.Exists(privateKeyFile))
        {
            return Fail($"Ya existe {privateKeyFile}. No se reemplaza: las licencias emitidas dejarían de funcionar.");
        }

        Directory.CreateDirectory(folder);
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            File.WriteAllText(privateKeyFile, key.ExportPkcs8PrivateKeyPem());
            Console.WriteLine($"Clave privada: {privateKeyFile}  (hacé una copia de seguridad)");
            Console.WriteLine("Clave pública (va en src/Auros.Ram.App/Services/Edition.cs):");
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        return 0;

    case ["clave-publica"]:
        using (var key = LoadPrivateKey())
        {
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        return 0;

    case ["emitir", var machine, var customer]:
        using (var key = LoadPrivateKey())
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var license = License.Issue(new License(customer, machine, today), key);
            var publicKey = key.ExportSubjectPublicKeyInfo();
            License.Verify(license, machine, publicKey); // control: la clave emitida se verifica bien

            File.AppendAllText(registry,
                string.Join(';', today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Csv(customer), Csv(machine.Trim().ToUpperInvariant()), license)
                + Environment.NewLine, new UTF8Encoding(true));
            Console.WriteLine($"Licencia para «{customer.Trim()}», equipo {machine.Trim().ToUpperInvariant()}:");
            Console.WriteLine();
            Console.WriteLine(license);
            Console.WriteLine();
            Console.WriteLine($"(quedó anotada en {registry})");
        }

        return 0;

    default:
        Console.WriteLine("""
            Uso:
              emitir "<código de equipo>" "<cliente>"   genera la clave de licencia para ese equipo
              clave-publica                             muestra la clave pública que lleva la aplicación
              crear-claves                              crea el par de claves (una sola vez)
            """);
        return 1;
}

ECDsa LoadPrivateKey()
{
    if (!File.Exists(privateKeyFile))
    {
        throw new FileNotFoundException($"No está la clave privada en {privateKeyFile}.");
    }

    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(privateKeyFile));
    return key;
}

static string Csv(string value) => "\"" + value.Trim().Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
