using Auros.Ram.Core.Model;
using Auros.Ram.Core.Resources;
using Auros.Ram.Core.Services;

namespace Auros.Ram.Core;

[Flags]
public enum DocumentChangeKind
{
    None = 0,
    Project = 1,
    Scales = 2,
    Risks = 4,
    All = Project | Scales | Risks,
}

public sealed class DocumentChangedEventArgs(DocumentChangeKind kind) : EventArgs
{
    public DocumentChangeKind Kind { get; } = kind;
}

/// <summary>
/// Contenido de un archivo de matriz: proyecto, escalas y riesgos (RN-12).
/// Toda modificación pasa por aquí, se valida y queda en el historial de deshacer.
/// </summary>
public sealed class RiskMatrixDocument
{
    private readonly List<Scale> _scales;
    private readonly List<Risk> _risks;
    private long _savedStateId;
    private bool _forceDirty;

    public RiskMatrixDocument(ProjectInfo project, IEnumerable<Scale> scales, IEnumerable<Risk> risks)
    {
        Project = project;
        _scales = [.. scales];
        _risks = [.. risks];
        History.Changed += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public ProjectInfo Project { get; private set; }
    public IReadOnlyList<Scale> Scales => _scales;
    public IReadOnlyList<Risk> Risks => _risks;
    public UndoStack History { get; } = new();

    public bool IsDirty => _forceDirty || History.StateId != _savedStateId;

    public event EventHandler<DocumentChangedEventArgs>? Changed;
    public event EventHandler? DirtyChanged;

    /// <summary>Archivo nuevo con las escalas estándar (o las de una plantilla) ya asignadas al proyecto.</summary>
    public static RiskMatrixDocument CreateNew(ProjectInfo? project = null, IEnumerable<Scale>? scales = null)
    {
        var scaleList = (scales ?? DefaultScales.Create()).Select(s => s.Clone()).ToList();
        var info = project?.Clone() ?? new ProjectInfo();
        info.RiskScaleId ??= scaleList.FirstOrDefault(s => s.Type == ScaleType.Risk)?.Id;
        info.ImpactScaleId ??= scaleList.FirstOrDefault(s => s.Type == ScaleType.Impact)?.Id;
        info.ProbabilityScaleId ??= scaleList.FirstOrDefault(s => s.Type == ScaleType.Probability)?.Id;
        return new RiskMatrixDocument(info, scaleList, []);
    }

    /// <param name="stateId">
    /// Estado que se guardó (<see cref="UndoStack.StateId"/> al tomar la copia). Si hubo ediciones
    /// mientras se guardaba en segundo plano, el documento sigue marcado con cambios.
    /// </param>
    public void MarkSaved(long? stateId = null)
    {
        _savedStateId = stateId ?? History.StateId;
        _forceDirty = false;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marca el documento con cambios pendientes sin pasar por el historial (p. ej., tras recuperar).</summary>
    public void MarkDirty()
    {
        _forceDirty = true;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Consultas ---------------------------------------------------------

    public Scale? FindScale(Guid? id) => id is null ? null : _scales.Find(s => s.Id == id);

    public Scale? ProjectScale(ScaleType type) => FindScale(Project.ScaleIdFor(type));

    public Level? FindLevel(Guid levelId)
    {
        foreach (var scale in _scales)
        {
            if (scale.FindLevel(levelId) is { } level)
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>
    /// Nivel del riesgo en una escala. Para <see cref="ScaleType.Risk"/> no se elige: sale del mapa de calor
    /// según su impacto y su probabilidad.
    /// </summary>
    public Level? LevelOf(Risk risk, ScaleType type) => type == ScaleType.Risk
        ? HeatMap.Resolve(this, risk.ImpactLevelId, risk.ProbabilityLevelId)
        : ProjectScale(type)?.FindLevel(risk.LevelIdFor(type));

    public Level? RiskLevelOf(Risk risk) => LevelOf(risk, ScaleType.Risk);

    public Risk? FindRisk(Guid id) => _risks.Find(r => r.Id == id);

    /// <summary>RF-05: un nivel está en uso si algún riesgo lo tiene o si es el nivel de riesgo que le toca a alguno.</summary>
    public bool IsLevelInUse(Guid levelId) =>
        _risks.Exists(r => r.ImpactLevelId == levelId || r.ProbabilityLevelId == levelId || RiskLevelOf(r)?.Id == levelId);

    public bool IsScaleAssigned(Guid scaleId) =>
        Project.RiskScaleId == scaleId || Project.ImpactScaleId == scaleId || Project.ProbabilityScaleId == scaleId;

    /// <summary>RF-09: con riesgos cargados las escalas asignadas quedan fijas.</summary>
    public bool CanChangeProjectScales => _risks.Count == 0;

    // ---- Proyecto ----------------------------------------------------------

    public void UpdateProject(ProjectInfo updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var next = updated.Clone();
        Validation.ValidateProject(next, this);

        if (!CanChangeProjectScales &&
            (next.RiskScaleId != Project.RiskScaleId ||
             next.ImpactScaleId != Project.ImpactScaleId ||
             next.ProbabilityScaleId != Project.ProbabilityScaleId))
        {
            throw new DomainException(Messages.Error_ScalesLockedByRisks);
        }

        next.HeatMap = HeatMap.Prune(next.HeatMap, FindScale(next.RiskScaleId), FindScale(next.ImpactScaleId), FindScale(next.ProbabilityScaleId));
        var previous = Project;
        Apply(Messages.Undo_EditProject, DocumentChangeKind.Project,
            () => Project = next,
            () => Project = previous);
    }

    /// <summary>
    /// Cambia el nivel de riesgo de una celda del mapa de calor. Se puede hacer con riesgos cargados:
    /// el nivel de los riesgos de esa celda cambia con ella.
    /// </summary>
    public void SetHeatCell(Guid impactLevelId, Guid probabilityLevelId, Guid riskLevelId)
    {
        var impact = ProjectScale(ScaleType.Impact)?.FindLevel(impactLevelId);
        var probability = ProjectScale(ScaleType.Probability)?.FindLevel(probabilityLevelId);
        var level = ProjectScale(ScaleType.Risk)?.FindLevel(riskLevelId);
        if (impact is null || probability is null || level is null)
        {
            throw new DomainException(Messages.Error_HeatCellInvalid);
        }

        var next = Project.Clone();
        next.HeatMap = [.. Project.HeatMap.Where(c => c.ImpactLevelId != impactLevelId || c.ProbabilityLevelId != probabilityLevelId),
            new HeatCell(impactLevelId, probabilityLevelId, riskLevelId)];
        var previous = Project;
        Apply(Validation.Format(Messages.Undo_EditHeatMap, impact.Name, probability.Name), DocumentChangeKind.Project,
            () => Project = next,
            () => Project = previous);
    }

    /// <summary>Vuelve todo el mapa de calor a los niveles por omisión.</summary>
    public void ResetHeatMap()
    {
        if (Project.HeatMap.Count == 0)
        {
            return;
        }

        var next = Project.Clone();
        next.HeatMap = [];
        var previous = Project;
        Apply(Messages.Undo_ResetHeatMap, DocumentChangeKind.Project,
            () => Project = next,
            () => Project = previous);
    }

    // ---- Escalas -----------------------------------------------------------

    public Scale AddScale(Scale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        var copy = scale.Clone();
        copy.Name = copy.Name.Trim();
        foreach (var level in copy.Levels)
        {
            level.Name = level.Name.Trim();
            level.Color = Palette.Normalize(level.Color);
        }

        Validation.ValidateScale(copy);
        if (_scales.Exists(s => s.Id == copy.Id) || copy.Levels.Exists(l => FindLevel(l.Id) is not null))
        {
            copy = RenewIds(copy);
        }

        Apply(Validation.Format(Messages.Undo_AddScale, copy.Name), DocumentChangeKind.Scales,
            () => _scales.Add(copy),
            () => _scales.Remove(copy));
        return copy;
    }

    /// <summary>
    /// RF-04: renombrar, agregar niveles y cambiar nombre, color u orden.
    /// RF-05: quitar un nivel en uso se rechaza. El tipo no cambia (RF-02).
    /// </summary>
    public void UpdateScale(Scale updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var index = _scales.FindIndex(s => s.Id == updated.Id);
        if (index < 0)
        {
            throw new DomainException(Messages.Error_ScaleNotFound);
        }

        var previous = _scales[index];
        var next = updated.Clone();
        next.Type = previous.Type;
        next.Name = next.Name.Trim();
        foreach (var level in next.Levels)
        {
            level.Name = level.Name.Trim();
            level.Color = Palette.Normalize(level.Color);
        }

        Validation.ValidateScale(next);

        foreach (var removed in previous.Levels.Where(l => next.FindLevel(l.Id) is null))
        {
            if (IsLevelInUse(removed.Id))
            {
                throw new DomainException(Validation.Format(Messages.Error_LevelInUse, removed.Name));
            }
        }

        Apply(Validation.Format(Messages.Undo_EditScale, next.Name), DocumentChangeKind.Scales,
            () => _scales[index] = next,
            () => _scales[index] = previous);
    }

    public void RemoveScale(Guid scaleId)
    {
        var index = _scales.FindIndex(s => s.Id == scaleId);
        if (index < 0)
        {
            throw new DomainException(Messages.Error_ScaleNotFound);
        }

        var scale = _scales[index];
        if (IsScaleAssigned(scaleId))
        {
            throw new DomainException(Validation.Format(Messages.Error_ScaleAssigned, scale.Name));
        }

        Apply(Validation.Format(Messages.Undo_RemoveScale, scale.Name), DocumentChangeKind.Scales,
            () => _scales.RemoveAt(index),
            () => _scales.Insert(index, scale));
    }

    // ---- Riesgos -----------------------------------------------------------

    public Risk AddRisk(Risk risk)
    {
        ArgumentNullException.ThrowIfNull(risk);
        var copy = Normalize(risk.Clone());
        if (_risks.Exists(r => r.Id == copy.Id))
        {
            copy.Id = Guid.NewGuid();
        }

        copy.CreatedUtc = copy.ModifiedUtc = DateTime.UtcNow;
        Validation.ValidateRisk(copy, this);

        Apply(Validation.Format(Messages.Undo_AddRisk, copy.Title), DocumentChangeKind.Risks,
            () => _risks.Add(copy),
            () => _risks.Remove(copy));
        return copy;
    }

    public void UpdateRisk(Risk updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var index = _risks.FindIndex(r => r.Id == updated.Id);
        if (index < 0)
        {
            throw new DomainException(Messages.Error_RiskNotFound);
        }

        var previous = _risks[index];
        var next = Normalize(updated.Clone());
        next.CreatedUtc = previous.CreatedUtc;
        next.ModifiedUtc = DateTime.UtcNow;
        Validation.ValidateRisk(next, this);

        Apply(Validation.Format(Messages.Undo_EditRisk, next.Title), DocumentChangeKind.Risks,
            () => _risks[index] = next,
            () => _risks[index] = previous);
    }

    public void RemoveRisk(Guid riskId)
    {
        var index = _risks.FindIndex(r => r.Id == riskId);
        if (index < 0)
        {
            throw new DomainException(Messages.Error_RiskNotFound);
        }

        var risk = _risks[index];
        Apply(Validation.Format(Messages.Undo_RemoveRisk, risk.Title), DocumentChangeKind.Risks,
            () => _risks.RemoveAt(index),
            () => _risks.Insert(index, risk));
    }

    /// <summary>Copia profunda para guardar en segundo plano sin bloquear la edición (RNF-22).</summary>
    public RiskMatrixDocument Snapshot() =>
        new(Project.Clone(), _scales.Select(s => s.Clone()), _risks.Select(r => r.Clone()));

    // ---- Interno -----------------------------------------------------------

    private void Apply(string description, DocumentChangeKind kind, Action redo, Action undo)
    {
        redo();
        History.Push(description,
            () => { undo(); Raise(kind); },
            () => { redo(); Raise(kind); });
        Raise(kind);
    }

    private void Raise(DocumentChangeKind kind) => Changed?.Invoke(this, new DocumentChangedEventArgs(kind));

    private static Risk Normalize(Risk risk)
    {
        risk.Title = risk.Title.Trim();
        risk.ShortDescription = risk.ShortDescription.Trim();
        risk.LongDescription = risk.LongDescription.Trim();
        risk.ImpactJustification = risk.ImpactJustification.Trim();
        risk.ProbabilityJustification = risk.ProbabilityJustification.Trim();
        return risk;
    }

    private static Scale RenewIds(Scale scale)
    {
        scale.Id = Guid.NewGuid();
        foreach (var level in scale.Levels)
        {
            level.Id = Guid.NewGuid();
        }

        return scale;
    }
}
