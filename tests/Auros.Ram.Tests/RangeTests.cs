using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;
using Auros.Ram.Persistence;

namespace Auros.Ram.Tests;

/// <summary>Rangos de los niveles (monto en Impacto, porcentaje en Probabilidad) y monto y porcentaje de cada riesgo.</summary>
[Collection("AppPaths")]
public class RangeTests
{
    /// <summary>Matriz 5 × 5 con montos por nivel de impacto: 0–999,99; 1.000–4.999,99; … hasta 100.000–1.000.000.</summary>
    private static RiskMatrixDocument WithAmounts()
    {
        var doc = Sample.Document(0);
        var impact = doc.ProjectScale(ScaleType.Impact)!.Clone();
        decimal[][] ranges = [[0m, 999.99m], [1000m, 4999.99m], [5000m, 19999.99m], [20000m, 99999.99m], [100000m, 1000000m]];
        var levels = impact.OrderedLevels;
        for (var i = 0; i < levels.Count; i++)
        {
            (levels[i].RangeFrom, levels[i].RangeTo) = (ranges[i][0], ranges[i][1]);
        }

        doc.UpdateScale(impact);
        return doc;
    }

    [Fact]
    public void New_probability_scales_come_with_even_percent_ranges()
    {
        var five = DefaultScales.Create(ScalePreset.Matrix5x5).Single(s => s.Type == ScaleType.Probability).OrderedLevels;
        Assert.Equal([(0m, 20m), (20.01m, 40m), (40.01m, 60m), (60.01m, 80m), (80.01m, 100m)], five.Select(l => (l.RangeFrom!.Value, l.RangeTo!.Value)));

        var three = DefaultScales.Create(ScalePreset.Matrix3x3).Single(s => s.Type == ScaleType.Probability).OrderedLevels;
        Assert.Equal([(0m, 33.33m), (33.34m, 66.67m), (66.68m, 100m)], three.Select(l => (l.RangeFrom!.Value, l.RangeTo!.Value)));

        // El impacto no trae montos: dependen de cada empresa.
        Assert.All(DefaultScales.Create().Single(s => s.Type == ScaleType.Impact).Levels, l => Assert.False(l.HasRange));
    }

    [Fact]
    public void Ranges_are_validated()
    {
        var doc = WithAmounts();
        var impact = doc.ProjectScale(ScaleType.Impact)!;
        Assert.True(impact.OrderedLevels[1].Contains(1000m));
        Assert.True(impact.OrderedLevels[1].Contains(4999.99m));
        Assert.False(impact.OrderedLevels[1].Contains(5000m));

        void Rejects(ScaleType type, Action<IReadOnlyList<Level>> change)
        {
            var scale = doc.ProjectScale(type)!.Clone();
            change(scale.OrderedLevels);
            Assert.Throws<DomainException>(() => doc.UpdateScale(scale));
        }

        Rejects(ScaleType.Impact, l => l[1].RangeFrom = 900m);                       // se superpone con el anterior
        Rejects(ScaleType.Impact, l => l[0].RangeTo = null);                         // incompleto
        Rejects(ScaleType.Impact, l => (l[0].RangeFrom, l[0].RangeTo) = (10m, 5m));   // desde > hasta
        Rejects(ScaleType.Probability, l => l[4].RangeTo = 120m);                    // más de 100 %
        Rejects(ScaleType.Risk, l => (l[0].RangeFrom, l[0].RangeTo) = (0m, 1m));      // el nivel de riesgo no tiene rangos

        // Se guardan con 2 decimales.
        var rounded = doc.ProjectScale(ScaleType.Impact)!.Clone();
        rounded.OrderedLevels[4].RangeTo = 1000000.456m;
        doc.UpdateScale(rounded);
        Assert.Equal(1000000.46m, doc.ProjectScale(ScaleType.Impact)!.OrderedLevels[4].RangeTo);
    }

    [Fact]
    public void Amount_and_percent_are_kept_in_the_risk_and_in_each_assessment()
    {
        var doc = WithAmounts();
        var risk = Sample.NewRisk(doc, "Fraude", 1, 2);
        risk.ImpactAmount = 2500.555m;
        risk.ProbabilityPercent = 45m;
        var added = doc.AddRisk(risk, RiskMatrixDocument.Today.AddDays(-10));
        Assert.Equal(2500.56m, added.ImpactAmount);
        Assert.Equal(2500.56m, added.Assessments[0].ImpactAmount);
        Assert.Equal(45m, added.Assessments[0].ProbabilityPercent);

        // Nueva evaluación con otros valores; la anterior conserva los suyos.
        doc.AddAssessment(added.Id, Sample.Level(doc, ScaleType.Impact, 3).Id, added.ProbabilityLevelId,
            RiskMatrixDocument.Today, "Creció la exposición", impactAmount: 25000m, probabilityPercent: 50m);
        var now = doc.FindRisk(added.Id)!;
        Assert.Equal(25000m, now.ImpactAmount);
        Assert.Equal(2500.56m, now.Assessments[0].ImpactAmount);

        // Editar el monto corrige la evaluación vigente, sin agregar otra.
        var edit = now.Clone();
        edit.ImpactAmount = 26000m;
        doc.UpdateRisk(edit);
        now = doc.FindRisk(added.Id)!;
        Assert.Equal(2, now.Assessments.Count);
        Assert.Equal(26000m, now.Assessments[^1].ImpactAmount);

        // Valores fuera de rango.
        edit = now.Clone();
        edit.ImpactAmount = -1m;
        Assert.Throws<DomainException>(() => doc.UpdateRisk(edit));
        edit.ImpactAmount = 1m;
        edit.ProbabilityPercent = 101m;
        Assert.Throws<DomainException>(() => doc.UpdateRisk(edit));
    }

    [Fact]
    public void The_preferences_can_require_the_amount_or_the_percent()
    {
        var doc = WithAmounts();
        doc.EntryPolicy = RiskEntryPolicy.Default with { Impact = EntryMode.Value };
        var risk = Sample.NewRisk(doc, "Sin monto", 0, 0);
        Assert.Equal(nameof(Risk.ImpactAmount), Assert.Throws<DomainException>(() => doc.AddRisk(risk)).Field);
        risk.ImpactAmount = 10m;
        doc.AddRisk(risk);

        doc.EntryPolicy = RiskEntryPolicy.Default with { Probability = EntryMode.Value };
        var other = Sample.NewRisk(doc, "Sin porcentaje", 0, 0);
        Assert.Equal(nameof(Risk.ProbabilityPercent), Assert.Throws<DomainException>(() => doc.AddRisk(other)).Field);
    }

    [Fact]
    public void Ranges_and_amounts_survive_saving_and_the_json_round_trip()
    {
        using var dir = new TempDir();
        var doc = WithAmounts();
        var risk = Sample.NewRisk(doc, "Fraude", 1, 2);
        (risk.ImpactAmount, risk.ProbabilityPercent) = (2500m, 45.5m);
        var id = doc.AddRisk(risk).Id;

        var path = dir.File("rangos.rmx");
        using (var key = FileKey.CreateNew("clave-del-archivo"))
        {
            RmxFile.Save(doc, path, key);
        }

        var opened = RmxFile.Open(path, "clave-del-archivo");
        using var _ = opened.Key;
        foreach (var copy in new[] { opened.Document, JsonExchange.Import(JsonExchange.Export(doc)) })
        {
            Assert.Equal((1000m, 4999.99m), (copy.ProjectScale(ScaleType.Impact)!.OrderedLevels[1].RangeFrom!.Value,
                copy.ProjectScale(ScaleType.Impact)!.OrderedLevels[1].RangeTo!.Value));
            var r = copy.FindRisk(id)!;
            Assert.Equal((2500m, 45.5m), (r.ImpactAmount!.Value, r.ProbabilityPercent!.Value));
            Assert.Equal(2500m, r.Assessments[0].ImpactAmount);
        }
    }
}
