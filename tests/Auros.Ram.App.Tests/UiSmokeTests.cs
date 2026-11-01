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
        Shot(window, "26-pelicula-inicio");
        docVm.Reports.Playback.NextCommand.Execute(null);
        docVm.Reports.Playback.NextCommand.Execute(null);
        Shot(window, "27-pelicula-cambio");
        reportTabs.SelectedIndex = 3;
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

        docVm.SelectedSection = (int)Section.Risks;
        // Riesgo con varias evaluaciones desplegado y seleccionado: sus evaluaciones quedan seleccionadas con él.
        var multi = docVm.Risks.Items.First(r => r.EntriesCount > 2);
        multi.IsExpanded = true;
        docVm.Risks.Items.First(r => r != multi && r.EntriesCount > 1).IsExpanded = true;
        docVm.Risks.Selection.Select(docVm.Risks.Rows.IndexOf(multi));
        Shot(window, "37-riesgos-jerarquicos");
        docVm.Risks.Selection.Clear();

        docVm.SelectedSection = (int)Section.Matrix;
        DisplayOptions.Current.ShowImpactAmount = true;
        Shot(window, "38-matriz-montos");
        DisplayOptions.Current.ShowImpactAmount = false;

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
        ShowDialog(new CatalogsWindow(new CatalogsViewModel(doc)), "39-catalogos");

        // Varias empresas: maestro, y las vistas filtradas por la empresa y la unidad elegidas en la barra.
        var north = doc.AddCompany("Filial Norte");
        var plant = doc.AddUnit(north.Id, "Planta Rosario");
        foreach (var r in doc.Risks.Take(3).ToList())
        {
            var moved = r.Clone();
            (moved.CompanyId, moved.UnitId) = (north.Id, plant.Id);
            doc.UpdateRisk(moved);
        }

        ShowDialog(new OrganizationsWindow(new OrganizationsViewModel(doc, ui)), "41-empresas");
        Assert.Equal(3, docVm.CompanyOptions.Count);   // "Todas" y las dos empresas
        docVm.ScopeCompany = docVm.CompanyOptions.First(o => o.Id == north.Id);
        Assert.Equal(3, docVm.Risks.Items.Count);
        docVm.ScopeUnit = docVm.UnitOptions.First(o => o.Id == north.Units[0].Id);
        Assert.Empty(docVm.Risks.Items);
        docVm.ScopeUnit = docVm.UnitOptions.First(o => o.Id == plant.Id);
        docVm.SelectedSection = (int)Section.Matrix;
        Shot(window, "42-matriz-por-empresa");
        docVm.SelectedSection = (int)Section.Risks;
        Shot(window, "43-riesgos-por-empresa");
        var scopedNew = new RiskEditorViewModel(doc, null, new Risk { CompanyId = north.Id, UnitId = plant.Id });
        Assert.Equal(("Filial Norte", "Planta Rosario"), (scopedNew.Company, scopedNew.Unit));
        ShowDialog(new RiskEditorWindow(scopedNew), "44-riesgo-empresa");
        docVm.ScopeCompany = docVm.CompanyOptions[0];
        Assert.Equal(doc.Risks.Count, docVm.Risks.Items.Count);
        ShowDialog(new RiskEditorWindow(new RiskEditorViewModel(doc, doc.Risks[0])), "40-riesgo-consecuencias", scrollTo: 430);
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

        // Película de la matriz: una foto al comienzo y una por fecha con evaluaciones; se recorre y se reproduce.
        var movie = docVm.Reports.Playback;
        Assert.True(movie.HasFrames);
        movie.FirstCommand.Execute(null);
        Assert.Equal(0, movie.Index);
        Assert.False(movie.HasChanges);
        movie.LastCommand.Execute(null);
        Assert.Equal(movie.LastIndex, movie.Index);
        Assert.True(movie.HasChanges);
        movie.Seconds = 0.5m;
        movie.PlayPauseCommand.Execute(null);   // en la última foto: vuelve a empezar y reproduce
        Assert.True(movie.IsPlaying);
        Assert.Equal(0, movie.Index);
        movie.NextCommand.Execute(null);        // navegar a mano pausa
        Assert.False(movie.IsPlaying);
        Assert.Equal(1, movie.Index);

        // Consecuencias y criterio de materialidad: del combo, o escritos (se agregan al catálogo al guardar).
        var withCatalogs = new RiskEditorViewModel(doc, doc.Risks[1]);
        var before = withCatalogs.Consequences.Count;
        withCatalogs.NewConsequence = "Pérdida de reputación";
        withCatalogs.AddConsequenceCommand.Execute(null);
        withCatalogs.NewConsequence = "Pérdida de reputación";   // repetida: no se agrega dos veces
        withCatalogs.AddConsequenceCommand.Execute(null);
        withCatalogs.NewConsequence = "Paralización de planta";
        withCatalogs.AddConsequenceCommand.Execute(null);
        withCatalogs.Materiality = "Supera el 5 % del resultado";
        withCatalogs.SaveCommand.Execute(null);
        Assert.Null(withCatalogs.Error);
        var updated = doc.FindRisk(doc.Risks[1].Id)!;
        Assert.Contains("Paralización de planta", doc.ConsequenceNames(updated));
        Assert.Equal("Supera el 5 % del resultado", doc.MaterialityName(updated));
        Assert.Contains(doc.Project.Consequences, c => c.Name == "Paralización de planta");
        Assert.True(withCatalogs.Consequences.Count <= before + 2);

        // Catálogos: renombrar y quitar desde Configuración.
        var catalogs = new CatalogsViewModel(doc);
        var row = catalogs.Consequences.Items.First(i => i.Name == "Paralización de planta");
        row.Name = "Paralización de la planta";
        Assert.True(catalogs.Close());
        Assert.Contains("Paralización de la planta", doc.ConsequenceNames(doc.FindRisk(updated.Id)!));
        catalogs = new CatalogsViewModel(doc);
        catalogs.Consequences.RemoveCommand.Execute(catalogs.Consequences.Items.First(i => i.Name == "Paralización de la planta"));
        Assert.NotNull(catalogs.Error);   // la usa un riesgo

        // Grilla jerárquica: el riesgo es la fila padre y sus evaluaciones, las hijas (al desplegarlo).
        var risks = docVm.Risks;
        risks.ClearFiltersCommand.Execute(null);
        var first = risks.Items.First(r => r.Risk.Assessments.Count > 1);
        Assert.Equal(first.Risk.Assessments.Count, first.Entries.Count);
        Assert.True(first.Entries[0].IsCurrent);
        Assert.DoesNotContain(first.Entries[0], risks.Rows);
        first.IsExpanded = true;
        var at = risks.Rows.IndexOf(first);
        Assert.Equal(first.Entries, risks.Rows.Skip(at + 1).Take(first.Entries.Count));

        // Seleccionar el riesgo selecciona todas sus evaluaciones; quitar una deja de seleccionar el riesgo entero.
        risks.Selection.Select(at);
        Assert.All(first.Entries, e => Assert.Contains(e, risks.Selection.SelectedItems));
        Assert.Equal([first], risks.SelectedRisks);
        Assert.Empty(risks.SelectedEntries);
        Assert.Same(first, risks.Selected);
        risks.Selection.Deselect(at + 1);
        Assert.Empty(risks.SelectedRisks);
        Assert.Equal(first.Entries.Count - 1, risks.SelectedEntries.Count);

        // Un riesgo seleccionado y plegado: al desplegarlo, sus evaluaciones aparecen seleccionadas.
        risks.Selection.Clear();
        first.IsExpanded = false;
        risks.Selection.Select(risks.Rows.IndexOf(first));
        first.IsExpanded = true;
        Assert.All(first.Entries, e => Assert.Contains(e, risks.Selection.SelectedItems));

        // Lo desplegado y lo seleccionado se conservan al refrescar.
        risks.Refresh();
        var refreshed = risks.Items.First(r => r.Risk.Id == first.Risk.Id);
        Assert.True(refreshed.IsExpanded);
        Assert.Contains(refreshed, risks.SelectedRisks);
        Assert.True(risks.HasSelection);
        risks.Selection.Clear();
        Assert.False(risks.HasSelection);

        // Clonar un riesgo: "Nuevo riesgo" con todo copiado; al guardar es otro riesgo con una sola evaluación.
        var original = doc.Risks.First(r => r.ConsequenceIds.Count > 0 && r.Assessments.Count > 1);
        var clone = RiskEditorViewModel.CloneOf(doc, original);
        Assert.True(clone.IsNew);
        Assert.Equal(original.Title + " (copia)", clone.Title);
        Assert.Equal(original.ImpactLevelId, clone.Dimensions[0].SelectedId);
        Assert.Equal(doc.ConsequenceNames(original), clone.Consequences.Select(c => c.Name));
        var risksBefore = doc.Risks.Count;
        clone.SaveCommand.Execute(null);
        Assert.Null(clone.Error);
        Assert.Equal(risksBefore + 1, doc.Risks.Count);
        var copy = doc.Risks.Single(r => r.Title == original.Title + " (copia)");
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Single(copy.Assessments);
        Assert.Equal((original.ImpactLevelId, original.ProbabilityLevelId, original.ImpactAmount),
            (copy.ImpactLevelId, copy.ProbabilityLevelId, copy.ImpactAmount));
        Assert.Equal(original.ConsequenceIds, copy.ConsequenceIds);
        Assert.Equal(original.LongDescription, copy.LongDescription);

        // Clonar una evaluación: "Nueva evaluación" del mismo riesgo con los valores de esa evaluación.
        var firstEval = original.Assessments[0];
        var fromEntry = RiskEditorViewModel.ReassessFrom(doc, original, firstEval);
        Assert.True(fromEntry.IsReassess);
        Assert.Equal(firstEval.ImpactLevelId, fromEntry.Dimensions[0].SelectedId);
        Assert.Equal(firstEval.ProbabilityLevelId, fromEntry.Dimensions[1].SelectedId);
        Assert.Equal(firstEval.Note, fromEntry.AssessmentNote);
        var evalsBefore = original.Assessments.Count;
        fromEntry.SaveCommand.Execute(null);
        Assert.Null(fromEntry.Error);
        var reassessed = doc.FindRisk(original.Id)!;
        Assert.Equal(evalsBefore + 1, reassessed.Assessments.Count);
        Assert.Equal(firstEval.ImpactLevelId, reassessed.ImpactLevelId);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), reassessed.Assessments[^1].Date);

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
    public void Amount_and_percent_suggest_the_level_when_automatic()
    {
        var doc = SampleDocument();
        var impact = doc.ProjectScale(ScaleType.Impact)!.Clone();
        decimal[][] ranges = [[0m, 999.99m], [1000m, 4999.99m], [5000m, 19999.99m], [20000m, 99999.99m], [100000m, 1000000m]];
        for (var i = 0; i < ranges.Length; i++)
        {
            (impact.OrderedLevels[i].RangeFrom, impact.OrderedLevels[i].RangeTo) = (ranges[i][0], ranges[i][1]);
        }

        doc.UpdateScale(impact);

        // Automático: el monto elige el nivel; se puede cambiar a mano y se advierte que no coincide.
        var editor = new RiskEditorViewModel(doc, null);
        var amount = editor.Dimensions[0];
        amount.Value = 7500m;
        Assert.Equal(impact.OrderedLevels[2].Id, amount.SelectedId);
        Assert.Null(amount.ValueWarning);
        amount.Options[4].SelectCommand.Execute(null);
        Assert.NotNull(amount.ValueWarning);
        amount.Value = 2_000_000m;   // fuera de todos los rangos
        Assert.NotNull(amount.ValueWarning);

        // Porcentaje: la escala de probabilidad trae rangos parejos (0–20, 20,01–40, …).
        var percent = editor.Dimensions[1];
        percent.Value = 55m;
        Assert.Equal(doc.ProjectScale(ScaleType.Probability)!.OrderedLevels[2].Id, percent.SelectedId);

        // Sin ajuste automático (Preferencias), el nivel no cambia solo.
        doc.EntryPolicy = RiskEntryPolicy.Default with { ImpactAuto = false, Impact = EntryMode.Value };
        var manual = new RiskEditorViewModel(doc, null);
        manual.Dimensions[0].Value = 7500m;
        Assert.Equal(Guid.Empty, manual.Dimensions[0].SelectedId);
        Assert.Contains(Auros.Ram.App.Resources.Strings.Risk_Required, manual.Dimensions[0].ValueLabel, StringComparison.Ordinal);

        // Se guarda con el monto y el porcentaje.
        manual.Title = "Con monto";
        manual.ShortDescription = "Resumen";
        manual.LongDescription = "Detalle";
        manual.Dimensions[0].Options[2].SelectCommand.Execute(null);
        manual.Dimensions[1].Value = 30m;
        manual.SaveCommand.Execute(null);
        Assert.Null(manual.Error);
        var saved = doc.Risks.Single(r => r.Title == "Con monto");
        Assert.Equal((7500m, 30m), (saved.ImpactAmount!.Value, saved.ProbabilityPercent!.Value));
        Assert.Equal(doc.ProjectScale(ScaleType.Probability)!.OrderedLevels[1].Id, saved.ProbabilityLevelId);

        var shots = new ScaleEditorViewModel(doc, doc.ProjectScale(ScaleType.Impact), ScaleType.Impact);
        ShowDialog(new ScaleEditorWindow(shots), "28-escala-montos");
        ShowDialog(new ScaleEditorWindow(new ScaleEditorViewModel(doc, doc.ProjectScale(ScaleType.Probability), ScaleType.Probability)), "29-escala-porcentajes");
        var withValues = new RiskEditorViewModel(doc, saved);
        ShowDialog(new RiskEditorWindow(withValues), "30-riesgo-con-monto", scrollToEnd: true);
        ShowDialog(new PreferencesWindow(new PreferencesViewModel { AutosaveMinutes = 5, LockMinutes = 15 }), "31-preferencias-carga");
    }

    [AvaloniaFact]
    public void Matrix_image_renders_as_png_and_bitmap()
    {
        AppPaths.UseRoot(Path.Combine(Path.GetTempPath(), "auros-ram-uitests", Guid.NewGuid().ToString("N")));
        var window = new MainWindow { Width = 1366, Height = 900 };
        var ui = new UiService(window);
        var vm = new MainWindowViewModel(ui, new AppSettings());
        window.DataContext = vm;
        window.Show();
        var docVm = new DocumentViewModel(Path.Combine(Path.GetTempPath(), "Relevamiento ACME.rmx"), SampleDocument(), FileKey.CreateNew("contraseña-de-prueba"), ui);
        vm.Documents.Add(docVm);
        vm.Active = docVm;
        docVm.SelectedSection = (int)Section.Reports;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants<TabControl>().Last();
        tabs.SelectedIndex = 3;   // Imagen de la matriz
        Shot(window, "32-imagen-matriz");

        var view = window.GetVisualDescendants<ReportsView>().Single();
        var image = view.RenderImage(2);
        Assert.NotNull(image);
        Assert.Equal(2560, image.Width);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], image.Png[..4]);

        // Mapa de bits para el portapapeles: encabezado de 40 bytes, 32 bits, filas de abajo hacia arriba.
        Assert.Equal(40, BitConverter.ToInt32(image.Dib, 0));
        Assert.Equal(image.Width, BitConverter.ToInt32(image.Dib, 4));
        Assert.Equal(image.Height, BitConverter.ToInt32(image.Dib, 8));
        Assert.Equal(32, BitConverter.ToInt16(image.Dib, 14));
        Assert.Equal(40 + (image.Width * image.Height * 4), image.Dib.Length);

        // La esquina de arriba a la izquierda es el fondo blanco (en el DIB es la última fila).
        var topLeft = 40 + ((image.Height - 1) * image.Width * 4);
        Assert.Equal([255, 255, 255], image.Dib[topLeft..(topLeft + 3)]);

        if (ShotDir is not null)
        {
            File.WriteAllBytes(Path.Combine(ShotDir, "33-imagen-exportada.png"), image.Png);
        }

        // Sin título ni leyenda la imagen es más baja.
        docVm.Reports.Image.ShowTitle = false;
        docVm.Reports.Image.ShowLegend = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var noHeader = view.RenderImage(1)!;
        Assert.True(noHeader.Height < image.Height / 2);

        // Solo el mapa de calor: sin encabezado, rótulos, ejes, riesgos ni leyenda; sin lugares vacíos.
        var options = docVm.Reports.Image;
        (options.ShowSubtitle, options.ShowLogo, options.ShowLevelLabels, options.ShowAxisNames, options.ShowRisks, options.ShowAbbreviations) =
            (false, false, false, false, false, false);
        Assert.False(options.HasHeader);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var cellsOnly = view.RenderImage(1)!;
        Assert.Equal(1280, cellsOnly.Width);
        Assert.True(cellsOnly.Height < noHeader.Height);
        Assert.All(docVm.Reports.Image.Rows.SelectMany(r => r.Cells), c => Assert.Empty(c.Items));
        if (ShotDir is not null)
        {
            File.WriteAllBytes(Path.Combine(ShotDir, "34-imagen-solo-celdas.png"), cellsOnly.Png);
            options.ShowLevelLabels = true;
            options.ShowAxisNames = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            File.WriteAllBytes(Path.Combine(ShotDir, "35-imagen-con-ejes.png"), view.RenderImage(1)!.Png);
            Shot(window, "36-imagen-opciones");
        }

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

    private static void ShowDialog(Window dialog, string name, bool scrollToEnd = false, double? scrollTo = null)
    {
        dialog.Show();
        if (scrollToEnd || scrollTo is not null)
        {
            // La ventana termina de armarse en varias pasadas (editores, historial): se desplaza hasta que se estabiliza.
            var scroll = dialog.GetVisualDescendants<ScrollViewer>().OrderByDescending(v => v.Extent.Height).First();
            for (var i = 0; i < 4; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(30);
                dialog.UpdateLayout();
                scroll.Offset = new Vector(0, scrollTo ?? scroll.Extent.Height);
                dialog.UpdateLayout();
            }
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
                ImpactAmount = (n * 7500m) + 2500m,
                ConsequenceIds = [doc.Project.Consequences[n % 3].Id, doc.Project.Consequences[3 + (n % 4)].Id],
                MaterialityId = doc.Project.MaterialityCriteria[n % 5].Id,
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
