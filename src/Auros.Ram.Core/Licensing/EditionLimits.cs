namespace Auros.Ram.Core.Licensing;

/// <summary>
/// Cuánto admite cada matriz según la edición: la gratuita, hasta 1 empresa y 5 riesgos; la paga (con licencia), sin
/// límites. Un archivo que ya los supera (hecho con la versión paga) se abre en la gratuita solo para consultar y exportar.
/// </summary>
public sealed record EditionLimits(int? MaxCompanies, int? MaxRisks)
{
    public static EditionLimits Unlimited { get; } = new(null, null);

    public static EditionLimits Free { get; } = new(1, 5);

    public bool IsUnlimited => MaxCompanies is null && MaxRisks is null;

    public bool TooManyCompanies(int count) => count > MaxCompanies;

    public bool TooManyRisks(int count) => count > MaxRisks;
}
