namespace Auros.Ram.Core.Model;

/// <summary>Las tres dimensiones con que se clasifica un riesgo (F1).</summary>
public enum ScaleType
{
    Risk = 1,
    Value = 2,
    Difficulty = 3,
}

public enum ProjectStatus
{
    InProgress = 0,
    Finished = 1,
}

/// <summary>Un valor posible dentro de una escala (RF-03).</summary>
public sealed class Level
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = Palette.Gray;
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

/// <summary>Datos del proyecto que contiene el archivo (RF-07, RF-08).</summary>
public sealed class ProjectInfo
{
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public string Consultancy { get; set; } = "";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public ProjectStatus Status { get; set; }

    // Los logos se tratan como inmutables: se reemplaza el arreglo, nunca se modifica.
    public byte[]? ConsultancyLogo { get; set; }
    public byte[]? ClientLogo { get; set; }

    public Guid? RiskScaleId { get; set; }
    public Guid? ValueScaleId { get; set; }
    public Guid? DifficultyScaleId { get; set; }

    public Guid? ScaleIdFor(ScaleType type) => type switch
    {
        ScaleType.Risk => RiskScaleId,
        ScaleType.Value => ValueScaleId,
        ScaleType.Difficulty => DifficultyScaleId,
        _ => null,
    };

    public ProjectInfo Clone() => (ProjectInfo)MemberwiseClone();
}

/// <summary>Un riesgo clasificado en las tres dimensiones (F3).</summary>
public sealed class Risk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public string LongDescription { get; set; } = "";

    public Guid RiskLevelId { get; set; }
    public Guid ValueLevelId { get; set; }
    public Guid DifficultyLevelId { get; set; }

    public string RiskJustification { get; set; } = "";
    public string ValueJustification { get; set; } = "";
    public string DifficultyJustification { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public Guid LevelIdFor(ScaleType type) => type switch
    {
        ScaleType.Risk => RiskLevelId,
        ScaleType.Value => ValueLevelId,
        ScaleType.Difficulty => DifficultyLevelId,
        _ => Guid.Empty,
    };

    public string JustificationFor(ScaleType type) => type switch
    {
        ScaleType.Risk => RiskJustification,
        ScaleType.Value => ValueJustification,
        ScaleType.Difficulty => DifficultyJustification,
        _ => "",
    };

    public Risk Clone() => (Risk)MemberwiseClone();
}
