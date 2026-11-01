using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;
using Auros.Ram.Persistence;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public sealed record SketchRow(IReadOnlyList<LevelChip> Cells);

public sealed record RecentItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>Ventana principal: archivos abiertos en pestañas y comandos de documento (RNF-43).</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IUiService _ui;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _lockTimer;
    private DateTime _lastActivity = DateTime.UtcNow;

    public MainWindowViewModel(IUiService ui, AppSettings settings)
    {
        _ui = ui;
        _settings = settings;
        DisplayOptions.Current.ShowLevelAbbreviations = settings.ShowLevelAbbreviations;
        DisplayOptions.Current.ShowImpactAmount = settings.ShowImpactAmount;
        DisplayOptions.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DisplayOptions.ShowImpactAmount) && _settings.ShowImpactAmount != DisplayOptions.Current.ShowImpactAmount)
            {
                _settings.ShowImpactAmount = DisplayOptions.Current.ShowImpactAmount;
                _settings.Save();
            }
        };
        DisplayOptions.Current.DatePolicy = settings.DatePolicy;
        DisplayOptions.Current.EntryPolicy = settings.EntryPolicy;
        RefreshRecent();

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(settings.AutosaveMinutes) };
        _autosaveTimer.Tick += async (_, _) =>
        {
            foreach (var doc in Documents.ToList())
            {
                await doc.AutosaveAsync();
            }
        };
        _autosaveTimer.Start();

        _lockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _lockTimer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - _lastActivity > TimeSpan.FromMinutes(_settings.LockMinutes))
            {
                LockAll();
            }
        };
        _lockTimer.Start();

        Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDocuments));
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];
    public ObservableCollection<RecentItem> Recent { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(HasActive))]
    private DocumentViewModel? _active;

    public bool HasDocuments => Documents.Count > 0;
    public bool HasActive => Active is not null;
    public bool CanEdit => Active is { IsLocked: false };
    public bool HasRecent => Recent.Count > 0;
    public string Version => AppInfo.Version;

    /// <summary>Mapa de calor de las escalas estándar, para la pantalla de bienvenida (el impacto mayor arriba).</summary>
    public IReadOnlyList<SketchRow> SampleHeatMap { get; } = BuildSample();

    private static IReadOnlyList<SketchRow> BuildSample()
    {
        var m = Auros.Ram.Core.Services.PriorityMatrix.Build(RiskMatrixDocument.CreateNew());
        return [.. Enumerable.Range(0, m.Rows.Count).Select(r =>
            new SketchRow([.. Enumerable.Range(0, m.Columns.Count).Select(c => LevelChip.From(m.LevelAt(r, c))!)]))];
    }

    partial void OnActiveChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnActivePropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnActivePropertyChanged;
        }
    }

    private void OnActivePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.IsLocked))
        {
            OnPropertyChanged(nameof(CanEdit));
        }
    }

    /// <summary>La ventana avisa de cada interacción para medir la inactividad (RNF-36).</summary>
    public void NotifyActivity() => _lastActivity = DateTime.UtcNow;

    public void LockAll()
    {
        // Un archivo sin contraseña no se puede desbloquear con una, así que no se bloquea.
        foreach (var doc in Documents.Where(d => !d.IsLocked && d.Key.IsProtected))
        {
            doc.Lock();
        }
    }

    // ---- Nuevo / abrir -----------------------------------------------------

    [RelayCommand]
    private async Task New()
    {
        var request = await _ui.AskNewMatrixAsync();
        if (request is null)
        {
            return;
        }

        IReadOnlyList<Scale>? scales;
        if (request.Preset is { } preset)
        {
            scales = DefaultScales.Create(preset);
        }
        else
        {
            scales = await LoadTemplateAsync();
            if (scales is null)
            {
                return;
            }
        }

        var doc = RiskMatrixDocument.CreateNew(new ProjectInfo
        {
            Name = request.Name.Trim(),
            Company = request.Company.Trim(),
        }, scales);
        await CreateFileAsync(doc, FileNameFor(request.Name));
    }

    [RelayCommand]
    private async Task Open()
    {
        var path = await _ui.PickOpenFileAsync(Strings.Open_Title, FileKind.Matrix);
        if (path is not null)
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private Task OpenRecent(RecentItem item) => OpenPathAsync(item.Path);

    [RelayCommand]
    private async Task ImportJson()
    {
        var path = await _ui.PickOpenFileAsync(Strings.Import_Title, FileKind.Json);
        if (path is null)
        {
            return;
        }

        RiskMatrixDocument doc;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            doc = JsonExchange.Import(json);
        }
        catch (DomainException ex)
        {
            await _ui.AlertAsync(Strings.Import_ErrorTitle, ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _ui.AlertAsync(Strings.Import_ErrorTitle, Strings.Error_ReadFile);
            return;
        }

        await CreateFileAsync(doc, Path.GetFileNameWithoutExtension(path) + RmxFile.MatrixExtension);
    }

    public async Task OpenPathAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (Documents.FirstOrDefault(d => string.Equals(d.Path, full, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            Active = open;
            return;
        }

        if (!File.Exists(full))
        {
            _settings.RemoveRecent(full);
            RefreshRecent();
            await _ui.AlertAsync(Strings.Open_ErrorTitle, string.Format(CultureInfo.CurrentCulture, Strings.Open_NotFound, full));
            return;
        }

        RmxHeader header;
        try
        {
            header = RmxFile.ReadHeader(full);
        }
        catch (RmxFileException ex)
        {
            await _ui.AlertAsync(Strings.Open_ErrorTitle, ex.Message);
            return;
        }

        RmxOpenResult? result = null;
        RmxFileException? failure = null;
        if (header.IsProtected)
        {
            var vm = new PasswordDialogViewModel(PasswordMode.Enter, Strings.Open_PasswordTitle, Path.GetFileName(full));
            vm.Submit = async d =>
            {
                try
                {
                    result = await Task.Run(() => RmxFile.Open(full, d.Current));
                    return null;
                }
                catch (RmxFileException ex) when (ex.Kind == RmxErrorKind.WrongPassword)
                {
                    return ex.Message;
                }
                catch (RmxFileException ex)
                {
                    failure = ex;
                    return null;
                }
            };
            await _ui.AskPasswordAsync(vm);
        }
        else
        {
            try
            {
                result = await Task.Run(() => RmxFile.Open(full, null));
            }
            catch (RmxFileException ex)
            {
                failure = ex;
            }
        }

        if (failure is not null)
        {
            Log.Error(failure, "Open");
            await _ui.AlertAsync(Strings.Open_ErrorTitle, failure.Message);
            return;
        }

        if (result is null)
        {
            return;
        }

        var doc = result.Document;
        if (result.BackupPath is not null)
        {
            await _ui.AlertAsync(Strings.Open_MigratedTitle,
                string.Format(CultureInfo.CurrentCulture, Strings.Open_MigratedMessage, Path.GetFileName(result.BackupPath)));
        }

        // RNF-25: cambios sin guardar de un cierre anormal.
        if (RecoveryStore.HasRecovery(full))
        {
            var recover = await _ui.ConfirmAsync(Strings.Recovery_Title, Strings.Recovery_Message, Strings.Recovery_Recover, Strings.Recovery_Discard);
            if (recover && RecoveryStore.TryLoad(full, result.Key) is { } recovered)
            {
                doc = recovered;
                doc.MarkDirty();
            }
            else
            {
                RecoveryStore.Delete(full);
            }
        }
        else
        {
            RecoveryStore.Delete(full);
        }

        AddDocument(new DocumentViewModel(full, doc, result.Key, _ui));
        _settings.AddRecent(full);
        RefreshRecent();
    }

    private async Task<IReadOnlyList<Scale>?> LoadTemplateAsync()
    {
        var path = await _ui.PickOpenFileAsync(Strings.Template_OpenTitle, FileKind.Template);
        if (path is null)
        {
            return null;
        }

        IReadOnlyList<Scale>? scales = null;
        RmxFileException? failure = null;
        try
        {
            if (!RmxFile.ReadHeader(path).IsProtected)
            {
                scales = await Task.Run(() => RmxFile.OpenTemplate(path, null));
            }
        }
        catch (RmxFileException ex)
        {
            failure = ex;
        }

        if (scales is null && failure is null)
        {
            var vm = new PasswordDialogViewModel(PasswordMode.Enter, Strings.Template_PasswordOpenTitle, Path.GetFileName(path));
            vm.Submit = async d =>
            {
                try
                {
                    scales = await Task.Run(() => RmxFile.OpenTemplate(path, d.Current));
                    return null;
                }
                catch (RmxFileException ex) when (ex.Kind == RmxErrorKind.WrongPassword)
                {
                    return ex.Message;
                }
                catch (RmxFileException ex)
                {
                    failure = ex;
                    return null;
                }
            };
            await _ui.AskPasswordAsync(vm);
        }
        if (failure is not null)
        {
            await _ui.AlertAsync(Strings.Template_ErrorTitle, failure.Message);
        }

        return scales;
    }

    /// <summary>Elige ubicación y contraseña (opcional por ahora) de un archivo nuevo y lo guarda (RNF-34).</summary>
    private async Task CreateFileAsync(RiskMatrixDocument doc, string suggestedName)
    {
        var path = await _ui.PickSaveFileAsync(Strings.New_SaveTitle, suggestedName, FileKind.Matrix);
        if (path is null)
        {
            return;
        }

        path = Path.GetFullPath(path);
        if (Documents.Any(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.AlertAsync(Strings.New_ErrorTitle, Strings.New_FileIsOpen);
            return;
        }

        FileKey? key = null;
        var vm = new PasswordDialogViewModel(PasswordMode.Create, Strings.New_PasswordTitle, Path.GetFileName(path));
        vm.Submit = async d =>
        {
            var candidate = await Task.Run(() => FileKey.Create(d.NewPassword));
            try
            {
                await Task.Run(() => RmxFile.Save(doc, path, candidate));
            }
            catch (RmxFileException ex)
            {
                candidate.Dispose();
                return ex.Message;
            }

            key = candidate;
            return null;
        };
        await _ui.AskPasswordAsync(vm);
        if (key is null)
        {
            return;
        }

        doc.MarkSaved();
        var docVm = new DocumentViewModel(path, doc, key, _ui) { SelectedSection = (int)Section.Project };
        AddDocument(docVm);
        _settings.AddRecent(path);
        RefreshRecent();
    }

    private void AddDocument(DocumentViewModel doc)
    {
        Documents.Add(doc);
        Active = doc;
    }

    // ---- Guardar / cerrar --------------------------------------------------

    [RelayCommand]
    private async Task Save()
    {
        if (Active is { IsLocked: false } doc)
        {
            await doc.SaveAsync();
        }
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        if (Active is not { IsLocked: false } doc)
        {
            return;
        }

        var path = await _ui.PickSaveFileAsync(Strings.SaveAs_Title, doc.FileName, FileKind.Matrix);
        if (path is null)
        {
            return;
        }

        var full = Path.GetFullPath(path);
        if (Documents.Any(d => d != doc && string.Equals(d.Path, full, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.AlertAsync(Strings.New_ErrorTitle, Strings.New_FileIsOpen);
            return;
        }

        if (await doc.SaveAsync(full))
        {
            _settings.AddRecent(full);
            RefreshRecent();
        }
    }

    [RelayCommand]
    private async Task ChangePassword()
    {
        if (Active is { IsLocked: false } doc)
        {
            await doc.ChangePasswordAsync();
        }
    }

    [RelayCommand]
    private async Task Close(DocumentViewModel? doc)
    {
        doc ??= Active;
        if (doc is not null)
        {
            await CloseDocumentAsync(doc);
        }
    }

    /// <summary>RNF-43: confirmación al cerrar con cambios sin guardar.</summary>
    public async Task<bool> CloseDocumentAsync(DocumentViewModel doc)
    {
        if (doc.IsDirty)
        {
            Active = doc;
            if (doc.IsLocked)
            {
                await _ui.AlertAsync(Strings.Close_LockedTitle, Strings.Close_LockedMessage);
                return false;
            }

            switch (await _ui.AskSaveChangesAsync(doc.FileName))
            {
                case SaveChoice.Cancel:
                    return false;
                case SaveChoice.Save when !await doc.SaveAsync():
                    return false;
            }
        }

        RecoveryStore.Delete(doc.Path);
        var index = Documents.IndexOf(doc);
        Documents.Remove(doc);
        doc.Dispose();
        if (Active == doc || Active is null)
        {
            Active = Documents.Count == 0 ? null : Documents[Math.Clamp(index, 0, Documents.Count - 1)];
        }

        return true;
    }

    public async Task<bool> CloseAllAsync()
    {
        foreach (var doc in Documents.ToList())
        {
            if (!await CloseDocumentAsync(doc))
            {
                return false;
            }
        }

        return true;
    }

    // ---- Edición y navegación ----------------------------------------------

    [RelayCommand]
    private void Undo() => Active?.Undo();

    [RelayCommand]
    private void Redo() => Active?.Redo();

    [RelayCommand]
    private void ShowSection(string section)
    {
        if (Active is { } doc && Enum.TryParse<Section>(section, out var s))
        {
            doc.SelectedSection = (int)s;
        }
    }

    [RelayCommand]
    private Task NewRisk() => Active is { IsLocked: false } doc ? doc.EditRiskAsync(null) : Task.CompletedTask;

    [RelayCommand]
    private Task Export(string kind) =>
        Active is { IsLocked: false } doc && Enum.TryParse<ExportKind>(kind, out var k) ? doc.ExportAsync(k) : Task.CompletedTask;

    [RelayCommand]
    private void LockNow() => LockAll();

    [RelayCommand]
    private async Task Preferences()
    {
        var vm = new PreferencesViewModel
        {
            AutosaveMinutes = _settings.AutosaveMinutes,
            LockMinutes = _settings.LockMinutes,
            ShowAbbreviations = _settings.ShowLevelAbbreviations,
            AllowPastDateOnCreate = _settings.AllowPastDateOnCreate,
            AllowDateBeforeRecorded = _settings.AllowDateBeforeRecorded,
            AllowPastDateOnEdit = _settings.AllowPastDateOnEdit,
            ImpactAuto = _settings.ImpactAuto,
            ProbabilityAuto = _settings.ProbabilityAuto,
        };
        vm.ImpactEntry = vm.ImpactEntries.First(c => c.Value == _settings.ImpactEntry);
        vm.ProbabilityEntry = vm.ProbabilityEntries.First(c => c.Value == _settings.ProbabilityEntry);
        if (await _ui.EditPreferencesAsync(vm))
        {
            _settings.AutosaveMinutes = (int)Math.Clamp(vm.AutosaveMinutes ?? 5, 1, 60);
            _settings.LockMinutes = (int)Math.Clamp(vm.LockMinutes ?? 15, 1, 240);
            _settings.ShowLevelAbbreviations = vm.ShowAbbreviations;
            DisplayOptions.Current.ShowLevelAbbreviations = vm.ShowAbbreviations;
            _settings.AllowPastDateOnCreate = vm.AllowPastDateOnCreate;
            _settings.AllowDateBeforeRecorded = vm.AllowDateBeforeRecorded;
            _settings.AllowPastDateOnEdit = vm.AllowPastDateOnEdit;
            _settings.ImpactEntry = vm.ImpactEntry.Value;
            _settings.ImpactAuto = vm.ImpactAuto;
            _settings.ProbabilityEntry = vm.ProbabilityEntry.Value;
            _settings.ProbabilityAuto = vm.ProbabilityAuto;
            DisplayOptions.Current.DatePolicy = _settings.DatePolicy;
            DisplayOptions.Current.EntryPolicy = _settings.EntryPolicy;
            foreach (var doc in Documents)
            {
                doc.Document.DatePolicy = _settings.DatePolicy;
                doc.Document.EntryPolicy = _settings.EntryPolicy;
            }
            _settings.Save();
            _autosaveTimer.Interval = TimeSpan.FromMinutes(_settings.AutosaveMinutes);
        }
    }

    [RelayCommand]
    private Task About() => _ui.ShowAboutAsync();

    /// <summary>Configuración → Consecuencias y criterios: los catálogos de la matriz activa.</summary>
    [RelayCommand]
    private Task Catalogs() =>
        Active is { IsLocked: false } doc ? _ui.EditCatalogsAsync(new CatalogsViewModel(doc.Document)) : Task.CompletedTask;

    /// <summary>Configuración → Empresas y unidades de negocio: el maestro de la matriz activa.</summary>
    [RelayCommand]
    private Task Organizations() =>
        Active is { IsLocked: false } doc ? _ui.EditOrganizationsAsync(new OrganizationsViewModel(doc.Document, _ui)) : Task.CompletedTask;

    private void RefreshRecent()
    {
        Recent.Clear();
        foreach (var path in _settings.RecentFiles)
        {
            Recent.Add(new RecentItem(path));
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    private static string FileNameFor(string projectName)
    {
        var name = string.Concat(projectName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return (string.IsNullOrWhiteSpace(name) ? Strings.New_DefaultFileName : name) + RmxFile.MatrixExtension;
    }
}
