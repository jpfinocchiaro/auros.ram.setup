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

        // Las evaluaciones del historial también usan niveles de las escalas del proyecto.
        var impact = doc.ProjectScale(ScaleType.Impact);
        var probability = doc.ProjectScale(ScaleType.Probability);
        if (risk.Assessments.Any(a => impact?.FindLevel(a.ImpactLevelId) is null || probability?.FindLevel(a.ProbabilityLevelId) is null))
        {
            throw new DomainException(Messages.Error_AssessmentLevelInvalid);
        }
    }

    public static string ScaleTypeName(ScaleType type) => type.DisplayName();

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
