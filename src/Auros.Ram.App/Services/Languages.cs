using System.Text;
using Auros.Ram.Core.Text;
using Auros.Ram.Persistence;

namespace Auros.Ram.App.Services;

/// <summary>Un idioma que se puede elegir en Preferencias.</summary>
/// <param name="Region">País de la bandera (AR, US, BR…).</param>
public sealed record LanguageOption(string Code, string Name, string? Path, string Region = "")
{
    public override string ToString() => Name;
}

/// <summary>
/// Idiomas de la aplicación: el español (de origen), los incluidos (inglés y portugués) y los que agrega el usuario
/// en la carpeta "idiomas" de sus datos (un archivo agregado reemplaza al incluido del mismo código).
/// El idioma elegido se aplica al iniciar.
/// </summary>
public static class Languages
{
    private const string EmbeddedPrefix = "Languages.";

    public static string Folder => AppPaths.LanguageDirectory;

    /// <summary>El idioma con el que está funcionando la aplicación (el elegido se aplica al reiniciar).</summary>
    public static LanguageOption Current { get; private set; } = new(Localization.BaseCode, "Español", null, "ES");

    /// <summary>Registra los textos traducibles y aplica el idioma guardado en las preferencias.</summary>
    public static void Startup(string? code)
    {
        Localization.Register(
            typeof(Resources.Strings),
            typeof(Core.Resources.Messages),
            typeof(Export.Resources.ExportText),
            typeof(Persistence.Resources.FileMessages));
        try
        {
            Localization.Apply(Load(code));
            Current = List().FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? List()[0];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Languages.Startup");
        }
    }

    public static IReadOnlyList<LanguageOption> List()
    {
        var options = new Dictionary<string, LanguageOption>(StringComparer.OrdinalIgnoreCase)
        {
            [Localization.BaseCode] = new(Localization.BaseCode, "Español", null, "ES"),   // bandera de España (las fechas siguen el formato de Windows)
        };
        foreach (var (code, content) in Embedded())
        {
            var file = Localization.Parse(content);
            options[code] = new(code, file.Name, null, Localization.RegionOf(file));
        }

        foreach (var path in Directory.EnumerateFiles(Folder, "*.txt"))
        {
            try
            {
                var file = Localization.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (file.Code.Length > 0 && file.Code != Localization.BaseCode)
                {
                    options[file.Code] = new(file.Code, file.Name, path, Localization.RegionOf(file));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error(ex, "Languages.List");
            }
        }

        return [.. options.Values.OrderBy(o => o.Code == Localization.BaseCode ? 0 : 1).ThenBy(o => o.Name, StringComparer.CurrentCulture)];
    }

    /// <summary>El archivo del idioma (null para el español o si no existe).</summary>
    public static LanguageFile? Load(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals(Localization.BaseCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var user = Path.Combine(Folder, code.ToLowerInvariant() + ".txt");
        if (File.Exists(user))
        {
            return Localization.Parse(File.ReadAllText(user, Encoding.UTF8));
        }

        return Embedded().FirstOrDefault(e => e.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { Content: { } content }
            ? Localization.Parse(content)
            : null;
    }

    /// <summary>
    /// Nombres estándar de escalas y niveles (los de los tamaños 2 × 2 a 10 × 10) escritos en cualquiera de los
    /// idiomas disponibles, con su traducción al idioma actual.
    /// </summary>
    public static IReadOnlyDictionary<string, string> StandardNames()
    {
        static bool IsStandard(string id) =>
            id.StartsWith("Level_", StringComparison.Ordinal) || id.StartsWith("Graded_", StringComparison.Ordinal) ||
            id.StartsWith("Impact_", StringComparison.Ordinal) || id.StartsWith("Probability_", StringComparison.Ordinal) ||
            id.StartsWith("DefaultScale_", StringComparison.Ordinal);

        static IEnumerable<string> Expand(string id, string text) =>
            id is "DefaultScale_Impact" or "DefaultScale_Probability"
                ? Enumerable.Range(2, 9).Select(size => text.Replace("{0}", size.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                : [text];

        var section = nameof(Core.Resources.Messages);
        var spanish = Localization.BaseTexts()[section].Where(t => IsStandard(t.Key)).ToList();
        var others = List().Where(l => l.Code != Localization.BaseCode).Select(l => Load(l.Code)).OfType<LanguageFile>().ToList();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, text) in spanish)
        {
            var current = Expand(id, Core.Resources.Messages.ResourceManager.GetString(id) ?? text).ToList();
            foreach (var source in others.Select(o => o.Get(section, id)).OfType<string>().Prepend(text))
            {
                foreach (var (from, to) in Expand(id, source).Zip(current))
                {
                    map.TryAdd(from.Trim(), to);
                }
            }
        }

        return map;
    }

    /// <summary>Copia un archivo de idioma a la carpeta de idiomas (con el nombre de su código) y devuelve su revisión.</summary>
    public static (LanguageFile File, LanguageCheck Check) Install(string path)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
        var file = Localization.Parse(content);
        if (file.Code.Length is < 2 or > 10 || file.Code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            file.Code == Localization.BaseCode)
        {
            throw new InvalidDataException();
        }

        File.WriteAllText(Path.Combine(Folder, file.Code + ".txt"), content, new UTF8Encoding(true));
        return (file, Localization.Check(file));
    }

    private static IEnumerable<(string Code, string Content)> Embedded()
    {
        var assembly = typeof(Languages).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            yield return (Path.GetFileNameWithoutExtension(name[EmbeddedPrefix.Length..]), reader.ReadToEnd());
        }
    }
}
