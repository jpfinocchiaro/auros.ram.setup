using Auros.Ram.Core.Model;

namespace Auros.Ram.Core.Services;

public enum RiskSortField
{
    Risk,
    Value,
    Difficulty,
    Title,
    Created,
}

/// <summary>Filtro, búsqueda y orden de la lista de riesgos (RF-12).</summary>
public sealed record RiskFilter
{
    public string? Text { get; init; }
    public Guid? RiskLevelId { get; init; }
    public Guid? ValueLevelId { get; init; }
    public Guid? DifficultyLevelId { get; init; }
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
            query = query.Where(x => x.RiskLevelId == r);
        }

        if (filter.ValueLevelId is { } v)
        {
            query = query.Where(x => x.ValueLevelId == v);
        }

        if (filter.DifficultyLevelId is { } d)
        {
            query = query.Where(x => x.DifficultyLevelId == d);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            query = query.Where(x =>
                Contains(x.Title, text) || Contains(x.ShortDescription, text) || Contains(x.LongDescription, text));
        }

        var orders = LevelOrders(doc);
        int Ord(Guid id) => orders.TryGetValue(id, out var o) ? o : int.MinValue;

        IOrderedEnumerable<Risk> sorted = filter.SortBy switch
        {
            RiskSortField.Value => Sort(query, x => Ord(x.ValueLevelId), filter.Descending),
            RiskSortField.Difficulty => Sort(query, x => Ord(x.DifficultyLevelId), filter.Descending),
            RiskSortField.Title => filter.Descending
                ? query.OrderByDescending(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
                : query.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            RiskSortField.Created => Sort(query, x => x.CreatedUtc, filter.Descending),
            _ => Sort(query, x => Ord(x.RiskLevelId), filter.Descending),
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
