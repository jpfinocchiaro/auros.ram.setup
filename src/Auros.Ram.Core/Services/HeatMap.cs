using Auros.Ram.Core.Model;

namespace Auros.Ram.Core.Services;

/// <summary>
/// Mapa de calor: el nivel de riesgo de cada combinación de impacto y probabilidad.
/// El usuario puede fijar el nivel de cualquier celda; las demás toman el nivel por omisión.
/// </summary>
public static class HeatMap
{
    /// <summary>Nivel de riesgo de una combinación, según el mapa del proyecto o el valor por omisión.</summary>
    public static Level? Resolve(RiskMatrixDocument doc, Guid impactLevelId, Guid probabilityLevelId)
    {
        var risk = doc.ProjectScale(ScaleType.Risk);
        var impact = doc.ProjectScale(ScaleType.Impact);
        var probability = doc.ProjectScale(ScaleType.Probability);
        if (risk is null || impact is null || probability is null)
        {
            return null;
        }

        foreach (var cell in doc.Project.HeatMap)
        {
            if (cell.ImpactLevelId == impactLevelId && cell.ProbabilityLevelId == probabilityLevelId &&
                risk.FindLevel(cell.RiskLevelId) is { } chosen)
            {
                return chosen;
            }
        }

        return Default(risk, impact, probability, impactLevelId, probabilityLevelId);
    }

    /// <summary>
    /// Nivel por omisión. Con las posiciones i (impacto) y p (probabilidad) contadas desde 1, el puntaje
    /// s = i·p / (cantidad de impactos · cantidad de probabilidades) se compara con los umbrales
    /// ((k+1)/(n+1))², k = 1…n−1, donde n es la cantidad de niveles de riesgo. Para 5×5 con los tres
    /// niveles estándar da: producto 1–6 bajo, 8–12 medio, 15–25 alto (con 4 niveles, la clásica 1–4 / 5–9 / 10–16 / 20–25).
    /// </summary>
    public static Level? Default(Scale risk, Scale impact, Scale probability, Guid impactLevelId, Guid probabilityLevelId)
    {
        var levels = risk.OrderedLevels;
        var impacts = impact.OrderedLevels;
        var probabilities = probability.OrderedLevels;
        var i = IndexOf(impacts, impactLevelId);
        var p = IndexOf(probabilities, probabilityLevelId);
        if (levels.Count == 0 || i < 0 || p < 0)
        {
            return null;
        }

        var score = (i + 1.0) * (p + 1.0) / (impacts.Count * probabilities.Count);
        var n = levels.Count;
        var index = 0;
        for (var k = 1; k < n; k++)
        {
            var threshold = Math.Pow((k + 1.0) / (n + 1.0), 2);
            if (score > threshold + 1e-9)
            {
                index = k;
            }
        }

        return levels[index];
    }

    /// <summary>
    /// Deja solo las celdas que siguen teniendo sentido con las escalas del proyecto
    /// (niveles que existen en las escalas de impacto, probabilidad y riesgo asignadas).
    /// </summary>
    public static IReadOnlyList<HeatCell> Prune(IReadOnlyList<HeatCell> cells, Scale? risk, Scale? impact, Scale? probability)
    {
        if (risk is null || impact is null || probability is null)
        {
            return [];
        }

        return [.. cells
            .Where(c => impact.FindLevel(c.ImpactLevelId) is not null &&
                        probability.FindLevel(c.ProbabilityLevelId) is not null &&
                        risk.FindLevel(c.RiskLevelId) is not null)
            .GroupBy(c => (c.ImpactLevelId, c.ProbabilityLevelId))
            .Select(g => g.Last())];
    }

    private static int IndexOf(IReadOnlyList<Level> levels, Guid id)
    {
        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
