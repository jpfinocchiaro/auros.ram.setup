using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media;
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

/// <summary>Un renglón de la evolución: estado al inicio y al final del período, tendencia y cambios.</summary>
public sealed record EvolutionItem(
    string Title,
    LevelChip? StartLevel,
    LevelChip? StartImpact,
    LevelChip? StartProbability,
    LevelChip? EndLevel,
    LevelChip EndImpact,
    LevelChip EndProbability,
    RiskTrend Trend,
    string Changes)
{
    public bool IsNew => Trend == RiskTrend.New;
    public bool HasStart => !IsNew;
    public string TrendText => $"{TrendSymbol} {Trend.DisplayName()}";

    public string TrendSymbol => Trend switch
    {
        RiskTrend.Worse => "▲",
        RiskTrend.Better => "▼",
        RiskTrend.New => "●",
        _ => "=",
    };

    public IBrush TrendBrush => Trend switch
    {
        RiskTrend.Worse => new SolidColorBrush(Color.Parse("#C02020")),
        RiskTrend.Better => new SolidColorBrush(Color.Parse("#188038")),
        RiskTrend.New => new SolidColorBrush(Color.Parse("#2F72E0")),
        _ => Avalonia.Media.Brushes.Gray,
    };
}

/// <summary>Ficha resumen (RF-16), ficha detallada (RF-17) y evolución en un período, generadas en el momento (RF-19).</summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;

    public ReportsViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    public ObservableCollection<ReportItem> Items { get; } = [];
    public ObservableCollection<EvolutionItem> Evolution { get; } = [];

    /// <summary>Comienzo y fin del período de la evolución (por omisión: desde la primera evaluación hasta hoy).</summary>
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private string _evolutionSummary = "";

    partial void OnFromDateChanged(DateTime? value) => ReloadEvolution();

    partial void OnToDateChanged(DateTime? value) => ReloadEvolution();

    /// <summary>El período elegido (lo que falte, del período por omisión).</summary>
    public (DateOnly From, DateOnly To) Period
    {
        get
        {
            var (from, to) = RiskHistory.DefaultPeriod(_owner.Document);
            return (FromDate is { } f ? DateOnly.FromDateTime(f) : from, ToDate is { } t ? DateOnly.FromDateTime(t) : to);
        }
    }

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
        ReloadEvolution();
    }

    private void ReloadEvolution()
    {
        var doc = _owner.Document;
        var (from, to) = Period;
        Evolution.Clear();
        foreach (var x in RiskHistory.Evolution(doc, from, to))
        {
            var changes = string.Join("\n", x.Changes.Select(c =>
                $"{c.Assessment.Date.ToString("d", CultureInfo.CurrentCulture)}: " +
                string.Format(CultureInfo.CurrentCulture, Strings.Evolution_Change, c.Impact.Name, c.Probability.Name, c.RiskLevel?.Name ?? "—") +
                (string.IsNullOrWhiteSpace(c.Assessment.Note) ? "" : $" — {c.Assessment.Note}")));
            Evolution.Add(new EvolutionItem(
                x.Risk.Title,
                LevelChip.From(x.From?.RiskLevel), LevelChip.From(x.From?.Impact), LevelChip.From(x.From?.Probability),
                LevelChip.From(x.To.RiskLevel), new LevelChip(x.To.Impact), new LevelChip(x.To.Probability),
                x.Trend,
                changes.Length == 0 ? Strings.Evolution_NoChanges : changes));
        }

        int Count(RiskTrend t) => Evolution.Count(e => e.Trend == t);
        EvolutionSummary = string.Format(CultureInfo.CurrentCulture, Strings.Evolution_Summary,
            Count(RiskTrend.Worse), Count(RiskTrend.Better), Count(RiskTrend.New), Count(RiskTrend.Same));
    }

    private static DetailDimension Dim(ScaleType type, Level level, string justification) =>
        new(type.DisplayName(), new LevelChip(level),
            string.IsNullOrWhiteSpace(justification) ? Resources.Strings.Reports_NoJustification : justification,
            !string.IsNullOrWhiteSpace(justification));
}
