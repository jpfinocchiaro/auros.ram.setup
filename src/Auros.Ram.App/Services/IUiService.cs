using Auros.Ram.Core.Model;
using Auros.Ram.App.ViewModels;

namespace Auros.Ram.App.Services;

public enum FileKind
{
    Matrix,
    Template,
    Json,
    Image,
    Xlsx,
    Pdf,
    Csv,
}

public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <param name="Preset">Escalas estándar a usar; null = cargarlas desde una plantilla.</param>
public sealed record NewMatrixRequest(string Name, string Company, ScalePreset? Preset);

/// <summary>Respuesta a la advertencia de exportación en claro (RNF-38).</summary>
public sealed record ExportConfirmation(string? PdfPassword);

/// <summary>Diálogos y selectores de archivo. Los ViewModels no conocen las ventanas.</summary>
public interface IUiService
{
    Task<string?> PickOpenFileAsync(string title, FileKind kind);
    Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind);

    Task AlertAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string? cancelText = null);
    Task<SaveChoice> AskSaveChangesAsync(string fileName);

    /// <summary>Muestra el diálogo de contraseña; se cierra cuando <see cref="PasswordDialogViewModel.Submit"/> no devuelve error.</summary>
    Task<string?> AskPasswordAsync(PasswordDialogViewModel vm);

    Task<NewMatrixRequest?> AskNewMatrixAsync();
    Task<ExportConfirmation?> ConfirmExportAsync(bool isPdf);
    Task EditRiskAsync(RiskEditorViewModel vm);
    Task EditScaleAsync(ScaleEditorViewModel vm);
    Task<bool> EditPreferencesAsync(PreferencesViewModel vm);
    Task ShowAboutAsync();

    Task EditCatalogsAsync(CatalogsViewModel vm);

    Task EditOrganizationsAsync(OrganizationsViewModel vm);
    Task OpenWithSystemAsync(string path);
}
