using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Model;

/// <summary>Juegos de escalas estándar para una matriz nueva.</summary>
public enum ScalePreset
{
    /// <summary>Matriz clásica de 5 × 5 con cuatro niveles de riesgo.</summary>
    Standard5x5,

    /// <summary>Matriz simple de 3 × 3: Bajo, Medio y Alto en impacto, probabilidad y nivel de riesgo.</summary>
    Simple3x3,
}

/// <summary>
/// Las tres escalas estándar que se cargan en un archivo nuevo (F1, F2). Impacto y probabilidad usan
/// una rampa de azules para no confundirse con el semáforo del nivel de riesgo.
/// </summary>
public static class DefaultScales
{
    private static readonly string[] Ramp = ["#DCE7F3", "#A9C4E4", "#6E9BD1", "#3A6FB5", "#1F4A85"];

    public static IReadOnlyList<Scale> Create(ScalePreset preset = ScalePreset.Standard5x5) =>
        preset == ScalePreset.Simple3x3 ? Simple3x3() : Standard5x5();

    private static IReadOnlyList<Scale> Simple3x3() =>
    [
        Build(Messages.DefaultScale_Risk3, ScaleType.Risk,
            (Messages.Level_Low, Palette.Green),
            (Messages.Level_Medium, Palette.Yellow),
            (Messages.Level_High, Palette.Red)),
        Build(Messages.DefaultScale_Impact3, ScaleType.Impact,
            (Messages.Level_Low, Ramp[1]),
            (Messages.Level_Medium, Ramp[2]),
            (Messages.Level_High, Ramp[4])),
        Build(Messages.DefaultScale_Probability3, ScaleType.Probability,
            (Messages.Level_Low, Ramp[1]),
            (Messages.Level_Medium, Ramp[2]),
            (Messages.Level_High, Ramp[4])),
    ];

    private static IReadOnlyList<Scale> Standard5x5() =>
    [
        Build(Messages.DefaultScale_Risk, ScaleType.Risk,
            (Messages.Level_Low, Palette.Green),
            (Messages.Level_Medium, Palette.Yellow),
            (Messages.Level_High, Palette.Orange),
            (Messages.Level_Critical, Palette.Red)),
        Build(Messages.DefaultScale_Impact, ScaleType.Impact,
            (Messages.Impact_Insignificant, Ramp[0]),
            (Messages.Impact_Minor, Ramp[1]),
            (Messages.Impact_Moderate, Ramp[2]),
            (Messages.Impact_Major, Ramp[3]),
            (Messages.Impact_Catastrophic, Ramp[4])),
        Build(Messages.DefaultScale_Probability, ScaleType.Probability,
            (Messages.Probability_Rare, Ramp[0]),
            (Messages.Probability_Unlikely, Ramp[1]),
            (Messages.Probability_Possible, Ramp[2]),
            (Messages.Probability_Likely, Ramp[3]),
            (Messages.Probability_AlmostCertain, Ramp[4])),
    ];

    private static Scale Build(string name, ScaleType type, params (string Name, string Color)[] levels) => new()
    {
        Name = name,
        Type = type,
        Levels = [.. levels.Select((l, i) => new Level { Name = l.Name, Color = l.Color, TextColor = Palette.ContrastingText(l.Color), Order = i + 1 })],
    };
}
