using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Un riesgo en una celda de la foto; <paramref name="Changed"/> si se movió o apareció en esta foto.</summary>
public sealed record PlaybackItem(string Title, bool Changed, string Tooltip, string Amount = "")
{
    public bool HasAmount => Amount.Length > 0;
}

public sealed record PlaybackCell(IReadOnlyList<PlaybackItem> Items, LevelChip? Level, double MinHeight);

public sealed record PlaybackRow(LevelChip Level, IReadOnlyList<PlaybackCell> Cells, double MinHeight);

/// <summary>Una foto ya armada para la pantalla.</summary>
public sealed record PlaybackFrame(DateOnly Date, IReadOnlyList<PlaybackRow> Rows, IReadOnlyList<string> Changes);

/// <summary>
/// Película de la matriz: muestra una tras otra las "fotos" de la matriz entre dos fechas, cada una durante
/// los segundos elegidos, con los riesgos que cambiaron resaltados.
/// </summary>
public sealed partial class MatrixPlaybackViewModel : ObservableObject
{
    public const double ItemHeight = 26;
    private readonly DocumentViewModel _owner;
    private readonly DispatcherTimer _timer;
    private List<PlaybackFrame> _frames = [];

    public MatrixPlaybackViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Advance();
        Reload();
    }

    /// <summary>Período de la película (vacío: desde la primera evaluación hasta hoy).</summary>
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;

    /// <summary>Segundos que se muestra cada foto.</summary>
    [ObservableProperty] private decimal? _seconds = 2;

    /// <summary>Al llegar a la última foto, vuelve a empezar.</summary>
    [ObservableProperty] private bool _loop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(FrameTitle), nameof(Counter), nameof(HasChanges))]
    private int _index;

    public ObservableCollection<LevelChip> Columns { get; } = [];

    [ObservableProperty] private int _columnCount = 1;
    [ObservableProperty] private int _lastIndex;

    public PlaybackFrame? Current => Index >= 0 && Index < _frames.Count ? _frames[Index] : null;

    public bool HasFrames => _frames.Count > 0;

    public bool HasChanges => Current is { Changes.Count: > 0 };

    public string FrameTitle => Current is { } f
        ? string.Format(CultureInfo.CurrentCulture, Strings.Playback_FrameTitle, f.Date.ToString("D", CultureInfo.CurrentCulture))
        : "";

    public string Counter => string.Format(CultureInfo.CurrentCulture, Strings.Playback_Counter, Index + 1, _frames.Count);

    public string PlayLabel => IsPlaying ? Strings.Playback_Pause : Strings.Playback_Play;

    partial void OnFromDateChanged(DateTime? value) => Reload();

    partial void OnToDateChanged(DateTime? value) => Reload();

    partial void OnSecondsChanged(decimal? value) => _timer.Interval = TimeSpan.FromSeconds((double)Math.Clamp(value ?? 2, 0.5m, 60m));

    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    /// <summary>Vuelve a armar las fotos (cambió el período o el archivo); conserva la fecha que se estaba viendo.</summary>
    public void Reload()
    {
        var doc = _owner.Document;
        var (defaultFrom, defaultTo) = RiskHistory.DefaultPeriod(doc);
        var from = FromDate is { } f ? DateOnly.FromDateTime(f) : defaultFrom;
        var to = ToDate is { } t ? DateOnly.FromDateTime(t) : defaultTo;
        var snapshots = RiskHistory.Snapshots(doc, from, to);

        // Todas las fotos con la misma altura de celda, para que la matriz no salte de una a otra.
        var height = (Math.Max(1, snapshots.Select(s => s.Matrix.MaxCellCount).DefaultIfEmpty(1).Max()) * ItemHeight) + 30;
        var shownDate = Current?.Date;
        _frames = [.. snapshots.Select(s => Frame(s, height))];

        Columns.Clear();
        foreach (var c in snapshots.FirstOrDefault()?.Matrix.Columns ?? [])
        {
            Columns.Add(new LevelChip(c));
        }

        ColumnCount = Math.Max(1, Columns.Count);
        LastIndex = Math.Max(0, _frames.Count - 1);
        var keep = _frames.FindIndex(fr => fr.Date == shownDate);
        Index = keep >= 0 ? keep : 0;
        if (_frames.Count <= 1)
        {
            IsPlaying = false;
        }

        OnPropertyChanged(nameof(HasFrames));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(FrameTitle));
        OnPropertyChanged(nameof(Counter));
        OnPropertyChanged(nameof(HasChanges));
    }

    private static PlaybackFrame Frame(MatrixSnapshot s, double height)
    {
        var m = s.Matrix;
        var changed = s.Changes.Select(c => c.Risk.Id).ToHashSet();
        var rows = new List<PlaybackRow>();
        for (var r = 0; r < m.Rows.Count; r++)
        {
            var cells = new List<PlaybackCell>();
            for (var c = 0; c < m.Columns.Count; c++)
            {
                cells.Add(new PlaybackCell(
                    [.. m[r, c].Select(x => new PlaybackItem(x.Title, changed.Contains(x.Id), x.Title))],
                    LevelChip.From(m.LevelAt(r, c)), height));
            }

            rows.Add(new PlaybackRow(new LevelChip(m.Rows[r]), cells, height));
        }

        var changes = s.Changes.Select(c => c.Before is { } b
            ? string.Format(CultureInfo.CurrentCulture, Strings.Playback_Moved, c.Risk.Title,
                b.RiskLevel?.Name ?? "—", c.After.RiskLevel?.Name ?? "—", c.After.Impact.Name, c.After.Probability.Name)
            : string.Format(CultureInfo.CurrentCulture, Strings.Playback_Appeared, c.Risk.Title,
                c.After.RiskLevel?.Name ?? "—", c.After.Impact.Name, c.After.Probability.Name)).ToList();
        return new PlaybackFrame(s.Date, rows, changes);
    }

    private void Advance()
    {
        if (Index < _frames.Count - 1)
        {
            Index++;
        }
        else if (Loop && _frames.Count > 1)
        {
            Index = 0;
        }
        else
        {
            IsPlaying = false;
        }
    }

    /// <summary>Reproduce o pausa. Si estaba en la última foto, empieza de nuevo.</summary>
    [RelayCommand]
    private void PlayPause()
    {
        if (!IsPlaying && Index >= _frames.Count - 1)
        {
            Index = 0;
        }

        IsPlaying = !IsPlaying && _frames.Count > 1;
    }

    [RelayCommand]
    private void First()
    {
        IsPlaying = false;
        Index = 0;
    }

    [RelayCommand]
    private void Previous()
    {
        IsPlaying = false;
        Index = Math.Max(0, Index - 1);
    }

    [RelayCommand]
    private void Next()
    {
        IsPlaying = false;
        Index = Math.Min(_frames.Count - 1, Index + 1);
    }

    [RelayCommand]
    private void Last()
    {
        IsPlaying = false;
        Index = Math.Max(0, _frames.Count - 1);
    }

    /// <summary>Detiene la película (p. ej., al cerrar el archivo).</summary>
    public void Stop() => IsPlaying = false;
}
