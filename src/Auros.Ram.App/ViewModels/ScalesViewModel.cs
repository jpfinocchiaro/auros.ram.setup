using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public sealed record ScaleItem(Scale Scale, IReadOnlyList<LevelChip> Levels, bool IsAssigned)
{
    public string Name => Scale.Name;
    public string LevelCount => string.Format(CultureInfo.CurrentCulture, Strings.Scales_LevelCount, Scale.Levels.Count);
}

public sealed record ScaleGroup(ScaleType Type, string Title, string Purpose, IReadOnlyList<ScaleItem> Scales);

/// <summary>Un nivel de riesgo que se puede asignar a una celda del mapa de calor.</summary>
public sealed partial class HeatOption(HeatCellItem cell, LevelChip level) : ObservableObject
{
    public LevelChip Level { get; } = level;
    public string Name => Level.Name;
    public bool IsCurrent => cell.Level?.Id == Level.Id;

    [RelayCommand]
    private void Choose() => cell.Choose(Level.Id);
}

/// <summary>Celda del mapa de calor: impacto × probabilidad y el nivel de riesgo que le corresponde.</summary>
public sealed class HeatCellItem
{
    private readonly Action<HeatCellItem, Guid> _choose;

    public HeatCellItem(Guid impactId, Guid probabilityId, LevelChip? level, IEnumerable<Level> riskLevels, string tooltip, Action<HeatCellItem, Guid> choose)
    {
        ImpactId = impactId;
        ProbabilityId = probabilityId;
        Level = level;
        Tooltip = tooltip;
        _choose = choose;
        Options = [.. riskLevels.Select(l => new HeatOption(this, new LevelChip(l)))];
    }

    public Guid ImpactId { get; }
    public Guid ProbabilityId { get; }
    public LevelChip? Level { get; }
    public string Tooltip { get; }
    public IReadOnlyList<HeatOption> Options { get; }

    public void Choose(Guid riskLevelId) => _choose(this, riskLevelId);
}

public sealed record HeatRow(LevelChip Impact, IReadOnlyList<HeatCellItem> Cells);

/// <summary>Configuración → Escalas (RF-01 a RF-06), agrupadas por tipo.</summary>
public sealed partial class ScalesViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;

    public ScalesViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    public ObservableCollection<ScaleGroup> Groups { get; } = [];

    /// <summary>Mapa de calor: filas de impacto (el mayor arriba) y columnas de probabilidad (de menor a mayor).</summary>
    public ObservableCollection<HeatRow> HeatRows { get; } = [];
    public ObservableCollection<LevelChip> HeatColumns { get; } = [];

    [ObservableProperty] private bool _hasHeatMap;
    [ObservableProperty] private bool _isHeatMapCustom;

    public void Reload()
    {
        var doc = _owner.Document;
        ReloadHeatMap();
        Groups.Clear();
        foreach (var type in new[] { ScaleType.Impact, ScaleType.Probability, ScaleType.Risk })
        {
            var purpose = type switch
            {
                ScaleType.Risk => Strings.Scales_PurposeRisk,
                ScaleType.Impact => Strings.Scales_PurposeImpact,
                _ => Strings.Scales_PurposeProbability,
            };
            Groups.Add(new ScaleGroup(type, type.DisplayName(), purpose, [.. doc.Scales
                .Where(s => s.Type == type)
                .Select(s => new ScaleItem(s, [.. s.OrderedLevels.Select(l => new LevelChip(l, RangeFormat.UnitFor(s.Type)))], doc.IsScaleAssigned(s.Id)))]));
        }
    }

    [RelayCommand]
    private Task Add(ScaleGroup group) => _owner.EditScaleAsync(null, group.Type);

    [RelayCommand]
    private Task Edit(ScaleItem item) => _owner.EditScaleAsync(item.Scale, item.Scale.Type);

    [RelayCommand]
    private async Task Delete(ScaleItem item)
    {
        if (item.IsAssigned)
        {
            await _owner.Ui.AlertAsync(Strings.Scales_DeleteTitle,
                string.Format(CultureInfo.CurrentCulture, Strings.Scales_DeleteAssigned, item.Name));
            return;
        }

        var ok = await _owner.Ui.ConfirmAsync(Strings.Scales_DeleteTitle,
            string.Format(CultureInfo.CurrentCulture, Strings.Scales_DeleteMessage, item.Name), Strings.Common_Delete);
        if (ok)
        {
            _owner.Run(() => _owner.Document.RemoveScale(item.Scale.Id));
        }
    }

    [RelayCommand]
    private Task SaveTemplate() => _owner.SaveTemplateAsync();

    [RelayCommand]
    private void ResetHeatMap() => _owner.Run(() => _owner.Document.ResetHeatMap());

    private void ReloadHeatMap()
    {
        var doc = _owner.Document;
        var m = Auros.Ram.Core.Services.PriorityMatrix.Build(doc);
        var riskLevels = doc.ProjectScale(ScaleType.Risk)?.OrderedLevels ?? [];
        HeatColumns.Clear();
        foreach (var c in m.Columns)
        {
            HeatColumns.Add(new LevelChip(c));
        }

        HeatRows.Clear();
        for (var r = 0; r < m.Rows.Count; r++)
        {
            var impact = m.Rows[r];
            var cells = new List<HeatCellItem>();
            for (var c = 0; c < m.Columns.Count; c++)
            {
                var probability = m.Columns[c];
                var tip = string.Format(CultureInfo.CurrentCulture, Strings.HeatMap_CellTip, impact.Name, probability.Name);
                cells.Add(new HeatCellItem(impact.Id, probability.Id, LevelChip.From(m.LevelAt(r, c)), riskLevels, tip,
                    (cell, level) => _owner.Run(() => _owner.Document.SetHeatCell(cell.ImpactId, cell.ProbabilityId, level))));
            }

            HeatRows.Add(new HeatRow(new LevelChip(impact), cells));
        }

        HasHeatMap = HeatRows.Count > 0 && HeatColumns.Count > 0 && riskLevels.Count > 0;
        IsHeatMapCustom = doc.Project.HeatMap.Count > 0;
    }
}
