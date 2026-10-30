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
    public ObservableCollection<Scale> ValueScales { get; } = [];
    public ObservableCollection<Scale> DifficultyScales { get; } = [];
    public ObservableCollection<ScalePreview> AssignedScales { get; } = [];
    public ObservableCollection<DistributionRow> Distribution { get; } = [];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _client = "";
    [ObservableProperty] private string _consultancy = "";
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private Choice<ProjectStatus>? _status;
    [ObservableProperty] private Scale? _riskScale;
    [ObservableProperty] private Scale? _valueScale;
    [ObservableProperty] private Scale? _difficultyScale;
    [ObservableProperty] private byte[]? _consultancyLogo;
    [ObservableProperty] private byte[]? _clientLogo;
    [ObservableProperty] private Bitmap? _consultancyLogoImage;
    [ObservableProperty] private Bitmap? _clientLogoImage;
    [ObservableProperty] private bool _canChangeScales;
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private int _riskCount;

    public string ScalesHint => CanChangeScales ? Strings.Project_ScalesHint : Strings.Project_ScalesLocked;

    partial void OnCanChangeScalesChanged(bool value) => OnPropertyChanged(nameof(ScalesHint));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is nameof(Name) or nameof(Client) or nameof(Consultancy) or nameof(Date)
            or nameof(Status) or nameof(RiskScale) or nameof(ValueScale) or nameof(DifficultyScale)
            or nameof(ConsultancyLogo) or nameof(ClientLogo))
        {
            HasChanges = true;
        }
    }

    partial void OnConsultancyLogoChanged(byte[]? value) => ConsultancyLogoImage = Brushes.Image(value);
    partial void OnClientLogoChanged(byte[]? value) => ClientLogoImage = Brushes.Image(value);

    /// <summary>Vuelve a leer la ficha desde el documento (descarta lo no aplicado).</summary>
    public void Reload()
    {
        _loading = true;
        var doc = _owner.Document;
        var p = doc.Project;
        Name = p.Name;
        Client = p.Client;
        Consultancy = p.Consultancy;
        Date = p.Date.ToDateTime(TimeOnly.MinValue);
        Status = Statuses.First(s => s.Value == p.Status);
        Fill(RiskScales, doc.Scales.Where(s => s.Type == ScaleType.Risk));
        Fill(ValueScales, doc.Scales.Where(s => s.Type == ScaleType.Value));
        Fill(DifficultyScales, doc.Scales.Where(s => s.Type == ScaleType.Difficulty));
        RiskScale = RiskScales.FirstOrDefault(s => s.Id == p.RiskScaleId);
        ValueScale = ValueScales.FirstOrDefault(s => s.Id == p.ValueScaleId);
        DifficultyScale = DifficultyScales.FirstOrDefault(s => s.Id == p.DifficultyScaleId);
        ConsultancyLogo = p.ConsultancyLogo;
        ClientLogo = p.ClientLogo;
        CanChangeScales = doc.CanChangeProjectScales;
        RiskCount = doc.Risks.Count;

        Fill(AssignedScales, Enum.GetValues<ScaleType>().Select(t =>
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
        p.Client = Client.Trim();
        p.Consultancy = Consultancy.Trim();
        p.Date = Date is { } d ? DateOnly.FromDateTime(d.Date) : p.Date;
        p.Status = Status?.Value ?? p.Status;
        p.RiskScaleId = RiskScale?.Id;
        p.ValueScaleId = ValueScale?.Id;
        p.DifficultyScaleId = DifficultyScale?.Id;
        p.ConsultancyLogo = ConsultancyLogo;
        p.ClientLogo = ClientLogo;
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
    private async Task PickLogo(string which)
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

        if (which == "client")
        {
            ClientLogo = bytes;
        }
        else
        {
            ConsultancyLogo = bytes;
        }
    }

    [RelayCommand]
    private void ClearLogo(string which)
    {
        if (which == "client")
        {
            ClientLogo = null;
        }
        else
        {
            ConsultancyLogo = null;
        }
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
