using System.Diagnostics;
using System.Text;
using Auros.Ram.Core.Model;
using Auros.Ram.Persistence;

namespace Auros.Ram.Tests;

[Collection("AppPaths")]
public class FileFormatTests
{
    private const string Password = "contraseña-de-prueba-123";

    [Fact]
    public void Roundtrip_create_encrypt_close_reopen_compare_RNF67()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(25);
        var p = doc.Project.Clone();
        p.ConsultancyLogo = [9, 8, 7];
        doc.UpdateProject(p);
        var path = dir.File("matriz.rmx");

        using (var key = FileKey.CreateNew(Password))
        {
            RmxFile.Save(doc, path, key);
        }

        var result = RmxFile.Open(path, Password);
        using var _ = result.Key;
        var back = result.Document;
        Assert.Null(result.BackupPath);
        Assert.Equal(doc.Project.Name, back.Project.Name);
        Assert.Equal(doc.Project.ConsultancyLogo, back.Project.ConsultancyLogo);
        Assert.Equal(doc.Project.RiskScaleId, back.Project.RiskScaleId);
        Assert.Equal(doc.Scales.Select(s => (s.Id, s.Name, s.Type, s.Levels.Count)), back.Scales.Select(s => (s.Id, s.Name, s.Type, s.Levels.Count)));
        Assert.Equal(
            doc.Risks.Select(r => (r.Id, r.Title, r.ShortDescription, r.LongDescription, r.RiskLevelId, r.ValueLevelId, r.DifficultyLevelId, r.RiskJustification)),
            back.Risks.Select(r => (r.Id, r.Title, r.ShortDescription, r.LongDescription, r.RiskLevelId, r.ValueLevelId, r.DifficultyLevelId, r.RiskJustification)));
        Assert.Equal(RmxFile.CurrentSchemaVersion, result.Header.SchemaVersion);
        Assert.Equal(KdfAlgorithm.Argon2id, result.Header.Kdf.Algorithm);
    }

    [Fact]
    public void File_contains_no_plaintext_RNF12_RNF13()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(3);
        var path = dir.File("m.rmx");
        using var key = FileKey.CreateNew(Password);
        RmxFile.Save(doc, path, key);

        var bytes = File.ReadAllBytes(path);
        var latin = Encoding.Latin1.GetString(bytes);
        var utf8 = Encoding.UTF8.GetString(bytes);
        foreach (var secret in new[] { "Relevamiento", "Cliente S.A.", "Riesgo 1", "SQLite format", "CREATE TABLE" })
        {
            Assert.DoesNotContain(secret, latin, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, utf8, StringComparison.Ordinal);
        }

        Assert.StartsWith("AUROSRAM", latin, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_password_is_rejected_and_file_untouched_RNF30()
    {
        using var dir = new TempDir();
        var path = dir.File("m.rmx");
        using (var key = FileKey.CreateNew(Password))
        {
            RmxFile.Save(Sample.Document(2), path, key);
        }

        var before = File.ReadAllBytes(path);
        var ex = Assert.Throws<RmxFileException>(() => RmxFile.Open(path, "otra-contraseña-xx"));
        Assert.Equal(RmxErrorKind.WrongPassword, ex.Kind);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFiles(AppPaths.WorkDirectory));
    }

    [Fact]
    public void Corruption_is_detected_RNF26()
    {
        using var dir = new TempDir();
        var path = dir.File("m.rmx");
        using (var key = FileKey.CreateNew(Password))
        {
            RmxFile.Save(Sample.Document(2), path, key);
        }

        var original = File.ReadAllBytes(path);

        var payloadDamaged = (byte[])original.Clone();
        payloadDamaged[^100] ^= 0xFF;
        File.WriteAllBytes(path, payloadDamaged);
        Assert.Equal(RmxErrorKind.Corrupted, Assert.Throws<RmxFileException>(() => RmxFile.Open(path, Password)).Kind);

        var headerDamaged = (byte[])original.Clone();
        headerDamaged[30] ^= 0xFF;
        File.WriteAllBytes(path, headerDamaged);
        Assert.Equal(RmxErrorKind.Corrupted, Assert.Throws<RmxFileException>(() => RmxFile.Open(path, Password)).Kind);

        File.WriteAllText(path, "hola");
        Assert.Equal(RmxErrorKind.NotRmx, Assert.Throws<RmxFileException>(() => RmxFile.Open(path, Password)).Kind);
    }

    [Fact]
    public void Newer_schema_is_rejected_RNF14()
    {
        using var dir = new TempDir();
        var path = dir.File("m.rmx");
        using var key = FileKey.CreateNew(Password);
        RmxFile.Save(Sample.Document(1), path, key);

        var bytes = File.ReadAllBytes(path);
        var header = RmxHeader.Parse(bytes) with { SchemaVersion = RmxFile.CurrentSchemaVersion + 1 };
        header.ToBytes().CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);

        Assert.Equal(RmxErrorKind.NewerVersion, Assert.Throws<RmxFileException>(() => RmxFile.Open(path, Password)).Kind);
    }

    [Fact]
    public void Save_is_atomic_and_leaves_no_temp_files_RNF24()
    {
        using var dir = new TempDir();
        var path = dir.File("m.rmx");
        using var key = FileKey.CreateNew(Password);
        RmxFile.Save(Sample.Document(1), path, key);
        RmxFile.Save(Sample.Document(4), path, key);
        Assert.Equal(["m.rmx"], Directory.EnumerateFiles(dir.Path).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFiles(AppPaths.WorkDirectory));
    }

    [Fact]
    public void Template_roundtrip_and_kind_check_RF06()
    {
        using var dir = new TempDir();
        var path = dir.File("plantilla.rmt");
        using var key = FileKey.CreateNew(Password);
        RmxFile.SaveTemplate(DefaultScales.Create(), path, key);

        var scales = RmxFile.OpenTemplate(path, Password);
        Assert.Equal(3, scales.Count);
        Assert.Equal(RmxErrorKind.WrongKind, Assert.Throws<RmxFileException>(() => RmxFile.Open(path, Password)).Kind);
    }

    [Fact]
    public void Recovery_file_uses_same_key_RNF25_RNF37()
    {
        using var dir = new TempDir();
        var path = dir.File("m.rmx");
        using var key = FileKey.CreateNew(Password);
        var doc = Sample.Document(1);
        RmxFile.Save(doc, path, key);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));

        doc.AddRisk(Sample.NewRisk(doc, "sin guardar", 0, 0, 0));
        RecoveryStore.Save(doc, path, key);
        Assert.True(RecoveryStore.HasRecovery(path));

        var recovered = RecoveryStore.TryLoad(path, key);
        Assert.NotNull(recovered);
        Assert.Equal(2, recovered!.Risks.Count);

        using var other = FileKey.CreateNew("otra-contraseña-larga");
        Assert.Null(RecoveryStore.TryLoad(path, other));

        RecoveryStore.Delete(path);
        Assert.False(RecoveryStore.HasRecovery(path));
    }

    [Fact]
    public void Key_matches_password_and_change_password_reencrypts_RNF35_RNF36()
    {
        using var dir = new TempDir();
        var path = dir.File("m.rmx");
        using var key = FileKey.CreateNew(Password);
        Assert.True(key.Matches(Password));
        Assert.False(key.Matches(Password + "x"));

        RmxFile.Save(Sample.Document(1), path, key);
        using var newKey = FileKey.CreateNew("nueva-contraseña-segura");
        RmxFile.Save(RmxFile.Open(path, Password).Document, path, newKey);
        Assert.Throws<RmxFileException>(() => RmxFile.Open(path, Password));
        Assert.Single(RmxFile.Open(path, "nueva-contraseña-segura").Document.Risks);
    }

    [Fact]
    public void Pbkdf2_files_can_be_opened_RNF32()
    {
        using var dir = new TempDir();
        var path = dir.File("m.rmx");
        var parameters = new KdfParameters(KdfAlgorithm.Pbkdf2Sha512, 300_000, 0, 0, new byte[KdfParameters.SaltLength]);
        using var key = FileKey.Derive(Password, parameters);
        RmxFile.Save(Sample.Document(1), path, key);
        Assert.Equal(KdfAlgorithm.Pbkdf2Sha512, RmxFile.Open(path, Password).Header.Kdf.Algorithm);

        var weak = parameters with { Iterations = 1000 };
        Assert.Throws<RmxFileException>(() => FileKey.Derive(Password, weak));
    }

    [Fact]
    public void Reference_volume_performance_RNF19_RNF22()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(0);
        var bulk = Enumerable.Range(0, 5000).Select(i => Sample.NewRisk(doc, $"Riesgo {i}", i % 4, i % 3, i % 3)).ToList();
        foreach (var r in bulk)
        {
            r.LongDescription = new string('x', 1500);
            doc.AddRisk(r);
        }

        var path = dir.File("grande.rmx");
        using var key = FileKey.CreateNew(Password);

        var sw = Stopwatch.StartNew();
        RmxFile.Save(doc, path, key);
        var save = sw.Elapsed;

        sw.Restart();
        using var opened = RmxFile.Open(path, Password).Key;
        var open = sw.Elapsed;

        Assert.True(save < TimeSpan.FromSeconds(2), $"guardado {save.TotalMilliseconds} ms");
        Assert.True(open < TimeSpan.FromSeconds(2), $"apertura {open.TotalMilliseconds} ms");
    }

    [Fact]
    public void Fixture_files_of_every_published_version_open_RNF68()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var files = Directory.Exists(fixtures) ? Directory.GetFiles(fixtures, "*.rmx") : [];
        Assert.NotEmpty(files);
        using var dir = new TempDir();
        foreach (var file in files)
        {
            // Se abre una copia: la migración reescribe el archivo.
            var copy = dir.File(Path.GetFileName(file));
            File.Copy(file, copy);
            var result = RmxFile.Open(copy, FixtureGenerator.Password);
            using var _ = result.Key;
            Assert.Equal(FixtureGenerator.RiskCount, result.Document.Risks.Count);
        }
    }
}

[CollectionDefinition("AppPaths", DisableParallelization = true)]
public class AppPathsCollection;
