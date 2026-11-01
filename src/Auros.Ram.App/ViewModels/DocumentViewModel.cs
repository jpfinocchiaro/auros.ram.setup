using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;
using Auros.Ram.Export;
using Auros.Ram.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public enum ExportKind
{
    Xlsx,
    PdfMatrix,
    PdfSummary,
    PdfDetail,
    PdfAll,
    PdfEvolution,
    CsvRisks,
    CsvMatrix,
    Json,
    Print,
}

/// <summary>Una opción de los combos de empresa y unidad de negocio (Id null: "Todas").</summary>
public sealed record ScopeOption(Guid? Id, string Name)
{
    public override string ToString() => Name;
}

public enum Section
{
    Project = 0,
    Risks = 1,
    Matrix = 2,
    Reports = 3,
    Scales = 4,
}

/// <summary>Un archivo de matriz abierto, con su propia clave y sesión (RNF-15).</summary>
public sealed partial class DocumentViewModel : ObservableObject, IDisposable
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _disposed;

    public DocumentViewModel(string path, RiskMatrixDocument document, FileKey key, IUiService ui)
    {
        _path = System.IO.Path.GetFullPath(path);
        Document = document;
        // Las reglas de fechas de evaluación vienen de las Preferencias, no del archivo.
        Document.DatePolicy = DisplayOptions.Current.DatePolicy;
        Document.EntryPolicy = DisplayOptions.Current.EntryPolicy;
        Key = key;
        Ui = ui;
        View = Document;
        RebuildScopeOptions();
        Project = new ProjectViewModel(this);
        Risks = new RisksViewModel(this);
        Matrix = new MatrixViewModel(this);
        Reports = new ReportsViewModel(this);
        Scales = new ScalesViewModel(this);

        Document.Changed += OnDocumentChanged;
        Document.DirtyChanged += (_, _) => OnPropertyChanged(nameof(IsDirty));
        Document.History.Changed += (_, _) => OnPropertyChanged(nameof(UndoText));
        Document.History.Changed += (_, _) => OnPropertyChanged(nameof(RedoText));
    }

    public RiskMatrixDocument Document { get; }

    // ---- Empresa y unidad de negocio que se miran ------------------------------

    /// <summary>
    /// Lo que muestran las vistas y los informes: el documento filtrado por la empresa y la unidad elegidas
    /// (o todo, en el consolidado). Las modificaciones siempre van a <see cref="Document"/>.
    /// </summary>
    public RiskMatrixDocument View { get; private set; }

    /// <summary>Opciones del combo de empresas: "Todas" y cada empresa.</summary>
    public ObservableCollection<ScopeOption> CompanyOptions { get; } = [];

    /// <summary>Opciones del combo de unidades: "Todas" y las unidades de la empresa elegida.</summary>
    public ObservableCollection<ScopeOption> UnitOptions { get; } = [];

    [ObservableProperty]
    private ScopeOption? _scopeCompany;

    [ObservableProperty]
    private ScopeOption? _scopeUnit;

    private bool _scopeLoading;

    public RiskScope Scope => new(ScopeCompany?.Id, ScopeUnit?.Id);

    /// <summary>Hay una empresa elegida: el combo de unidades tiene sentido.</summary>
    public bool HasScopeCompany => ScopeCompany?.Id is not null;

    partial void OnScopeCompanyChanged(ScopeOption? value)
    {
        OnPropertyChanged(nameof(HasScopeCompany));
        if (_scopeLoading)
        {
            return;
        }

        _scopeLoading = true;
        RebuildUnits(null);
        _scopeLoading = false;
        ApplyScope();
    }

    partial void OnScopeUnitChanged(ScopeOption? value)
    {
        if (!_scopeLoading)
        {
            ApplyScope();
        }
    }

    /// <summary>Vuelve a armar las vistas con la empresa y la unidad elegidas.</summary>
    private void ApplyScope()
    {
        View = Document.ScopedTo(Scope);
        Project.ReloadDistribution();
        Risks.Reload();
        Matrix.Reload();
        Reports.Reload();
    }

    /// <summary>Las opciones de los combos (las empresas pueden haber cambiado); se conserva lo elegido si sigue existiendo.</summary>
    private void RebuildScopeOptions()
    {
        _scopeLoading = true;
        var company = ScopeCompany?.Id;
        var unit = ScopeUnit?.Id;
        CompanyOptions.Clear();
        CompanyOptions.Add(new ScopeOption(null, Strings.Scope_AllCompanies));
        foreach (var c in Document.Project.Companies)
        {
            CompanyOptions.Add(new ScopeOption(c.Id, c.Name));
        }

        ScopeCompany = CompanyOptions.FirstOrDefault(o => o.Id == company) ?? CompanyOptions[0];
        RebuildUnits(unit);
        _scopeLoading = false;
        View = Document.ScopedTo(Scope);
    }

    private void RebuildUnits(Guid? keep)
    {
        UnitOptions.Clear();
        UnitOptions.Add(new ScopeOption(null, Strings.Scope_AllUnits));
        foreach (var u in (ScopeCompany?.Id is { } id ? Document.Project.FindCompany(id)?.Units : null) ?? [])
        {
            UnitOptions.Add(new ScopeOption(u.Id, u.Name));
        }

        ScopeUnit = UnitOptions.FirstOrDefault(o => o.Id == keep) ?? UnitOptions[0];
    }
    public FileKey Key { get; private set; }
    public IUiService Ui { get; }

    public ProjectViewModel Project { get; }
    public RisksViewModel Risks { get; }
    public MatrixViewModel Matrix { get; }
    public ReportsViewModel Reports { get; }
    public ScalesViewModel Scales { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(Header))]
    private string _path;

    [ObservableProperty] private int _selectedSection = (int)Section.Risks;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private string _unlockPassword = "";
    [ObservableProperty] private string? _unlockError;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _status;

    public string FileName => System.IO.Path.GetFileName(Path);
    public bool IsDirty => Document.IsDirty;
    public string Header => IsDirty ? $"{FileName} •" : FileName;
    public string UndoText => Document.History.UndoDescription is { } d ? $"{Strings.Menu_Undo}: {d}" : Strings.Menu_Undo;
    public string RedoText => Document.History.RedoDescription is { } d ? $"{Strings.Menu_Redo}: {d}" : Strings.Menu_Redo;

    partial void OnIsLockedChanged(bool value) => UnlockPassword = "";

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsDirty))
        {
            OnPropertyChanged(nameof(Header));
        }
    }

    // ---- Edición -----------------------------------------------------------

    /// <summary>Ejecuta una operación del dominio y muestra el error de negocio, si lo hay.</summary>
    public void Run(Action action)
    {
        try
        {
            action();
        }
        catch (DomainException ex)
        {
            _ = Ui.AlertAsync(Strings.Common_CannotDo, ex.Message);
        }
    }

    /// <summary>Nueva evaluación de un riesgo existente (ventana en modo "Nueva evaluación").</summary>
    /// <summary>Clonar un riesgo: ventana "Nuevo riesgo" con todo copiado del original.</summary>
    public async Task CloneRiskAsync(Guid riskId)
    {
        if (Document.FindRisk(riskId) is { } risk)
        {
            await Ui.EditRiskAsync(RiskEditorViewModel.CloneOf(Document, risk));
        }
    }

    /// <summary>Clonar una evaluación: ventana "Nueva evaluación" del mismo riesgo con sus valores ya cargados.</summary>
    public async Task CloneAssessmentAsync(Guid riskId, Guid assessmentId)
    {
        if (Document.FindRisk(riskId) is { } risk && risk.Assessments.FirstOrDefault(a => a.Id == assessmentId) is { } assessment)
        {
            await Ui.EditRiskAsync(RiskEditorViewModel.ReassessFrom(Document, risk, assessment));
        }
    }

    public async Task ReassessRiskAsync(Guid riskId)
    {
        if (Document.FindRisk(riskId) is { } risk)
        {
            await Ui.EditRiskAsync(new RiskEditorViewModel(Document, risk, mode: RiskEditorMode.Reassess));
        }
    }

    public async Task EditRiskAsync(Guid? riskId)
    {
        if (IsLocked)
        {
            return;
        }

        var risk = riskId is { } id ? Document.FindRisk(id) : null;
        if (riskId is not null && risk is null)
        {
            return;
        }

        await Ui.EditRiskAsync(new RiskEditorViewModel(Document, risk, risk is null ? ScopePreset() : null));
    }

    /// <summary>Para un riesgo nuevo: la empresa y la unidad elegidas en la barra (si hay).</summary>
    private Risk ScopePreset() => new()
    {
        CompanyId = ScopeCompany?.Id ?? Guid.Empty,
        UnitId = ScopeUnit?.Id ?? Guid.Empty,
    };

    /// <summary>Riesgo nuevo con impacto y probabilidad ya elegidos (doble clic en una celda de la matriz).</summary>
    public async Task NewRiskAtAsync(Guid impactLevelId, Guid probabilityLevelId)
    {
        if (IsLocked)
        {
            return;
        }

        var preset = ScopePreset();
        preset.ImpactLevelId = impactLevelId;
        preset.ProbabilityLevelId = probabilityLevelId;
        await Ui.EditRiskAsync(new RiskEditorViewModel(Document, null, preset));
    }

    public async Task EditScaleAsync(Scale? scale, ScaleType type)
    {
        if (IsLocked)
        {
            return;
        }

        var vm = new ScaleEditorViewModel(Document, scale, type)
        {
            ConfirmRemove = name => Ui.ConfirmAsync(Strings.Scale_RemoveLevelTitle,
                string.Format(CultureInfo.CurrentCulture, Strings.Scale_RemoveLevelMessage, name), Strings.Common_Remove),
        };
        await Ui.EditScaleAsync(vm);
    }

    public void Undo()
    {
        if (!IsLocked)
        {
            Document.History.Undo();
        }
    }

    public void Redo()
    {
        if (!IsLocked)
        {
            Document.History.Redo();
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.Kind.HasFlag(DocumentChangeKind.Project))
        {
            RebuildScopeOptions();
        }

        View = Document.ScopedTo(Scope);
        if (e.Kind.HasFlag(DocumentChangeKind.Project) || e.Kind.HasFlag(DocumentChangeKind.Scales) || !Project.HasChanges)
        {
            Project.Reload();
        }
        else
        {
            Project.ReloadDistribution();
        }

        Risks.Reload();
        Matrix.Reload();
        Reports.Reload();
        if (e.Kind.HasFlag(DocumentChangeKind.Scales) || e.Kind.HasFlag(DocumentChangeKind.Project) || e.Kind.HasFlag(DocumentChangeKind.Risks))
        {
            Scales.Reload();
        }
    }

    // ---- Guardado ----------------------------------------------------------

    /// <summary>Guarda sin bloquear la interfaz (RNF-22). Con <paramref name="newPath"/> es "Guardar como".</summary>
    public async Task<bool> SaveAsync(string? newPath = null)
    {
        var target = newPath is null ? Path : System.IO.Path.GetFullPath(newPath);
        var snapshot = Document.Snapshot();
        var state = Document.History.StateId;
        using var key = Key.Clone();

        await _saveGate.WaitAsync();
        IsBusy = true;
        Status = Strings.Status_Saving;
        try
        {
            await Task.Run(() => RmxFile.Save(snapshot, target, key));
        }
        catch (RmxFileException ex)
        {
            Log.Error(ex, "Save");
            Status = null;
            await Ui.AlertAsync(Strings.Error_SaveTitle, ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            _saveGate.Release();
        }

        if (!string.Equals(target, Path, StringComparison.OrdinalIgnoreCase))
        {
            RecoveryStore.Delete(Path);
            Path = target;
        }

        Document.MarkSaved(state);
        if (!Document.IsDirty)
        {
            RecoveryStore.Delete(Path);
        }

        Status = string.Format(CultureInfo.CurrentCulture, Strings.Status_Saved, DateTime.Now.ToString("t", CultureInfo.CurrentCulture));
        return true;
    }

    /// <summary>Copia de recuperación cifrada con la misma clave (RNF-25, RNF-37).</summary>
    public async Task AutosaveAsync()
    {
        if (_disposed || !Document.IsDirty || !await _saveGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var snapshot = Document.Snapshot();
            using var key = Key.Clone();
            await Task.Run(() => RecoveryStore.Save(snapshot, Path, key));
            Status = string.Format(CultureInfo.CurrentCulture, Strings.Status_Autosaved, DateTime.Now.ToString("t", CultureInfo.CurrentCulture));
        }
        catch (RmxFileException ex)
        {
            Log.Error(ex, "Autosave");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>
    /// RNF-35: cambiar la contraseña vuelve a cifrar el archivo completo con una sal nueva.
    /// También permite agregar una contraseña a un archivo que no tenía, o quitarla.
    /// </summary>
    public async Task ChangePasswordAsync()
    {
        var vm = Key.IsProtected
            ? new PasswordDialogViewModel(PasswordMode.Change, Strings.Password_ChangeTitle, FileName)
            : new PasswordDialogViewModel(PasswordMode.Create, Strings.Password_SetTitle, FileName);
        vm.Submit = async d =>
        {
            if (Key.IsProtected && !await Task.Run(() => Key.Matches(d.Current)))
            {
                return Strings.Password_CurrentWrong;
            }

            var newKey = await Task.Run(() => FileKey.Create(d.NewPassword));
            var snapshot = Document.Snapshot();
            var state = Document.History.StateId;
            await _saveGate.WaitAsync();
            try
            {
                await Task.Run(() => RmxFile.Save(snapshot, Path, newKey));
            }
            catch (RmxFileException ex)
            {
                newKey.Dispose();
                return ex.Message;
            }
            finally
            {
                _saveGate.Release();
            }

            var old = Key;
            Key = newKey;
            old.Dispose();
            RecoveryStore.Delete(Path);
            Document.MarkSaved(state);
            Status = Key.IsProtected ? Strings.Status_PasswordChanged : Strings.Status_PasswordRemoved;
            return null;
        };
        await Ui.AskPasswordAsync(vm);
    }

    // ---- Bloqueo por inactividad (RNF-36) -----------------------------------

    public void Lock()
    {
        Reports.Playback.Stop();
        IsLocked = true;
        UnlockError = null;
    }

    [RelayCommand]
    private async Task Unlock()
    {
        if (string.IsNullOrEmpty(UnlockPassword))
        {
            UnlockError = Strings.Password_Required;
            return;
        }

        IsBusy = true;
        var password = UnlockPassword;
        var ok = await Task.Run(() => Key.Matches(password));
        IsBusy = false;
        if (ok)
        {
            IsLocked = false;
            UnlockError = null;
        }
        else
        {
            UnlockPassword = "";
            UnlockError = Strings.Password_Wrong;
        }
    }

    // ---- Plantillas y exportación ------------------------------------------

    public async Task SaveTemplateAsync()
    {
        var path = await Ui.PickSaveFileAsync(Strings.Template_SaveTitle, Strings.Template_DefaultName + RmxFile.TemplateExtension, FileKind.Template);
        if (path is null)
        {
            return;
        }

        var scales = Document.Scales.Select(s => s.Clone()).ToList();
        var vm = new PasswordDialogViewModel(PasswordMode.Create, Strings.Template_PasswordTitle, System.IO.Path.GetFileName(path));
        vm.Submit = async d =>
        {
            try
            {
                using var key = await Task.Run(() => FileKey.Create(d.NewPassword));
                await Task.Run(() => RmxFile.SaveTemplate(scales, path, key));
                return null;
            }
            catch (RmxFileException ex)
            {
                return ex.Message;
            }
        };
        if (await Ui.AskPasswordAsync(vm) is not null)
        {
            Status = Strings.Status_TemplateSaved;
        }
    }

    public async Task ExportAsync(ExportKind kind)
    {
        var isPdf = kind is ExportKind.PdfMatrix or ExportKind.PdfSummary or ExportKind.PdfDetail or ExportKind.PdfAll or ExportKind.PdfEvolution or ExportKind.Print;
        if (await Ui.ConfirmExportAsync(isPdf) is not { } confirmation)
        {
            return;
        }

        var (fileKind, suffix) = kind switch
        {
            ExportKind.Xlsx => (FileKind.Xlsx, ".xlsx"),
            ExportKind.PdfMatrix => (FileKind.Pdf, $" - {Strings.Export_SuffixMatrix}.pdf"),
            ExportKind.PdfSummary => (FileKind.Pdf, $" - {Strings.Export_SuffixSummary}.pdf"),
            ExportKind.PdfDetail => (FileKind.Pdf, $" - {Strings.Export_SuffixDetail}.pdf"),
            ExportKind.PdfEvolution => (FileKind.Pdf, $" - {Strings.Export_SuffixEvolution}.pdf"),
            ExportKind.PdfAll or ExportKind.Print => (FileKind.Pdf, $" - {Strings.Export_SuffixReport}.pdf"),
            ExportKind.CsvRisks => (FileKind.Csv, $" - {Strings.Export_SuffixRisks}.csv"),
            ExportKind.CsvMatrix => (FileKind.Csv, $" - {Strings.Export_SuffixMatrix}.csv"),
            _ => (FileKind.Json, ".json"),
        };

        var baseName = System.IO.Path.GetFileNameWithoutExtension(Path);
        var title = kind == ExportKind.Print ? Strings.Print_SaveTitle : Strings.Export_SaveTitle;
        var target = await Ui.PickSaveFileAsync(title, baseName + suffix, fileKind);
        if (target is null)
        {
            return;
        }

        // Lo que se exporta es lo que se mira (empresa y unidad elegidas); el JSON es siempre el archivo completo.
        var snapshot = (kind == ExportKind.Json ? Document : View).Snapshot();
        var period = Reports.Period;
        var password = confirmation.PdfPassword;
        IsBusy = true;
        Status = Strings.Status_Exporting;
        try
        {
            await Task.Run(() =>
            {
                switch (kind)
                {
                    case ExportKind.Xlsx: XlsxExport.Write(snapshot, target); break;
                    case ExportKind.PdfMatrix: PdfExport.Write(snapshot, target, PdfReports.Matrix, password, AppInfo.Version); break;
                    case ExportKind.PdfSummary: PdfExport.Write(snapshot, target, PdfReports.Summary, password, AppInfo.Version); break;
                    case ExportKind.PdfDetail: PdfExport.Write(snapshot, target, PdfReports.Detail, password, AppInfo.Version); break;
                    case ExportKind.PdfEvolution: PdfExport.Write(snapshot, target, PdfReports.Evolution, password, AppInfo.Version, period); break;
                    case ExportKind.PdfAll:
                    case ExportKind.Print: PdfExport.Write(snapshot, target, PdfReports.All, password, AppInfo.Version); break;
                    case ExportKind.CsvRisks: CsvExport.WriteRisks(snapshot, target); break;
                    case ExportKind.CsvMatrix: CsvExport.WriteMatrix(snapshot, target); break;
                    default: File.WriteAllText(target, JsonExchange.Export(snapshot)); break;
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error(ex, "Export");
            Status = null;
            await Ui.AlertAsync(Strings.Export_ErrorTitle, Strings.Export_ErrorMessage);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        Status = string.Format(CultureInfo.CurrentCulture, Strings.Status_Exported, System.IO.Path.GetFileName(target));
        if (kind == ExportKind.Print)
        {
            // RNF-65: el PDF se abre en el visor del sistema, que ofrece el diálogo de impresión del SO.
            await Ui.OpenWithSystemAsync(target);
        }
    }

    public void Dispose()
    {
        Reports.Playback.Stop();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Document.Changed -= OnDocumentChanged;
        Key.Dispose();
        _saveGate.Dispose();
    }
}
