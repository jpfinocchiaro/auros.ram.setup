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

    /// <summary>Muestra en la matriz el monto del impacto junto a cada riesgo (se recuerda entre sesiones).</summary>
    [ObservableProperty]
    private bool _showImpactAmount;

    /// <summary>Qué fechas de evaluación se aceptan (Preferencias); se aplica a todos los archivos abiertos.</summary>
    public Auros.Ram.Core.Model.AssessmentDatePolicy DatePolicy { get; set; } = Auros.Ram.Core.Model.AssessmentDatePolicy.AllowAll;

    /// <summary>Cómo se cargan impacto y probabilidad (Preferencias); se aplica a todos los archivos abiertos.</summary>
    public Auros.Ram.Core.Model.RiskEntryPolicy EntryPolicy { get; set; } = Auros.Ram.Core.Model.RiskEntryPolicy.Default;
}
