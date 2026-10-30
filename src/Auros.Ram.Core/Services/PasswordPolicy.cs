using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Services;

public enum PasswordStrength
{
    VeryWeak = 0,
    Weak = 1,
    Fair = 2,
    Strong = 3,
    VeryStrong = 4,
}

/// <summary>RNF-35: mínimo de 12 caracteres e indicador de fortaleza.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    public static bool IsAcceptable(string? password) => password is not null && password.Length >= MinLength;

    public static PasswordStrength Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return PasswordStrength.VeryWeak;
        }

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;

        var distinct = password.Distinct().Count();
        var score = password.Length switch
        {
            < 8 => 0,
            < MinLength => 1,
            < 16 => 2,
            < 20 => 3,
            _ => 4,
        };
        score += classes >= 3 ? 1 : 0;
        score -= classes <= 1 ? 1 : 0;
        score -= distinct < password.Length / 3 ? 1 : 0;
        if (password.Length < MinLength)
        {
            score = Math.Min(score, (int)PasswordStrength.Weak);
        }

        return (PasswordStrength)Math.Clamp(score, 0, 4);
    }

    public static string Describe(PasswordStrength strength) => strength switch
    {
        PasswordStrength.VeryWeak => Messages.Strength_VeryWeak,
        PasswordStrength.Weak => Messages.Strength_Weak,
        PasswordStrength.Fair => Messages.Strength_Fair,
        PasswordStrength.Strong => Messages.Strength_Strong,
        _ => Messages.Strength_VeryStrong,
    };
}
