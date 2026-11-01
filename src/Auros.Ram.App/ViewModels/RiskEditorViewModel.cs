using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Views;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Un botón de color por nivel de la escala del proyecto (RF-10).</summary>
public sealed partial class LevelOption(LevelChip chip, DimensionEditor owner) : ObservableObject
{
    public LevelChip Chip { get; } = chip;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => owner.Select(this);
}

/// <summary>
/// Una dimensión (Impacto o Probabilidad): nivel elegido, valor (monto del impacto o probabilidad en %) y
/// justificación. Con ajuste automático, al ingresar el valor se elige el nivel cuyo rango lo contiene; el
/// nivel se puede cambiar después.
/// </summary>
public sealed partial class DimensionEditor : ObservableObject
{
    private readonly Scale? _scale;
    private bool _settingValue;

    public DimensionEditor(ScaleType type, Scale? scale, Guid selected, string justification, decimal? value = null,
        EntryMode mode = EntryMode.Level, bool auto = true)
    {
        Type = type;
        _scale = scale;
        Mode = mode;
        Auto = auto;
        Label = type.DisplayName();
        ScaleName = scale?.Name ?? "";
        Options = [.. (scale?.OrderedLevels ?? []).Select(l => new LevelOption(new LevelChip(l, RangeFormat.UnitFor(type)), this))];
        foreach (var o in Options)
        {
            o.IsSelected = o.Chip.Id == selected;
        }

        _justification = justification;
        _value = value;
    }

    public EntryMode Mode { get; }
    public bool Auto { get; }

    /// <summary>Monto del impacto o probabilidad en porcentaje (2 decimales).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueWarning))]
    private decimal? _value;

    public bool IsProbability => Type == ScaleType.Probability;

    public decimal ValueMax => IsProbability ? 100m : 999_999_999_999.99m;

    public string ValueLabel => (IsProbability ? Strings.Risk_PercentLabel : Strings.Risk_AmountLabel) + " " + Mode switch
    {
        EntryMode.Value => Strings.Risk_Required,
        EntryMode.Either => Strings.Risk_OrLevel,
        _ => Strings.Risk_Optional,
    };

    public bool HasRanges => _scale?.Levels.Any(l => l.HasRange) == true;

    /// <summary>Ayuda bajo el valor: si el nivel se ajusta solo, o si la escala no tiene rangos.</summary>
    public string ValueHint => !HasRanges ? Strings.Risk_NoRanges : Auto ? Strings.Risk_AutoHint : Strings.Risk_ManualHint;

    /// <summary>Aviso si el valor no cae en ningún rango o no corresponde al nivel elegido.</summary>
    public string? ValueWarning
    {
        get
        {
            if (Value is not { } v || !HasRanges)
            {
                return null;
            }

            var unit = RangeFormat.UnitFor(Type);
            var match = _scale!.OrderedLevels.FirstOrDefault(l => l.Contains(v));
            if (match is null)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Risk_ValueOutOfRanges, RangeFormat.Value(v, unit));
            }

            var selected = _scale.FindLevel(SelectedId);
            return selected is not null && selected.Id != match.Id
                ? string.Format(CultureInfo.CurrentCulture, Strings.Risk_ValueOtherLevel, RangeFormat.Value(v, unit), match.Name, selected.Name)
                : null;
        }
    }

    partial void OnValueChanged(decimal? value)
    {
        // Ajuste automático: el nivel cuyo rango contiene el valor (si hay uno); después se puede cambiar a mano.
        if (!Auto || value is not { } v || _settingValue)
        {
            return;
        }

        var match = _scale?.OrderedLevels.FirstOrDefault(l => l.Contains(v));
        if (match is not null && Options.FirstOrDefault(o => o.Chip.Id == match.Id) is { } option && !option.IsSelected)
        {
            Select(option);
        }
    }

    public ScaleType Type { get; }
    public string Label { get; }
    public string ScaleName { get; }
    public IReadOnlyList<LevelOption> Options { get; }

    public Guid SelectedId => Options.FirstOrDefault(o => o.IsSelected)?.Chip.Id ?? Guid.Empty;

    public string SelectedText => Options.FirstOrDefault(o => o.IsSelected)?.Chip.Name ?? Strings.Risk_NoLevel;

    [ObservableProperty]
    private string _justification;

    public event EventHandler? SelectionChanged;

    public void Select(LevelOption option)
    {
        foreach (var o in Options)
        {
            o.IsSelected = ReferenceEquals(o, option);
        }

        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(ValueWarning));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Carga un valor sin ajustar el nivel (p. ej., al volver a leer el riesgo).</summary>
    public void SetValueQuietly(decimal? value)
    {
        _settingValue = true;
        try
        {
            Value = value;
        }
        finally
        {
            _settingValue = false;
        }
    }
}

/// <summary>Una evaluación del historial del riesgo, para la tabla del editor. Su fecha se puede corregir.</summary>
public sealed partial class HistoryEntry(
    Guid id, DateOnly date, LevelChip impact, LevelChip probability, LevelChip? riskLevel, string note, bool isCurrent, bool canRemove,
    Action<HistoryEntry, DateOnly> onDateChanged) : ObservableObject
{
    public Guid Id { get; } = id;
    public DateOnly OriginalDate { get; } = date;
    public LevelChip Impact { get; } = impact;
    public LevelChip Probability { get; } = probability;
    public LevelChip? RiskLevel { get; } = riskLevel;
    public string Note { get; } = note;
    public bool IsCurrent { get; } = isCurrent;
    public bool CanRemove { get; } = canRemove;

    /// <summary>Monto y porcentaje de la evaluación (vacíos si no se cargaron).</summary>
    public string ImpactValue { get; init; } = "";
    public string ProbabilityValue { get; init; } = "";

    /// <summary>Fecha en el selector; al cambiarla se corrige la evaluación.</summary>
    [ObservableProperty]
    private DateTime? _date = date.ToDateTime(TimeOnly.MinValue);

    partial void OnDateChanged(DateTime? value)
    {
        if (value is { } d && DateOnly.FromDateTime(d) != OriginalDate)
        {
            onDateChanged(this, DateOnly.FromDateTime(d));
        }
    }
}

/// <summary>Alta y modificación de un riesgo (F3). Los errores se muestran sin cerrar el diálogo.</summary>
/// <summary>Una consecuencia elegida para el riesgo (Id vacío: nueva, se agrega al catálogo al guardar).</summary>
public sealed record ConsequenceChoice(Guid Id, string Name);

/// <summary>Qué se hace con la ventana del riesgo.</summary>
public enum RiskEditorMode
{
    /// <summary>Alta: datos del riesgo y su primera evaluación.</summary>
    New,

    /// <summary>Edición: corrige los datos; un cambio de impacto o probabilidad corrige la evaluación vigente.</summary>
    Edit,

    /// <summary>Nueva evaluación de un riesgo existente: su impacto y su probabilidad en una fecha (queda en el historial).</summary>
    Reassess,
}

public sealed partial class RiskEditorViewModel : ObservableObject
{
    private readonly RiskMatrixDocument _doc;
    private readonly Risk? _original;
    private Guid _currentImpact;
    private Guid _currentProbability;

    /// <param name="risk">El riesgo a modificar, o null para uno nuevo.</param>
    /// <param name="preset">Para uno nuevo: niveles ya elegidos (p. ej., la celda de la matriz donde se hizo doble clic).</param>
    /// <param name="mode">Para un riesgo existente: editarlo (por omisión) o registrar una nueva evaluación.</param>
    public RiskEditorViewModel(RiskMatrixDocument doc, Risk? risk, Risk? preset = null, RiskEditorMode mode = RiskEditorMode.Edit)
    {
        _doc = doc;
        _original = risk;
        _mode = risk is null ? RiskEditorMode.New : mode;
        _title = risk?.Title ?? "";
        _shortDescription = risk?.ShortDescription ?? "";
        _longDescription = risk?.LongDescription ?? "";
        var source = risk ?? preset;
        Dimensions = [.. ScaleTypeExtensions.Dimensions.Select(t =>
            new DimensionEditor(t, doc.ProjectScale(t), source?.LevelIdFor(t) ?? Guid.Empty, risk?.JustificationFor(t) ?? "",
                risk?.ValueFor(t), doc.EntryPolicy.ModeFor(t), doc.EntryPolicy.AutoFor(t)))];
        foreach (var d in Dimensions)
        {
            d.SelectionChanged += (_, _) =>
            {
                UpdateRiskLevel();
                OnPropertyChanged(nameof(IsReassessing));
                OnPropertyChanged(nameof(IsCorrecting));
                OnPropertyChanged(nameof(DateWarning));
            };
        }

        // Consecuencias potenciales y criterio de materialidad, del catálogo de la matriz.
        foreach (var id in risk?.ConsequenceIds ?? [])
        {
            if (doc.Project.Consequences.FirstOrDefault(c => c.Id == id) is { } item)
            {
                Consequences.Add(new ConsequenceChoice(item.Id, item.Name));
            }
        }

        _materiality = risk?.MaterialityId is { } m ? doc.Project.MaterialityCriteria.FirstOrDefault(c => c.Id == m)?.Name ?? "" : "";

        // Empresa y unidad: las del riesgo (o las elegidas en la barra, para uno nuevo); si no, la primera.
        var company = (source is not null ? doc.Project.FindCompany(source.CompanyId) : null) ?? doc.Project.Companies.FirstOrDefault();
        _company = company?.Name ?? "";
        _unit = (company?.Units.FirstOrDefault(u => u.Id == source?.UnitId) ?? company?.Units.FirstOrDefault())?.Name ?? "";

        _currentImpact = risk?.ImpactLevelId ?? Guid.Empty;
        _currentProbability = risk?.ProbabilityLevelId ?? Guid.Empty;
        UpdateRiskLevel();
        LoadHistory();
    }

    // ---- Clonar ----------------------------------------------------------------

    /// <summary>
    /// Alta de un riesgo nuevo copiado de otro: título con "(copia)", descripciones, consecuencias, criterio,
    /// niveles, justificaciones, monto y porcentaje. Arranca con una sola evaluación (la fecha se elige al guardar).
    /// </summary>
    public static RiskEditorViewModel CloneOf(RiskMatrixDocument doc, Risk source)
    {
        var vm = new RiskEditorViewModel(doc, null, source);
        var title = string.Format(CultureInfo.CurrentCulture, Strings.Clone_Title, source.Title);
        vm.Title = title.Length <= Limits.RiskTitleMax ? title : source.Title;
        vm.ShortDescription = source.ShortDescription;
        vm.LongDescription = source.LongDescription;
        foreach (var d in vm.Dimensions)
        {
            d.Justification = source.JustificationFor(d.Type);
            d.SetValueQuietly(source.ValueFor(d.Type));
        }

        foreach (var name in doc.ConsequenceNames(source))
        {
            vm.NewConsequence = name;
            vm.AddConsequence();
        }

        vm.Materiality = doc.MaterialityName(source);
        vm.AssessmentNote = string.Format(CultureInfo.CurrentCulture, Strings.Clone_Note, source.Title);
        return vm;
    }

    /// <summary>
    /// Nueva evaluación del riesgo a partir de una registrada: sus niveles, monto, porcentaje y comentario ya
    /// cargados; la fecha es la de hoy (se puede cambiar) y se ajusta lo que cambió.
    /// </summary>
    public static RiskEditorViewModel ReassessFrom(RiskMatrixDocument doc, Risk risk, RiskAssessment source)
    {
        var vm = new RiskEditorViewModel(doc, risk, mode: RiskEditorMode.Reassess);
        foreach (var d in vm.Dimensions)
        {
            var levelId = d.Type == ScaleType.Impact ? source.ImpactLevelId : source.ProbabilityLevelId;
            if (d.Options.FirstOrDefault(o => o.Chip.Id == levelId) is { } option)
            {
                d.Select(option);
            }

            d.SetValueQuietly(d.Type == ScaleType.Impact ? source.ImpactAmount : source.ProbabilityPercent);
        }

        vm.AssessmentNote = source.Note;
        return vm;
    }

    // ---- Empresa y unidad de negocio --------------------------------------

    /// <summary>Empresa del riesgo (obligatoria): se elige del maestro o se escribe una nueva, que se agrega.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitOptions))]
    private string _company;

    /// <summary>Unidad de negocio de la empresa (obligatoria): se elige o se escribe una nueva, que se agrega.</summary>
    [ObservableProperty]
    private string _unit;

    public IReadOnlyList<string> CompanyOptions => [.. _doc.Project.Companies.Select(c => c.Name)];

    /// <summary>Abre el maestro de empresas y unidades (lo provee quien muestra el editor); null = sin botón.</summary>
    public Func<RiskMatrixDocument, Task>? OrganizationsEditor { get; set; }

    public bool CanEditOrganizations => OrganizationsEditor is not null;

    [RelayCommand]
    private async Task EditOrganizations()
    {
        if (OrganizationsEditor is null)
        {
            return;
        }

        await OrganizationsEditor(_doc);

        // Los nombres pudieron cambiar: se vuelven a leer las opciones y se conserva lo elegido si sigue existiendo.
        var company = Company;
        var unit = Unit;
        OnPropertyChanged(nameof(CompanyOptions));
        OnPropertyChanged(nameof(UnitOptions));
        Company = company;
        Unit = unit;
    }

    /// <summary>Las unidades de la empresa elegida.</summary>
    public IReadOnlyList<string> UnitOptions => [.. (FindCompany(Company)?.Units ?? []).Select(u => u.Name)];

    partial void OnCompanyChanged(string value)
    {
        // Al cambiar de empresa, la unidad pasa a la primera de la nueva si la escrita no es de ella.
        if (FindCompany(value) is { } company && company.Units.All(u => !Same(u.Name, Unit)))
        {
            Unit = company.Units.FirstOrDefault()?.Name ?? "";
        }
    }

    private Company? FindCompany(string? name) =>
        _doc.Project.Companies.FirstOrDefault(c => Same(c.Name, (name ?? "").Trim()));

    private static bool Same(string a, string? b) => string.Equals(a.Trim(), (b ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Ids de la empresa y la unidad; las escritas que no están en el maestro se agregan a él.</summary>
    private (Guid Company, Guid Unit) ResolveOrganization()
    {
        if (string.IsNullOrWhiteSpace(Company))
        {
            return (Guid.Empty, Guid.Empty);
        }

        var company = FindCompany(Company) ?? _doc.AddCompany(Company);
        var unit = string.IsNullOrWhiteSpace(Unit) ? null : company.Units.FirstOrDefault(u => Same(u.Name, Unit)) ?? _doc.AddUnit(company.Id, Unit);
        return (company.Id, unit?.Id ?? Guid.Empty);
    }

    // ---- Consecuencias y criterio de materialidad --------------------------

    /// <summary>Consecuencias potenciales elegidas para el riesgo.</summary>
    public ObservableCollection<ConsequenceChoice> Consequences { get; } = [];

    /// <summary>Opciones del combo: el catálogo de la matriz (se puede escribir una nueva).</summary>
    public IReadOnlyList<string> ConsequenceOptions => [.. _doc.Project.Consequences.Select(c => c.Name)];

    public IReadOnlyList<string> MaterialityOptions => [.. _doc.Project.MaterialityCriteria.Select(c => c.Name)];

    /// <summary>Lo escrito o elegido en el combo de consecuencias, antes de agregarlo.</summary>
    [ObservableProperty]
    private string? _newConsequence;

    /// <summary>Criterio de materialidad elegido o escrito (vacío: sin criterio).</summary>
    [ObservableProperty]
    private string? _materiality;

    public bool HasConsequences => Consequences.Count > 0;

    /// <summary>Agrega la consecuencia del combo. Si no está en el catálogo, se agrega a él al guardar el riesgo.</summary>
    [RelayCommand]
    public void AddConsequence()
    {
        var name = (NewConsequence ?? "").Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (Consequences.All(c => !string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            var known = _doc.Project.Consequences.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase));
            Consequences.Add(new ConsequenceChoice(known?.Id ?? Guid.Empty, known?.Name ?? name));
            OnPropertyChanged(nameof(HasConsequences));
        }

        NewConsequence = null;
    }

    [RelayCommand]
    private void RemoveConsequence(ConsequenceChoice choice)
    {
        Consequences.Remove(choice);
        OnPropertyChanged(nameof(HasConsequences));
    }

    /// <summary>Ids de las consecuencias y del criterio; los escritos que no están en el catálogo se agregan a él.</summary>
    private (IReadOnlyList<Guid> Consequences, Guid? Materiality) ResolveCatalogs()
    {
        // Lo que quedó escrito en el combo sin apretar "Agregar" también cuenta.
        AddConsequence();
        var consequences = Consequences
            .Select(c => c.Id != Guid.Empty ? c.Id : _doc.AddCatalogItem(CatalogKind.Consequence, c.Name).Id)
            .ToList();
        var materiality = string.IsNullOrWhiteSpace(Materiality) ? (Guid?)null : _doc.AddCatalogItem(CatalogKind.Materiality, Materiality).Id;
        return (consequences, materiality);
    }

    // ---- Evaluación e historial -------------------------------------------

    /// <summary>Fecha de la evaluación: la primera de un riesgo nuevo, o la del cambio de impacto o probabilidad.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateWarning))]
    private DateTime? _assessmentDate = DateTime.Today;

    /// <summary>
    /// Aviso sobre la fecha elegida: anterior a hoy (si la configuración lo permite, solo se advierte), anterior a una
    /// evaluación registrada (se intercala) o no permitida por la configuración (no se podrá guardar).
    /// </summary>
    public string? DateWarning
    {
        get
        {
            if (!ShowAssessment || AssessmentDate is not { } d)
            {
                return null;
            }

            var date = DateOnly.FromDateTime(d);
            var policy = _doc.DatePolicy;
            var today = RiskMatrixDocument.Today;
            if (date > today)
            {
                return Strings.History_WarnFuture;
            }

            var latest = _original is null ? null : _doc.FindRisk(_original.Id)?.Assessments.Where(a => a.Date != date).Select(a => (DateOnly?)a.Date).Max();
            var messages = new List<string>();
            if (date < today)
            {
                var allowed = IsNew ? policy.AllowPastOnCreate : policy.AllowPastOnEdit;
                messages.Add(allowed ? Strings.History_WarnPast : Strings.History_WarnPastNotAllowed);
            }

            if (latest is { } last && date < last)
            {
                var shown = last.ToString("d", CultureInfo.CurrentCulture);
                messages.Add(string.Format(CultureInfo.CurrentCulture,
                    policy.AllowBeforeRecorded ? Strings.History_WarnBeforeRecorded : Strings.History_WarnBeforeRecordedNotAllowed, shown));
            }

            return messages.Count == 0 ? null : string.Join(" ", messages);
        }
    }

    [ObservableProperty]
    private string _assessmentNote = "";

    // ---- Modo: alta, edición o nueva evaluación ----------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew), nameof(IsEdit), nameof(IsReassess), nameof(CanEditTexts), nameof(ShowAssessment),
        nameof(IsCorrecting), nameof(WindowTitle), nameof(ModeTitle), nameof(ModeHint), nameof(AssessmentTitle),
        nameof(AssessmentHint), nameof(DateWarning))]
    private RiskEditorMode _mode;

    public bool IsNew => Mode == RiskEditorMode.New;
    public bool IsEdit => Mode == RiskEditorMode.Edit;
    public bool IsReassess => Mode == RiskEditorMode.Reassess;

    /// <summary>En una nueva evaluación los textos del riesgo se ven, pero no se editan.</summary>
    public bool CanEditTexts => !IsReassess;

    /// <summary>Se eligió un impacto o una probabilidad distintos de los vigentes.</summary>
    public bool IsReassessing => _original is not null &&
        (Dimensions[0].SelectedId != _currentImpact || Dimensions[1].SelectedId != _currentProbability);

    /// <summary>En la edición, un cambio de impacto o probabilidad corrige la evaluación vigente: se avisa.</summary>
    public bool IsCorrecting => IsEdit && IsReassessing;

    /// <summary>La fecha y el comentario se piden al dar de alta y en una nueva evaluación.</summary>
    public bool ShowAssessment => !IsEdit;

    public string AssessmentTitle => IsNew ? Strings.History_FirstAssessment : Strings.History_NewAssessment;

    public string AssessmentHint => IsNew ? Strings.History_FirstHint : Strings.History_NewHint;

    public string ModeTitle => Mode switch
    {
        RiskEditorMode.New => Strings.Mode_NewTitle,
        RiskEditorMode.Reassess => Strings.Mode_ReassessTitle,
        _ => Strings.Mode_EditTitle,
    };

    public string ModeHint => Mode switch
    {
        RiskEditorMode.New => Strings.Mode_NewHint,
        RiskEditorMode.Reassess => Strings.Mode_ReassessHint,
        _ => Strings.Mode_EditHint,
    };

    /// <summary>Fecha de la evaluación vigente, para el aviso de corrección.</summary>
    public string CorrectionHint
    {
        get
        {
            var current = _original is null ? null : _doc.FindRisk(_original.Id)?.Assessments.LastOrDefault();
            return string.Format(CultureInfo.CurrentCulture, Strings.Mode_CorrectionHint,
                current?.Date.ToString("d", CultureInfo.CurrentCulture) ?? "—");
        }
    }

    /// <summary>Desde la edición: lo elegido se registra como una nueva evaluación (con fecha) en lugar de corregir la vigente.</summary>
    [RelayCommand]
    private void SwitchToReassess() => Mode = RiskEditorMode.Reassess;

    public ObservableCollection<HistoryEntry> History { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<ChartPoint> _chartPoints = [];

    public IReadOnlyList<string> ChartLevels =>
        [.. (_doc.ProjectScale(ScaleType.Risk)?.OrderedLevels ?? []).Select(l => l.Name)];

    public bool HasHistory => History.Count > 0;

    private void LoadHistory()
    {
        History.Clear();
        var risk = _original is null ? null : _doc.FindRisk(_original.Id);
        if (risk is null)
        {
            ChartPoints = [];
            OnPropertyChanged(nameof(HasHistory));
            return;
        }

        var levels = _doc.ProjectScale(ScaleType.Risk)?.OrderedLevels ?? [];
        var steps = RiskHistory.Steps(_doc, risk);
        var points = new List<ChartPoint>();
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var s = steps[i];
            History.Add(new HistoryEntry(s.Assessment.Id, s.Assessment.Date, new LevelChip(s.Impact), new LevelChip(s.Probability),
                LevelChip.From(s.RiskLevel), s.Assessment.Note, i == steps.Count - 1, steps.Count > 1, ChangeAssessmentDate)
            {
                ImpactValue = RangeFormat.Value(s.Assessment.ImpactAmount),
                ProbabilityValue = RangeFormat.Value(s.Assessment.ProbabilityPercent, "%"),
            });
        }

        foreach (var s in steps)
        {
            var index = s.RiskLevel is null ? 0 : levels.ToList().FindIndex(l => l.Id == s.RiskLevel.Id);
            var chip = LevelChip.From(s.RiskLevel);
            points.Add(new ChartPoint(s.Assessment.Date, Math.Max(0, index), chip?.Background ?? Avalonia.Media.Brushes.Gray,
                $"{s.Assessment.Date.ToString("d", CultureInfo.CurrentCulture)}: {s.RiskLevel?.Name}"));
        }

        ChartPoints = points;
        OnPropertyChanged(nameof(HasHistory));
    }

    /// <summary>
    /// Quita una evaluación cargada por error. Se aplica en el momento (se puede deshacer desde el menú) y el riesgo
    /// vuelve al estado de la última evaluación que quede.
    /// </summary>
    [RelayCommand]
    private void RemoveAssessment(HistoryEntry entry)
    {
        if (_original is null)
        {
            return;
        }

        try
        {
            _doc.RemoveAssessment(_original.Id, entry.Id);
            Error = null;
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
            return;
        }

        SyncWithDocument();
    }

    /// <summary>
    /// Corrige la fecha de una evaluación registrada. Se aplica en el momento (se puede deshacer desde el menú); si la
    /// configuración no lo permite, se muestra el motivo y la fecha vuelve a la anterior.
    /// </summary>
    private void ChangeAssessmentDate(HistoryEntry entry, DateOnly date)
    {
        if (_original is null)
        {
            return;
        }

        try
        {
            _doc.ChangeAssessmentDate(_original.Id, entry.Id, date);
            Error = null;
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        // Se recarga después del cambio del selector (reordena el historial o devuelve la fecha anterior).
        Avalonia.Threading.Dispatcher.UIThread.Post(SyncWithDocument);
    }

    /// <summary>Vuelve a leer el historial y el estado actual del riesgo (puede cambiar al corregir o quitar evaluaciones).</summary>
    private void SyncWithDocument()
    {
        if (_original is not null && _doc.FindRisk(_original.Id) is { } risk &&
            (risk.ImpactLevelId != _currentImpact || risk.ProbabilityLevelId != _currentProbability))
        {
            var reassessing = IsReassessing;
            _currentImpact = risk.ImpactLevelId;
            _currentProbability = risk.ProbabilityLevelId;
            if (!reassessing)
            {
                foreach (var d in Dimensions)
                {
                    if (d.Options.FirstOrDefault(o => o.Chip.Id == risk.LevelIdFor(d.Type)) is { } option)
                    {
                        d.Select(option);
                    }

                    d.SetValueQuietly(risk.ValueFor(d.Type));
                }
            }
        }

        LoadHistory();
        OnPropertyChanged(nameof(IsReassessing));
        OnPropertyChanged(nameof(IsCorrecting));
        OnPropertyChanged(nameof(CorrectionHint));
        OnPropertyChanged(nameof(DateWarning));
    }

    /// <summary>Nivel de riesgo que resulta del impacto y la probabilidad elegidos (mapa de calor).</summary>
    [ObservableProperty]
    private LevelChip? _riskLevel;

    public string RiskLevelLabel => ScaleType.Risk.DisplayName();

    private void UpdateRiskLevel() =>
        RiskLevel = LevelChip.From(HeatMap.Resolve(_doc, Dimensions[0].SelectedId, Dimensions[1].SelectedId));

    public string WindowTitle => Mode switch
    {
        RiskEditorMode.New => Strings.Risk_New,
        RiskEditorMode.Reassess => string.Format(CultureInfo.CurrentCulture, Strings.Risk_ReassessTitle, _original?.Title),
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Risk_EditTitle, _original?.Title),
    };
    public ObservableCollection<DimensionEditor> Dimensions { get; }
    public int TitleMax => Limits.RiskTitleMax;
    public int ShortMax => Limits.RiskShortDescriptionMax;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleCounter))]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortCounter))]
    private string _shortDescription;

    [ObservableProperty]
    private string _longDescription;

    [ObservableProperty]
    private string? _error;

    public string TitleCounter => $"{Title.Length}/{Limits.RiskTitleMax}";
    /// <summary>Caracteres visibles (sin las marcas de formato), que es lo que cuenta el límite.</summary>
    public string ShortCounter => $"{Auros.Ram.Core.Text.RichText.ToPlain(ShortDescription).Length}/{Limits.RiskShortDescriptionMax}";

    /// <summary>Se dispara cuando el riesgo quedó guardado en el documento.</summary>
    public event EventHandler? Committed;

    [RelayCommand]
    private void Save()
    {
        var risk = _original?.Clone() ?? new Risk();
        risk.Title = Title;
        risk.ShortDescription = ShortDescription;
        risk.LongDescription = LongDescription;
        risk.ImpactLevelId = Dimensions[0].SelectedId;
        risk.ProbabilityLevelId = Dimensions[1].SelectedId;
        risk.ImpactJustification = Dimensions[0].Justification;
        risk.ProbabilityJustification = Dimensions[1].Justification;
        risk.ImpactAmount = Dimensions[0].Value;
        risk.ProbabilityPercent = Dimensions[1].Value;

        var date = DateOnly.FromDateTime(AssessmentDate ?? DateTime.Today);
        try
        {
            if (CanEditTexts)
            {
                (risk.CompanyId, risk.UnitId) = ResolveOrganization();
                (risk.ConsequenceIds, risk.MaterialityId) = ResolveCatalogs();
            }

            switch (Mode)
            {
                case RiskEditorMode.New:
                    _doc.AddRisk(risk, date, AssessmentNote);
                    break;
                case RiskEditorMode.Reassess:
                    // Los textos no se editan en una nueva evaluación, salvo que se haya pasado desde la edición con cambios.
                    if (_doc.FindRisk(risk.Id) is { } stored &&
                        (stored.Title != Title.Trim() || stored.ShortDescription != ShortDescription.Trim() || stored.LongDescription != LongDescription.Trim()))
                    {
                        var texts = stored.Clone();
                        (texts.Title, texts.ShortDescription, texts.LongDescription) = (Title, ShortDescription, LongDescription);
                        _doc.UpdateRisk(texts);
                    }

                    _doc.AddAssessment(risk.Id, risk.ImpactLevelId, risk.ProbabilityLevelId, date, AssessmentNote,
                        risk.ImpactJustification, risk.ProbabilityJustification, risk.ImpactAmount, risk.ProbabilityPercent);
                    break;
                default:
                    _doc.UpdateRisk(risk);
                    break;
            }

            Error = null;
            Committed?.Invoke(this, EventArgs.Empty);
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }
    }
}
