using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auros.Ram.Core.Licensing;
using Auros.Ram.Core.Services;
using Auros.Ram.Licencias;

// Herramienta de licencias y control de ventas de auros.ram (no se distribuye con la aplicación).
//
//   dotnet run --project tools/Auros.Ram.Licencias -- emitir "<código de equipo>" "<cliente>" [opciones de la venta]
//   dotnet run --project tools/Auros.Ram.Licencias -- reenviar "<código de equipo>"
//   dotnet run --project tools/Auros.Ram.Licencias -- ventas
//   dotnet run --project tools/Auros.Ram.Licencias -- clave-publica
//   dotnet run --project tools/Auros.Ram.Licencias -- crear-claves      (una sola vez: ya está hecho)
//
// La clave privada vive en %USERPROFILE%\auros.ram-licencias\clave-privada.pem (o en la carpeta de la variable
// AUROS_LICENCIAS). Hay que guardarla con una copia de seguridad: sin ella no se pueden emitir más licencias, y si
// alguien la obtiene puede emitirlas. Nunca se sube al repositorio.
//
// Cada licencia emitida es una venta y queda como fila en ventas.csv, en la misma carpeta (separado por «;», se abre
// con Excel): fecha, cliente, email, equipo, monto, moneda, medio de pago, nota y la clave entregada. Si la venta
// tiene email, la licencia se le envía al cliente con la cuenta configurada en correo.json (ver Correo.cs).
Console.OutputEncoding = Encoding.UTF8;

var folder = Environment.GetEnvironmentVariable("AUROS_LICENCIAS") is { Length: > 0 } custom
    ? custom
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "auros.ram-licencias");
var privateKeyFile = Path.Combine(folder, "clave-privada.pem");
var sales = Path.Combine(folder, "ventas.csv");
var oldRegistry = Path.Combine(folder, "registro.csv");
string[] header = ["fecha", "cliente", "email", "equipo", "monto", "moneda", "medio_pago", "nota", "licencia"];
var utf8Bom = new UTF8Encoding(true);

try
{
    return Run();
}
catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or DomainException or IOException)
{
    return Fail(ex.Message);
}

int Run()
{
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
                Console.WriteLine($"Clave privada: {privateKeyFile}  (haz una copia de seguridad)");
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

        case ["emitir", var machine, var customer, .. var rest]:
            return Issue(machine, customer, ParseOptions(rest));

        case ["reenviar", var machine, .. var rest]:
            return Resend(machine, ParseOptions(rest));

        case ["ventas"]:
            return ListSales();

        default:
            Console.WriteLine("""
                Uso:
                  emitir "<código de equipo>" "<cliente>" [opciones]   genera la licencia y anota la venta
                      --email <correo>       email del cliente
                      --monto <importe>      lo que pagó, por ejemplo 120 o 120.50
                      --moneda <código>      USD, ARS, EUR… (si no se indica: USD)
                      --pago <medio>         por ejemplo "Mercado Pago", "transferencia", "PayPal"
                      --nota <texto>         cualquier aclaración (factura, reemplazo de equipo…)
                      --idioma <es|en|pt|fr> idioma del email al cliente (si no se indica: es)
                      --no-enviar            anota la venta pero no envía el email
                  reenviar "<código de equipo>" [--email <correo>] [--idioma <es|en|pt|fr>]
                                             vuelve a enviar la última licencia de ese equipo
                  ventas                     lista las ventas y los totales por moneda y por mes
                  clave-publica              muestra la clave pública que lleva la aplicación
                  crear-claves               crea el par de claves (una sola vez)

                Ejemplo:
                  emitir "QVXP-XQSA-FMEW-6NRM" "Estudio Pérez" --email ana@perez.com --monto 120 --pago "Mercado Pago"
                """);
            return 1;
    }
}

int Issue(string machine, string customer, Dictionary<string, string> options)
{
    machine = machine.Trim().ToUpperInvariant();
    customer = customer.Trim();
    var amount = "";
    if (options.TryGetValue("monto", out var rawAmount))
    {
        if (!decimal.TryParse(rawAmount.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            return Fail($"El monto «{rawAmount}» no es un número válido. Usa, por ejemplo, 120 o 120.50.");
        }

        amount = value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    var currency = options.GetValueOrDefault("moneda", amount.Length > 0 ? "USD" : "").Trim().ToUpperInvariant();
    var email = options.GetValueOrDefault("email", "");
    var language = Language(options);

    using var key = LoadPrivateKey();
    var today = DateOnly.FromDateTime(DateTime.Today);
    var license = License.Issue(new License(customer, machine, today), key);
    License.Verify(license, machine, key.ExportSubjectPublicKeyInfo()); // control: la clave emitida se verifica bien

    PrepareSalesFile();
    File.AppendAllText(sales, Row(
        today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), customer, email,
        machine, amount, currency, options.GetValueOrDefault("pago", ""), options.GetValueOrDefault("nota", ""), license), utf8Bom);

    Console.WriteLine($"Licencia para «{customer}», equipo {machine}:");
    Console.WriteLine();
    Console.WriteLine(license);
    Console.WriteLine();
    Console.WriteLine($"(venta anotada en {sales})");
    if (amount.Length == 0)
    {
        Console.WriteLine("Aviso: no indicaste --monto; la venta quedó sin importe. Puedes completarlo en la planilla.");
    }

    if (email.Length == 0)
    {
        Console.WriteLine("No indicaste --email: la licencia no se envió. Pásasela al cliente tú o usa «reenviar» con --email.");
    }
    else if (!options.ContainsKey("no-enviar"))
    {
        Deliver(email, customer, machine, license, language);
    }

    return 0;
}

int Resend(string machine, Dictionary<string, string> options)
{
    machine = machine.Trim().ToUpperInvariant();
    PrepareSalesFile();
    var row = File.ReadAllLines(sales, utf8Bom).Skip(1).Where(l => l.Length > 0).Select(ParseRow)
        .LastOrDefault(r => string.Equals(r[3], machine, StringComparison.Ordinal));
    if (row is null)
    {
        return Fail($"No hay ninguna licencia emitida para el equipo {machine} en {sales}.");
    }

    var email = options.GetValueOrDefault("email", row[2]);
    if (email.Length == 0)
    {
        return Fail($"La venta del equipo {machine} no tiene email. Indícalo con --email.");
    }

    return Deliver(email, row[1], machine, row[8], Language(options)) ? 0 : 1;
}

bool Deliver(string email, string customer, string machine, string license, string language)
{
    var error = Correo.Send(folder, email, customer, machine, license, language);
    if (error is null)
    {
        Console.WriteLine($"Licencia enviada a {email}.");
        return true;
    }

    Console.Error.WriteLine($"No se pudo enviar el email a {email}: {error}");
    Console.Error.WriteLine($"La venta quedó anotada igual. Para reintentar: reenviar \"{machine}\"");
    return false;
}

static string Language(Dictionary<string, string> options)
{
    var language = options.GetValueOrDefault("idioma", "es").ToLowerInvariant();
    return Correo.Languages.Contains(language)
        ? language
        : throw new ArgumentException($"Idioma no reconocido: {language}. Usa es, en, pt o fr.");
}

int ListSales()
{
    PrepareSalesFile();
    var rows = File.ReadAllLines(sales, utf8Bom).Skip(1).Where(l => l.Length > 0).Select(ParseRow).ToList();
    if (rows.Count == 0)
    {
        Console.WriteLine($"Todavía no hay ventas en {sales}.");
        return 0;
    }

    Console.WriteLine($"{"Fecha",-10}  {"Cliente",-28}  {"Email",-26}  {"Monto",12}  {"Pago",-16}  Equipo");
    foreach (var r in rows)
    {
        var money = r[4].Length > 0 ? $"{r[4]} {r[5]}" : "—";
        Console.WriteLine($"{r[0],-10}  {Cut(r[1], 28),-28}  {Cut(r[2], 26),-26}  {money,12}  {Cut(r[6], 16),-16}  {r[3]}");
    }

    Console.WriteLine();
    Console.WriteLine($"Licencias emitidas: {rows.Count}");
    var paid = rows.Where(r => r[4].Length > 0).ToList();
    foreach (var g in paid.GroupBy(r => r[5]).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"Total {g.Key}: {g.Sum(r => Amount(r[4])).ToString("N2", CultureInfo.InvariantCulture)}  ({g.Count()} {(g.Count() == 1 ? "venta" : "ventas")})");
    }

    if (rows.Count > paid.Count)
    {
        Console.WriteLine($"Sin monto: {rows.Count - paid.Count}");
    }

    Console.WriteLine();
    Console.WriteLine("Por mes:");
    foreach (var g in rows.GroupBy(r => r[0][..7]).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        var totals = string.Join(", ", g.Where(r => r[4].Length > 0).GroupBy(r => r[5])
            .Select(c => $"{c.Sum(r => Amount(r[4])).ToString("N2", CultureInfo.InvariantCulture)} {c.Key}"));
        Console.WriteLine($"  {g.Key}  {g.Count(),3} {(g.Count() == 1 ? "licencia " : "licencias")}  {totals}");
    }

    return 0;
}

// Crea ventas.csv con su encabezado. Si existe el registro.csv de antes (fecha;cliente;equipo;licencia), pasa sus
// filas a la planilla nueva y lo deja como registro.csv.anterior.
void PrepareSalesFile()
{
    if (File.Exists(sales))
    {
        return;
    }

    Directory.CreateDirectory(folder);
    var text = new StringBuilder(Row(header));
    if (File.Exists(oldRegistry))
    {
        foreach (var line in File.ReadAllLines(oldRegistry, utf8Bom).Where(l => l.Length > 0))
        {
            var old = ParseRow(line);
            if (old.Length >= 4)
            {
                text.Append(Row(old[0], old[1], "", old[2], "", "", "", "emitida antes de llevar el control de ventas", old[3]));
            }
        }

        File.WriteAllText(sales, text.ToString(), utf8Bom);
        File.Move(oldRegistry, oldRegistry + ".anterior");
        Console.WriteLine($"(las licencias de registro.csv pasaron a {sales})");
        return;
    }

    File.WriteAllText(sales, text.ToString(), utf8Bom);
}

Dictionary<string, string> ParseOptions(string[] rest)
{
    string[] known = ["email", "monto", "moneda", "pago", "nota", "idioma"];
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "--no-enviar")
        {
            options["no-enviar"] = "";
            continue;
        }

        var name = rest[i].StartsWith("--", StringComparison.Ordinal) ? rest[i][2..] : "";
        if (!known.Contains(name) || i + 1 >= rest.Length)
        {
            throw new ArgumentException($"Opción no reconocida o sin valor: {rest[i]}. Opciones: --email, --monto, --moneda, --pago, --nota, --idioma, --no-enviar.");
        }

        options[name] = rest[++i].Trim();
    }

    return options;
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

static string Row(params string[] values) => string.Join(';', values.Select(Csv)) + Environment.NewLine;

static string Csv(string value) =>
    value.IndexOfAny([';', '"', '\n', '\r']) >= 0 || value.Length != value.Trim().Length
        ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
        : value;

// Lee una fila separada por «;» con comillas al estilo de Excel.
static string[] ParseRow(string line)
{
    var fields = new List<string>();
    var current = new StringBuilder();
    var quoted = false;
    for (var i = 0; i < line.Length; i++)
    {
        var c = line[i];
        if (quoted)
        {
            if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
            else if (c == '"') { quoted = false; }
            else { current.Append(c); }
        }
        else if (c == '"') { quoted = true; }
        else if (c == ';') { fields.Add(current.ToString()); current.Clear(); }
        else { current.Append(c); }
    }

    fields.Add(current.ToString());
    while (fields.Count < 9) { fields.Add(""); }
    return [.. fields];
}

static decimal Amount(string value) =>
    decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;

static string Cut(string value, int width) => value.Length <= width ? value : value[..(width - 1)] + "…";

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
