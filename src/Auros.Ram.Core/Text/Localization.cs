using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;

namespace Auros.Ram.Core.Text;

/// <summary>
/// Un archivo de idioma: el código y el nombre del idioma, y los textos traducidos de cada sección (una por cada
/// clase de recursos: Strings, Messages, ExportText, FileMessages), por id.
/// </summary>
public sealed class LanguageFile
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Formato de fechas y números (en-US, en-GB, pt-BR…); vacío = el habitual del idioma.</summary>
    public string Culture { get; init; } = "";

    public Dictionary<string, Dictionary<string, string>> Sections { get; } = new(StringComparer.Ordinal);

    public string? Get(string section, string id) =>
        Sections.TryGetValue(section, out var texts) && texts.TryGetValue(id, out var text) ? text : null;
}

/// <summary>Resultado de revisar un archivo de idioma contra los textos en español.</summary>
public sealed record LanguageCheck(int Total, int Translated, IReadOnlyList<string> Missing, IReadOnlyList<string> Unknown, IReadOnlyList<string> BadPlaceholders)
{
    public bool IsComplete => Missing.Count == 0 && Unknown.Count == 0 && BadPlaceholders.Count == 0;
}

/// <summary>
/// Idiomas: los textos de pantallas, mensajes e informes están en español en las clases de recursos; un archivo de
/// idioma los reemplaza por id. Lo que falta en el archivo (o tiene distintos marcadores {0}) se muestra en español.
/// </summary>
public static partial class Localization
{
    public const string BaseCode = "es";
    public const string LanguageSection = "Idioma";

    private static readonly Dictionary<string, (FieldInfo Field, ResourceManager Original)> Sources = new(StringComparer.Ordinal);

    /// <summary>Formato de fechas y números de Windows, el que se usa en español.</summary>
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;
    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;

    /// <summary>Registra las clases de recursos (las generadas desde los .resx) que se pueden traducir.</summary>
    public static void Register(params Type[] resourceClasses)
    {
        foreach (var type in resourceClasses)
        {
            var field = type.GetField("resourceMan", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"{type.Name} no es una clase de recursos generada.");
            var original = (ResourceManager)type.GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
            if (field.GetValue(null) is OverlayResourceManager overlay)
            {
                original = overlay.Inner;
            }

            Sources[type.Name] = (field, original);
        }
    }

    /// <summary>Los textos en español de cada sección, ordenados por id.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string>>> BaseTexts() =>
        Sources.OrderBy(s => SectionOrder(s.Key)).ToDictionary(s => s.Key, s => (IReadOnlyList<KeyValuePair<string, string>>)Read(s.Value.Original));

    /// <summary>Aplica un idioma (null = español). Los textos que no se pueden usar quedan en español.</summary>
    public static void Apply(LanguageFile? language)
    {
        foreach (var (section, (field, original)) in Sources)
        {
            if (language is null)
            {
                field.SetValue(null, original);
                continue;
            }

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (id, spanish) in Read(original))
            {
                if (language.Get(section, id) is { } text && SamePlaceholders(spanish, text))
                {
                    texts[id] = text;
                }
            }

            field.SetValue(null, new OverlayResourceManager(original, texts));
        }

        // Fechas y números con el formato del idioma (en español, el de Windows).
        var (culture, uiCulture) = language is null ? (SystemCulture, SystemUiCulture) : CulturesFor(language);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = uiCulture;
    }

    /// <summary>
    /// País del idioma (código ISO de región: AR, US, BR…), para mostrar su bandera: el de la Cultura del archivo o el
    /// habitual del idioma; en español (null), el de Windows. Vacío si no se conoce.
    /// </summary>
    public static string RegionOf(LanguageFile? language)
    {
        var culture = language is null ? SystemCulture : CulturesFor(language).Culture;
        try
        {
            return culture.IsNeutralCulture || culture.Name.Length == 0 ? "" : new RegionInfo(culture.Name).TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    /// <summary>La cultura del archivo (Cultura=) o la habitual del código del idioma; si .NET no la conoce, la de Windows.</summary>
    private static (CultureInfo Culture, CultureInfo UiCulture) CulturesFor(LanguageFile language)
    {
        CultureInfo? Find(Func<CultureInfo> create)
        {
            try
            {
                return create();
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        var culture = (language.Culture.Length > 0 ? Find(() => CultureInfo.GetCultureInfo(language.Culture)) : null)
            ?? (language.Code.Length > 0 ? Find(() => CultureInfo.CreateSpecificCulture(language.Code)) : null)
            ?? SystemCulture;
        var ui = (language.Code.Length > 0 ? Find(() => CultureInfo.GetCultureInfo(language.Code)) : null) ?? SystemUiCulture;
        return (culture.IsNeutralCulture ? SystemCulture : culture, ui);
    }

    /// <summary>Revisa un archivo de idioma: cuántos textos trae, cuáles faltan, cuáles no existen y cuáles cambian los marcadores.</summary>
    public static LanguageCheck Check(LanguageFile language)
    {
        var missing = new List<string>();
        var bad = new List<string>();
        var total = 0;
        var translated = 0;
        foreach (var (section, texts) in BaseTexts())
        {
            foreach (var (id, spanish) in texts)
            {
                total++;
                if (language.Get(section, id) is not { } text)
                {
                    missing.Add($"{section}.{id}");
                }
                else if (!SamePlaceholders(spanish, text))
                {
                    bad.Add($"{section}.{id}");
                }
                else
                {
                    translated++;
                }
            }
        }

        var known = BaseTexts().ToDictionary(s => s.Key, s => s.Value.Select(t => t.Key).ToHashSet(StringComparer.Ordinal));
        var unknown = language.Sections
            .SelectMany(s => s.Value.Keys.Where(id => !known.TryGetValue(s.Key, out var ids) || !ids.Contains(id)).Select(id => $"{s.Key}.{id}"))
            .ToList();
        return new LanguageCheck(total, translated, missing, unknown, bad);
    }

    // ---- Formato del archivo ----------------------------------------------------

    /// <summary>
    /// Lee un archivo de idioma: líneas "id=texto" agrupadas en secciones "[Strings]", "[Messages]", etc.; la sección
    /// [Idioma] trae el código y el nombre. "#" empieza un comentario; "\n" en el texto es un salto de línea.
    /// </summary>
    public static LanguageFile Parse(string content)
    {
        string? code = null;
        string? name = null;
        string? culture = null;
        var file = new LanguageFile();
        Dictionary<string, string>? current = null;
        var inHeader = false;
        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimStart('﻿').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                inHeader = string.Equals(section, LanguageSection, StringComparison.OrdinalIgnoreCase);
                current = inHeader ? null : (file.Sections.TryGetValue(section, out var existing) ? existing : file.Sections[section] = new(StringComparer.Ordinal));
                continue;
            }

            var eq = raw.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var id = raw[..eq].Trim().TrimStart('﻿');
            var text = raw[(eq + 1)..].TrimEnd('\r').Replace("\\n", "\n", StringComparison.Ordinal);
            if (inHeader)
            {
                if (id.Equals("Codigo", StringComparison.OrdinalIgnoreCase) || id.Equals("Código", StringComparison.OrdinalIgnoreCase))
                {
                    code = text.Trim();
                }
                else if (id.Equals("Nombre", StringComparison.OrdinalIgnoreCase))
                {
                    name = text.Trim();
                }
                else if (id.Equals("Cultura", StringComparison.OrdinalIgnoreCase))
                {
                    culture = text.Trim();
                }
            }
            else
            {
                current?.TryAdd(id, text);
            }
        }

        var result = new LanguageFile { Code = (code ?? "").ToLowerInvariant(), Name = name ?? code ?? "", Culture = culture ?? "" };
        foreach (var (section, texts) in file.Sections)
        {
            result.Sections[section] = texts;
        }

        return result;
    }

    /// <summary>
    /// Arma el archivo para traducir: las instrucciones, la sección [Idioma] y todos los textos por id. Con un idioma
    /// ya traducido, conserva sus traducciones y marca con un comentario las que faltan (quedan en español).
    /// </summary>
    public static string Template(string code, string name, LanguageFile? existing = null, string culture = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("# auros.ram — archivo de idioma");
        sb.AppendLine("#");
        sb.AppendLine("# INSTRUCCIONES PARA TRADUCIR (a mano o con una IA):");
        sb.AppendLine("# - Cada línea tiene el formato ID=texto. Traducir solo el texto que está después del primer \"=\".");
        sb.AppendLine("# - No modificar los ID ni los títulos de sección entre corchetes, no agregar ni quitar líneas y mantener el orden.");
        sb.AppendLine("# - Las líneas que empiezan con \"#\" son comentarios: dejarlas igual.");
        sb.AppendLine("# - Conservar exactamente los marcadores {0}, {1}, etc. y los \\n (saltos de línea); se pueden mover dentro de la frase.");
        sb.AppendLine("# - Conservar los símbolos y la puntuación especial: …, →, ·, %, ✕.");
        sb.AppendLine("# - Usar terminología de gestión de riesgos y auditoría (riesgo, impacto, probabilidad de ocurrencia,");
        sb.AppendLine("#   criterio de materialidad, unidad de negocio). Preferir textos cortos: son botones, títulos y etiquetas.");
        sb.AppendLine("# - En [Idioma], Codigo es el código ISO del idioma (en, pt, fr, it, de…) y Nombre, el nombre del idioma");
        sb.AppendLine("#   escrito en ese idioma (English, Português, Français…). Cultura es el formato de fechas y números del país");
        sb.AppendLine("#   (en-US: 11/1/2026; en-GB: 01/11/2026; pt-BR: 01/11/2026); si queda vacía se usa el habitual del idioma.");
        sb.AppendLine("# - Un texto que falte o pierda un marcador se muestra en español.");
        sb.AppendLine();
        sb.AppendLine($"[{LanguageSection}]");
        sb.AppendLine($"Codigo={code}");
        sb.AppendLine($"Nombre={name}");
        sb.AppendLine($"Cultura={culture}");
        foreach (var (section, texts) in BaseTexts())
        {
            sb.AppendLine();
            sb.AppendLine($"[{section}]");
            string? group = null;
            foreach (var (id, spanish) in texts)
            {
                var prefix = id.Split('_')[0];
                if (prefix != group)
                {
                    sb.AppendLine($"# --- {prefix} ---");
                    group = prefix;
                }

                var text = existing?.Get(section, id) is { } t && SamePlaceholders(spanish, t) ? t : null;
                if (existing is not null && text is null)
                {
                    sb.AppendLine("# falta traducir:");
                }

                sb.Append(id).Append('=').AppendLine((text ?? spanish).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal));
            }
        }

        return sb.ToString();
    }

    /// <summary>Los marcadores {0}, {1}… del texto en español y de la traducción tienen que ser los mismos.</summary>
    public static bool SamePlaceholders(string spanish, string text) =>
        Placeholders(spanish).SetEquals(Placeholders(text));

    private static HashSet<string> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value)];

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    private static List<KeyValuePair<string, string>> Read(ResourceManager manager)
    {
        var set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        var list = new List<KeyValuePair<string, string>>();
        foreach (DictionaryEntry entry in set!)
        {
            if (entry.Value is string text)
            {
                list.Add(new((string)entry.Key, text));
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list;
    }

    private static int SectionOrder(string section) => section switch
    {
        "Strings" => 0,
        "Messages" => 1,
        "ExportText" => 2,
        _ => 3,
    };

    /// <summary>Devuelve la traducción si la hay; si no, el texto en español.</summary>
    private sealed class OverlayResourceManager(ResourceManager inner, Dictionary<string, string> texts) : ResourceManager
    {
        public ResourceManager Inner { get; } = inner;

        public override string? GetString(string name) => GetString(name, null);

        public override string? GetString(string name, CultureInfo? culture) =>
            texts.TryGetValue(name, out var text) ? text : Inner.GetString(name, culture);

        public override object? GetObject(string name, CultureInfo? culture) =>
            texts.TryGetValue(name, out var text) ? text : Inner.GetObject(name, culture);

        public override ResourceSet? GetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents) =>
            Inner.GetResourceSet(culture, createIfNotExists, tryParents);
    }
}
