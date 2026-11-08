using System.Security.Cryptography;
using Auros.Ram.Core;
using Auros.Ram.Core.Licensing;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;

namespace Auros.Ram.Tests;

/// <summary>Versión gratuita (1 empresa y 5 riesgos por matriz) y licencias atadas al equipo.</summary>
public sealed class EditionTests
{
    private static readonly DateOnly Day = new(2026, 11, 7);

    private static RiskMatrixDocument FreeDocument()
    {
        var doc = RiskMatrixDocument.CreateNew(new ProjectInfo { Name = "Prueba", Company = "Auros" });
        doc.Limits = EditionLimits.Free;
        return doc;
    }

    private static Risk NewRisk(RiskMatrixDocument doc, int n) => Sample.NewRisk(doc, $"Riesgo {n}", n % 5, (n * 2) % 5);

    [Fact]
    public void Free_edition_allows_five_risks_and_one_company()
    {
        var doc = FreeDocument();
        for (var i = 1; i <= 5; i++)
        {
            doc.AddRisk(NewRisk(doc, i), Day);
        }

        var ex = Assert.Throws<DomainException>(() => doc.AddRisk(NewRisk(doc, 6), Day));
        Assert.Contains("5", ex.Message);
        Assert.Equal(5, doc.Risks.Count);
        Assert.Throws<DomainException>(() => doc.AddCompany("Otra empresa"));
        Assert.Single(doc.Project.Companies);
        Assert.False(doc.IsReadOnly);

        // Lo que ya está se puede seguir modificando y quitando.
        doc.RemoveRisk(doc.Risks[0].Id);
        doc.AddRisk(NewRisk(doc, 7), Day);
        Assert.Equal(5, doc.Risks.Count);
    }

    [Fact]
    public void Matrix_over_the_limits_is_read_only_until_licensed()
    {
        var doc = RiskMatrixDocument.CreateNew(new ProjectInfo { Name = "Prueba", Company = "Auros" });
        for (var i = 1; i <= 7; i++)
        {
            doc.AddRisk(NewRisk(doc, i), Day);
        }

        doc.AddCompany("Segunda");
        doc.Limits = EditionLimits.Free;
        Assert.True(doc.IsReadOnly);
        Assert.Throws<DomainException>(() => doc.RemoveRisk(doc.Risks[0].Id));
        Assert.Throws<DomainException>(() => doc.AddCompany("Tercera"));
        Assert.Equal(7, doc.Risks.Count);

        doc.Limits = EditionLimits.Unlimited;
        Assert.False(doc.IsReadOnly);
        doc.AddRisk(NewRisk(doc, 8), Day);
        Assert.Equal(8, doc.Risks.Count);
    }

    [Fact]
    public void License_works_only_on_its_machine()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var machine = License.MachineCodeFrom("5c1d7a3e-0000-4b8e-9d2a-123456789abc");
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", machine);

        var issued = License.Issue(new License("Cliente S.A.", machine, Day), key);

        // Se acepta aunque se pegue con espacios o saltos de línea y el código en minúsculas.
        var license = License.Verify("  " + issued[..20] + "\n" + issued[20..] + " ", machine.ToLowerInvariant(), publicKey);
        Assert.Equal("Cliente S.A.", license.Customer);
        Assert.Equal(Day, license.IssuedOn);

        var other = License.MachineCodeFrom("otro-equipo");
        Assert.Throws<DomainException>(() => License.Verify(issued, other, publicKey));
    }

    [Fact]
    public void Tampered_or_foreign_keys_are_rejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var foreign = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var machine = License.MachineCodeFrom("equipo");
        var issued = License.Issue(new License("Cliente", machine, Day), key);

        // Firmada con otra clave privada.
        Assert.Throws<DomainException>(() => License.Verify(issued, machine, foreign.ExportSubjectPublicKeyInfo()));

        // Datos cambiados (otro cliente) con la firma original.
        var parts = issued.Split('.');
        var forged = License.Issue(new License("Otro cliente", machine, Day), foreign).Split('.')[0] + "." + parts[1];
        Assert.Throws<DomainException>(() => License.Verify(forged, machine, key.ExportSubjectPublicKeyInfo()));

        Assert.Throws<DomainException>(() => License.Verify("", machine, key.ExportSubjectPublicKeyInfo()));
        Assert.Throws<DomainException>(() => License.Verify("AURAM1-basura.basura", machine, key.ExportSubjectPublicKeyInfo()));
    }
}
