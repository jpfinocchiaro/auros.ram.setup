using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core.Text;

namespace Auros.Ram.App.Tests;

/// <summary>
/// Idiomas: archivo para traducir, lectura, revisión y aplicación de un idioma. Con AUROS_WRITE_LANGUAGE=archivo
/// guarda el archivo base (en español) para traducir.
/// </summary>
[Collection("Languages")]
public class LanguageTests
{
    public LanguageTests() => Languages.Startup(null);

    [Fact]
    public void Writes_the_base_file_when_asked()
    {
        var target = Environment.GetEnvironmentVariable("AUROS_WRITE_LANGUAGE");
        if (!string.IsNullOrEmpty(target))
        {
            File.WriteAllText(target, Localization.Template("", ""), new System.Text.UTF8Encoding(true));
        }
    }

    [Fact]
    public void The_template_has_every_text_by_id_and_reads_back()
    {
        var file = Localization.Parse(Localization.Template("xx", "Prueba"));
        Assert.Equal(("xx", "Prueba"), (file.Code, file.Name));
        Assert.Equal(["Strings", "Messages", "ExportText", "FileMessages"], file.Sections.Keys);
        Assert.Equal(Strings.Org_Title, file.Get("Strings", "Org_Title"));
        Assert.Equal(Strings.Language_Missing.Replace("\r\n", "\n"), file.Get("Strings", "Language_Missing"));   // con salto de línea
        var check = Localization.Check(file);
        Assert.True(check.IsComplete);
        Assert.Equal(check.Total, check.Translated);
    }

    [Fact]
    public void A_language_replaces_texts_by_id_and_keeps_spanish_where_it_cannot()
    {
        var spanishTitle = Strings.Org_Title;
        var spanishUsage = Strings.Catalog_Usage;
        var spanishSave = Strings.Common_Save;
        var file = Localization.Parse("""
            [Idioma]
            Codigo=en
            Nombre=English
            [Strings]
            Org_Title=Companies and business units
            Catalog_Usage=used by risks
            Inexistente=Nothing
            [Messages]
            Default_Unit=General unit
            """);

        var check = Localization.Check(file);
        Assert.Contains("Strings.Catalog_Usage", check.BadPlaceholders);   // perdió el {0}
        Assert.Contains("Strings.Inexistente", check.Unknown);
        Assert.Contains("Strings.Common_Save", check.Missing);

        try
        {
            Localization.Apply(file);
            Assert.Equal("Companies and business units", Strings.Org_Title);
            Assert.Equal(spanishUsage, Strings.Catalog_Usage);
            Assert.Equal(spanishSave, Strings.Common_Save);
            Assert.Equal("General unit", Auros.Ram.Core.Resources.Messages.Default_Unit);
        }
        finally
        {
            Localization.Apply(null);
        }

        Assert.Equal(spanishTitle, Strings.Org_Title);
    }

    [Theory]
    [InlineData("en", "11/1/2026", "1,234.50")]
    [InlineData("pt", "01/11/2026", "1.234,50")]
    [InlineData("fr", "01/11/2026", "1 234,50")]
    public void Dates_and_numbers_follow_the_language(string code, string date, string number)
    {
        var system = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            Localization.Apply(Languages.Load(code));
            Assert.Equal(date, new DateOnly(2026, 11, 1).ToString("d", System.Globalization.CultureInfo.CurrentCulture));
            Assert.Equal(number, 1234.5m.ToString("N2", System.Globalization.CultureInfo.CurrentCulture));
        }
        finally
        {
            Localization.Apply(null);
        }

        Assert.Equal(system.Name, System.Globalization.CultureInfo.CurrentCulture.Name);   // en español, el formato de Windows
    }

    [Fact]
    public void Standard_scale_names_are_translated_and_modified_ones_kept()
    {
        var doc = Core.RiskMatrixDocument.CreateNew(scales: Core.Model.DefaultScales.Create(Core.Model.ScalePreset.Matrix7x7));
        var impact = doc.ProjectScale(Core.Model.ScaleType.Impact)!.Clone();
        impact.OrderedLevels[0].Name = "Sin efecto";   // modificado por el usuario
        doc.UpdateScale(impact);
        try
        {
            Localization.Apply(Languages.Load("en"));
            Assert.True(doc.RenameStandardNames(Languages.StandardNames()) > 0);
            Assert.Equal("Impact (7 levels)", doc.ProjectScale(Core.Model.ScaleType.Impact)!.Name);
            Assert.Equal(["Sin efecto", "Low", "Fairly low", "Moderate", "Fairly high", "High", "Very high"],
                doc.ProjectScale(Core.Model.ScaleType.Impact)!.OrderedLevels.Select(l => l.Name));
            Assert.Equal(["Low", "Medium", "High"], doc.ProjectScale(Core.Model.ScaleType.Risk)!.OrderedLevels.Select(l => l.Name));
            Assert.Equal(0, doc.RenameStandardNames(Languages.StandardNames()));   // ya está en inglés

            // De inglés a portugués, y se puede deshacer.
            Localization.Apply(Languages.Load("pt"));
            doc.RenameStandardNames(Languages.StandardNames());
            Assert.Equal("Muito alto", doc.ProjectScale(Core.Model.ScaleType.Impact)!.OrderedLevels[^1].Name);
            doc.History.Undo();
            Assert.Equal("Very high", doc.ProjectScale(Core.Model.ScaleType.Impact)!.OrderedLevels[^1].Name);
        }
        finally
        {
            Localization.Apply(null);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("fr")]
    public void Included_languages_translate_every_text(string code)
    {
        var file = Languages.Load(code);
        Assert.NotNull(file);
        Assert.Equal(code, file.Code);
        var check = Localization.Check(file);
        Assert.Empty(check.Missing);
        Assert.Empty(check.BadPlaceholders);
        Assert.Empty(check.Unknown);
        Assert.Contains(Languages.List(), l => l.Code == code && l.Name == file.Name);
    }
}

[CollectionDefinition("Languages", DisableParallelization = true)]
public class LanguagesCollection;
