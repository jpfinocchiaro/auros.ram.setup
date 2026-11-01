using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Una evaluación (entrada) de un riesgo en la grilla jerárquica; se puede marcar para quitarla.</summary>
public sealed partial class EntryRow(Guid riskId, HistoryStep step, bool isCurrent, Action onCheck) : ObservableObject
{
    public Guid RiskId { get; } = riskId;
    public Guid Id { get; } = step.Assessment.Id;
    public string Date { get; } = step.Assessment.Date.ToString("d", CultureInfo.CurrentCulture);
    public LevelChip Impact { get; } = new(step.Impact);
    public LevelChip Probability { get; } = new(step.Probability);
    public LevelChip? RiskLevel { get; } = LevelChip.From(step.RiskLevel);
    public string ImpactValue { get; } = RangeFormat.Value(step.Assessment.ImpactAmount);
    public string ProbabilityValue { get; } = RangeFormat.Value(step.Assessment.ProbabilityPercent, "%");
    public string Note { get; } = step.Assessment.Note;

    /// <summary>La vigente (la más reciente): el estado actual del riesgo.</summary>
    public bool IsCurrent { get; } = isCurrent;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => onCheck();
}

/// <summary>Un riesgo en la grilla jerárquica: se despliega para ver sus evaluaciones y se puede marcar para eliminarlo.</summary>
public sealed partial class RiskRow(Risk risk, LevelChip? riskLevel, LevelChip? impactLevel, LevelChip? probabilityLevel) : ObservableObject
{
    public Risk Risk { get; } = risk;
    public LevelChip? RiskLevel { get; } = riskLevel;
    public LevelChip? ImpactLevel { get; } = impactLevel;
    public LevelChip? ProbabilityLevel { get; } = probabilityLevel;

    public string Title => Risk.Title;

    /// <summary>Sin marcas de formato: en la lista se muestra como texto simple de dos líneas.</summary>
    public string ShortDescription => Auros.Ram.Core.Text.RichText.ToPlain(Risk.ShortDescription);

    public string ImpactValue => RangeFormat.Value(Risk.ImpactAmount);
    public string ProbabilityValue => RangeFormat.Value(Risk.ProbabilityPercent, "%");

    /// <summary>Cantidad de evaluaciones y fecha de la última, para las columnas de la grilla.</summary>
    public int EntriesCount => Risk.Assessments.Count;

    public string LastDate => Risk.Assessments.Count > 0 ? Risk.Assessments[^1].Date.ToString("d", CultureInfo.CurrentCulture) : "";

    public ObservableCollection<EntryRow> Entries { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isChecked;

    public Action? CheckChanged { get; set; }

    partial void OnIsCheckedChanged(bool value) => CheckChanged?.Invoke();
}

/// <summary>Lista de riesgos con búsqueda, filtros y orden (RF-10, RF-12).</summary>
public sealed partial class RisksViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;
    private bool _loading;

    public RisksViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        SortOptions =
        [
            new(ScaleType.Risk.DisplayName(), RiskSortField.Risk),
            new(ScaleType.Impact.DisplayName(), RiskSortField.Impact),
            new(ScaleType.Probability.DisplayName(), RiskSortField.Probability),
            new(Strings.Risks_SortTitle, RiskSortField.Title),
            new(Strings.Risks_SortCreated, RiskSortField.Created),
        ];
        _sortBy = SortOptions[0];
        Reload();
    }

    public ObservableCollection<RiskRow> Items { get; } = [];
    public ObservableCollection<FilterOption> RiskFilters { get; } = [];
    public ObservableCollection<FilterOption> ImpactFilters { get; } = [];
    public ObservableCollection<FilterOption> ProbabilityFilters { get; } = [];
    public IReadOnlyList<Choice<RiskSortField>> SortOptions { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private FilterOption? _riskFilter;
    [ObservableProperty] private FilterOption? _impactFilter;
    [ObservableProperty] private FilterOption? _probabilityFilter;
    [ObservableProperty] private Choice<RiskSortField> _sortBy;
    [ObservableProperty] private bool _descending = true;
    [ObservableProperty] private RiskRow? _selected;
    [ObservableProperty] private string _countText = "";

    /// <summary>Riesgos y evaluaciones marcados para eliminar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChecked))]
    private string _checkedText = "";

    public bool HasChecked => CheckedRisks.Any() || CheckedEntries.Any();

    private IEnumerable<RiskRow> CheckedRisks => Items.Where(r => r.IsChecked);

    /// <summary>Evaluaciones marcadas de riesgos que no se eliminan enteros.</summary>
    private IEnumerable<EntryRow> CheckedEntries => Items.Where(r => !r.IsChecked).SelectMany(r => r.Entries).Where(e => e.IsChecked);

    public bool HasRisks => _owner.Document.Risks.Count > 0;

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnRiskFilterChanged(FilterOption? value) => Refresh();
    partial void OnImpactFilterChanged(FilterOption? value) => Refresh();
    partial void OnProbabilityFilterChanged(FilterOption? value) => Refresh();
    partial void OnSortByChanged(Choice<RiskSortField> value) => Refresh();
    partial void OnDescendingChanged(bool value) => Refresh();

    /// <summary>Reconstruye los filtros (las escalas pueden haber cambiado) y la lista.</summary>
    public void Reload()
    {
        _loading = true;
        var doc = _owner.Document;
        RiskFilter = Rebuild(RiskFilters, doc.ProjectScale(ScaleType.Risk), RiskFilter);
        ImpactFilter = Rebuild(ImpactFilters, doc.ProjectScale(ScaleType.Impact), ImpactFilter);
        ProbabilityFilter = Rebuild(ProbabilityFilters, doc.ProjectScale(ScaleType.Probability), ProbabilityFilter);
        _loading = false;
        Refresh();
        OnPropertyChanged(nameof(HasRisks));
    }

    public void Refresh()
    {
        if (_loading)
        {
            return;
        }

        var doc = _owner.Document;
        var selectedId = Selected?.Risk.Id;

        // Se conserva lo desplegado y lo marcado al volver a armar la lista.
        var expanded = Items.Where(r => r.IsExpanded).Select(r => r.Risk.Id).ToHashSet();
        var checkedRisks = CheckedRisks.Select(r => r.Risk.Id).ToHashSet();
        var checkedEntries = Items.SelectMany(r => r.Entries).Where(e => e.IsChecked).Select(e => e.Id).ToHashSet();
        var filter = new RiskFilter
        {
            Text = SearchText,
            RiskLevelId = RiskFilter?.LevelId,
            ImpactLevelId = ImpactFilter?.LevelId,
            ProbabilityLevelId = ProbabilityFilter?.LevelId,
            SortBy = SortBy.Value,
            Descending = Descending,
        };

        var levels = new Dictionary<Guid, LevelChip>();
        foreach (var type in Enum.GetValues<ScaleType>())
        {
            foreach (var l in doc.ProjectScale(type)?.Levels ?? [])
            {
                levels[l.Id] = new LevelChip(l);
            }
        }

        var rows = RiskQuery.Apply(doc, filter).Select(r => new RiskRow(
            r,
            doc.RiskLevelOf(r) is { } level ? levels.GetValueOrDefault(level.Id) : null,
            levels.GetValueOrDefault(r.ImpactLevelId),
            levels.GetValueOrDefault(r.ProbabilityLevelId))).ToList();

        Items.Clear();
        foreach (var row in rows)
        {
            var steps = RiskHistory.Steps(doc, row.Risk);
            for (var i = steps.Count - 1; i >= 0; i--)
            {
                row.Entries.Add(new EntryRow(row.Risk.Id, steps[i], i == steps.Count - 1, UpdateChecked) { IsChecked = checkedEntries.Contains(steps[i].Assessment.Id) });
            }

            row.IsExpanded = expanded.Contains(row.Risk.Id);
            row.IsChecked = checkedRisks.Contains(row.Risk.Id);
            row.CheckChanged = UpdateChecked;
            Items.Add(row);
        }

        UpdateChecked();

        Selected = Items.FirstOrDefault(i => i.Risk.Id == selectedId);
        CountText = string.Format(CultureInfo.CurrentCulture, Strings.Risks_Count, rows.Count, doc.Risks.Count);
        OnPropertyChanged(nameof(HasRisks));
    }

    [RelayCommand]
    private Task Add() => _owner.EditRiskAsync(null);

    [RelayCommand]
    private Task Edit(RiskRow? row) => (row ?? Selected) is { } r ? _owner.EditRiskAsync(r.Risk.Id) : Task.CompletedTask;

    /// <summary>Nueva evaluación del riesgo: su impacto y su probabilidad en una fecha (queda en el historial).</summary>
    [RelayCommand]
    private Task Reassess(RiskRow? row) => (row ?? Selected) is { } r ? _owner.ReassessRiskAsync(r.Risk.Id) : Task.CompletedTask;

    [RelayCommand]
    private async Task Delete(RiskRow? row)
    {
        if ((row ?? Selected) is not { } r)
        {
            return;
        }

        // Se avisa que con el riesgo se borran todas sus evaluaciones.
        var ok = await _owner.Ui.ConfirmAsync(Strings.Risks_DeleteTitle,
            string.Format(CultureInfo.CurrentCulture, Strings.Risks_DeleteMessage, r.Title, r.Risk.Assessments.Count), Strings.Common_Delete);
        if (ok)
        {
            _owner.Run(() => _owner.Document.RemoveRisk(r.Risk.Id));
        }
    }

    /// <summary>
    /// Elimina los riesgos marcados (con todas sus evaluaciones) y quita las evaluaciones marcadas de los demás,
    /// después de confirmar. A un riesgo no se le pueden quitar todas sus evaluaciones: para eso se elimina.
    /// </summary>
    [RelayCommand]
    private async Task DeleteChecked()
    {
        var risks = CheckedRisks.ToList();
        var entries = CheckedEntries.ToList();
        if (risks.Count == 0 && entries.Count == 0)
        {
            return;
        }

        var parts = new List<string>();
        if (risks.Count > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Risks_DeleteCheckedRisks,
                risks.Count, risks.Sum(r => r.Risk.Assessments.Count)));
        }

        if (entries.Count > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Risks_DeleteCheckedEntries, entries.Count));
        }

        var ok = await _owner.Ui.ConfirmAsync(Strings.Risks_DeleteCheckedTitle,
            string.Join("\n", parts) + "\n\n" + Strings.Risks_DeleteCheckedUndo, Strings.Common_Delete);
        if (!ok)
        {
            return;
        }

        _owner.Run(() =>
        {
            if (entries.Count > 0)
            {
                _owner.Document.RemoveAssessments([.. entries.Select(e => (e.RiskId, e.Id))]);
            }

            if (risks.Count > 0)
            {
                _owner.Document.RemoveRisks([.. risks.Select(r => r.Risk.Id)]);
            }
        });
    }

    [RelayCommand]
    private void ExpandAll(bool expand)
    {
        foreach (var row in Items)
        {
            row.IsExpanded = expand;
        }
    }

    /// <summary>Marca o desmarca todos los riesgos que se ven (con el filtro aplicado).</summary>
    [RelayCommand]
    private void CheckAll(bool check)
    {
        foreach (var row in Items)
        {
            row.IsChecked = check;
            foreach (var entry in row.Entries.Where(e => !check))
            {
                entry.IsChecked = false;
            }
        }
    }

    private void UpdateChecked()
    {
        var risks = CheckedRisks.Count();
        var entries = CheckedEntries.Count();
        CheckedText = risks + entries == 0 ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Risks_CheckedText, risks, entries);
        OnPropertyChanged(nameof(HasChecked));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _loading = true;
        SearchText = "";
        RiskFilter = RiskFilters.FirstOrDefault();
        ImpactFilter = ImpactFilters.FirstOrDefault();
        ProbabilityFilter = ProbabilityFilters.FirstOrDefault();
        _loading = false;
        Refresh();
    }

    private static FilterOption? Rebuild(ObservableCollection<FilterOption> target, Scale? scale, FilterOption? current)
    {
        target.Clear();
        target.Add(new FilterOption(Strings.Common_All, null));
        foreach (var l in scale?.OrderedLevels.Reverse() ?? [])
        {
            target.Add(new FilterOption(l.Name, l.Id));
        }

        return target.FirstOrDefault(o => o.LevelId == current?.LevelId) ?? target[0];
    }
}
