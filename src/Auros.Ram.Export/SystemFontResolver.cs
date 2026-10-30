using PdfSharp.Fonts;

namespace Auros.Ram.Export;

/// <summary>
/// PDFsharp (compilación multiplataforma) necesita que le indiquen de dónde leer las fuentes.
/// Usa una sans-serif del sistema: Arial en Windows y macOS, Liberation Sans o DejaVu Sans en Linux.
/// </summary>
internal sealed class SystemFontResolver : IFontResolver
{
    public const string Family = "AurosSans";
    private static readonly object Gate = new();
    private static bool _installed;

    private readonly Dictionary<string, string> _faces;

    private SystemFontResolver(Dictionary<string, string> faces) => _faces = faces;

    public static void EnsureInstalled()
    {
        lock (Gate)
        {
            if (_installed)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new SystemFontResolver(FindFaces());
            _installed = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = (bold, italic) switch
        {
            (true, true) => "bi",
            (true, false) => "b",
            (false, true) => "i",
            _ => "r",
        };
        if (!_faces.ContainsKey(face))
        {
            // Si falta la variante, se simula a partir de la regular.
            return new FontResolverInfo("r", bold, italic);
        }

        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName) =>
        _faces.TryGetValue(faceName, out var file) ? File.ReadAllBytes(file) : null;

    private static Dictionary<string, string> FindFaces()
    {
        (string r, string b, string i, string bi)[] candidates =
        [
            (@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf", @"C:\Windows\Fonts\ariali.ttf", @"C:\Windows\Fonts\arialbi.ttf"),
            (@"C:\Windows\Fonts\segoeui.ttf", @"C:\Windows\Fonts\segoeuib.ttf", @"C:\Windows\Fonts\segoeuii.ttf", @"C:\Windows\Fonts\segoeuiz.ttf"),
            ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
             "/System/Library/Fonts/Supplemental/Arial Italic.ttf", "/System/Library/Fonts/Supplemental/Arial Bold Italic.ttf"),
            .. LinuxCandidates(),
        ];

        foreach (var (r, b, i, bi) in candidates)
        {
            if (!File.Exists(r))
            {
                continue;
            }

            var faces = new Dictionary<string, string> { ["r"] = r };
            if (File.Exists(b)) faces["b"] = b;
            if (File.Exists(i)) faces["i"] = i;
            if (File.Exists(bi)) faces["bi"] = bi;
            return faces;
        }

        throw new InvalidOperationException("No se encontró una fuente del sistema para generar el PDF.");
    }

    private static IEnumerable<(string, string, string, string)> LinuxCandidates()
    {
        foreach (var root in new[] { "/usr/share/fonts", "/usr/local/share/fonts" })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var (regular, suffixes) in new[]
            {
                ("LiberationSans-Regular.ttf", new[] { "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf" }),
                ("DejaVuSans.ttf", new[] { "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf" }),
            })
            {
                var found = Directory.EnumerateFiles(root, regular, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null)
                {
                    var dir = Path.GetDirectoryName(found)!;
                    yield return (found, Path.Combine(dir, suffixes[0]), Path.Combine(dir, suffixes[1]), Path.Combine(dir, suffixes[2]));
                }
            }
        }
    }
}
