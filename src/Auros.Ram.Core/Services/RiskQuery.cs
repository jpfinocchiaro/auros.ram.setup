using Auros.Ram.Core.Model;
using Auros.Ram.Core.Text;

namespace Auros.Ram.Core.Services;

public enum RiskSortField
{
    Risk,
    Impact,
    Probability,
    Title,
    Created,
}

/// <summary>Filtro, búsqueda y orden de la lista de riesgos (RF-12).</summary>
public sealed record RiskFilter
{
    public string? Text { get; init; }

    /// <summary>Nivel de riesgo resultante del mapa de calor.</summary>
    public Guid? RiskLevelId { get; init; }

    public Guid? ImpactLevelId { get; init; }
    public Guid? ProbabilityLevelId { get; init; }
    public RiskSortField SortBy { get; init; } = RiskSortField.Risk;
    public bool Descending { get; init; } = true;
}

public static class RiskQuery
{
    public static IReadOnlyList<Risk> Apply(RiskMatrixDocument doc, RiskFilter filter)
    {
        IEnumerable<Risk> query = doc.Risks;

        if (filter.RiskLevelId is { } r)
        {
            query = query.Where(x => doc.RiskLevelOf(x)?.Id == r);
        }

        if (filter.ImpactLevelId is { } i)
        {
            query = query.Where(x => x.ImpactLevelId == i);
        }

        if (filter.ProbabilityLevelId is { } p)
        {
            query = query.Where(x => x.ProbabilityLevelId == p);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            query = query.Where(x =>
                Contains(x.Title, text) || Contains(RichText.ToPlain(x.ShortDescription), text) || Contains(RichText.ToPlain(x.LongDescription), text));
        }

        var orders = LevelOrders(doc);
        int Ord(Guid id) => orders.TryGetValue(id, out var o) ? o : int.MinValue;
        int RiskOrd(Risk x) => doc.RiskLevelOf(x)?.Order ?? int.MinValue;

        IOrderedEnumerable<Risk> sorted = filter.SortBy switch
        {
            RiskSortField.Impact => Sort(query, x => Ord(x.ImpactLevelId), filter.Descending),
            RiskSortField.Probability => Sort(query, x => Ord(x.ProbabilityLevelId), filter.Descending),
            RiskSortField.Title => filter.Descending
                ? query.OrderByDescending(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
                : query.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            RiskSortField.Created => Sort(query, x => x.CreatedUtc, filter.Descending),

            // A igual nivel de riesgo, primero el de mayor impacto y después el más probable.
            _ => Sort(query, RiskOrd, filter.Descending)
                .ThenByDescending(x => Ord(x.ImpactLevelId))
                .ThenByDescending(x => Ord(x.ProbabilityLevelId)),
        };

        return [.. sorted.ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Orden de cada nivel por id, para ordenar sin buscar escala por escala.</summary>
    public static Dictionary<Guid, int> LevelOrders(RiskMatrixDocument doc)
    {
        var map = new Dictionary<Guid, int>();
        foreach (var level in doc.Scales.SelectMany(s => s.Levels))
        {
            map[level.Id] = level.Order;
        }

        return map;
    }

    private static IOrderedEnumerable<Risk> Sort<TKey>(IEnumerable<Risk> source, Func<Risk, TKey> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
}
