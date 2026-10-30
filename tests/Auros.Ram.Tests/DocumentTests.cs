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
        Assert.Equal(5, doc.ProjectScale(ScaleType.Impact)!.Levels.Count);
        Assert.Equal(5, doc.ProjectScale(ScaleType.Probability)!.Levels.Count);
        Assert.Empty(doc.Project.HeatMap);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Default_heat_map_is_the_classic_5x5()
    {
        var doc = Sample.Document(0);
        var m = PriorityMatrix.Build(doc);

        // Filas: impacto del mayor (arriba) al menor; columnas: probabilidad de menor a mayor.
        string LevelAt(int impact, int probability) => m.LevelAt(4 - impact, probability)!.Name;

        Assert.Equal("Bajo", LevelAt(0, 0));        // 1 × 1
        Assert.Equal("Bajo", LevelAt(1, 1));        // 2 × 2 = 4
        Assert.Equal("Medio", LevelAt(0, 4));       // 1 × 5 = 5
        Assert.Equal("Medio", LevelAt(2, 2));       // 3 × 3 = 9
        Assert.Equal("Alto", LevelAt(1, 4));        // 2 × 5 = 10
        Assert.Equal("Alto", LevelAt(3, 3));        // 4 × 4 = 16
        Assert.Equal("Crítico", LevelAt(3, 4));     // 4 × 5 = 20
        Assert.Equal("Crítico", LevelAt(4, 4));     // 5 × 5

        var counts = Enumerable.Range(0, 5).SelectMany(i => Enumerable.Range(0, 5).Select(p => LevelAt(i, p)))
            .GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(8, counts["Bajo"]);
        Assert.Equal(7, counts["Medio"]);
        Assert.Equal(7, counts["Alto"]);
        Assert.Equal(3, counts["Crítico"]);
    }

    [Fact]
    public void Risk_level_comes_from_heat_map_and_follows_its_edits()
    {
        var doc = Sample.Document(0);
        var risk = doc.AddRisk(Sample.NewRisk(doc, "r", 4, 4));
        Assert.Equal("Crítico", doc.RiskLevelOf(risk)!.Name);

        var low = Sample.Level(doc, ScaleType.Risk, 0);
        doc.SetHeatCell(risk.ImpactLevelId, risk.ProbabilityLevelId, low.Id);
        Assert.Equal("Bajo", doc.RiskLevelOf(risk)!.Name);
        Assert.Single(doc.Project.HeatMap);

        doc.History.Undo();
        Assert.Equal("Crítico", doc.RiskLevelOf(risk)!.Name);
        doc.History.Redo();
        doc.ResetHeatMap();
        Assert.Equal("Crítico", doc.RiskLevelOf(risk)!.Name);
        Assert.Empty(doc.Project.HeatMap);

        Assert.Throws<DomainException>(() => doc.SetHeatCell(Guid.NewGuid(), risk.ProbabilityLevelId, low.Id));
    }

    [Fact]
    public void Risk_requires_mandatory_fields()
    {
        var doc = Sample.Document(0);
        var risk = Sample.NewRisk(doc, "", 0, 0);
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

        risk.LongDescription = "ok";
        risk.ProbabilityLevelId = Guid.Empty;
        Assert.Equal(nameof(Risk.ProbabilityLevelId), Assert.Throws<DomainException>(() => doc.AddRisk(risk)).Field);
    }

    [Fact]
    public void Risk_level_must_belong_to_project_scale_RF11()
    {
        var doc = Sample.Document(0);
        var other = doc.AddScale(new Scale { Name = "Otra", Type = ScaleType.Impact, Levels = [new Level { Name = "X", Color = Palette.Red, Order = 1 }] });
        var risk = Sample.NewRisk(doc, "r", 0, 0);
        risk.ImpactLevelId = other.Levels[0].Id;
        var ex = Assert.Throws<DomainException>(() => doc.AddRisk(risk));
        Assert.Equal(nameof(Risk.ImpactLevelId), ex.Field);
    }

    [Fact]
    public void Scale_validation_RF02_RF03()
    {
        var doc = Sample.Document(0);
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = "Vacía", Type = ScaleType.Impact }));
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = new string('x', 151), Type = ScaleType.Impact, Levels = [new Level { Name = "a", Order = 1 }] }));
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = "s", Type = ScaleType.Impact, Levels = [new Level { Name = "a", Color = "rojo", Order = 1 }] }));
        Assert.Throws<DomainException>(() => doc.AddScale(new Scale { Name = "s", Type = ScaleType.Impact, Levels = [new Level { Name = "a", Order = 101 }] }));
        var ok = doc.AddScale(new Scale { Name = " s ", Type = ScaleType.Impact, Levels = [new Level { Name = "a", Color = "#aabbcc", Order = 0 }] });
        Assert.Equal("s", ok.Name);
        Assert.Equal("#AABBCC", ok.Levels[0].Color);
    }

    [Fact]
    public void Level_in_use_cannot_be_removed_RF05()
    {
        var doc = Sample.Document(0);
        var risk = doc.AddRisk(Sample.NewRisk(doc, "r", 0, 0));

        var impact = doc.ProjectScale(ScaleType.Impact)!.Clone();
        impact.Levels.RemoveAll(l => l.Id == risk.ImpactLevelId);
        Assert.Throws<DomainException>(() => doc.UpdateScale(impact));

        // El nivel de riesgo que le toca a un riesgo también está en uso.
        var levels = doc.ProjectScale(ScaleType.Risk)!.Clone();
        var used = doc.RiskLevelOf(risk)!.Id;
        levels.Levels.RemoveAll(l => l.Id == used);
        Assert.Throws<DomainException>(() => doc.UpdateScale(levels));

        var unused = doc.ProjectScale(ScaleType.Impact)!.Clone();
        unused.Levels.RemoveAll(l => l.Id != risk.ImpactLevelId);
        doc.UpdateScale(unused);
        Assert.Single(doc.ProjectScale(ScaleType.Impact)!.Levels);
    }

    [Fact]
    public void Level_rename_is_reflected_everywhere_RF04()
    {
        var doc = Sample.Document(1);
        var scale = doc.ProjectScale(ScaleType.Impact)!.Clone();
        scale.FindLevel(doc.Risks[0].ImpactLevelId)!.Name = "Renombrado";
        doc.UpdateScale(scale);
        Assert.Equal("Renombrado", doc.LevelOf(doc.Risks[0], ScaleType.Impact)!.Name);
        Assert.Equal("Renombrado", Reports.Summary(doc)[0].ImpactLevel.Name);
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
        p.RiskScaleId = p.ImpactScaleId;
        Assert.Throws<DomainException>(() => doc.UpdateProject(p));

        // Cambiar la escala de riesgo descarta las celdas del mapa que ya no corresponden.
        var risk = Sample.Level(doc, ScaleType.Risk, 0);
        doc.SetHeatCell(Sample.Level(doc, ScaleType.Impact, 0).Id, Sample.Level(doc, ScaleType.Probability, 0).Id, risk.Id);
        var extra = doc.AddScale(new Scale { Name = "Extra", Type = ScaleType.Risk, Levels = [new Level { Name = "a", Order = 1 }] });
        p = doc.Project.Clone();
        p.RiskScaleId = extra.Id;
        doc.UpdateProject(p);
        Assert.Equal(extra.Id, doc.ProjectScale(ScaleType.Risk)!.Id);
        Assert.Empty(doc.Project.HeatMap);

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
        var r = doc.AddRisk(Sample.NewRisk(doc, "uno", 0, 0));
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
            doc.AddRisk(Sample.NewRisk(doc, $"r{i}", 0, 0));
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
    public void Matrix_places_risks_by_impact_and_probability_RF13_RF14()
    {
        var doc = Sample.Document(0);
        var b = doc.AddRisk(Sample.NewRisk(doc, "b", 4, 4));
        var a = doc.AddRisk(Sample.NewRisk(doc, "a", 4, 4));
        doc.AddRisk(Sample.NewRisk(doc, "otro", 0, 0));

        var m = PriorityMatrix.Build(doc);
        Assert.Equal(5, m.Columns.Count);
        Assert.Equal(5, m.Rows.Count);
        Assert.Equal("Catastrófico", m.Rows[0].Name);   // el mayor impacto arriba
        Assert.Equal("Insignificante", m.Rows[^1].Name);
        Assert.Equal("Rara", m.Columns[0].Name);        // menor probabilidad a la izquierda

        Assert.Equal([a.Id, b.Id], m[0, 4].Select(r => r.Id));   // catastrófico × casi segura, por título
        Assert.Equal(2, m.MaxCellCount);
        Assert.Single(m[4, 0]);
    }

    [Fact]
    public void Matrix_size_follows_scales_RF13()
    {
        var doc = Sample.Document(0);
        var probability = doc.ProjectScale(ScaleType.Probability)!.Clone();
        probability.Levels.Add(new Level { Name = "Segura", Color = Palette.Violet, Order = 6 });
        doc.UpdateScale(probability);
        Assert.Equal(6, PriorityMatrix.Build(doc).Columns.Count);
    }

    [Fact]
    public void Reports_summary_and_distribution_RF16_RF18()
    {
        var doc = Sample.Document(0);
        doc.AddRisk(Sample.NewRisk(doc, "b", 0, 0));   // bajo
        doc.AddRisk(Sample.NewRisk(doc, "a", 3, 3));   // alto (16)
        doc.AddRisk(Sample.NewRisk(doc, "c", 4, 2));   // alto (15): mismo nivel que "a" pero mayor impacto, va primero

        var rows = Reports.Summary(doc);
        Assert.Equal([1, 2, 3], rows.Select(r => r.Number));
        Assert.Equal(["c", "a", "b"], rows.Select(r => r.Risk.Title));
        Assert.Equal("Alto", rows[0].RiskLevel.Name);

        var dist = Reports.Distribution(doc);
        Assert.Equal("Crítico", dist[0].Level.Name);
        Assert.Equal(2, dist.Single(d => d.Level.Name == "Alto").Count);
        Assert.Equal(1.0, dist.Sum(d => d.Share), 3);
    }

    [Fact]
    public void Query_filters_searches_and_sorts_RF12()
    {
        var doc = Sample.Document(0);
        doc.AddRisk(Sample.NewRisk(doc, "Accesos privilegiados", 4, 4));   // crítico
        doc.AddRisk(Sample.NewRisk(doc, "Backups", 0, 4));                 // medio, la más probable
        var r = doc.AddRisk(Sample.NewRisk(doc, "Contraseñas", 3, 2));     // alto
        var edit = r.Clone();
        edit.LongDescription = "Política de CLAVES débil";
        doc.UpdateRisk(edit);

        Assert.Equal(["Accesos privilegiados", "Contraseñas", "Backups"], RiskQuery.Apply(doc, new RiskFilter()).Select(x => x.Title));
        Assert.Equal(["Backups", "Contraseñas", "Accesos privilegiados"], RiskQuery.Apply(doc, new RiskFilter { Descending = false }).Select(x => x.Title));
        Assert.Equal(["Contraseñas"], RiskQuery.Apply(doc, new RiskFilter { Text = "claves" }).Select(x => x.Title));
        Assert.Equal(["Contraseñas"], RiskQuery.Apply(doc, new RiskFilter { RiskLevelId = Sample.Level(doc, ScaleType.Risk, 2).Id }).Select(x => x.Title));
        Assert.Equal(["Accesos privilegiados"], RiskQuery.Apply(doc, new RiskFilter { ImpactLevelId = Sample.Level(doc, ScaleType.Impact, 4).Id }).Select(x => x.Title));
        Assert.Equal("Contraseñas", RiskQuery.Apply(doc, new RiskFilter { SortBy = RiskSortField.Probability, Descending = false }).First().Title);
        Assert.Equal("Accesos privilegiados", RiskQuery.Apply(doc, new RiskFilter { SortBy = RiskSortField.Title, Descending = false }).First().Title);
    }

    [Fact]
    public void Level_text_color_is_validated_and_contrast_is_measured()
    {
        var doc = Sample.Document(0);
        var scale = doc.ProjectScale(ScaleType.Risk)!.Clone();
        scale.Levels[0].TextColor = "blanco";
        Assert.Equal(nameof(Level.TextColor), Assert.Throws<DomainException>(() => doc.UpdateScale(scale)).Field);

        Assert.Equal(21, Palette.ContrastRatio(Palette.White, Palette.Black), 1);
        Assert.True(Palette.ContrastRatio(Palette.Yellow, Palette.White) < 4.5);
        Assert.All(DefaultScales.Create().SelectMany(s => s.Levels), l => Assert.Equal(Palette.ContrastingText(l.Color), l.TextColor));
    }

    [Fact]
    public void Json_roundtrip_RNF63()
    {
        var doc = Sample.Document(5);
        var p = doc.Project.Clone();
        p.CompanyLogo = [1, 2, 3];
        doc.UpdateProject(p);
        var scale = doc.ProjectScale(ScaleType.Risk)!.Clone();
        scale.Levels[0].TextColor = "#123456";
        doc.UpdateScale(scale);
        var cellRisk = doc.Risks[0];
        doc.SetHeatCell(cellRisk.ImpactLevelId, cellRisk.ProbabilityLevelId, Sample.Level(doc, ScaleType.Risk, 3).Id);

        var json = JsonExchange.Export(doc);
        var back = JsonExchange.Import(json);
        Assert.Equal(doc.Risks.Select(r => r.Title), back.Risks.Select(r => r.Title));
        Assert.Equal(doc.Risks.Select(r => (r.ImpactLevelId, r.ProbabilityLevelId)), back.Risks.Select(r => (r.ImpactLevelId, r.ProbabilityLevelId)));
        Assert.Equal("Cliente S.A.", back.Project.Company);
        Assert.Equal(doc.Project.CompanyLogo, back.Project.CompanyLogo);
        Assert.Equal(doc.Project.HeatMap, back.Project.HeatMap);
        Assert.Equal("Crítico", back.RiskLevelOf(back.Risks[0])!.Name);
        Assert.Equal("#123456", back.FindScale(scale.Id)!.FindLevel(scale.Levels[0].Id)!.TextColor);
        Assert.Equal(doc.Scales.Count, back.Scales.Count);
        Assert.Throws<DomainException>(() => JsonExchange.Import("{\"format\":\"otro\"}"));
        Assert.Throws<DomainException>(() => JsonExchange.Import("no es json"));
    }

    [Fact]
    public void Json_from_trial_versions_is_rejected_with_a_clear_message()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonExchange.Export(Sample.Document(1)))!.AsObject();
        node["formatVersion"] = 2;
        var ex = Assert.Throws<DomainException>(() => JsonExchange.Import(node.ToJsonString()));
        Assert.Contains("versión de prueba", ex.Message, StringComparison.Ordinal);
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
