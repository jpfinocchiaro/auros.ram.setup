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

    /// <summary>
    /// Rango del nivel (opcional), con 2 decimales y ambos extremos incluidos: en Impacto, un monto desde–hasta;
    /// en Probabilidad, un porcentaje de 0 a 100. Permite elegir el nivel a partir del monto o del porcentaje.
    /// </summary>
    public decimal? RangeFrom { get; set; }

    public decimal? RangeTo { get; set; }

    public bool HasRange => RangeFrom is not null && RangeTo is not null;

    public bool Contains(decimal value) => HasRange && value >= RangeFrom && value <= RangeTo;

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

/// <summary>Las dos listas de opciones de cada matriz.</summary>
public enum CatalogKind
{
    /// <summary>Consecuencias potenciales ("Accidente laboral", "Pérdida de reputación", …): varias por riesgo.</summary>
    Consequence = 1,

    /// <summary>Criterios de materialidad: uno por riesgo.</summary>
    Materiality = 2,
}

/// <summary>Una opción de un catálogo: una descripción muy corta.</summary>
public sealed record CatalogItem(Guid Id, string Name)
{
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

    /// <summary>Catálogo de consecuencias potenciales de la matriz (se elige de él en cada riesgo).</summary>
    public IReadOnlyList<CatalogItem> Consequences { get; set; } = [];

    /// <summary>Catálogo de criterios de materialidad de la matriz.</summary>
    public IReadOnlyList<CatalogItem> MaterialityCriteria { get; set; } = [];

    public IReadOnlyList<CatalogItem> CatalogFor(CatalogKind kind) =>
        kind == CatalogKind.Consequence ? Consequences : MaterialityCriteria;

    public Guid? ScaleIdFor(ScaleType type) => type switch
    {
        ScaleType.Risk => RiskScaleId,
        ScaleType.Impact => ImpactScaleId,
        ScaleType.Probability => ProbabilityScaleId,
        _ => null,
    };

    public ProjectInfo Clone() => (ProjectInfo)MemberwiseClone();
}

/// <summary>
/// Una evaluación del riesgo: su impacto y su probabilidad a partir de una fecha. El historial de evaluaciones
/// permite ver cómo evolucionó el riesgo; la última es su estado actual.
/// </summary>
/// <param name="Note">Comentario opcional del cambio (por qué se reevaluó).</param>
/// <param name="RecordedUtc">Cuándo se registró (ordena dos evaluaciones del mismo día).</param>
public sealed record RiskAssessment(Guid Id, DateOnly Date, Guid ImpactLevelId, Guid ProbabilityLevelId, string Note, DateTime RecordedUtc)
{
    /// <summary>Monto del impacto en esa evaluación (opcional, 2 decimales).</summary>
    public decimal? ImpactAmount { get; init; }

    /// <summary>Probabilidad de ocurrencia en esa evaluación, de 0 a 100 % (opcional, 2 decimales).</summary>
    public decimal? ProbabilityPercent { get; init; }
}

/// <summary>Qué se pide al cargar el impacto o la probabilidad de un riesgo (Preferencias).</summary>
public enum EntryMode
{
    /// <summary>El nivel es obligatorio; el monto o porcentaje, opcional.</summary>
    Level = 0,

    /// <summary>El monto o porcentaje es obligatorio (el nivel sale del rango o se elige).</summary>
    Value = 1,

    /// <summary>Alcanza con uno de los dos (el nivel sale del rango si se ingresó el valor).</summary>
    Either = 2,
}

/// <summary>
/// Cómo se cargan impacto y probabilidad (Preferencias): qué dato es obligatorio y si el nivel se ajusta
/// automáticamente al monto o porcentaje según los rangos de la escala.
/// </summary>
public sealed record RiskEntryPolicy(EntryMode Impact, bool ImpactAuto, EntryMode Probability, bool ProbabilityAuto)
{
    public static RiskEntryPolicy Default { get; } = new(EntryMode.Level, true, EntryMode.Level, true);

    public EntryMode ModeFor(ScaleType type) => type == ScaleType.Impact ? Impact : Probability;

    public bool AutoFor(ScaleType type) => type == ScaleType.Impact ? ImpactAuto : ProbabilityAuto;
}

/// <summary>
/// Qué fechas se aceptan para las evaluaciones (Preferencias). Una fecha futura nunca se acepta; las anteriores a
/// hoy, si se permiten, solo se advierten en la edición.
/// </summary>
/// <param name="AllowPastOnCreate">Al dar de alta un riesgo, una fecha anterior a hoy.</param>
/// <param name="AllowBeforeRecorded">Al modificar un riesgo, una fecha anterior a la de una evaluación ya registrada (se intercala en el historial).</param>
/// <param name="AllowPastOnEdit">Al modificar un riesgo o corregir una evaluación, una fecha anterior a hoy.</param>
public sealed record AssessmentDatePolicy(bool AllowPastOnCreate, bool AllowBeforeRecorded, bool AllowPastOnEdit)
{
    public static AssessmentDatePolicy AllowAll { get; } = new(true, true, true);
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

    /// <summary>Consecuencias potenciales del riesgo (del catálogo de la matriz), en el orden en que se cargaron.</summary>
    public IReadOnlyList<Guid> ConsequenceIds { get; set; } = [];

    /// <summary>Criterio de materialidad del riesgo (del catálogo de la matriz), si tiene.</summary>
    public Guid? MaterialityId { get; set; }

    /// <summary>Monto del impacto (opcional, 2 decimales): el de la evaluación vigente.</summary>
    public decimal? ImpactAmount { get; set; }

    /// <summary>Probabilidad de ocurrencia de 0 a 100 % (opcional, 2 decimales): la de la evaluación vigente.</summary>
    public decimal? ProbabilityPercent { get; set; }

    /// <summary>El monto (Impacto) o el porcentaje (Probabilidad).</summary>
    public decimal? ValueFor(ScaleType type) => type switch
    {
        ScaleType.Impact => ImpactAmount,
        ScaleType.Probability => ProbabilityPercent,
        _ => null,
    };

    public string ImpactJustification { get; set; } = "";
    public string ProbabilityJustification { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Historial de evaluaciones, de la más antigua a la más reciente. <see cref="ImpactLevelId"/> y
    /// <see cref="ProbabilityLevelId"/> son siempre los de la última. Como el mapa de calor, se reemplaza, nunca se modifica.
    /// </summary>
    public IReadOnlyList<RiskAssessment> Assessments { get; set; } = [];

    /// <summary>La evaluación vigente en una fecha (la última hasta ese día), o null si el riesgo todavía no estaba evaluado.</summary>
    public RiskAssessment? AssessmentAt(DateOnly date) => Assessments.LastOrDefault(a => a.Date <= date);

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
