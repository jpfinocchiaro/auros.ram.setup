using Auros.Ram.App;
using Auros.Ram.App.Services;
using Auros.Ram.App.ViewModels;
using Auros.Ram.App.Views;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;

[assembly: AvaloniaTestApplication(typeof(Auros.Ram.App.Tests.TestAppBuilder))]

namespace Auros.Ram.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithPlexFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .LogToDelegate(BindingErrors.Add, LogEventLevel.Warning, LogArea.Binding);
}

/// <summary>Junta los errores de enlace de datos que Avalonia informa al renderizar.</summary>
public static class BindingErrors
{
    public static List<string> All { get; } = [];

    public static void Add(string message)
    {
        lock (All)
        {
            All.Add(message);
        }
    }
}

/// <summary>
/// Pruebas de humo de la interfaz: cada pantalla se renderiza con un archivo de ejemplo sin errores
/// de enlace. Con AUROS_SCREENSHOTS=carpeta guarda una captura de cada pantalla para revisión visual.
/// </summary>
public class UiSmokeTests
{
    private static readonly string? ShotDir = Environment.GetEnvironmentVariable("AUROS_SCREENSHOTS");

    [AvaloniaFact]
    public void All_sections_and_dialogs_render_without_binding_errors()
    {
        AppPaths.UseRoot(Path.Combine(Path.GetTempPath(), "auros-ram-uitests", Guid.NewGuid().ToString("N")));
        BindingErrors.All.Clear();
        // AUROS_THEME=Dark para revisar el tema oscuro en las capturas.
        if (Environment.GetEnvironmentVariable("AUROS_THEME") == "Dark")
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }

        var window = new MainWindow { Width = 1366, Height = 768 };
        var ui = new UiService(window);
        var vm = new MainWindowViewModel(ui, new AppSettings());
        window.DataContext = vm;
        window.Show();
        Shot(window, "00-bienvenida");

        var doc = SampleDocument();
        var docVm = new DocumentViewModel(Path.Combine(Path.GetTempPath(), "Relevamiento ACME.rmx"), doc, FileKey.CreateNew("contraseña-de-prueba"), ui);
        vm.Documents.Add(docVm);
        vm.Active = docVm;

        foreach (var section in Enum.GetValues<Section>())
        {
            docVm.SelectedSection = (int)section;
            Shot(window, $"{(int)section + 1:00}-{section}");
        }

        docVm.SelectedSection = (int)Section.Reports;
        var reportTabs = window.GetVisualDescendants<TabControl>().Last();
        reportTabs.SelectedIndex = 1;
        Shot(window, "06-ficha-detallada");

        // Preferencia: matriz sin abreviaturas de nivel.
        docVm.SelectedSection = (int)Section.Matrix;
        DisplayOptions.Current.ShowLevelAbbreviations = false;
        Shot(window, "16-matriz-sin-abreviaturas");
        DisplayOptions.Current.ShowLevelAbbreviations = true;

        docVm.Lock();
        Shot(window, "07-bloqueado");
        docVm.IsLocked = false;

        ShowDialog(new RiskEditorWindow(new RiskEditorViewModel(doc, doc.Risks[0])), "08-editar-riesgo");
        ShowDialog(new ScaleEditorWindow(new ScaleEditorViewModel(doc, doc.ProjectScale(ScaleType.Risk), ScaleType.Risk)), "09-editar-escala");
        ShowDialog(new PasswordWindow(new PasswordDialogViewModel(PasswordMode.Create, "Contraseña del archivo nuevo", "matriz.rmx") { NewPassword = "Una-frase-larga-2026" }), "10-crear-contraseña");
        ShowDialog(new PasswordWindow(new PasswordDialogViewModel(PasswordMode.Create, "Contraseña del archivo nuevo", "matriz.rmx")), "10b-crear-sin-contraseña");
        ShowDialog(new NewMatrixWindow(new NewMatrixViewModel()), "11-nueva-matriz");
        ShowDialog(new ExportConfirmWindow(new ExportConfirmViewModel(true) { ProtectPdf = true }), "12-exportar");
        ShowDialog(new AboutWindow(), "13-acerca-de");
        ShowDialog(new SplashWindow(), "15-inicio");
        ShowDialog(new PreferencesWindow(new PreferencesViewModel { AutosaveMinutes = 5, LockMinutes = 15, ShowAbbreviations = true }), "17-preferencias");
        ShowDialog(new MessageWindow("Cambios sin guardar", "¿Guardar los cambios?", ["Cancelar", "No guardar", "Guardar"], 2), "14-mensaje");

        // "Value is null" son nulos transitorios (sin documento activo, plantillas aún sin árbol visual).
        var real = BindingErrors.All.Where(e => !e.Contains("Value is null", StringComparison.Ordinal)).Distinct().ToList();
        Assert.True(real.Count == 0, string.Join(Environment.NewLine, real));
    }

    [AvaloniaFact]
    public void Edits_through_viewmodels_update_every_section()
    {
        AppPaths.UseRoot(Path.Combine(Path.GetTempPath(), "auros-ram-uitests", Guid.NewGuid().ToString("N")));
        var window = new MainWindow();
        var ui = new UiService(window);
        var doc = SampleDocument();
        var docVm = new DocumentViewModel("x.rmx", doc, FileKey.CreateNew("contraseña-de-prueba"), ui);

        var editor = new RiskEditorViewModel(doc, null)
        {
            Title = "Nuevo desde la UI",
            ShortDescription = "Resumen",
            LongDescription = "Detalle",
        };
        editor.SaveCommand.Execute(null);
        Assert.NotNull(editor.Error);   // faltan los niveles (RF-11)

        foreach (var d in editor.Dimensions)
        {
            d.Options[^1].SelectCommand.Execute(null);
        }

        editor.SaveCommand.Execute(null);
        Assert.Null(editor.Error);
        Assert.Contains(docVm.Risks.Items, r => r.Title == "Nuevo desde la UI");
        Assert.Contains(docVm.Reports.Items, r => r.Title == "Nuevo desde la UI");
        Assert.Contains(docVm.Matrix.Rows.SelectMany(r => r.Cells).SelectMany(c => c.Items), i => i.Title == "Nuevo desde la UI");
        Assert.True(docVm.IsDirty);

        docVm.Risks.SearchText = "desde la UI";
        Assert.Single(docVm.Risks.Items);

        docVm.Undo();
        Assert.DoesNotContain(docVm.Reports.Items, r => r.Title == "Nuevo desde la UI");

        var scaleEditor = new ScaleEditorViewModel(doc, doc.ProjectScale(ScaleType.Risk), ScaleType.Risk);
        scaleEditor.Levels[0].Name = "Muy bajo";
        scaleEditor.SaveCommand.Execute(null);
        Assert.Null(scaleEditor.Error);
        Assert.Contains(docVm.Project.Distribution, d => d.Level.Name == "Muy bajo");

        // Doble clic en una celda de la matriz: el riesgo nuevo llega con el impacto y la probabilidad de esa celda.
        var cell = docVm.Matrix.Rows[0].Cells[^1];   // mayor impacto × mayor probabilidad
        var preset = new Risk { ImpactLevelId = cell.ImpactId, ProbabilityLevelId = cell.ProbabilityId };
        var fromCell = new RiskEditorViewModel(doc, null, preset);
        Assert.Equal(cell.ImpactId, fromCell.Dimensions[0].SelectedId);
        Assert.Equal(cell.ProbabilityId, fromCell.Dimensions[1].SelectedId);
        Assert.Equal(cell.Level!.Id, fromCell.RiskLevel!.Id);
        Assert.Equal("", fromCell.Dimensions[0].Justification);

        docVm.Project.Name = "Proyecto renombrado";
        docVm.Project.ApplyCommand.Execute(null);
        Assert.Equal("Proyecto renombrado", doc.Project.Name);
        Assert.False(docVm.Project.HasChanges);
    }

    private static void ShowDialog(Window dialog, string name)
    {
        dialog.Show();
        Shot(dialog, name);
        dialog.Close();
    }

    private static void Shot(TopLevel top, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var frame = top.CaptureRenderedFrame();
        if (ShotDir is not null && frame is not null)
        {
            Directory.CreateDirectory(ShotDir);
            frame.Save(Path.Combine(ShotDir, name + ".png"));
        }
    }

    private static RiskMatrixDocument SampleDocument()
    {
        var doc = RiskMatrixDocument.CreateNew(new ProjectInfo
        {
            Name = "Relevamiento de seguridad 2026",
            Company = "ACME S.A.",
        });
        // Impacto y probabilidad: posición del nivel, 0 = el menor.
        (string Title, int I, int P)[] risks =
        [
            ("Cuentas de administrador compartidas", 4, 3),
            ("Sin copias de seguridad fuera del sitio", 4, 1),
            ("Contraseñas sin vencimiento", 2, 3),
            ("Parches de servidores atrasados", 3, 3),
            ("Falta de plan de continuidad", 4, 0),
            ("Inventario de activos incompleto", 1, 2),
            ("Registros de auditoría desactivados", 2, 2),
            ("Accesos de ex empleados vigentes", 3, 4),
        ];
        foreach (var (title, i, p) in risks)
        {
            doc.AddRisk(new Risk
            {
                Title = title,
                ShortDescription = $"Resumen ejecutivo: {title.ToLowerInvariant()}.",
                LongDescription = "Detalle del hallazgo, análisis del impacto y recomendación de mejora.",
                ImpactLevelId = doc.ProjectScale(ScaleType.Impact)!.OrderedLevels[i].Id,
                ProbabilityLevelId = doc.ProjectScale(ScaleType.Probability)!.OrderedLevels[p].Id,
                ImpactJustification = i >= 3 ? "Expone información sensible de clientes." : "",
            });
        }

        doc.MarkSaved();
        return doc;
    }
}

internal static class VisualExtensions
{
    public static IEnumerable<T> GetVisualDescendants<T>(this Avalonia.Visual root) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
