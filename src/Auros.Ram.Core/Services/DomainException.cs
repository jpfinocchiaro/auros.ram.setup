namespace Auros.Ram.Core.Services;

/// <summary>
/// Violación de una regla de negocio. El mensaje está pensado para mostrarse
/// tal cual al usuario (RNF-47): dice qué pasó y qué puede hacer.
/// </summary>
public sealed class DomainException(string message, string? field = null) : Exception(message)
{
    /// <summary>Campo al que se refiere el error, si corresponde (para resaltarlo en pantalla).</summary>
    public string? Field { get; } = field;
}
