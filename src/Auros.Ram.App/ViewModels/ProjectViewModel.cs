using System.Collections.ObjectModel;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public sealed record ScalePreview(string TypeName, string ScaleName, IReadOnlyList<LevelChip> Levels);

public sealed record DistributionRow(LevelChip Level, int Count, string Percent, double BarWidth);

/// <summary>Ficha del proyecto (RF-07 a RF-09) y panel de distribución (RF-18).</summary>
public sealed partial class ProjectViewModel : ObservableObject
{
    private const int MaxLogoBytes = 2 * 1024 * 1024;
    private readonly DocumentViewModel _owner;
    private bool _loading;

    public ProjectViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    public IReadOnlyList<Choice<ProjectStatus>> Statuses { get; } =
        [.. Enum.GetValues<ProjectStatus>().Select(s => new Choice<ProjectStatus>(s.DisplayName(), s))];

    public ObservableCollection<Scale> RiskScales { get; } = [];
    public ObservableCollection<Scale> ImpactScales { get; } = [];
    public ObservableCollection<Scale> ProbabilityScales { get; } = [];
    public ObservableCollection<ScalePreview> AssignedScales { get; } = [];
    public ObservableCollection<DistributionRow> Distribution { get; } = [];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private Choice<ProjectStatus>? _status;
    [ObservableProperty] private Scale? _riskScale;
    [ObservableProperty] private Scale? _impactScale;
    [ObservableProperty] private Scale? _probabilityScale;
    [ObservableProperty] private byte[]? _companyLogo;
    [ObservableProperty] private Bitmap? _companyLogoImage;
    [ObservableProperty] private bool _canChangeScales;
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private int _riskCount;

    public string ScalesHint => CanChangeScales ? Strings.Project_ScalesHint : Strings.Project_ScalesLocked;

    partial void OnCanChangeScalesChanged(bool value) => OnPropertyChanged(nameof(ScalesHint));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is nameof(Name) or nameof(Company) or nameof(Date)
            or nameof(Status) or nameof(RiskScale) or nameof(ImpactScale) or nameof(ProbabilityScale)
            or nameof(CompanyLogo))
        {
            HasChanges = true;
        }
    }

    partial void OnCompanyLogoChanged(byte[]? value) => CompanyLogoImage = Brushes.Image(value);

    /// <summary>Vuelve a leer la ficha desde el documento (descarta lo no aplicado).</summary>
    public void Reload()
    {
        _loading = true;
        var doc = _owner.Document;
        var p = doc.Project;
        Name = p.Name;
        Company = p.Company;
        Date = p.Date.ToDateTime(TimeOnly.MinValue);
        Status = Statuses.First(s => s.Value == p.Status);
        Fill(RiskScales, doc.Scales.Where(s => s.Type == ScaleType.Risk));
        Fill(ImpactScales, doc.Scales.Where(s => s.Type == ScaleType.Impact));
        Fill(ProbabilityScales, doc.Scales.Where(s => s.Type == ScaleType.Probability));
        RiskScale = RiskScales.FirstOrDefault(s => s.Id == p.RiskScaleId);
        ImpactScale = ImpactScales.FirstOrDefault(s => s.Id == p.ImpactScaleId);
        ProbabilityScale = ProbabilityScales.FirstOrDefault(s => s.Id == p.ProbabilityScaleId);
        CompanyLogo = p.CompanyLogo;
        CanChangeScales = doc.CanChangeProjectScales;
        RiskCount = doc.Risks.Count;

        Fill(AssignedScales, new[] { ScaleType.Impact, ScaleType.Probability, ScaleType.Risk }.Select(t =>
        {
            var s = doc.ProjectScale(t);
            return new ScalePreview(t.DisplayName(), s?.Name ?? "—", [.. (s?.OrderedLevels ?? []).Select(l => new LevelChip(l))]);
        }));

        var dist = Reports.Distribution(doc);
        var max = Math.Max(1, dist.Count == 0 ? 1 : dist.Max(d => d.Count));
        Fill(Distribution, dist.Select(d => new DistributionRow(new LevelChip(d.Level), d.Count, d.Share.ToString("P0", System.Globalization.CultureInfo.CurrentCulture), 140.0 * d.Count / max)));

        Error = null;
        HasChanges = false;
        _loading = false;
    }

    [RelayCommand]
    private void Apply()
    {
        var p = _owner.Document.Project.Clone();
        p.Name = Name.Trim();
        p.Company = Company.Trim();
        p.Date = Date is { } d ? DateOnly.FromDateTime(d.Date) : p.Date;
        p.Status = Status?.Value ?? p.Status;
        p.RiskScaleId = RiskScale?.Id;
        p.ImpactScaleId = ImpactScale?.Id;
        p.ProbabilityScaleId = ProbabilityScale?.Id;
        p.CompanyLogo = CompanyLogo;
        try
        {
            _owner.Document.UpdateProject(p);
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void Revert() => Reload();

    [RelayCommand]
    private async Task PickLogo()
    {
        var path = await _owner.Ui.PickOpenFileAsync(Strings.Project_PickLogo, FileKind.Image);
        if (path is null)
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length > MaxLogoBytes)
        {
            Error = Strings.Project_LogoTooLarge;
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path);
        if (Brushes.Image(bytes) is null)
        {
            Error = Strings.Project_LogoInvalid;
            return;
        }

        CompanyLogo = bytes;
    }

    [RelayCommand]
    private void ClearLogo() => CompanyLogo = null;

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
