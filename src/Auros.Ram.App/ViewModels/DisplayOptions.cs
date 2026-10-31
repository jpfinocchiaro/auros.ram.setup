using CommunityToolkit.Mvvm.ComponentModel;

namespace Auros.Ram.App.ViewModels;

/// <summary>
/// Opciones de presentación que valen para toda la aplicación (Configuración → Preferencias).
/// Las vistas se enlazan a <see cref="Current"/> y se actualizan al cambiar una opción.
/// </summary>
public sealed partial class DisplayOptions : ObservableObject
{
    public static DisplayOptions Current { get; } = new();

    /// <summary>Muestra en la matriz de riesgo la abreviatura del nivel de cada celda ([ALT], [MED], [BAJ]).</summary>
    [ObservableProperty]
    private bool _showLevelAbbreviations = true;
}
