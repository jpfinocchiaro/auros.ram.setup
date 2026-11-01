using System.Globalization;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Resources;
using Auros.Ram.Core.Text;

namespace Auros.Ram.Core.Services;

/// <summary>Límites de los campos (RF-02, RF-03, F3).</summary>
public static class Limits
{
    public const int ScaleNameMax = 150;
    public const int LevelNameMax = 80;
    public const int LevelOrderMin = 0;
    public const int LevelOrderMax = 100;
    public const int RiskTitleMax = 200;
    public const int RiskShortDescriptionMax = 500;
    public const int ProjectTextMax = 200;
    public const int CatalogNameMax = 80;
}

internal static class Validation
{
    public static string Format(string template, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, template, args);

    public static void ValidateScale(Scale scale)
    {
        if (string.IsNullOrWhiteSpace(scale.Name))
        {
            throw new DomainException(Messages.Error_ScaleNameRequired, nameof(Scale.Name));
        }

        if (scale.Name.Length > Limits.ScaleNameMax)
        {
            throw new DomainException(Format(Messages.Error_ScaleNameTooLong, Limits.ScaleNameMax), nameof(Scale.Name));
        }

        if (!Enum.IsDefined(scale.Type))
        {
            throw new DomainException(Messages.Error_ScaleTypeInvalid, nameof(Scale.Type));
        }

        if (scale.Levels.Count == 0)
        {
            throw new DomainException(Messages.Error_ScaleNeedsLevel, nameof(Scale.Levels));
        }

        foreach (var level in scale.Levels)
        {
            ValidateLevel(level);
        }

        if (scale.Levels.Select(l => l.Id).Distinct().Count() != scale.Levels.Count)
        {
            throw new DomainException(Messages.Error_DuplicateLevelId, nameof(Scale.Levels));
        }

        ValidateRanges(scale);
    }

    /// <summary>
    /// Rangos de los niveles: solo en Impacto (montos) y Probabilidad (0 a 100 %); cada uno con desde y hasta,
    /// desde ≤ hasta, sin valores negativos y sin superponerse con el de otro nivel.
    /// </summary>
    private static void ValidateRanges(Scale scale)
    {
        foreach (var level in scale.Levels)
        {
            if (level.RangeFrom is null && level.RangeTo is null)
            {
                continue;
            }

            if (scale.Type == ScaleType.Risk)
            {
                throw new DomainException(Messages.Error_RangeNotForRisk, nameof(Level.RangeFrom));
            }

            if (level.RangeFrom is not { } from || level.RangeTo is not { } to)
            {
                throw new DomainException(Format(Messages.Error_RangeIncomplete, level.Name), nameof(Level.RangeFrom));
            }

            if (from < 0 || from > to)
            {
                throw new DomainException(Format(Messages.Error_RangeOrder, level.Name), nameof(Level.RangeFrom));
            }

            if (scale.Type == ScaleType.Probability && to > 100)
            {
                throw new DomainException(Format(Messages.Error_RangePercent, level.Name), nameof(Level.RangeTo));
            }
        }

        var ranged = scale.Levels.Where(l => l.HasRange).OrderBy(l => l.RangeFrom).ToList();
        for (var i = 1; i < ranged.Count; i++)
        {
            if (ranged[i].RangeFrom <= ranged[i - 1].RangeTo)
            {
                throw new DomainException(Format(Messages.Error_RangeOverlap, ranged[i - 1].Name, ranged[i].Name), nameof(Level.RangeFrom));
            }
        }
    }

    public static void ValidateLevel(Level level)
    {
        if (string.IsNullOrWhiteSpace(level.Name))
        {
            throw new DomainException(Messages.Error_LevelNameRequired, nameof(Level.Name));
        }

        if (level.Name.Length > Limits.LevelNameMax)
        {
            throw new DomainException(Format(Messages.Error_LevelNameTooLong, level.Name, Limits.LevelNameMax), nameof(Level.Name));
        }

        if (!Palette.IsValidHex(level.Color))
        {
            throw new DomainException(Format(Messages.Error_LevelColorInvalid, level.Name), nameof(Level.Color));
        }

        if (!Palette.IsValidHex(level.TextColor))
        {
            throw new DomainException(Format(Messages.Error_LevelTextColorInvalid, level.Name), nameof(Level.TextColor));
        }

        if (level.Order is < Limits.LevelOrderMin or > Limits.LevelOrderMax)
        {
            throw new DomainException(
                Format(Messages.Error_LevelOrderRange, level.Name, Limits.LevelOrderMin, Limits.LevelOrderMax),
                nameof(Level.Order));
        }
    }

    public static void ValidateProject(ProjectInfo project, RiskMatrixDocument doc)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            throw new DomainException(Messages.Error_ProjectNameRequired, nameof(ProjectInfo.Name));
        }

        CheckLength(project.Name, Messages.Field_ProjectName, nameof(ProjectInfo.Name));
        CheckLength(project.Company, Messages.Field_Company, nameof(ProjectInfo.Company));

        // RF-08 / RN-02: las tres escalas son obligatorias y del tipo correcto.
        foreach (var type in Enum.GetValues<ScaleType>())
        {
            var id = project.ScaleIdFor(type);
            var label = ScaleTypeName(type);
            if (id is null)
            {
                throw new DomainException(Format(Messages.Error_ProjectScaleRequired, label), ScaleField(type));
            }

            var scale = doc.FindScale(id.Value)
                ?? throw new DomainException(Format(Messages.Error_ProjectScaleMissing, label), ScaleField(type));
            if (scale.Type != type)
            {
                throw new DomainException(Format(Messages.Error_ProjectScaleWrongType, scale.Name, label), ScaleField(type));
            }
        }
    }

    public static void ValidateRisk(Risk risk, RiskMatrixDocument doc)
    {
        if (string.IsNullOrWhiteSpace(risk.Title))
        {
            throw new DomainException(Messages.Error_RiskTitleRequired, nameof(Risk.Title));
        }

        if (risk.Title.Length > Limits.RiskTitleMax)
        {
            throw new DomainException(Format(Messages.Error_RiskTitleTooLong, Limits.RiskTitleMax), nameof(Risk.Title));
        }

        // Los límites y la obligatoriedad se miden sobre el texto visible, sin las marcas de formato.
        var shortPlain = RichText.ToPlain(risk.ShortDescription);
        if (string.IsNullOrWhiteSpace(shortPlain))
        {
            throw new DomainException(Messages.Error_RiskShortRequired, nameof(Risk.ShortDescription));
        }

        if (shortPlain.Length > Limits.RiskShortDescriptionMax)
        {
            throw new DomainException(
                Format(Messages.Error_RiskShortTooLong, Limits.RiskShortDescriptionMax), nameof(Risk.ShortDescription));
        }

        if (string.IsNullOrWhiteSpace(RichText.ToPlain(risk.LongDescription)))
        {
            throw new DomainException(Messages.Error_RiskLongRequired, nameof(Risk.LongDescription));
        }

        // RF-11 / RN-03: impacto y probabilidad deben pertenecer a las escalas asignadas al proyecto.
        foreach (var type in ScaleTypeExtensions.Dimensions)
        {
            var label = ScaleTypeName(type);
            var scale = doc.ProjectScale(type)
                ?? throw new DomainException(Format(Messages.Error_ProjectScaleRequired, label), LevelField(type));
            var levelId = risk.LevelIdFor(type);
            if (levelId == Guid.Empty)
            {
                throw new DomainException(Format(Messages.Error_RiskLevelRequired, label), LevelField(type));
            }

            if (scale.FindLevel(levelId) is null)
            {
                throw new DomainException(Format(Messages.Error_RiskLevelNotInScale, label, scale.Name), LevelField(type));
            }
        }

        // Monto del impacto (no negativo) y probabilidad (0 a 100 %); obligatorios si así se configuró.
        if (risk.ImpactAmount is < 0)
        {
            throw new DomainException(Messages.Error_AmountNegative, nameof(Risk.ImpactAmount));
        }

        if (risk.ProbabilityPercent is < 0 or > 100)
        {
            throw new DomainException(Messages.Error_PercentRange, nameof(Risk.ProbabilityPercent));
        }

        if (doc.EntryPolicy.Impact == EntryMode.Value && risk.ImpactAmount is null)
        {
            throw new DomainException(Messages.Error_AmountRequired, nameof(Risk.ImpactAmount));
        }

        if (doc.EntryPolicy.Probability == EntryMode.Value && risk.ProbabilityPercent is null)
        {
            throw new DomainException(Messages.Error_PercentRequired, nameof(Risk.ProbabilityPercent));
        }

        // Empresa y unidad de negocio: del maestro de la matriz, y la unidad de esa empresa.
        if (doc.Project.Companies.Count > 0)
        {
            if (doc.Project.FindCompany(risk.CompanyId) is not { } company)
            {
                throw new DomainException(Messages.Error_RiskCompanyRequired, nameof(Risk.CompanyId));
            }

            if (company.Units.All(u => u.Id != risk.UnitId))
            {
                throw new DomainException(Validation.Format(Messages.Error_RiskUnitRequired, company.Name), nameof(Risk.UnitId));
            }
        }

        // Consecuencias y criterio de materialidad: del catálogo de la matriz.
        if (risk.ConsequenceIds.Any(id => doc.Project.Consequences.All(c => c.Id != id)) ||
            (risk.MaterialityId is { } m && doc.Project.MaterialityCriteria.All(c => c.Id != m)))
        {
            throw new DomainException(Messages.Error_CatalogItemNotFound);
        }

        // Las evaluaciones del historial también usan niveles de las escalas del proyecto.
        var impact = doc.ProjectScale(ScaleType.Impact);
        var probability = doc.ProjectScale(ScaleType.Probability);
        if (risk.Assessments.Any(a => impact?.FindLevel(a.ImpactLevelId) is null || probability?.FindLevel(a.ProbabilityLevelId) is null))
        {
            throw new DomainException(Messages.Error_AssessmentLevelInvalid);
        }
    }

    public static string ScaleTypeName(ScaleType type) => type.DisplayName();

    public static void ValidateCatalogName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(Messages.Error_CatalogNameRequired);
        }

        if (name.Length > Limits.CatalogNameMax)
        {
            throw new DomainException(Format(Messages.Error_CatalogNameTooLong, Limits.CatalogNameMax));
        }
    }

    private static void CheckLength(string value, string label, string field)
    {
        if (value.Length > Limits.ProjectTextMax)
        {
            throw new DomainException(Format(Messages.Error_FieldTooLong, label, Limits.ProjectTextMax), field);
        }
    }

    private static string ScaleField(ScaleType type) => type switch
    {
        ScaleType.Risk => nameof(ProjectInfo.RiskScaleId),
        ScaleType.Impact => nameof(ProjectInfo.ImpactScaleId),
        _ => nameof(ProjectInfo.ProbabilityScaleId),
    };

    private static string LevelField(ScaleType type) => type switch
    {
        ScaleType.Impact => nameof(Risk.ImpactLevelId),
        _ => nameof(Risk.ProbabilityLevelId),
    };
}
