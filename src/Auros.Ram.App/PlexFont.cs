using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Auros.Ram.App;

/// <summary>
/// IBM Plex Sans (OFL 1.1) incluida en la aplicación: normal y condensada, en 400 y 600.
/// La condensada se usa donde el espacio es escaso (celdas de la matriz).
/// </summary>
public static class PlexFont
{
    public const string Sans = "fonts:Plex#IBM Plex Sans";
    public const string Condensed = "fonts:Plex#IBM Plex Sans Condensed";

    public static AppBuilder WithPlexFont(this AppBuilder builder) =>
        builder
            .ConfigureFonts(fm => fm.AddFontCollection(new EmbeddedFontCollection(
                new Uri("fonts:Plex", UriKind.Absolute),
                new Uri("avares://auros.ram/Assets/Fonts", UriKind.Absolute))))
            .With(new FontManagerOptions { DefaultFamilyName = Sans });
}
