using Auros.Ram.App.Services;
using Auros.Ram.App.ViewModels;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Persistence;
using Avalonia.Headless.XUnit;

namespace Auros.Ram.App.Tests;

/// <summary>Al guardar un archivo sin contraseña se ofrece ponerle una (una vez por archivo abierto).</summary>
public class OfferPasswordTests
{
    private sealed class FakeUi(bool acceptOffer, string? savePath = null) : IUiService
    {
        public int Confirms { get; private set; }
        public List<PasswordDialogViewModel> PasswordDialogs { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string? cancelText = null)
        {
            Confirms++;
            return Task.FromResult(acceptOffer);
        }

        // La pantalla de contraseña se cancela.
        public Task<string?> AskPasswordAsync(PasswordDialogViewModel vm)
        {
            PasswordDialogs.Add(vm);
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickOpenFileAsync(string title, FileKind kind) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind) => Task.FromResult(savePath);
        public Task AlertAsync(string title, string message) => Task.CompletedTask;
        public Task<SaveChoice> AskSaveChangesAsync(string fileName) => Task.FromResult(SaveChoice.Cancel);
        public Task<NewMatrixRequest?> AskNewMatrixAsync() =>
            Task.FromResult<NewMatrixRequest?>(new NewMatrixRequest("Nueva", "ACME", ScalePreset.Matrix5x5));
        public Task<ExportConfirmation?> ConfirmExportAsync(bool isPdf) => Task.FromResult<ExportConfirmation?>(null);
        public Task EditRiskAsync(RiskEditorViewModel vm) => Task.CompletedTask;
        public Task EditScaleAsync(ScaleEditorViewModel vm) => Task.CompletedTask;
        public Task<bool> EditPreferencesAsync(PreferencesViewModel vm) => Task.FromResult(false);
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task ShowLicenseAsync(LicenseViewModel vm) => Task.CompletedTask;
        public Task EditCatalogsAsync(CatalogsViewModel vm) => Task.CompletedTask;
        public Task EditOrganizationsAsync(OrganizationsViewModel vm) => Task.CompletedTask;
        public Task OpenWithSystemAsync(string path) => Task.CompletedTask;
    }

    private static (MainWindowViewModel Vm, DocumentViewModel Doc, string Path) Open(FakeUi ui, FileKey key)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"auros-oferta-{Guid.NewGuid():N}.rmx");
        var vm = new MainWindowViewModel(ui, new AppSettings());
        var doc = new DocumentViewModel(path, RiskMatrixDocument.CreateNew(new ProjectInfo { Name = "Prueba", Company = "ACME" }), key, ui);
        vm.Documents.Add(doc);
        vm.Active = doc;
        return (vm, doc, path);
    }

    [AvaloniaFact]
    public async Task Saving_without_password_offers_one_once()
    {
        var ui = new FakeUi(acceptOffer: false);
        var (vm, doc, path) = Open(ui, FileKey.Create(null));
        try
        {
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            Assert.Equal(1, ui.Confirms);
            Assert.Empty(ui.PasswordDialogs);

            // "Ahora no": no se vuelve a preguntar en los siguientes guardados.
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal(1, ui.Confirms);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task Accepting_shows_the_password_screen_and_cancelling_keeps_the_file_saved()
    {
        var ui = new FakeUi(acceptOffer: true);
        var (vm, doc, path) = Open(ui, FileKey.Create(null));
        try
        {
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            Assert.Single(ui.PasswordDialogs);
            Assert.Equal(PasswordMode.Create, ui.PasswordDialogs[0].Mode);
            Assert.False(doc.Key.IsProtected);
            Assert.False(doc.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task Files_with_a_password_are_not_asked()
    {
        var ui = new FakeUi(acceptOffer: true);
        var (vm, _, path) = Open(ui, FileKey.CreateNew("contraseña-de-prueba"));
        try
        {
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal(0, ui.Confirms);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_file_asks_first_and_is_saved_even_without_password(bool accept)
    {
        // Al crear el archivo: primero la pregunta; si se acepta, la pantalla de contraseña (que aquí se cancela).
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"auros-nuevo-{Guid.NewGuid():N}.rmx");
        var ui = new FakeUi(accept, path);
        var vm = new MainWindowViewModel(ui, new AppSettings());
        try
        {
            await vm.NewCommand.ExecuteAsync(null);
            Assert.Equal(1, ui.Confirms);
            Assert.Equal(accept ? 1 : 0, ui.PasswordDialogs.Count);
            Assert.True(File.Exists(path));
            Assert.False(vm.Active!.Key.IsProtected);

            // Ya se preguntó al crearlo: al guardar no se vuelve a preguntar.
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal(1, ui.Confirms);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
