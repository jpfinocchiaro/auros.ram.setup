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
        if (info.Consequences.Count == 0 && info.MaterialityCriteria.Count == 0)
        {
            info.Consequences = SuggestedCatalog(CatalogKind.Consequence);
            info.MaterialityCriteria = SuggestedCatalog(CatalogKind.Materiality);
        }

        return new RiskMatrixDocument(info, scaleList, []);
    }

    /// <summary>Opciones con las que arranca cada catálogo (se pueden cambiar, quitar o agregar otras).</summary>
    public static IReadOnlyList<CatalogItem> SuggestedCatalog(CatalogKind kind) =>
        [.. (kind == CatalogKind.Consequence ? Messages.Suggested_Consequences : Messages.Suggested_Materiality)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => new CatalogItem(Guid.NewGuid(), name))];

    // ---- Catálogos -----------------------------------------------------------

    /// <summary>
    /// Agrega una opción al catálogo y la devuelve. Si ya hay una con el mismo nombre (sin distinguir
    /// mayúsculas), devuelve esa y no agrega otra.
    /// </summary>
    public CatalogItem AddCatalogItem(CatalogKind kind, string name)
    {
        var trimmed = (name ?? "").Trim();
        Validation.ValidateCatalogName(trimmed);
        var list = Project.CatalogFor(kind);
        if (list.FirstOrDefault(i => string.Equals(i.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) is { } existing)
        {
            return existing;
        }

        var item = new CatalogItem(Guid.NewGuid(), trimmed);
        SetCatalog(kind, [.. list, item], Validation.Format(Messages.Undo_AddCatalogItem, trimmed));
        return item;
    }

    public void RenameCatalogItem(CatalogKind kind, Guid id, string name)
    {
        var trimmed = (name ?? "").Trim();
        Validation.ValidateCatalogName(trimmed);
        var list = Project.CatalogFor(kind);
        if (list.All(i => i.Id != id))
        {
            throw new DomainException(Messages.Error_CatalogItemNotFound);
        }

        if (list.Any(i => i.Id != id && string.Equals(i.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new DomainException(Validation.Format(Messages.Error_CatalogDuplicate, trimmed));
        }

        SetCatalog(kind, [.. list.Select(i => i.Id == id ? i with { Name = trimmed } : i)], Validation.Format(Messages.Undo_EditCatalogItem, trimmed));
    }

    /// <summary>Cantidad de riesgos que usan la opción.</summary>
    public int CatalogItemUsage(CatalogKind kind, Guid id) => kind == CatalogKind.Consequence
        ? _risks.Count(r => r.ConsequenceIds.Contains(id))
        : _risks.Count(r => r.MaterialityId == id);

    /// <summary>Quita una opción del catálogo. No se puede si algún riesgo la usa.</summary>
    public void RemoveCatalogItem(CatalogKind kind, Guid id)
    {
        var list = Project.CatalogFor(kind);
        var item = list.FirstOrDefault(i => i.Id == id) ?? throw new DomainException(Messages.Error_CatalogItemNotFound);
        if (CatalogItemUsage(kind, id) is var used and > 0)
        {
            throw new DomainException(Validation.Format(Messages.Error_CatalogItemInUse, item.Name, used));
        }

        SetCatalog(kind, [.. list.Where(i => i.Id != id)], Validation.Format(Messages.Undo_RemoveCatalogItem, item.Name));
    }

    private void SetCatalog(CatalogKind kind, IReadOnlyList<CatalogItem> items, string description)
    {
        var next = Project.Clone();
        if (kind == CatalogKind.Consequence)
        {
            next.Consequences = items;
        }
        else
        {
            next.MaterialityCriteria = items;
        }

        var previous = Project;
        Apply(description, DocumentChangeKind.Project, () => Project = next, () => Project = previous);
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

    /// <summary>Nombres de las consecuencias potenciales del riesgo, en el orden en que se cargaron.</summary>
    public IReadOnlyList<string> ConsequenceNames(Risk risk) =>
        [.. risk.ConsequenceIds.Select(id => Project.Consequences.FirstOrDefault(c => c.Id == id)?.Name).OfType<string>()];

    /// <summary>Nombre del criterio de materialidad del riesgo (vacío si no tiene).</summary>
    public string MaterialityName(Risk risk) =>
        risk.MaterialityId is { } id ? Project.MaterialityCriteria.FirstOrDefault(c => c.Id == id)?.Name ?? "" : "";

    /// <summary>
    /// RF-05: un nivel está en uso si algún riesgo lo tiene (hoy o en una evaluación anterior del historial)
    /// o si es el nivel de riesgo que le toca a alguno.
    /// </summary>
    public bool IsLevelInUse(Guid levelId) =>
        _risks.Exists(r => r.ImpactLevelId == levelId || r.ProbabilityLevelId == levelId || RiskLevelOf(r)?.Id == levelId ||
                           r.Assessments.Any(a => a.ImpactLevelId == levelId || a.ProbabilityLevelId == levelId));

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
            NormalizeLevel(level);
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
            NormalizeLevel(level);
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

    /// <summary>Fecha de hoy para las evaluaciones.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Qué fechas de evaluación se aceptan (la aplicación la toma de las Preferencias).</summary>
    public AssessmentDatePolicy DatePolicy { get; set; } = AssessmentDatePolicy.AllowAll;

    /// <summary>Cómo se cargan impacto y probabilidad: dato obligatorio y ajuste automático (Preferencias).</summary>
    public RiskEntryPolicy EntryPolicy { get; set; } = RiskEntryPolicy.Default;

    /// <param name="assessedOn">Fecha de la primera evaluación (hoy si se omite).</param>
    /// <param name="note">Comentario de la evaluación.</param>
    public Risk AddRisk(Risk risk, DateOnly? assessedOn = null, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(risk);
        var copy = Normalize(risk.Clone());
        if (_risks.Exists(r => r.Id == copy.Id))
        {
            copy.Id = Guid.NewGuid();
        }

        copy.CreatedUtc = copy.ModifiedUtc = DateTime.UtcNow;
        Validation.ValidateRisk(copy, this);

        // Un riesgo nuevo empieza su historial con la evaluación de hoy (o de la fecha indicada).
        if (assessedOn is not null || copy.Assessments.Count == 0)
        {
            var date = assessedOn ?? Today;
            CheckAssessmentDate(date, creating: true, []);
            copy.Assessments = [NewAssessment(date, copy, note)];
        }
        else
        {
            WithCurrentFromHistory(copy);
        }

        Apply(Validation.Format(Messages.Undo_AddRisk, copy.Title), DocumentChangeKind.Risks,
            () => _risks.Add(copy),
            () => _risks.Remove(copy));
        return copy;
    }

    /// <summary>
    /// Edita (corrige) los datos del riesgo. Si cambió su impacto o su probabilidad, se toma como un error de carga:
    /// se corrige la evaluación vigente, sin agregar otra al historial. Un cambio en el tiempo se registra con
    /// <see cref="AddAssessment"/>.
    /// </summary>
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

        var history = previous.Assessments;
        next.Assessments = history;
        if (next.ImpactLevelId != previous.ImpactLevelId || next.ProbabilityLevelId != previous.ProbabilityLevelId ||
            next.ImpactAmount != previous.ImpactAmount || next.ProbabilityPercent != previous.ProbabilityPercent)
        {
            next.Assessments = history.Count == 0
                ? [NewAssessment(Today, next, null)]
                : [.. history.Take(history.Count - 1),
                    history[^1] with
                    {
                        ImpactLevelId = next.ImpactLevelId,
                        ProbabilityLevelId = next.ProbabilityLevelId,
                        ImpactAmount = next.ImpactAmount,
                        ProbabilityPercent = next.ProbabilityPercent,
                    }];
        }

        Apply(Validation.Format(Messages.Undo_EditRisk, next.Title), DocumentChangeKind.Risks,
            () => _risks[index] = next,
            () => _risks[index] = previous);
    }

    /// <summary>
    /// Nueva evaluación de un riesgo existente: registra su impacto y su probabilidad en una fecha (hoy si se omite).
    /// Si ya hay una evaluación ese día, la reemplaza. Si la fecha es anterior a otra evaluación registrada (lo permite
    /// <see cref="DatePolicy"/>), se intercala en el historial y el estado actual sigue siendo el de la más reciente.
    /// Las justificaciones, si se indican, pasan a ser las del riesgo cuando la evaluación queda como la vigente.
    /// </summary>
    public void AddAssessment(
        Guid riskId, Guid impactLevelId, Guid probabilityLevelId, DateOnly? date = null, string? note = null,
        string? impactJustification = null, string? probabilityJustification = null,
        decimal? impactAmount = null, decimal? probabilityPercent = null)
    {
        var index = _risks.FindIndex(r => r.Id == riskId);
        if (index < 0)
        {
            throw new DomainException(Messages.Error_RiskNotFound);
        }

        var previous = _risks[index];
        var next = previous.Clone();
        next.ImpactLevelId = impactLevelId;
        next.ProbabilityLevelId = probabilityLevelId;
        next.ImpactAmount = Round(impactAmount);
        next.ProbabilityPercent = Round(probabilityPercent);
        next.ImpactJustification = (impactJustification ?? previous.ImpactJustification).Trim();
        next.ProbabilityJustification = (probabilityJustification ?? previous.ProbabilityJustification).Trim();
        Validation.ValidateRisk(next, this);

        var day = date ?? Today;
        var history = previous.Assessments;
        var sameDay = history.FirstOrDefault(a => a.Date == day);
        CheckAssessmentDate(day, creating: false, [.. history.Where(a => a != sameDay)]);
        var entry = NewAssessment(day, next, note);
        next.Assessments = sameDay is null
            ? [.. history, entry]
            : [.. history.Select(a => a == sameDay ? entry with { Id = sameDay.Id } : a)];
        WithCurrentFromHistory(next);

        // Intercalada (no quedó como la vigente): las justificaciones del riesgo siguen siendo las del estado actual.
        if (next.Assessments[^1].Id != (sameDay?.Id ?? entry.Id))
        {
            next.ImpactJustification = previous.ImpactJustification;
            next.ProbabilityJustification = previous.ProbabilityJustification;
        }

        next.ModifiedUtc = DateTime.UtcNow;
        Apply(Validation.Format(Messages.Undo_AddAssessment, next.Title), DocumentChangeKind.Risks,
            () => _risks[index] = next,
            () => _risks[index] = previous);
    }

    /// <summary>
    /// Quita una evaluación del historial (p. ej., una cargada por error). El riesgo vuelve al estado de la última
    /// que quede; la única evaluación de un riesgo no se puede quitar.
    /// </summary>
    public void RemoveAssessment(Guid riskId, Guid assessmentId)
    {
        var index = _risks.FindIndex(r => r.Id == riskId);
        if (index < 0)
        {
            throw new DomainException(Messages.Error_RiskNotFound);
        }

        var previous = _risks[index];
        if (previous.Assessments.All(a => a.Id != assessmentId))
        {
            throw new DomainException(Messages.Error_AssessmentNotFound);
        }

        if (previous.Assessments.Count <= 1)
        {
            throw new DomainException(Messages.Error_LastAssessment);
        }

        var next = previous.Clone();
        next.Assessments = [.. previous.Assessments.Where(a => a.Id != assessmentId)];
        WithCurrentFromHistory(next);
        next.ModifiedUtc = DateTime.UtcNow;
        Apply(Validation.Format(Messages.Undo_RemoveAssessment, next.Title), DocumentChangeKind.Risks,
            () => _risks[index] = next,
            () => _risks[index] = previous);
    }

    /// <summary>
    /// Corrige la fecha de una evaluación registrada. El historial se reordena y el estado actual pasa a ser el de la
    /// evaluación más reciente. No puede quedar otra evaluación el mismo día.
    /// </summary>
    public void ChangeAssessmentDate(Guid riskId, Guid assessmentId, DateOnly date)
    {
        var index = _risks.FindIndex(r => r.Id == riskId);
        if (index < 0)
        {
            throw new DomainException(Messages.Error_RiskNotFound);
        }

        var previous = _risks[index];
        var target = previous.Assessments.FirstOrDefault(a => a.Id == assessmentId)
            ?? throw new DomainException(Messages.Error_AssessmentNotFound);
        if (target.Date == date)
        {
            return;
        }

        var others = previous.Assessments.Where(a => a.Id != assessmentId).ToList();
        CheckAssessmentDate(date, creating: false, others);
        if (others.Exists(a => a.Date == date))
        {
            throw new DomainException(Validation.Format(Messages.Error_AssessmentDateTaken, Display(date)), nameof(RiskAssessment.Date));
        }

        var next = previous.Clone();
        next.Assessments = [.. previous.Assessments.Select(a => a.Id == assessmentId ? a with { Date = date } : a)];
        WithCurrentFromHistory(next);
        next.ModifiedUtc = DateTime.UtcNow;
        Apply(Validation.Format(Messages.Undo_EditAssessmentDate, next.Title), DocumentChangeKind.Risks,
            () => _risks[index] = next,
            () => _risks[index] = previous);
    }

    /// <summary>Elimina varios riesgos (con todas sus evaluaciones) en un solo paso que se puede deshacer.</summary>
    public void RemoveRisks(IReadOnlyCollection<Guid> riskIds)
    {
        var removed = _risks.Select((r, i) => (Risk: r, Index: i)).Where(x => riskIds.Contains(x.Risk.Id)).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        if (removed.Count == 1)
        {
            RemoveRisk(removed[0].Risk.Id);
            return;
        }

        Apply(Validation.Format(Messages.Undo_RemoveRisks, removed.Count), DocumentChangeKind.Risks,
            () => _risks.RemoveAll(r => riskIds.Contains(r.Id)),
            () =>
            {
                foreach (var (risk, index) in removed)
                {
                    _risks.Insert(index, risk);
                }
            });
    }

    /// <summary>
    /// Quita varias evaluaciones (de uno o más riesgos) en un solo paso que se puede deshacer. Cada riesgo vuelve
    /// al estado de la última evaluación que le quede; a ninguno se le pueden quitar todas (para eso se elimina el riesgo).
    /// </summary>
    public void RemoveAssessments(IReadOnlyCollection<(Guid RiskId, Guid AssessmentId)> entries)
    {
        var changes = new List<(int Index, Risk Previous, Risk Next)>();
        foreach (var group in entries.GroupBy(e => e.RiskId))
        {
            var index = _risks.FindIndex(r => r.Id == group.Key);
            if (index < 0)
            {
                throw new DomainException(Messages.Error_RiskNotFound);
            }

            var previous = _risks[index];
            var ids = group.Select(e => e.AssessmentId).ToHashSet();
            if (previous.Assessments.All(a => ids.Contains(a.Id)))
            {
                throw new DomainException(Validation.Format(Messages.Error_AllAssessments, previous.Title));
            }

            var next = previous.Clone();
            next.Assessments = [.. previous.Assessments.Where(a => !ids.Contains(a.Id))];
            WithCurrentFromHistory(next);
            next.ModifiedUtc = DateTime.UtcNow;
            changes.Add((index, previous, next));
        }

        if (changes.Count == 0)
        {
            return;
        }

        Apply(Validation.Format(Messages.Undo_RemoveAssessments, entries.Count), DocumentChangeKind.Risks,
            () => changes.ForEach(c => _risks[c.Index] = c.Next),
            () => changes.ForEach(c => _risks[c.Index] = c.Previous));
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
        risk.ImpactAmount = Round(risk.ImpactAmount);
        risk.ProbabilityPercent = Round(risk.ProbabilityPercent);
        risk.ConsequenceIds = [.. risk.ConsequenceIds.Distinct()];
        return risk;
    }

    private static RiskAssessment NewAssessment(DateOnly date, Risk risk, string? note) =>
        new(Guid.NewGuid(), date, risk.ImpactLevelId, risk.ProbabilityLevelId, (note ?? "").Trim(), DateTime.UtcNow)
        {
            ImpactAmount = risk.ImpactAmount,
            ProbabilityPercent = risk.ProbabilityPercent,
        };

    /// <summary>Montos y porcentajes con 2 decimales.</summary>
    private static decimal? Round(decimal? value) => value is { } v ? Math.Round(v, 2, MidpointRounding.AwayFromZero) : null;

    private static void NormalizeLevel(Level level)
    {
        level.Name = level.Name.Trim();
        level.Color = Palette.Normalize(level.Color);
        level.RangeFrom = Round(level.RangeFrom);
        level.RangeTo = Round(level.RangeTo);
    }

    /// <summary>
    /// Una evaluación nunca puede ser futura. Una fecha anterior a hoy, o anterior a otra evaluación ya registrada
    /// (<paramref name="others"/>), solo se acepta si <see cref="DatePolicy"/> lo permite.
    /// </summary>
    private void CheckAssessmentDate(DateOnly date, bool creating, IReadOnlyCollection<RiskAssessment> others)
    {
        if (date > Today)
        {
            throw new DomainException(Messages.Error_AssessmentInFuture, nameof(RiskAssessment.Date));
        }

        if (date < Today && !(creating ? DatePolicy.AllowPastOnCreate : DatePolicy.AllowPastOnEdit))
        {
            throw new DomainException(
                creating ? Messages.Error_AssessmentPastOnCreate : Messages.Error_AssessmentPastOnEdit, nameof(RiskAssessment.Date));
        }

        if (!creating && !DatePolicy.AllowBeforeRecorded && others.Count > 0 && others.Max(a => a.Date) > date)
        {
            throw new DomainException(
                Validation.Format(Messages.Error_AssessmentBeforeLast, Display(others.Max(a => a.Date))), nameof(RiskAssessment.Date));
        }
    }

    private static string Display(DateOnly date) => date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Ordena el historial y deja como estado actual el de la última evaluación.</summary>
    public static void WithCurrentFromHistory(Risk risk)
    {
        risk.Assessments = [.. risk.Assessments.OrderBy(a => a.Date).ThenBy(a => a.RecordedUtc)];
        if (risk.Assessments.Count > 0)
        {
            risk.ImpactLevelId = risk.Assessments[^1].ImpactLevelId;
            risk.ProbabilityLevelId = risk.Assessments[^1].ProbabilityLevelId;
            risk.ImpactAmount = risk.Assessments[^1].ImpactAmount;
            risk.ProbabilityPercent = risk.Assessments[^1].ProbabilityPercent;
        }
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
