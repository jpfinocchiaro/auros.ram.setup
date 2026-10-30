using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Model;

public static class ScaleTypeExtensions
{
    public static string DisplayName(this ScaleType type) => type switch
    {
        ScaleType.Risk => Messages.ScaleType_Risk,
        ScaleType.Value => Messages.ScaleType_Value,
        ScaleType.Difficulty => Messages.ScaleType_Difficulty,
        _ => type.ToString(),
    };

    public static string DisplayName(this ProjectStatus status) => status switch
    {
        ProjectStatus.Finished => Messages.Status_Finished,
        _ => Messages.Status_InProgress,
    };
}
