using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;
using Auros.Ram.Persistence;

namespace Auros.Ram.Tests;

/// <summary>Historial de evaluaciones: cada cambio de impacto o probabilidad queda registrado con su fecha.</summary>
[Collection("AppPaths")]
public class HistoryTests
{
    private static readonly DateOnly Sep20 = new(2026, 9, 20);
    private static readonly DateOnly Oct30 = new(2026, 10, 30);

    /// <summary>El ejemplo del pedido: un riesgo en 3×3 con probabilidad alta e impacto bajo, que el 30/10 pasa a probabilidad media.</summary>
    private static (RiskMatrixDocument Doc, Risk Risk) Example()
    {
        var doc = RiskMatrixDocument.CreateNew(new ProjectInfo { Name = "Seguimiento", Company = "Cliente S.A." }, DefaultScales.Create(ScalePreset.Matrix3x3));
        var risk = doc.AddRisk(Sample.NewRisk(doc, "a", impact: 0, probability: 2), Sep20, "Relevamiento inicial");
        var edit = doc.FindRisk(risk.Id)!.Clone();
        edit.ProbabilityLevelId = Sample.Level(doc, ScaleType.Probability, 1).Id;
        doc.AddAssessment(edit.Id, edit.ImpactLevelId, edit.ProbabilityLevelId, Oct30, "Se aplicó un control");
        return (doc, doc.FindRisk(risk.Id)!);
    }

    [Fact]
    public void A_change_of_probability_is_recorded_with_its_date()
    {
        var (doc, risk) = Example();

        Assert.Equal(2, risk.Assessments.Count);
        Assert.Equal(Sep20, risk.Assessments[0].Date);
        Assert.Equal(Oct30, risk.Assessments[1].Date);
        Assert.Equal("Se aplicó un control", risk.Assessments[1].Note);

        // El estado actual es el de la última evaluación; el de una fecha anterior, el que regía entonces.
        Assert.Equal(Sample.Level(doc, ScaleType.Probability, 1).Id, risk.ProbabilityLevelId);
        Assert.Equal(Sample.Level(doc, ScaleType.Probability, 2).Id, risk.AssessmentAt(new DateOnly(2026, 10, 1))!.ProbabilityLevelId);
        Assert.Null(risk.AssessmentAt(new DateOnly(2026, 9, 19)));
        Assert.Equal(Sample.Level(doc, ScaleType.Impact, 0).Id, risk.AssessmentAt(Oct30)!.ImpactLevelId);
    }

    [Fact]
    public void Editing_texts_does_not_add_an_assessment_and_the_same_day_corrects_it()
    {
        var (doc, risk) = Example();
        var edit = risk.Clone();
        edit.Title = "a (renombrado)";
        doc.UpdateRisk(edit);
        Assert.Equal(2, doc.FindRisk(risk.Id)!.Assessments.Count);

        // Otra vez el mismo día: corrige la evaluación de ese día en lugar de agregar otra.
        edit = doc.FindRisk(risk.Id)!.Clone();
        edit.ImpactLevelId = Sample.Level(doc, ScaleType.Impact, 2).Id;
        doc.AddAssessment(edit.Id, edit.ImpactLevelId, edit.ProbabilityLevelId, Oct30, "Corrección");
        var history = doc.FindRisk(risk.Id)!.Assessments;
        Assert.Equal(2, history.Count);
        Assert.Equal(Sample.Level(doc, ScaleType.Impact, 2).Id, history[1].ImpactLevelId);
        Assert.Equal("Corrección", history[1].Note);
    }

    [Fact]
    public void Editing_the_levels_corrects_the_current_assessment_without_adding_one()
    {
        var (doc, risk) = Example();
        var edit = risk.Clone();
        edit.ImpactLevelId = Sample.Level(doc, ScaleType.Impact, 1).Id;
        doc.UpdateRisk(edit);

        var now = doc.FindRisk(risk.Id)!;
        Assert.Equal(2, now.Assessments.Count);
        Assert.Equal(Oct30, now.Assessments[1].Date);
        Assert.Equal("Se aplicó un control", now.Assessments[1].Note);
        Assert.Equal(edit.ImpactLevelId, now.Assessments[1].ImpactLevelId);
        Assert.Equal(edit.ImpactLevelId, now.ImpactLevelId);

        // La evaluación anterior no cambia.
        Assert.Equal(Sample.Level(doc, ScaleType.Impact, 0).Id, now.Assessments[0].ImpactLevelId);
    }

    [Fact]
    public void Snapshots_show_the_matrix_at_each_date_with_what_changed()
    {
        var (doc, risk) = Example();
        var other = doc.AddRisk(Sample.NewRisk(doc, "b", 0, 0), new DateOnly(2026, 10, 15));

        var frames = RiskHistory.Snapshots(doc, Sep20, Oct30);
        Assert.Equal([Sep20, new DateOnly(2026, 10, 15), Oct30], frames.Select(f => f.Date));

        // La primera foto es el estado al comienzo: sin cambios.
        Assert.Empty(frames[0].Changes);
        Assert.Contains(frames[0].Matrix[2, 2], r => r.Id == risk.Id);

        // El 15/10 aparece "b"; el 30/10 "a" pasa de probabilidad alta a media.
        var appeared = Assert.Single(frames[1].Changes);
        Assert.Equal(other.Id, appeared.Risk.Id);
        Assert.Null(appeared.Before);
        var moved = Assert.Single(frames[2].Changes);
        Assert.Equal(risk.Id, moved.Risk.Id);
        Assert.Equal(Sample.Level(doc, ScaleType.Probability, 2).Id, moved.Before!.Probability.Id);
        Assert.Contains(frames[2].Matrix[2, 1], r => r.Id == risk.Id);
    }

    [Fact]
    public void Future_dates_are_always_rejected()
    {
        var (doc, risk) = Example();
        var edit = risk.Clone();
        edit.ImpactLevelId = Sample.Level(doc, ScaleType.Impact, 1).Id;
        Assert.Throws<DomainException>(() => doc.AddAssessment(edit.Id, edit.ImpactLevelId, edit.ProbabilityLevelId, RiskMatrixDocument.Today.AddDays(1)));
        Assert.Throws<DomainException>(() => doc.ChangeAssessmentDate(risk.Id, risk.Assessments[0].Id, RiskMatrixDocument.Today.AddDays(1)));
        Assert.Equal(2, doc.FindRisk(risk.Id)!.Assessments.Count);
    }

    [Fact]
    public void A_date_before_a_recorded_assessment_is_interleaved_when_allowed()
    {
        var (doc, risk) = Example();
        var edit = risk.Clone();
        edit.ImpactLevelId = Sample.Level(doc, ScaleType.Impact, 2).Id;
        doc.AddAssessment(edit.Id, edit.ImpactLevelId, edit.ProbabilityLevelId, new DateOnly(2026, 10, 1), "Evaluación olvidada");

        // Queda en su lugar del historial; el estado actual sigue siendo el de la evaluación más reciente (30/10).
        var now = doc.FindRisk(risk.Id)!;
        Assert.Equal([Sep20, new DateOnly(2026, 10, 1), Oct30], now.Assessments.Select(a => a.Date));
        Assert.Equal(Sample.Level(doc, ScaleType.Impact, 0).Id, now.ImpactLevelId);
        Assert.Equal(Sample.Level(doc, ScaleType.Impact, 2).Id, now.AssessmentAt(new DateOnly(2026, 10, 15))!.ImpactLevelId);

        // Con la opción apagada en Preferencias, se rechaza.
        doc.DatePolicy = AssessmentDatePolicy.AllowAll with { AllowBeforeRecorded = false };
        edit = now.Clone();
        edit.ImpactLevelId = Sample.Level(doc, ScaleType.Impact, 1).Id;
        Assert.Throws<DomainException>(() => doc.AddAssessment(edit.Id, edit.ImpactLevelId, edit.ProbabilityLevelId, new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public void Past_dates_follow_the_preferences_for_creating_and_editing()
    {
        var (doc, risk) = Example();
        var yesterday = RiskMatrixDocument.Today.AddDays(-1);

        doc.DatePolicy = AssessmentDatePolicy.AllowAll with { AllowPastOnCreate = false };
        Assert.Throws<DomainException>(() => doc.AddRisk(Sample.NewRisk(doc, "b", 0, 0), yesterday));
        doc.AddRisk(Sample.NewRisk(doc, "b", 0, 0), RiskMatrixDocument.Today);

        doc.DatePolicy = AssessmentDatePolicy.AllowAll with { AllowPastOnEdit = false };
        var edit = doc.FindRisk(risk.Id)!.Clone();
        edit.ImpactLevelId = Sample.Level(doc, ScaleType.Impact, 2).Id;
        Assert.Throws<DomainException>(() => doc.AddAssessment(edit.Id, edit.ImpactLevelId, edit.ProbabilityLevelId, yesterday));
        Assert.Throws<DomainException>(() => doc.ChangeAssessmentDate(risk.Id, risk.Assessments[1].Id, RiskMatrixDocument.Today.AddDays(-3)));
        doc.AddAssessment(edit.Id, edit.ImpactLevelId, edit.ProbabilityLevelId, RiskMatrixDocument.Today);
        Assert.Equal(3, doc.FindRisk(risk.Id)!.Assessments.Count);
    }

    [Fact]
    public void Correcting_a_date_reorders_the_history_and_can_be_undone()
    {
        var (doc, risk) = Example();

        // La evaluación del 30/10 en realidad fue el 10/09: pasa a ser la primera y el estado actual es la del 20/09.
        doc.ChangeAssessmentDate(risk.Id, risk.Assessments[1].Id, new DateOnly(2026, 9, 10));
        var now = doc.FindRisk(risk.Id)!;
        Assert.Equal([new DateOnly(2026, 9, 10), Sep20], now.Assessments.Select(a => a.Date));
        Assert.Equal(Sample.Level(doc, ScaleType.Probability, 2).Id, now.ProbabilityLevelId);

        // No puede quedar otra evaluación el mismo día.
        Assert.Throws<DomainException>(() => doc.ChangeAssessmentDate(risk.Id, now.Assessments[0].Id, Sep20));

        doc.History.Undo();
        Assert.Equal([Sep20, Oct30], doc.FindRisk(risk.Id)!.Assessments.Select(a => a.Date));
        Assert.Equal(Sample.Level(doc, ScaleType.Probability, 1).Id, doc.FindRisk(risk.Id)!.ProbabilityLevelId);
    }

    [Fact]
    public void Removing_an_assessment_returns_to_the_previous_state_and_can_be_undone()
    {
        var (doc, risk) = Example();
        doc.RemoveAssessment(risk.Id, risk.Assessments[1].Id);
        var now = doc.FindRisk(risk.Id)!;
        Assert.Single(now.Assessments);
        Assert.Equal(Sample.Level(doc, ScaleType.Probability, 2).Id, now.ProbabilityLevelId);

        // La única evaluación no se puede quitar.
        Assert.Throws<DomainException>(() => doc.RemoveAssessment(risk.Id, now.Assessments[0].Id));

        doc.History.Undo();
        Assert.Equal(2, doc.FindRisk(risk.Id)!.Assessments.Count);
        Assert.Equal(Sample.Level(doc, ScaleType.Probability, 1).Id, doc.FindRisk(risk.Id)!.ProbabilityLevelId);
    }

    [Fact]
    public void Levels_used_in_the_history_cannot_be_removed()
    {
        var (doc, _) = Example();
        var scale = doc.ProjectScale(ScaleType.Probability)!.Clone();
        var high = Sample.Level(doc, ScaleType.Probability, 2);
        scale.Levels.RemoveAll(l => l.Id == high.Id);
        Assert.Throws<DomainException>(() => doc.UpdateScale(scale));
    }

    [Fact]
    public void Matrix_at_a_date_places_each_risk_where_it_was()
    {
        var (doc, risk) = Example();
        var lowImpact = 2;   // las filas van del impacto mayor al menor: el impacto bajo es la última fila del 3×3

        var then = PriorityMatrix.Build(doc, new DateOnly(2026, 10, 1));
        Assert.Contains(then[lowImpact, 2], r => r.Id == risk.Id);
        var now = PriorityMatrix.Build(doc);
        Assert.Contains(now[lowImpact, 1], r => r.Id == risk.Id);

        // Antes de la primera evaluación el riesgo no aparece.
        var before = PriorityMatrix.Build(doc, new DateOnly(2026, 9, 1));
        Assert.Equal(0, before.MaxCellCount);
    }

    [Fact]
    public void Evolution_compares_the_start_and_the_end_of_the_period()
    {
        var (doc, risk) = Example();
        var other = doc.AddRisk(Sample.NewRisk(doc, "b", 0, 0), new DateOnly(2026, 10, 15));

        var rows = RiskHistory.Evolution(doc, new DateOnly(2026, 10, 1), Oct30);
        var a = rows.Single(r => r.Risk.Id == risk.Id);
        Assert.Equal(RiskTrend.Better, a.Trend);
        Assert.Single(a.Changes);
        Assert.Equal(RiskTrend.New, rows.Single(r => r.Risk.Id == other.Id).Trend);

        // Fuera del período del cambio, el riesgo "a" sigue igual.
        Assert.Equal(RiskTrend.Same, RiskHistory.Evolution(doc, Sep20, new DateOnly(2026, 10, 1)).Single(r => r.Risk.Id == risk.Id).Trend);
    }

    [Fact]
    public void History_survives_saving_and_the_json_round_trip()
    {
        using var dir = new TempDir();
        var (doc, risk) = Example();
        var path = dir.File("historial.rmx");
        using (var key = FileKey.CreateNew("clave-del-archivo"))
        {
            RmxFile.Save(doc, path, key);
        }

        var opened = RmxFile.Open(path, "clave-del-archivo");
        using var _ = opened.Key;
        var reopened = opened.Document.FindRisk(risk.Id)!;
        Assert.Equal(risk.Assessments.Select(a => (a.Date, a.ImpactLevelId, a.ProbabilityLevelId, a.Note)),
            reopened.Assessments.Select(a => (a.Date, a.ImpactLevelId, a.ProbabilityLevelId, a.Note)));

        var imported = JsonExchange.Import(JsonExchange.Export(doc)).FindRisk(risk.Id)!;
        Assert.Equal(2, imported.Assessments.Count);
        Assert.Equal(risk.ProbabilityLevelId, imported.ProbabilityLevelId);
    }

    [Fact]
    public void A_version_3_json_starts_each_history_with_the_current_state()
    {
        var doc = Sample.Document(2);
        var json = JsonExchange.Export(doc)
            .Replace("\"formatVersion\": 5", "\"formatVersion\": 3", StringComparison.Ordinal);
        var v3 = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        foreach (var r in v3["risks"]!.AsArray())
        {
            r!.AsObject().Remove("assessments");
        }

        var imported = JsonExchange.Import(v3.ToJsonString());
        Assert.All(imported.Risks, r =>
        {
            Assert.Single(r.Assessments);
            Assert.Equal(r.ImpactLevelId, r.Assessments[0].ImpactLevelId);
        });
    }

    [Fact]
    public void Version_3_files_get_one_assessment_per_risk()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema-v3.rmx");
        using var dir = new TempDir();
        var copy = dir.File("v3.rmx");
        File.Copy(fixture, copy);
        var result = RmxFile.Open(copy, FixtureGenerator.Password);
        using var _ = result.Key;
        Assert.NotNull(result.BackupPath);
        Assert.All(result.Document.Risks, r =>
        {
            Assert.Single(r.Assessments);
            Assert.Equal(r.ProbabilityLevelId, r.Assessments[0].ProbabilityLevelId);
        });
    }
}
