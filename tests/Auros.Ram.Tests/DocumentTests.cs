using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;

namespace Auros.Ram.Tests;

public class DocumentTests
{
    [Fact]
    public void New_document_has_standard_scales_assigned()
    {
        var doc = RiskMatrixDocument.CreateNew();
        Assert.Equal(3, doc.Scales.Count);
        Assert.Equal(4, doc.ProjectScale(ScaleType.Risk)!.Levels.Count);
        Assert.Equal(3, doc.ProjectScale(ScaleType.Value)!.Levels.Count);
        Assert.Equal(3, doc.ProjectScale(ScaleType.Difficulty)!.Levels.Count);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Risk_requires_mandatory_fields()
    {
        var doc = Sample.Document(0);
        var risk = Sample.NewRisk(doc, "", 0, 0, 0);
        var ex = Assert.Throws<DomainException>(() => doc.AddRisk(risk));
        Assert.Equal(nameof(Risk.Title), ex.Field);

        risk.Title = new string('x', Limits.RiskTitleMax + 1);
        Assert.Throws<DomainException>(() => doc.AddRisk(risk));

        risk.Title = "ok";
        risk.ShortDescription = new string('x', Limits.RiskShortDescriptionMax + 1);
        Assert.Throws<DomainException>(() => doc.AddRisk(risk));

        risk.ShortDescription = "ok";
        risk.LongDescription = " ";
        Assert.Throws<DomainException>(() => doc.AddRisk(risk));
    }

    [Fact]
    public void Risk_level_must_belong_to_project_scale_RF11()
    {
        var doc = Sample.Document(0);
        var other = doc.AddScale(new Scale { Name = "Otra", Type = ScaleType.Risk, Levels = [new Level { Name = "X", Color = Palette.Red, Order = 1 }] });
        var risk = Sample.NewRisk(doc, "r", 0, 0, 0);
        risk.RiskLevelId = other.Levels[0].Id;
        var ex = Assert.Throws<DomainException>(() => doc.AddRisk(risk));
        Assert.Equal(nameof(Risk.RiskLevelId), ex.Field);
    }

    [Fact]
    public void Scale_validation_RF02_RF03()
    {
        var doc = Sample.Document(0);
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = "Vacía", Type = ScaleType.Value }));
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = new string('x', 151), Type = ScaleType.Value, Levels = [new Level { Name = "a", Order = 1 }] }));
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = "s", Type = ScaleType.Value, Levels = [new Level { Name = "a", Color = "rojo", Order = 1 }] }));
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = "s", Type = ScaleType.Value, Levels = [new Level { Name = "a", Order = 101 }] }));
        var ok = doc.AddScale(new Scale { Name = " s ", Type = ScaleType.Value, Levels = [new Level { Name = "a", Color = "#aabbcc", Order = 0 }] });
        Assert.Equal("s", ok.Name);
        Assert.Equal("#AABBCC", ok.Levels[0].Color);
    }

    [Fact]
    public void Level_in_use_cannot_be_removed_RF05()
    {
        var doc = Sample.Document(1);
        var scale = doc.ProjectScale(ScaleType.Risk)!.Clone();
        var used = doc.Risks[0].RiskLevelId;
        scale.Levels.RemoveAll(l => l.Id == used);
        Assert.Throws<DomainException>(() => doc.UpdateScale(scale));

        var unused = doc.ProjectScale(ScaleType.Risk)!.Clone();
        unused.Levels.RemoveAll(l => l.Id != used);
        doc.UpdateScale(unused);
        Assert.Single(doc.ProjectScale(ScaleType.Risk)!.Levels);
    }

    [Fact]
    public void Level_rename_is_reflected_everywhere_RF04()
    {
        var doc = Sample.Document(1);
        var scale = doc.ProjectScale(ScaleType.Risk)!.Clone();
        scale.FindLevel(doc.Risks[0].RiskLevelId)!.Name = "Renombrado";
        doc.UpdateScale(scale);
        Assert.Equal("Renombrado", doc.LevelOf(doc.Risks[0], ScaleType.Risk)!.Name);
        Assert.Equal("Renombrado", Reports.Summary(doc)[0].RiskLevel.Name);
    }

    [Fact]
    public void Assigned_scale_cannot_be_deleted_RF05()
    {
        var doc = Sample.Document(0);
        Assert.Throws<DomainException>(() => doc.RemoveScale(doc.Project.RiskScaleId!.Value));
        var extra = doc.AddScale(new Scale { Name = "Extra", Type = ScaleType.Risk, Levels = [new Level { Name = "a", Order = 1 }] });
        doc.RemoveScale(extra.Id);
        Assert.Equal(3, doc.Scales.Count);
    }

    [Fact]
    public void Project_scales_wrong_type_rejected_RF08_and_locked_with_risks_RF09()
    {
        var doc = Sample.Document(0);
        var p = doc.Project.Clone();
        p.RiskScaleId = p.ValueScaleId;
        Assert.Throws<DomainException>(() => doc.UpdateProject(p));

        var extra = doc.AddScale(new Scale { Name = "Extra", Type = ScaleType.Risk, Levels = [new Level { Name = "a", Order = 1 }] });
        p = doc.Project.Clone();
        p.RiskScaleId = extra.Id;
        doc.UpdateProject(p);
        Assert.Equal(extra.Id, doc.ProjectScale(ScaleType.Risk)!.Id);

        var withRisks = Sample.Document(1);
        var extra2 = withRisks.AddScale(new Scale { Name = "Extra", Type = ScaleType.Risk, Levels = [new Level { Name = "a", Order = 1 }] });
        p = withRisks.Project.Clone();
        p.RiskScaleId = extra2.Id;
        Assert.False(withRisks.CanChangeProjectScales);
        Assert.Throws<DomainException>(() => withRisks.UpdateProject(p));
    }

    [Fact]
    public void Undo_redo_and_dirty_tracking_RNF28()
    {
        var doc = Sample.Document(0);
        Assert.False(doc.IsDirty);
        var r = doc.AddRisk(Sample.NewRisk(doc, "uno", 0, 0, 0));
        Assert.True(doc.IsDirty);
        doc.History.Undo();
        Assert.Empty(doc.Risks);
        Assert.False(doc.IsDirty);
        doc.History.Redo();
        Assert.Equal(r.Id, doc.Risks[0].Id);

        var edited = r.Clone();
        edited.Title = "dos";
        doc.UpdateRisk(edited);
        doc.RemoveRisk(r.Id);
        doc.History.Undo();
        Assert.Equal("dos", doc.Risks[0].Title);
        doc.History.Undo();
        Assert.Equal("uno", doc.Risks[0].Title);
    }

    [Fact]
    public void Undo_keeps_at_least_50_levels()
    {
        var doc = Sample.Document(0);
        for (var i = 0; i < 60; i++)
        {
            doc.AddRisk(Sample.NewRisk(doc, $"r{i}", 0, 0, 0));
        }

        var undone = 0;
        while (doc.History.CanUndo)
        {
            doc.History.Undo();
            undone++;
        }

        Assert.True(undone >= 50);
        Assert.Empty(doc.Risks);
    }

    [Fact]
    public void Matrix_places_risks_and_orders_cells_RF13_RF14()
    {
        var doc = Sample.Document(0);
        var low = doc.AddRisk(Sample.NewRisk(doc, "bajo", 0, 2, 0));
        var crit = doc.AddRisk(Sample.NewRisk(doc, "crítico", 3, 2, 0));
        doc.AddRisk(Sample.NewRisk(doc, "otro", 1, 0, 2));

        var m = PriorityMatrix.Build(doc);
        Assert.Equal(3, m.Columns.Count);
        Assert.Equal(3, m.Rows.Count);
        Assert.Equal("Difícil", m.Rows[0].Name);   // la más difícil arriba
        Assert.Equal("Fácil", m.Rows[^1].Name);    // la más fácil abajo
        Assert.Equal("Bajo", m.Columns[0].Name);   // menor valor a la izquierda

        var cell = m[2, 2];                         // fácil × valor alto
        Assert.Equal([crit.Id, low.Id], cell.Select(r => r.Id));
        Assert.Equal(2, m.MaxCellCount);
        Assert.Single(m[0, 0]);
    }

    [Fact]
    public void Matrix_size_follows_scales_RF13()
    {
        var doc = Sample.Document(0);
        var value = doc.ProjectScale(ScaleType.Value)!.Clone();
        value.Levels.Add(new Level { Name = "Muy alto", Color = Palette.Violet, Order = 4 });
        doc.UpdateScale(value);
        Assert.Equal(4, PriorityMatrix.Build(doc).Columns.Count);
    }

    [Fact]
    public void Reports_summary_and_distribution_RF16_RF18()
    {
        var doc = Sample.Document(0);
        doc.AddRisk(Sample.NewRisk(doc, "b", 0, 0, 0));
        doc.AddRisk(Sample.NewRisk(doc, "a", 2, 0, 0));
        doc.AddRisk(Sample.NewRisk(doc, "c", 2, 1, 0));

        var rows = Reports.Summary(doc);
        Assert.Equal([1, 2, 3], rows.Select(r => r.Number));
        Assert.Equal(["c", "a", "b"], rows.Select(r => r.Risk.Title));

        var dist = Reports.Distribution(doc);
        Assert.Equal("Crítico", dist[0].Level.Name);
        Assert.Equal(2, dist.Single(d => d.Level.Name == "Alto").Count);
        Assert.Equal(1.0, dist.Sum(d => d.Share), 3);
    }

    [Fact]
    public void Query_filters_searches_and_sorts_RF12()
    {
        var doc = Sample.Document(0);
        doc.AddRisk(Sample.NewRisk(doc, "Accesos privilegiados", 3, 0, 0));
        doc.AddRisk(Sample.NewRisk(doc, "Backups", 1, 2, 1));
        var r = doc.AddRisk(Sample.NewRisk(doc, "Contraseñas", 2, 1, 2));
        var edit = r.Clone();
        edit.LongDescription = "Política de CLAVES débil";
        doc.UpdateRisk(edit);

        Assert.Equal(["Accesos privilegiados", "Contraseñas", "Backups"], RiskQuery.Apply(doc, new RiskFilter()).Select(x => x.Title));
        Assert.Equal(["Backups", "Contraseñas", "Accesos privilegiados"], RiskQuery.Apply(doc, new RiskFilter { Descending = false }).Select(x => x.Title));
        Assert.Equal(["Contraseñas"], RiskQuery.Apply(doc, new RiskFilter { Text = "claves" }).Select(x => x.Title));
        Assert.Equal(["Backups"], RiskQuery.Apply(doc, new RiskFilter { ValueLevelId = Sample.Level(doc, ScaleType.Value, 2).Id }).Select(x => x.Title));
        Assert.Equal("Backups", RiskQuery.Apply(doc, new RiskFilter { SortBy = RiskSortField.Value }).First().Title);
        Assert.Equal("Accesos privilegiados", RiskQuery.Apply(doc, new RiskFilter { SortBy = RiskSortField.Title, Descending = false }).First().Title);
    }

    [Fact]
    public void Json_roundtrip_RNF63()
    {
        var doc = Sample.Document(5);
        var p = doc.Project.Clone();
        p.ClientLogo = [1, 2, 3];
        doc.UpdateProject(p);

        var json = JsonExchange.Export(doc);
        var back = JsonExchange.Import(json);
        Assert.Equal(doc.Risks.Select(r => r.Title), back.Risks.Select(r => r.Title));
        Assert.Equal(doc.Project.ClientLogo, back.Project.ClientLogo);
        Assert.Equal(doc.Scales.Count, back.Scales.Count);
        Assert.Throws<DomainException>(() => JsonExchange.Import("{\"format\":\"otro\"}"));
        Assert.Throws<DomainException>(() => JsonExchange.Import("no es json"));
    }

    [Fact]
    public void Password_policy_RNF35()
    {
        Assert.False(PasswordPolicy.IsAcceptable("corta"));
        Assert.True(PasswordPolicy.IsAcceptable("doce-caracte"));
        Assert.True(PasswordPolicy.Evaluate("abc") <= PasswordStrength.Weak);
        Assert.True(PasswordPolicy.Evaluate("Tr3s-Tigres-Comen-Trigo!") >= PasswordStrength.Strong);
    }

    [Fact]
    public void Contrasting_text_color()
    {
        Assert.Equal("#000000", Palette.ContrastingText(Palette.Yellow));
        Assert.Equal("#FFFFFF", Palette.ContrastingText(Palette.Violet));
    }
}
