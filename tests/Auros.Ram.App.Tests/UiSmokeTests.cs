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
        Shot(window, "18-evolucion");
        reportTabs.SelectedIndex = 2;
        Shot(window, "06-ficha-detallada");

        // Evolución en la matriz: el recorrido de un riesgo y la matriz a una fecha pasada.
        docVm.SelectedSection = (int)Section.Matrix;
        docVm.Matrix.PathRisk = docVm.Matrix.PathRisks.First(p => p.Title.StartsWith("Cuentas", StringComparison.Ordinal));
        Shot(window, "19-matriz-recorrido");
        docVm.Matrix.AsOfDate = DateTime.Today.AddDays(-100);
        Shot(window, "20-matriz-a-fecha");
        docVm.Matrix.AsOfDate = null;
        docVm.Matrix.PathRisk = docVm.Matrix.PathRisks[0];

        // Preferencia: matriz sin abreviaturas de nivel.
        docVm.SelectedSection = (int)Section.Matrix;
        DisplayOptions.Current.ShowLevelAbbreviations = false;
        Shot(window, "16-matriz-sin-abreviaturas");
        DisplayOptions.Current.ShowLevelAbbreviations = true;

        docVm.Lock();
        Shot(window, "07-bloqueado");
        docVm.IsLocked = false;

        ShowDialog(new RiskEditorWindow(new RiskEditorViewModel(doc, doc.Risks[0])), "08-editar-riesgo");
        var correcting = new RiskEditorViewModel(doc, doc.Risks[3]);
        correcting.Dimensions[1].Options[0].SelectCommand.Execute(null);   // edición con otro nivel: aviso de corrección
        ShowDialog(new RiskEditorWindow(correcting), "23-editar-corrige", scrollToEnd: true);
        ShowDialog(new RiskEditorWindow(new RiskEditorViewModel(doc, null)), "24-nuevo-riesgo");
        var reassess = new RiskEditorViewModel(doc, doc.Risks[3], mode: RiskEditorMode.Reassess);
        ShowDialog(new RiskEditorWindow(reassess), "25-nueva-evaluacion");
        reassess.Dimensions[1].Options[0].SelectCommand.Execute(null);
        reassess.AssessmentDate = DateTime.Today.AddDays(-60);   // anterior a hoy y a la última evaluación: advertencias
        ShowDialog(new RiskEditorWindow(reassess), "21-reevaluar-riesgo", scrollToEnd: true);
        ShowDialog(new RiskEditorWindow(new RiskEditorViewModel(doc, doc.Risks[0])), "22-historial-riesgo", scrollToEnd: true);
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

        // Editar con otro impacto o probabilidad corrige la evaluación vigente: no agrega otra al historial.
        var target = doc.Risks[0];
        var count = target.Assessments.Count;
        var edit = new RiskEditorViewModel(doc, target);
        Assert.True(edit.IsEdit);
        Assert.False(edit.ShowAssessment);
        edit.Dimensions[1].Options.First(o => o.Chip.Id != target.ProbabilityLevelId).SelectCommand.Execute(null);
        Assert.True(edit.IsCorrecting);
        edit.SaveCommand.Execute(null);
        Assert.Null(edit.Error);
        Assert.Equal(count, doc.FindRisk(target.Id)!.Assessments.Count);
        Assert.Equal(edit.Dimensions[1].SelectedId, doc.FindRisk(target.Id)!.Assessments[^1].ProbabilityLevelId);

        // Nueva evaluación: el cambio de probabilidad queda en el historial con su fecha y comentario.
        target = doc.FindRisk(target.Id)!;
        var reassess = new RiskEditorViewModel(doc, target, mode: RiskEditorMode.Reassess);
        Assert.True(reassess.ShowAssessment);
        Assert.False(reassess.CanEditTexts);
        reassess.Dimensions[1].Options[0].SelectCommand.Execute(null);
        reassess.AssessmentDate = DateTime.Today.AddDays(-1);
        reassess.AssessmentNote = "Se aplicó un control";
        reassess.SaveCommand.Execute(null);
        Assert.Null(reassess.Error);
        var history = doc.FindRisk(target.Id)!.Assessments;
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), history[^1].Date);
        Assert.Equal("Se aplicó un control", history[^1].Note);
        Assert.Contains(docVm.Reports.Evolution, e => e.Title == target.Title);

        // El editor muestra el historial y permite quitar la evaluación cargada por error.
        var again = new RiskEditorViewModel(doc, target);
        Assert.Equal(history.Count, again.History.Count);
        again.RemoveAssessmentCommand.Execute(again.History[0]);
        Assert.Equal(history.Count - 1, doc.FindRisk(target.Id)!.Assessments.Count);

        // Corregir la fecha de una evaluación desde su fila (la más antigua, dos días antes).
        var fix = new RiskEditorViewModel(doc, target);
        var oldest = fix.History[^1];
        oldest.Date = oldest.OriginalDate.AddDays(-2).ToDateTime(TimeOnly.MinValue);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(fix.Error);
        Assert.Equal(oldest.OriginalDate.AddDays(-2), doc.FindRisk(target.Id)!.Assessments[0].Date);
        Assert.Equal(oldest.OriginalDate.AddDays(-2), fix.History[^1].OriginalDate);

        // Fecha anterior a hoy en el alta: solo advierte si las Preferencias lo permiten; si no, avisa que no se podrá guardar.
        var past = new RiskEditorViewModel(doc, null) { AssessmentDate = DateTime.Today.AddDays(-5) };
        Assert.Equal(Auros.Ram.App.Resources.Strings.History_WarnPast, past.DateWarning);
        doc.DatePolicy = AssessmentDatePolicy.AllowAll with { AllowPastOnCreate = false };
        Assert.Equal(Auros.Ram.App.Resources.Strings.History_WarnPastNotAllowed, past.DateWarning);
        doc.DatePolicy = AssessmentDatePolicy.AllowAll;

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
        var window = new Window { Content = new StackPanel { Children = { new TextBox(), shortEditor, longEditor } }, Width = 600, Height = 400 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotSame(shortEditor.Box.FlowDocument, longEditor.Box.FlowDocument);
        shortEditor.Publish();
        longEditor.Publish();
        Assert.Equal("Resumen **corto**", shortEditor.Text);
        Assert.Equal("Detalle *largo*\n- con lista", longEditor.Text);

        // Solo titila el cursor del editor donde se está escribiendo. Antes se deja terminar la inicialización
        // diferida de la biblioteca, que al cargar mueve el foco por su cuenta.
        for (var i = 0; i < 10; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        longEditor.FocusText();
        for (var i = 0; i < 10; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        // Las ventanas que dejan abiertas otras pruebas comparten el teclado y pueden quedarse con el foco:
        // se verifica la regla (cursor visible solo con foco), no dónde terminó el foco.
        Assert.Equal(!shortEditor.IsKeyboardFocusWithin, shortEditor.CaretHidden);
        Assert.Equal(!longEditor.IsKeyboardFocusWithin, longEditor.CaretHidden);

        // Y sigue oculto después de reacomodarse la ventana (la capa del cursor rehace su recorte).
        window.Width = 700;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(!shortEditor.IsKeyboardFocusWithin, shortEditor.CaretHidden);
        window.Close();
    }

    [AvaloniaFact]
    public void Formatted_editor_starts_empty_for_a_new_risk()
    {
        // Un riesgo nuevo abre el editor sin texto: debe cargar sin errores y pasar lo que se escribe.
        var errors = new List<Exception>();
        void Collect(object? s, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            errors.Add(e.Exception);
            e.Handled = true;
        }

        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += Collect;
        try
        {
            foreach (var initial in new string?[] { "", null })
            {
                var editor = new FormattedTextEditor { Text = initial };
                var window = new Window { Content = editor, Width = 600, Height = 300 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                editor.FocusText();
                window.KeyTextInput("Hallazgo nuevo");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                editor.Publish();
                Assert.Equal("Hallazgo nuevo", editor.Text);
                window.Close();
            }

            // La ventana de un riesgo nuevo: dos editores vacíos a la vez (el caso que fallaba), se escribe y se guarda.
            var doc = SampleDocument();
            var vm = new RiskEditorViewModel(doc, null) { Title = "Hallazgo desde la ventana" };
            var dialog = new RiskEditorWindow(vm);
            dialog.Show();
            for (var i = 0; i < 10; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }

            // Terminada la carga de los editores (que toman el foco por su cuenta), el foco queda en el título.
            Assert.Equal("TitleBox", (dialog.FocusManager!.GetFocusedElement() as Control)?.Name);
            var editors = dialog.GetVisualDescendants<FormattedTextEditor>().ToList();
            Assert.Equal(2, editors.Count);
            foreach (var (editor, text) in editors.Zip(["Resumen del hallazgo", "Detalle del hallazgo"]))
            {
                editor.FocusText();
                dialog.KeyTextInput(text);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                editor.Publish();
            }

            Assert.Equal("Resumen del hallazgo", vm.ShortDescription);
            Assert.Equal("Detalle del hallazgo", vm.LongDescription);
            foreach (var d in vm.Dimensions)
            {
                d.Options[0].SelectCommand.Execute(null);
            }

            vm.SaveCommand.Execute(null);
            Assert.Null(vm.Error);
            Assert.Contains(doc.Risks, r => r.Title == "Hallazgo desde la ventana" && r.LongDescription == "Detalle del hallazgo");
            dialog.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException -= Collect;
        }

        Assert.Empty(errors);
    }

    [AvaloniaFact]
    public void Formatted_editor_lists_keep_the_caret_after_the_bullet()
    {
        var editor = new FormattedTextEditor { Text = "Hola" };
        var window = new Window { Content = editor, Width = 600, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.FocusText();
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

    private static void ShowDialog(Window dialog, string name, bool scrollToEnd = false)
    {
        dialog.Show();
        if (scrollToEnd)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            var scroll = dialog.GetVisualDescendants<ScrollViewer>().First();
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            dialog.UpdateLayout();
        }

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
        // Historial de ejemplo: todos se evaluaron hace unos meses (el último, más tarde) y algunos cambiaron después.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var n = 0;
        foreach (var (title, i, p) in risks)
        {
            var start = today.AddDays(n++ == risks.Length - 1 ? -45 : -150);
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
            }, start, "Relevamiento inicial");
        }

        void Reassess(int index, int impact, int probability, int daysAgo, string note)
        {
            var r = doc.Risks[index].Clone();
            r.ImpactLevelId = doc.ProjectScale(ScaleType.Impact)!.OrderedLevels[impact].Id;
            r.ProbabilityLevelId = doc.ProjectScale(ScaleType.Probability)!.OrderedLevels[probability].Id;
            doc.AddAssessment(r.Id, r.ImpactLevelId, r.ProbabilityLevelId, today.AddDays(-daysAgo), note);
        }

        Reassess(0, 4, 4, 110, "Se detectaron más cuentas compartidas");
        Reassess(0, 3, 2, 60, "Se aplicó un control de acceso");
        Reassess(0, 2, 1, 12, "Cuentas nominales en todos los servidores");
        Reassess(3, 3, 1, 30, "Se automatizó la instalación de parches");
        Reassess(6, 3, 3, 20, "Se amplió el alcance del sistema");

        doc.MarkSaved();
        return doc;
    }
}

internal static class VisualExtensions
{
    public static IEnumerable<T> GetVisualDescendants<T>(this Avalonia.Visual root) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
