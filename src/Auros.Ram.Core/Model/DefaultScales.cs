using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Model;

/// <summary>Las tres escalas estándar que se cargan en un archivo nuevo (F1, F2).</summary>
public static class DefaultScales
{
    public static IReadOnlyList<Scale> Create() =>
    [
        Build(Messages.DefaultScale_Risk, ScaleType.Risk,
            (Messages.Level_Low, Palette.Green),
            (Messages.Level_Medium, Palette.Yellow),
            (Messages.Level_High, Palette.Red),
            (Messages.Level_Critical, Palette.Violet)),
        Build(Messages.DefaultScale_Value, ScaleType.Value,
            (Messages.Level_Low, Palette.Gray),
            (Messages.Level_Medium, Palette.Blue),
            (Messages.Level_High, Palette.Green)),
        Build(Messages.DefaultScale_Difficulty, ScaleType.Difficulty,
            (Messages.Level_Easy, Palette.Green),
            (Messages.Level_Moderate, Palette.Yellow),
            (Messages.Level_Hard, Palette.Red)),
    ];

    private static Scale Build(string name, ScaleType type, params (string Name, string Color)[] levels) => new()
    {
        Name = name,
        Type = type,
        Levels = [.. levels.Select((l, i) => new Level { Name = l.Name, Color = l.Color, Order = i + 1 })],
    };
}
