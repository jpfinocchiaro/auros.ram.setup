using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Model;

public static class ScaleTypeExtensions
{
    /// <summary>Las dimensiones en que se clasifica cada riesgo, en el orden en que se muestran.</summary>
    public static IReadOnlyList<ScaleType> Dimensions { get; } = [ScaleType.Impact, ScaleType.Probability];

    public static bool IsDimension(this ScaleType type) => type is ScaleType.Impact or ScaleType.Probability;

    public static string DisplayName(this ScaleType type) => type switch
    {
        ScaleType.Risk => Messages.ScaleType_Risk,
        ScaleType.Impact => Messages.ScaleType_Impact,
        ScaleType.Probability => Messages.ScaleType_Probability,
        _ => type.ToString(),
    };

    public static string DisplayName(this MitigationStatus status) => status switch
    {
        MitigationStatus.InProgress => Messages.Mitigation_InProgress,
        MitigationStatus.Implemented => Messages.Mitigation_Implemented,
        MitigationStatus.Discarded => Messages.Mitigation_Discarded,
        _ => Messages.Mitigation_Pending,
    };

    public static string DisplayName(this ProjectStatus status) => status switch
    {
        ProjectStatus.Finished => Messages.Status_Finished,
        _ => Messages.Status_InProgress,
    };
}
