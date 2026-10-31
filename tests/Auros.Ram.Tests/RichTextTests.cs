using Auros.Ram.Core.Services;
using Auros.Ram.Core.Text;

namespace Auros.Ram.Tests;

public class RichTextTests
{
    [Fact]
    public void Inline_marks_produce_runs_with_their_format()
    {
        var runs = RichText.Parse("Hay **acceso** *activo* en __tres__ {g}sistemas{/g} y {p}nota{/p}.")[0].Runs;

        Assert.Contains(runs, r => r.Text == "acceso" && r.Bold && !r.Italic);
        Assert.Contains(runs, r => r.Text == "activo" && r.Italic && !r.Bold);
        Assert.Contains(runs, r => r.Text == "tres" && r.Underline);
        Assert.Contains(runs, r => r.Text == "sistemas" && r.Size == RichSize.Large);
        Assert.Contains(runs, r => r.Text == "nota" && r.Size == RichSize.Small);
        Assert.Equal("Hay acceso activo en tres sistemas y nota.", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Marks_can_be_nested()
    {
        var runs = RichText.Parse("***muy importante***")[0].Runs;
        Assert.Single(runs);
        Assert.True(runs[0].Bold && runs[0].Italic);
        Assert.Equal("muy importante", runs[0].Text);
    }

    [Theory]
    [InlineData("2*3 = 6")]
    [InlineData("un ** suelto")]
    [InlineData("llave {g} sin cerrar")]
    [InlineData("snake__case")]
    public void Unclosed_marks_stay_as_plain_text(string text)
    {
        Assert.Equal(text, RichText.ToPlain(text));
    }

    [Fact]
    public void Lists_and_plain_text()
    {
        var blocks = RichText.Parse("Pasos:\n- uno\n- **dos**\n1. primero\n2. segundo");
        Assert.Equal([RichBlockKind.Paragraph, RichBlockKind.Bullet, RichBlockKind.Bullet, RichBlockKind.Numbered, RichBlockKind.Numbered],
            blocks.Select(b => b.Kind));
        Assert.Equal(2, blocks[4].Number);
        Assert.True(blocks[2].Runs[0].Bold);
        Assert.Equal("Pasos:\n• uno\n• dos\n1. primero\n2. segundo", RichText.ToPlain("Pasos:\n- uno\n- **dos**\n1. primero\n2. segundo"));
    }

    [Fact]
    public void Short_description_limit_counts_visible_text_only()
    {
        var doc = Sample.Document(0);
        var risk = Sample.NewRisk(doc, "r", 0, 0);

        // 500 caracteres visibles con marcas alrededor: vale.
        risk.ShortDescription = "**" + new string('x', Limits.RiskShortDescriptionMax) + "**";
        doc.AddRisk(risk);

        // Solo marcas, sin texto: es como estar vacía.
        var empty = Sample.NewRisk(doc, "vacía", 0, 0);
        empty.ShortDescription = "****";
        Assert.Throws<DomainException>(() => doc.AddRisk(empty));
    }

    [Fact]
    public void Search_ignores_format_marks()
    {
        var doc = Sample.Document(0);
        var risk = Sample.NewRisk(doc, "r", 0, 0);
        risk.LongDescription = "Política de **claves** débil";
        doc.AddRisk(risk);
        Assert.Single(RiskQuery.Apply(doc, new RiskFilter { Text = "claves débil" }));
    }
}
