using System.Globalization;
using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Model;

/// <summary>Tamaños estándar de matriz para una matriz nueva (impacto × probabilidad).</summary>
public enum ScalePreset
{
    Matrix3x3 = 3,
    Matrix5x5 = 5,
    Matrix7x7 = 7,
    Matrix9x9 = 9,
}

/// <summary>
/// Las tres escalas estándar que se cargan en un archivo nuevo (F1, F2): impacto y probabilidad con
/// 3, 5, 7 o 9 niveles, y el nivel de riesgo en semáforo (Bajo verde, Medio amarillo, Alto rojo).
/// Impacto y probabilidad también van del verde al rojo: en 3 × 3 son los mismos tres colores y en
/// los tamaños mayores, una escala de verdes, amarillos y rojos.
/// </summary>
public static class DefaultScales
{
    public static IReadOnlyList<ScalePreset> Presets { get; } =
        [ScalePreset.Matrix3x3, ScalePreset.Matrix5x5, ScalePreset.Matrix7x7, ScalePreset.Matrix9x9];

    public static IReadOnlyList<Scale> Create(ScalePreset preset = ScalePreset.Matrix5x5)
    {
        var size = (int)preset;
        var ramp = Ramp(size);
        return
        [
            Build(Messages.DefaultScale_Risk, ScaleType.Risk,
                [(Messages.Level_Low, Palette.Green), (Messages.Level_Medium, Palette.Yellow), (Messages.Level_High, Palette.Red)]),
            Build(Format(Messages.DefaultScale_Impact, size), ScaleType.Impact,
                [.. ImpactNames(size).Select((n, i) => (n, ramp[i]))]),
            Build(Format(Messages.DefaultScale_Probability, size), ScaleType.Probability,
                [.. ProbabilityNames(size).Select((n, i) => (n, ramp[i]))]),
        ];
    }

    private static string[] ImpactNames(int size) => size switch
    {
        3 => [Messages.Level_Low, Messages.Level_Medium, Messages.Level_High],
        5 => [Messages.Impact_Insignificant, Messages.Impact_Minor, Messages.Impact_Moderate, Messages.Impact_Major, Messages.Impact_Catastrophic],
        _ => GradedNames(size),
    };

    private static string[] ProbabilityNames(int size) => size switch
    {
        3 => [Messages.Level_Low, Messages.Level_Medium, Messages.Level_High],
        5 => [Messages.Probability_Rare, Messages.Probability_Unlikely, Messages.Probability_Possible, Messages.Probability_Likely, Messages.Probability_AlmostCertain],
        _ => GradedNames(size),
    };

    /// <summary>Escala graduada de 7 o 9 niveles: de "Muy bajo" a "Muy alto" (con "Mínimo" y "Extremo" en 9).</summary>
    private static string[] GradedNames(int size)
    {
        string[] seven =
        [
            Messages.Graded_VeryLow, Messages.Level_Low, Messages.Graded_FairlyLow, Messages.Graded_Moderate,
            Messages.Graded_FairlyHigh, Messages.Level_High, Messages.Graded_VeryHigh,
        ];
        return size == 9 ? [Messages.Graded_Minimal, .. seven, Messages.Graded_Extreme] : seven;
    }

    /// <summary>
    /// Escala de verde (nivel más bajo) a amarillo (medio) y rojo (más alto), con tantos tonos como niveles.
    /// Con 3 niveles son exactamente los colores del semáforo.
    /// </summary>
    private static string[] Ramp(int size)
    {
        int[][] stops = [Rgb(Palette.Green), Rgb(Palette.Yellow), Rgb(Palette.Red)];
        return [.. Enumerable.Range(0, size).Select(i =>
        {
            var t = size == 1 ? 0 : 2.0 * i / (size - 1);    // 0 = verde, 1 = amarillo, 2 = rojo
            var k = Math.Min(1, (int)t);
            var f = t - k;
            int Mix(int c) => (int)Math.Round(stops[k][c] + ((stops[k + 1][c] - stops[k][c]) * f));
            return $"#{Mix(0):X2}{Mix(1):X2}{Mix(2):X2}";
        })];
    }

    private static int[] Rgb(string hex) =>
        [Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16)];

    private static string Format(string template, int size) => string.Format(CultureInfo.CurrentCulture, template, size);

    private static Scale Build(string name, ScaleType type, (string Name, string Color)[] levels) => new()
    {
        Name = name,
        Type = type,
        Levels = [.. levels.Select((l, i) => new Level { Name = l.Name, Color = l.Color, TextColor = Palette.ContrastingText(l.Color), Order = i + 1 })],
    };
}
