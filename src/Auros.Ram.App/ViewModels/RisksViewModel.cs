using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Una fila de la grilla jerárquica: un riesgo (padre) o una de sus evaluaciones (hija).</summary>
public abstract class GridRow : ObservableObject
{
    public abstract RiskRow Owner { get; }
}

/// <summary>Una evaluación de un riesgo: fila hija en la grilla.</summary>
/// <summary>Resumen de las acciones mitigantes para la grilla: cantidad y abiertas, y la lista como ayuda emergente.</summary>
public static class ActionSummary
{
    public static string Text(IReadOnlyList<MitigationAction> actions)
    {
        var open = actions.Count(a => a.IsOpen);
        return actions.Count == 0 ? ""
            : open == 0 ? string.Format(CultureInfo.CurrentCulture, Strings.Actions_GridDone, actions.Count)
            : string.Format(CultureInfo.CurrentCulture, Strings.Actions_GridOpen, actions.Count, open);
    }

    public static string? Tip(IReadOnlyList<MitigationAction> actions) => actions.Count == 0 ? null
        : string.Join("\n", actions.Select(a => $"• {a.Description} — {a.Status.DisplayName()}" +
            (a.Responsible.Length > 0 ? $" ({a.Responsible})" : "") +
            (a.DueDate is { } d ? $", {d.ToString("d", CultureInfo.CurrentCulture)}" : "")));
}

public sealed class EntryRow(RiskRow parent, HistoryStep step, bool isCurrent) : GridRow
{
    public override RiskRow Owner { get; } = parent;
    public Guid RiskId => Owner.Risk.Id;
    public Guid Id { get; } = step.Assessment.Id;
    public string Date { get; } = step.Assessment.Date.ToString("d", CultureInfo.CurrentCulture);
    public LevelChip Impact { get; } = new(step.Impact);
    public LevelChip Probability { get; } = new(step.Probability);
    public LevelChip? RiskLevel { get; } = LevelChip.From(step.RiskLevel);
    public string ImpactValue { get; } = RangeFormat.Value(step.Assessment.ImpactAmount);
    public string ProbabilityValue { get; } = RangeFormat.Value(step.Assessment.ProbabilityPercent, "%");
    public string Note { get; } = step.Assessment.Note;
    public string Actions { get; } = ActionSummary.Text(step.Assessment.Actions);
    public string? ActionsTip { get; } = ActionSummary.Tip(step.Assessment.Actions);

    /// <summary>La vigente (la más reciente): el estado actual del riesgo.</summary>
    public bool IsCurrent { get; } = isCurrent;
}

/// <summary>Un riesgo: fila padre en la grilla; al desplegarlo aparecen sus evaluaciones como filas hijas.</summary>
public sealed partial class RiskRow(Risk risk, LevelChip? riskLevel, LevelChip? impactLevel, LevelChip? probabilityLevel) : GridRow
{
    public override RiskRow Owner => this;
    public Risk Risk { get; } = risk;
    public LevelChip? RiskLevel { get; } = riskLevel;
    public LevelChip? ImpactLevel { get; } = impactLevel;
    public LevelChip? ProbabilityLevel { get; } = probabilityLevel;

    /// <summary>Acciones mitigantes de la evaluación vigente.</summary>
    public string Actions => ActionSummary.Text(Risk.CurrentActions);
    public string? ActionsTip => ActionSummary.Tip(Risk.CurrentActions);

    public string Title => Risk.Title;

    /// <summary>"Empresa · Unidad de negocio" del riesgo.</summary>
    public string Organization { get; init; } = "";

    /// <summary>Sin marcas de formato: se muestra como ayuda emergente de la fila.</summary>
    public string ShortDescription => Auros.Ram.Core.Text.RichText.ToPlain(Risk.ShortDescription);

    public string ImpactValue => RangeFormat.Value(Risk.ImpactAmount);
    public string ProbabilityValue => RangeFormat.Value(Risk.ProbabilityPercent, "%");

    /// <summary>Cantidad de evaluaciones y fecha de la última, para las columnas de la grilla.</summary>
    public int EntriesCount => Risk.Assessments.Count;

    public string LastDate => Risk.Assessments.Count > 0 ? Risk.Assessments[^1].Date.ToString("d", CultureInfo.CurrentCulture) : "";

    /// <summary>Evaluaciones del riesgo, de la más reciente a la más antigua.</summary>
    public List<EntryRow> Entries { get; } = [];

    /// <summary>Lo cambia la grilla (la flecha): agrega o quita las filas hijas.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    public Action<RiskRow>? ExpandedChanged { get; set; }

    partial void OnIsExpandedChanged(bool value) => ExpandedChanged?.Invoke(this);
}

/// <summary>
/// Grilla jerárquica de riesgos (RF-10, RF-12): cada riesgo con sus evaluaciones como filas hijas, búsqueda, filtros
/// y orden. Se selecciona la línea completa (Ctrl y Mayús para varias); al seleccionar un riesgo quedan
/// seleccionadas también todas sus evaluaciones.
/// </summary>
public sealed partial class RisksViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;
    private bool _loading;
    private bool _syncing;

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
        Selection = new SelectionModel<GridRow>(Rows) { SingleSelect = false };
        Selection.SelectionChanged += OnSelectionChanged;
        Reload();
    }

    /// <summary>Los riesgos que se ven (con el filtro aplicado).</summary>
    public ObservableCollection<RiskRow> Items { get; } = [];

    /// <summary>Las filas de la grilla: cada riesgo seguido de sus evaluaciones si está desplegado.</summary>
    public ObservableCollection<GridRow> Rows { get; } = [];

    /// <summary>Selección de la grilla (varias filas).</summary>
    public SelectionModel<GridRow> Selection { get; }

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

    /// <summary>El riesgo de la fila seleccionada (para Editar y Nueva evaluación).</summary>
    [ObservableProperty] private RiskRow? _selected;
    [ObservableProperty] private string _countText = "";

    /// <summary>Resumen de lo seleccionado ("2 riesgos y 1 evaluación").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string _selectionText = "";

    public bool HasSelection => Selection.SelectedItems.Count > 0;

    /// <summary>Riesgos seleccionados (se eliminan enteros, con todas sus evaluaciones).</summary>
    public IReadOnlyList<RiskRow> SelectedRisks => [.. Selection.SelectedItems.OfType<RiskRow>()];

    /// <summary>Evaluaciones seleccionadas de riesgos que no están seleccionados enteros.</summary>
    public IReadOnlyList<EntryRow> SelectedEntries =>
        [.. Selection.SelectedItems.OfType<EntryRow>().Where(e => !Selection.SelectedItems.Contains(e.Owner))];

    public bool HasRisks => _owner.View.Risks.Count > 0;

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
        var doc = _owner.View;
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

        var doc = _owner.View;

        // Se conserva lo desplegado y lo seleccionado al volver a armar la grilla.
        var expanded = Items.Where(r => r.IsExpanded).Select(r => r.Risk.Id).ToHashSet();
        var selectedRisks = Selection.SelectedItems.OfType<RiskRow>().Select(r => r.Risk.Id).ToHashSet();
        var selectedEntries = Selection.SelectedItems.OfType<EntryRow>().Select(e => e.Id).ToHashSet();
        var primary = Selected?.Risk.Id;

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
                levels[l.Id] = new LevelChip(l, RangeFormat.UnitFor(type));
            }
        }

        _syncing = true;
        try
        {
            Selection.Clear();
            Items.Clear();
            Rows.Clear();
            foreach (var r in RiskQuery.Apply(doc, filter))
            {
                var row = new RiskRow(
                    r,
                    doc.RiskLevelOf(r) is { } level ? levels.GetValueOrDefault(level.Id) : null,
                    levels.GetValueOrDefault(r.ImpactLevelId),
                    levels.GetValueOrDefault(r.ProbabilityLevelId))
                {
                    Organization = doc.OrganizationName(r),
                };
                var steps = RiskHistory.Steps(doc, r);
                for (var i = steps.Count - 1; i >= 0; i--)
                {
                    row.Entries.Add(new EntryRow(row, steps[i], i == steps.Count - 1));
                }

                row.IsExpanded = expanded.Contains(r.Id);
                row.ExpandedChanged = OnExpandedChanged;
                Items.Add(row);
                Rows.Add(row);
                if (row.IsExpanded)
                {
                    foreach (var entry in row.Entries)
                    {
                        Rows.Add(entry);
                    }
                }
            }

            for (var i = 0; i < Rows.Count; i++)
            {
                if (Rows[i] is RiskRow rr ? selectedRisks.Contains(rr.Risk.Id) : selectedEntries.Contains(((EntryRow)Rows[i]).Id))
                {
                    Selection.Select(i);
                }
            }
        }
        finally
        {
            _syncing = false;
        }

        Selected = Items.FirstOrDefault(i => i.Risk.Id == primary);
        CountText = string.Format(CultureInfo.CurrentCulture, Strings.Risks_Count, Items.Count, doc.Risks.Count);
        UpdateSelectionText();
        OnPropertyChanged(nameof(HasRisks));
    }

    /// <summary>
    /// Al seleccionar un riesgo se seleccionan sus evaluaciones; al quitarlo, también. Si se quita una evaluación de un
    /// riesgo seleccionado, el riesgo deja de estar seleccionado entero (las demás evaluaciones siguen).
    /// </summary>
    private void OnSelectionChanged(object? sender, SelectionModelSelectionChangedEventArgs<GridRow> e)
    {
        if (_syncing)
        {
            return;
        }

        // Las listas del evento son vistas de la selección: se copian antes de cambiarla.
        var selectedRisks = e.SelectedItems.OfType<RiskRow>().ToList();
        var deselectedRisks = e.DeselectedItems.OfType<RiskRow>().ToList();
        var deselectedEntries = e.DeselectedItems.OfType<EntryRow>().ToList();
        _syncing = true;
        try
        {
            foreach (var entry in selectedRisks.SelectMany(r => r.Entries))
            {
                SelectRow(entry, true);
            }

            foreach (var entry in deselectedRisks.SelectMany(r => r.Entries))
            {
                SelectRow(entry, false);
            }

            foreach (var entry in deselectedEntries)
            {
                SelectRow(entry.Owner, false);
            }
        }
        finally
        {
            _syncing = false;
        }

        Selected = Selection.SelectedItem?.Owner;
        UpdateSelectionText();
    }

    /// <summary>Al desplegar un riesgo se agregan sus evaluaciones debajo (seleccionadas si el riesgo lo está); al plegarlo, se quitan.</summary>
    private void OnExpandedChanged(RiskRow row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        _syncing = true;
        try
        {
            if (row.IsExpanded)
            {
                var selected = Selection.IsSelected(index);
                for (var i = 0; i < row.Entries.Count; i++)
                {
                    Rows.Insert(index + 1 + i, row.Entries[i]);
                    if (selected)
                    {
                        Selection.Select(index + 1 + i);
                    }
                }
            }
            else
            {
                foreach (var entry in row.Entries)
                {
                    Rows.Remove(entry);
                }
            }
        }
        finally
        {
            _syncing = false;
        }

        UpdateSelectionText();
    }

    private void SelectRow(GridRow row, bool select)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        if (select)
        {
            Selection.Select(index);
        }
        else
        {
            Selection.Deselect(index);
        }
    }

    private void UpdateSelectionText()
    {
        var risks = SelectedRisks.Count;
        var entries = SelectedEntries.Count;
        SelectionText = risks + entries == 0 ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Risks_SelectionText, risks, entries);
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private Task Add() => _owner.EditRiskAsync(null);

    [RelayCommand]
    private Task Edit(RiskRow? row) => (row ?? Selected) is { } r ? _owner.EditRiskAsync(r.Risk.Id) : Task.CompletedTask;

    /// <summary>Nueva evaluación del riesgo: su impacto y su probabilidad en una fecha (queda en el historial).</summary>
    [RelayCommand]
    private Task Reassess(RiskRow? row) => (row ?? Selected) is { } r ? _owner.ReassessRiskAsync(r.Risk.Id) : Task.CompletedTask;

    /// <summary>
    /// Elimina lo seleccionado después de confirmar: los riesgos seleccionados con todas sus evaluaciones, y las
    /// evaluaciones seleccionadas de los demás. A un riesgo no se le pueden quitar todas sus evaluaciones.
    /// </summary>
    [RelayCommand]
    private async Task Delete()
    {
        var risks = SelectedRisks;
        var entries = SelectedEntries;
        if (risks.Count == 0 && entries.Count == 0)
        {
            return;
        }

        var parts = new List<string>();
        if (risks.Count == 1)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Risks_DeleteMessage, risks[0].Title, risks[0].EntriesCount));
        }
        else if (risks.Count > 1)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Risks_DeleteCheckedRisks, risks.Count, risks.Sum(r => r.EntriesCount)));
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

    /// <summary>
    /// Clonar lo seleccionado: un riesgo abre "Nuevo riesgo" con todo copiado; una evaluación abre "Nueva evaluación"
    /// del mismo riesgo a partir de ella.
    /// </summary>
    [RelayCommand]
    private Task Clone(GridRow? row) => (row ?? Selection.SelectedItem) switch
    {
        EntryRow entry => _owner.CloneAssessmentAsync(entry.RiskId, entry.Id),
        RiskRow risk => _owner.CloneRiskAsync(risk.Risk.Id),
        _ => Task.CompletedTask,
    };

    [RelayCommand]
    private void ExpandAll(bool expand)
    {
        foreach (var row in Items)
        {
            row.IsExpanded = expand;
        }
    }

    [RelayCommand]
    private void SelectAll() => Selection.SelectAll();

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
