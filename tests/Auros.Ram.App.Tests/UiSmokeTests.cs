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

    [AvaloniaFact]
    public void Formatted_editor_loads_saves_and_applies_format_in_place()
    {
        // Los saltos de línea del archivo fuente dependen de cómo lo dejó git; el editor guarda siempre "\n".
        var markup = """
            Texto con **negrita**, *itálica* y __subrayado__.
            {g}Grande{/g} y {p}chica{/p}
            - uno
            - dos
            1. primero
            """.ReplaceLineEndings("\n");
        var editor = new FormattedTextEditor { Text = markup, EditorHeight = 200 };
        var window = new Window { Content = editor, Width = 600, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Ida y vuelta sin cambios: lo que se carga en el editor es lo que se guarda.
        Assert.Equal(5, editor.Box.FlowDocument.Blocks.Count);
        editor.Publish();
        Assert.Equal(markup, editor.Text);

        // Negrita aplicada directamente sobre el texto seleccionado ("Texto").
        editor.Box.FlowDocument.Select(0, 5);
        editor.Box.FlowDocument.Selection.ApplyFormatting(Avalonia.Controls.Documents.TextElement.FontWeightProperty, Avalonia.Media.FontWeight.Bold);
        editor.Publish();
        Assert.StartsWith("**Texto** con **negrita**", editor.Text, StringComparison.Ordinal);

        // Cambiar el texto desde afuera (otro riesgo) recarga el editor.
        editor.Text = "Otro *riesgo*";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Single(editor.Box.FlowDocument.Blocks);
        Assert.Equal("Otro *riesgo*", Auros.Ram.App.Views.RichDocument.Save(editor.Box.FlowDocument));
        window.Close();
    }

    [AvaloniaFact]
    public void Two_formatted_editors_keep_their_own_text()
    {
        // La descripción corta y la larga están en la misma ventana: cada editor debe tener su propio documento.
        var shortEditor = new FormattedTextEditor { Text = "Resumen **corto**" };
        var longEditor = new FormattedTextEditor { Text = "Detalle *largo*\n- con lista" };
        var window = new Window { Content = new StackPanel { Children = { shortEditor, longEditor } }, Width = 600, Height = 400 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotSame(shortEditor.Box.FlowDocument, longEditor.Box.FlowDocument);
        shortEditor.Publish();
        longEditor.Publish();
        Assert.Equal("Resumen **corto**", shortEditor.Text);
        Assert.Equal("Detalle *largo*\n- con lista", longEditor.Text);

        // Solo titila el cursor del editor donde se está escribiendo.
        longEditor.Box.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(shortEditor.CaretHidden);
        Assert.False(longEditor.CaretHidden);

        // Y sigue oculto después de reacomodarse la ventana (la capa del cursor rehace su recorte).
        window.Width = 700;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(shortEditor.CaretHidden);
        window.Close();
    }

    [AvaloniaFact]
    public void Formatted_editor_lists_keep_the_caret_after_the_bullet()
    {
        var editor = new FormattedTextEditor { Text = "Hola" };
        var window = new Window { Content = editor, Width = 600, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.Box.Focus();
        editor.Box.FlowDocument.Select(4, 0);

        // Viñeta con el cursor al final: lo que se escribe sigue al texto, no queda antes de la viñeta.
        editor.FindControl<Button>("BulletsButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("- Hola", editor.Text);
        window.KeyTextInput(" uno");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.Publish();
        Assert.Equal("- Hola uno", editor.Text);

        // Enter sigue la lista; Enter sobre un ítem vacío la termina.
        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        window.KeyTextInput("dos");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.Publish();
        Assert.Equal("- Hola uno\n- dos", editor.Text);

        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        window.KeyTextInput("fin");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.Publish();
        Assert.Equal("- Hola uno\n- dos\nfin", editor.Text);
        window.Close();
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
                LongDescription = """
                    {g}Hallazgo{/g}
                    Se detectaron **cuentas activas** de personas que ya no trabajan en la empresa.
                    *Análisis:* el acceso permite __modificar datos de clientes__.
                    Recomendación:
                    - Revisar las bajas cada mes
                    - Automatizar el bloqueo
                    1. Inventario
                    2. Depuración
                    """,
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
