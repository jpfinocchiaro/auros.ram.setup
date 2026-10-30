using Auros.Ram.Persistence;

namespace Auros.Ram.Tests;

/// <summary>
/// Genera el archivo de prueba de la versión vigente del formato (RNF-68).
/// Al publicar una versión con esquema nuevo, ejecutar:
///   AUROS_WRITE_FIXTURES=tests/Auros.Ram.Tests/Fixtures dotnet test --filter FixtureGenerator
/// y conservar en el repositorio el archivo generado junto a los anteriores.
/// </summary>
[Collection("AppPaths")]
public class FixtureGenerator
{
    public const string Password = "fixture-auros-ram";
    public const int RiskCount = 7;

    [Fact]
    public void Generate_current_version_fixture()
    {
        var target = Environment.GetEnvironmentVariable("AUROS_WRITE_FIXTURES");
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        using var dir = new TempDir();
        Directory.CreateDirectory(target);
        var path = Path.Combine(target, $"schema-v{RmxFile.CurrentSchemaVersion}.rmx");
        using var key = FileKey.CreateNew(Password);
        RmxFile.Save(Sample.Document(RiskCount), path, key);
    }
}
