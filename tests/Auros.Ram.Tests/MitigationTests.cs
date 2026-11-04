using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;
using Auros.Ram.Persistence;

namespace Auros.Ram.Tests;

/// <summary>Acciones mitigantes de cada evaluación: alta, nueva evaluación, corrección, validación y archivo.</summary>
[Collection("AppPaths")]
public class MitigationTests
{
    private static MitigationAction Action(string description, MitigationStatus status = MitigationStatus.Pending, string responsible = "", DateOnly? due = null) =>
        new(Guid.Empty, description, responsible, due, status);

    [Fact]
    public void Each_assessment_keeps_its_own_actions()
    {
        var doc = Sample.Document(0);
        var today = RiskMatrixDocument.Today;
        var risk = doc.AddRisk(Sample.NewRisk(doc, "Accesos sin control", 3, 3), today.AddDays(-30), "Inicial",
            [Action("  Revisar las bajas  ", responsible: " RR. HH. ", due: today.AddDays(10)), Action("Bloqueo automático")]);
        var first = doc.FindRisk(risk.Id)!;
        Assert.Equal(["Revisar las bajas", "Bloqueo automático"], first.CurrentActions.Select(a => a.Description));
        Assert.Equal("RR. HH.", first.CurrentActions[0].Responsible);
        Assert.All(first.CurrentActions, a => Assert.NotEqual(Guid.Empty, a.Id));

        // La nueva evaluación trae sus acciones; la anterior conserva las suyas.
        doc.AddAssessment(risk.Id, first.ImpactLevelId, first.ProbabilityLevelId, today, "Se avanzó",
            actions: [first.CurrentActions[0] with { Id = Guid.Empty, Status = MitigationStatus.Implemented }, Action("Auditoría trimestral", MitigationStatus.InProgress)]);
        var second = doc.FindRisk(risk.Id)!;
        Assert.Equal(2, second.Assessments.Count);
        Assert.Equal(MitigationStatus.Pending, second.Assessments[0].Actions[0].Status);
        Assert.Equal([MitigationStatus.Implemented, MitigationStatus.InProgress], second.CurrentActions.Select(a => a.Status));
        Assert.Single(second.CurrentActions, a => a.IsOpen);

        // Editar corrige las acciones de la evaluación vigente.
        doc.UpdateRisk(second.Clone(), [Action("Auditoría trimestral", MitigationStatus.Discarded)]);
        Assert.Equal(MitigationStatus.Discarded, Assert.Single(doc.FindRisk(risk.Id)!.CurrentActions).Status);
        Assert.Equal(2, doc.FindRisk(risk.Id)!.Assessments[0].Actions.Count);
        doc.History.Undo();
        Assert.Equal(2, doc.FindRisk(risk.Id)!.CurrentActions.Count);
    }

    [Fact]
    public void Actions_need_a_description_and_fit_the_limits()
    {
        var doc = Sample.Document(1);
        var risk = doc.Risks[0];
        Assert.Throws<DomainException>(() => doc.UpdateRisk(risk.Clone(), [Action("  ")]));
        Assert.Throws<DomainException>(() => doc.UpdateRisk(risk.Clone(), [Action(new string('x', Limits.ActionDescriptionMax + 1))]));
        Assert.Throws<DomainException>(() => doc.UpdateRisk(risk.Clone(), [Action("Ok", responsible: new string('x', Limits.ActionResponsibleMax + 1))]));
        Assert.Throws<DomainException>(() => doc.UpdateRisk(risk.Clone(), [Action("Ok", (MitigationStatus)9)]));
    }

    [Fact]
    public void Actions_survive_saving_and_json()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(0);
        var due = new DateOnly(2026, 12, 15);
        var id = doc.AddRisk(Sample.NewRisk(doc, "Con acciones", 2, 2), null, null,
            [Action("Capacitar al personal", MitigationStatus.InProgress, "Seguridad", due), Action("Sin fecha")]).Id;

        var path = dir.File("acciones.rmx");
        using (var key = FileKey.CreateNew("clave-del-archivo"))
        {
            RmxFile.Save(doc, path, key);
        }

        var opened = RmxFile.Open(path, "clave-del-archivo");
        using var _ = opened.Key;
        foreach (var copy in new[] { opened.Document, JsonExchange.Import(JsonExchange.Export(doc)) })
        {
            var actions = copy.FindRisk(id)!.CurrentActions;
            Assert.Equal(doc.FindRisk(id)!.CurrentActions, actions);
            Assert.Equal(("Seguridad", due, MitigationStatus.InProgress), (actions[0].Responsible, actions[0].DueDate, actions[0].Status));
            Assert.Null(actions[1].DueDate);
        }
    }

    [Fact]
    public void Older_files_open_without_actions()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema-v7.rmx");
        using var dir = new TempDir();
        var copy = dir.File("v7.rmx");
        File.Copy(fixture, copy);
        var result = RmxFile.Open(copy, FixtureGenerator.Password);
        using var _ = result.Key;
        Assert.All(result.Document.Risks.SelectMany(r => r.Assessments), a => Assert.Empty(a.Actions));
    }
}
