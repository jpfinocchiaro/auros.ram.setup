namespace Auros.Ram.Core.Model;

/// <summary>
/// Tipos de escala. Un riesgo se clasifica en dos dimensiones, <see cref="Impact"/> y
/// <see cref="Probability"/>; su nivel de riesgo (<see cref="Risk"/>) no se elige: sale del mapa de calor.
/// </summary>
public enum ScaleType
{
    /// <summary>Nivel de riesgo: el resultado de combinar impacto y probabilidad (color del riesgo).</summary>
    Risk = 1,
    Impact = 2,
    Probability = 3,
}

public enum ProjectStatus
{
    InProgress = 0,
    Finished = 1,
}

/// <summary>Un valor posible dentro de una escala (RF-03). Se pinta con su color de fondo y su color de fuente.</summary>
public sealed class Level
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>Color de fondo del nivel (#RRGGBB).</summary>
    public string Color { get; set; } = Palette.Gray;

    /// <summary>Color de la fuente sobre <see cref="Color"/> (#RRGGBB).</summary>
    public string TextColor { get; set; } = Palette.ContrastingText(Palette.Gray);

    public int Order { get; set; }

    public Level Clone() => (Level)MemberwiseClone();

    public override string ToString() => Name;
}

/// <summary>Lista ordenada de niveles de un tipo (RF-01, RF-02).</summary>
public sealed class Scale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ScaleType Type { get; set; }
    public List<Level> Levels { get; set; } = [];

    /// <summary>Niveles de menor a mayor orden (RF-03).</summary>
    public IReadOnlyList<Level> OrderedLevels =>
        [.. Levels.OrderBy(l => l.Order).ThenBy(l => l.Name, StringComparer.CurrentCulture)];

    public Level? FindLevel(Guid id) => Levels.Find(l => l.Id == id);

    public Scale Clone() => new()
    {
        Id = Id,
        Name = Name,
        Type = Type,
        Levels = [.. Levels.Select(l => l.Clone())],
    };

    public override string ToString() => Name;
}

/// <summary>Una celda del mapa de calor: qué nivel de riesgo corresponde a un impacto y una probabilidad.</summary>
public sealed record HeatCell(Guid ImpactLevelId, Guid ProbabilityLevelId, Guid RiskLevelId);

/// <summary>Datos del proyecto que contiene el archivo (RF-07, RF-08). Cada matriz corresponde a una sola empresa.</summary>
public sealed class ProjectInfo
{
    public string Name { get; set; } = "";
    public string Company { get; set; } = "";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public ProjectStatus Status { get; set; }

    // Los logos y el mapa de calor se tratan como inmutables: se reemplazan, nunca se modifican.
    public byte[]? CompanyLogo { get; set; }

    public Guid? RiskScaleId { get; set; }
    public Guid? ImpactScaleId { get; set; }
    public Guid? ProbabilityScaleId { get; set; }

    /// <summary>
    /// Celdas del mapa de calor elegidas por el usuario. Las combinaciones que no figuran toman el
    /// nivel por omisión (ver <see cref="Services.HeatMap"/>).
    /// </summary>
    public IReadOnlyList<HeatCell> HeatMap { get; set; } = [];

    public Guid? ScaleIdFor(ScaleType type) => type switch
    {
        ScaleType.Risk => RiskScaleId,
        ScaleType.Impact => ImpactScaleId,
        ScaleType.Probability => ProbabilityScaleId,
        _ => null,
    };

    public ProjectInfo Clone() => (ProjectInfo)MemberwiseClone();
}

/// <summary>Un riesgo clasificado por su impacto y su probabilidad de ocurrencia (F3).</summary>
public sealed class Risk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public string LongDescription { get; set; } = "";

    public Guid ImpactLevelId { get; set; }
    public Guid ProbabilityLevelId { get; set; }

    public string ImpactJustification { get; set; } = "";
    public string ProbabilityJustification { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public Guid LevelIdFor(ScaleType type) => type switch
    {
        ScaleType.Impact => ImpactLevelId,
        ScaleType.Probability => ProbabilityLevelId,
        _ => Guid.Empty,
    };

    public string JustificationFor(ScaleType type) => type switch
    {
        ScaleType.Impact => ImpactJustification,
        ScaleType.Probability => ProbabilityJustification,
        _ => "",
    };

    public Risk Clone() => (Risk)MemberwiseClone();
}
