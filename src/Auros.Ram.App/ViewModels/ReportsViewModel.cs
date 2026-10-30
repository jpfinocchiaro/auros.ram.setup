using System.Collections.ObjectModel;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Auros.Ram.App.ViewModels;

public sealed record DetailDimension(string Label, LevelChip Level, string Justification, bool HasJustification);

public sealed record ReportItem(
    int Number,
    Guid RiskId,
    string Title,
    string ShortDescription,
    string LongDescription,
    LevelChip RiskLevel,
    LevelChip ImpactLevel,
    LevelChip ProbabilityLevel,
    IReadOnlyList<DetailDimension> Dimensions)
{
    public string Heading => $"{Number}. {Title}";
}

/// <summary>Ficha resumen (RF-16) y ficha detallada (RF-17), generadas en el momento (RF-19).</summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;

    public ReportsViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    public ObservableCollection<ReportItem> Items { get; } = [];

    [ObservableProperty] private ReportItem? _selected;
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _companyName = "";

    public void Reload()
    {
        var doc = _owner.Document;
        var selectedId = Selected?.RiskId;
        Items.Clear();
        foreach (var row in Reports.Summary(doc))
        {
            var r = row.Risk;
            Items.Add(new ReportItem(
                row.Number, r.Id, r.Title, r.ShortDescription, r.LongDescription,
                new LevelChip(row.RiskLevel), new LevelChip(row.ImpactLevel), new LevelChip(row.ProbabilityLevel),
                [
                    new DetailDimension(ScaleType.Risk.DisplayName(), new LevelChip(row.RiskLevel), Resources.Strings.Reports_FromHeatMap, false),
                    Dim(ScaleType.Impact, row.ImpactLevel, r.ImpactJustification),
                    Dim(ScaleType.Probability, row.ProbabilityLevel, r.ProbabilityJustification),
                ]));
        }

        Selected = Items.FirstOrDefault(i => i.RiskId == selectedId) ?? Items.FirstOrDefault();
        ProjectName = doc.Project.Name;
        CompanyName = doc.Project.Company;
    }

    private static DetailDimension Dim(ScaleType type, Level level, string justification) =>
        new(type.DisplayName(), new LevelChip(level),
            string.IsNullOrWhiteSpace(justification) ? Resources.Strings.Reports_NoJustification : justification,
            !string.IsNullOrWhiteSpace(justification));
}
