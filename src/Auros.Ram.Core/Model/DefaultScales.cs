using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Model;

/// <summary>
/// Las tres escalas estándar que se cargan en un archivo nuevo (F1, F2): la matriz clásica de 5 × 5
/// con cuatro niveles de riesgo. Impacto y probabilidad usan una rampa de azules para no confundirse
/// con el semáforo del nivel de riesgo.
/// </summary>
public static class DefaultScales
{
    private static readonly string[] Ramp = ["#DCE7F3", "#A9C4E4", "#6E9BD1", "#3A6FB5", "#1F4A85"];

    public static IReadOnlyList<Scale> Create() =>
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
