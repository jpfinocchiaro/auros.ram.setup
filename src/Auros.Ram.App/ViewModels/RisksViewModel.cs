using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public sealed record RiskRow(Risk Risk, LevelChip? RiskLevel, LevelChip? ImpactLevel, LevelChip? ProbabilityLevel)
{
    public string Title => Risk.Title;
    /// <summary>Sin marcas de formato: en la lista se muestra como texto simple de dos líneas.</summary>
    public string ShortDescription => Auros.Ram.Core.Text.RichText.ToPlain(Risk.ShortDescription);
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
            Items.Add(row);
        }

        Selected = Items.FirstOrDefault(i => i.Risk.Id == selectedId);
        CountText = string.Format(CultureInfo.CurrentCulture, Strings.Risks_Count, rows.Count, doc.Risks.Count);
        OnPropertyChanged(nameof(HasRisks));
    }

    [RelayCommand]
    private Task Add() => _owner.EditRiskAsync(null);

    [RelayCommand]
    private Task Edit(RiskRow? row) => (row ?? Selected) is { } r ? _owner.EditRiskAsync(r.Risk.Id) : Task.CompletedTask;

    [RelayCommand]
    private async Task Delete(RiskRow? row)
    {
        if ((row ?? Selected) is not { } r)
        {
            return;
        }

        var ok = await _owner.Ui.ConfirmAsync(Strings.Risks_DeleteTitle,
            string.Format(CultureInfo.CurrentCulture, Strings.Risks_DeleteMessage, r.Title), Strings.Common_Delete);
        if (ok)
        {
            _owner.Run(() => _owner.Document.RemoveRisk(r.Risk.Id));
        }
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
