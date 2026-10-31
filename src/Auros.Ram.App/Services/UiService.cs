using Auros.Ram.Core.Model;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.ViewModels;
using Auros.Ram.App.Views;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Auros.Ram.App.Services;

/// <summary>Implementación de los diálogos sobre la ventana principal.</summary>
public sealed class UiService(Window owner) : IUiService
{
    private static readonly Dictionary<FileKind, (string Name, string[] Patterns, string Ext)> Types = new()
    {
        [FileKind.Matrix] = (Strings.FileType_Matrix, ["*.rmx"], "rmx"),
        [FileKind.Template] = (Strings.FileType_Template, ["*.rmt"], "rmt"),
        [FileKind.Json] = (Strings.FileType_Json, ["*.json"], "json"),
        [FileKind.Image] = (Strings.FileType_Image, ["*.png", "*.jpg", "*.jpeg"], "png"),
        [FileKind.Xlsx] = (Strings.FileType_Xlsx, ["*.xlsx"], "xlsx"),
        [FileKind.Pdf] = (Strings.FileType_Pdf, ["*.pdf"], "pdf"),
        [FileKind.Csv] = (Strings.FileType_Csv, ["*.csv"], "csv"),
    };

    public async Task<string?> PickOpenFileAsync(string title, FileKind kind)
    {
        var t = Types[kind];
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(t.Name) { Patterns = t.Patterns }],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind)
    {
        var t = Types[kind];
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = t.Ext,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(t.Name) { Patterns = t.Patterns }],
        });
        var path = file?.TryGetLocalPath();
        if (path is not null && !path.EndsWith("." + t.Ext, StringComparison.OrdinalIgnoreCase) && kind != FileKind.Image)
        {
            path += "." + t.Ext;
        }

        return path;
    }

    public Task AlertAsync(string title, string message) =>
        new MessageWindow(title, message, [Strings.Common_Accept]).ShowDialog<int>(Top());

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string? cancelText = null) =>
        await new MessageWindow(title, message, [cancelText ?? Strings.Common_Cancel, confirmText], defaultIndex: 1).ShowDialog<int>(Top()) == 1;

    public async Task<SaveChoice> AskSaveChangesAsync(string fileName)
    {
        var message = string.Format(CultureInfo.CurrentCulture, Strings.Close_SaveMessage, fileName);
        var result = await new MessageWindow(Strings.Close_SaveTitle, message,
            [Strings.Common_Cancel, Strings.Close_DontSave, Strings.Common_Save], defaultIndex: 2).ShowDialog<int>(Top());
        return result switch
        {
            2 => SaveChoice.Save,
            1 => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    public Task<string?> AskPasswordAsync(PasswordDialogViewModel vm) =>
        new PasswordWindow(vm).ShowDialog<string?>(Top());

    public async Task<NewMatrixRequest?> AskNewMatrixAsync()
    {
        var vm = new NewMatrixViewModel();
        var ok = await new NewMatrixWindow(vm).ShowDialog<bool>(Top());
        return ok ? new NewMatrixRequest(vm.Name, vm.Company, vm.SelectedPreset) : null;
    }

    public async Task<ExportConfirmation?> ConfirmExportAsync(bool isPdf)
    {
        var vm = new ExportConfirmViewModel(isPdf);
        var ok = await new ExportConfirmWindow(vm).ShowDialog<bool>(Top());
        return ok ? new ExportConfirmation(isPdf && vm.ProtectPdf ? vm.Password : null) : null;
    }

    public Task EditRiskAsync(RiskEditorViewModel vm) => new RiskEditorWindow(vm).ShowDialog(Top());

    public Task EditScaleAsync(ScaleEditorViewModel vm) => new ScaleEditorWindow(vm).ShowDialog(Top());

    public Task<bool> EditPreferencesAsync(PreferencesViewModel vm) => new PreferencesWindow(vm).ShowDialog<bool>(Top());

    public Task ShowAboutAsync() => new AboutWindow().ShowDialog(Top());

    public async Task OpenWithSystemAsync(string path)
    {
        if (!await owner.Launcher.LaunchFileInfoAsync(new FileInfo(path)))
        {
            await AlertAsync(Strings.Print_Title, string.Format(CultureInfo.CurrentCulture, Strings.Print_OpenFailed, path));
        }
    }

    /// <summary>La ventana de más arriba, para que un diálogo abierto desde otro diálogo quede encima.</summary>
    private Window Top()
    {
        var top = owner;
        while (top.OwnedWindows.LastOrDefault(w => w.IsVisible) is { } child)
        {
            top = child;
        }

        return top;
    }
}
