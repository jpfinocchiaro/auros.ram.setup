using Microsoft.Data.Sqlite;

namespace Auros.Ram.Persistence;

/// <summary>
/// Esquema de la base interna y sus migraciones (RNF-14, RNF-69).
/// Cada paso es acumulativo e idempotente; nunca se edita un paso ya publicado:
/// los cambios se agregan como un paso nuevo al final de <see cref="Steps"/>.
/// </summary>
internal static class Schema
{
    private static readonly (int Version, string Sql)[] Steps =
    [
        // v3: riesgos clasificados por impacto y probabilidad; el nivel de riesgo sale del mapa de calor.
        // Las versiones 1 y 2 (valor agregado y dificultad) fueron de prueba: no se migran (ver OldestSupportedVersion).
        (3, """
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS project (
                id                    INTEGER PRIMARY KEY CHECK (id = 1),
                name                  TEXT NOT NULL,
                company               TEXT NOT NULL,
                date                  TEXT NOT NULL,
                status                INTEGER NOT NULL,
                company_logo          BLOB,
                risk_scale_id         TEXT,
                impact_scale_id       TEXT,
                probability_scale_id  TEXT
            );
            CREATE TABLE IF NOT EXISTS scale (
                id        TEXT PRIMARY KEY,
                position  INTEGER NOT NULL,
                name      TEXT NOT NULL,
                type      INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS level (
                id          TEXT PRIMARY KEY,
                scale_id    TEXT NOT NULL REFERENCES scale(id) ON DELETE CASCADE,
                name        TEXT NOT NULL,
                color       TEXT NOT NULL,
                text_color  TEXT NOT NULL,
                ord         INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_level_scale ON level(scale_id);
            CREATE TABLE IF NOT EXISTS heat_cell (
                impact_level_id       TEXT NOT NULL,
                probability_level_id  TEXT NOT NULL,
                risk_level_id         TEXT NOT NULL,
                PRIMARY KEY (impact_level_id, probability_level_id)
            );
            CREATE TABLE IF NOT EXISTS risk (
                id                         TEXT PRIMARY KEY,
                position                   INTEGER NOT NULL,
                title                      TEXT NOT NULL,
                short_description          TEXT NOT NULL,
                long_description           TEXT NOT NULL,
                impact_level_id            TEXT NOT NULL,
                probability_level_id       TEXT NOT NULL,
                impact_justification       TEXT NOT NULL,
                probability_justification  TEXT NOT NULL,
                created_utc                TEXT NOT NULL,
                modified_utc               TEXT NOT NULL
            );
            """),

        // v4: historial de evaluaciones de cada riesgo. Los riesgos existentes empiezan su historial con su estado
        // actual, fechado el día en que se crearon.
        (4, """
            CREATE TABLE IF NOT EXISTS risk_assessment (
                id                    TEXT PRIMARY KEY,
                risk_id               TEXT NOT NULL REFERENCES risk(id) ON DELETE CASCADE,
                date                  TEXT NOT NULL,
                impact_level_id       TEXT NOT NULL,
                probability_level_id  TEXT NOT NULL,
                note                  TEXT NOT NULL,
                recorded_utc          TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_assessment_risk ON risk_assessment(risk_id);
            INSERT INTO risk_assessment (id, risk_id, date, impact_level_id, probability_level_id, note, recorded_utc)
                SELECT lower(hex(randomblob(16))), id, substr(created_utc, 1, 10), impact_level_id, probability_level_id, '', created_utc
                FROM risk
                WHERE id NOT IN (SELECT risk_id FROM risk_assessment);
            """),

        // v5: rangos de los niveles (monto en Impacto, porcentaje en Probabilidad) y, en cada riesgo y evaluación,
        // el monto del impacto y la probabilidad en porcentaje. Decimales como texto invariante ("1234.50").
        (5, """
            ALTER TABLE level ADD COLUMN range_from TEXT;
            ALTER TABLE level ADD COLUMN range_to TEXT;
            ALTER TABLE risk ADD COLUMN impact_amount TEXT;
            ALTER TABLE risk ADD COLUMN probability_percent TEXT;
            ALTER TABLE risk_assessment ADD COLUMN impact_amount TEXT;
            ALTER TABLE risk_assessment ADD COLUMN probability_percent TEXT;
            """),

        // v6: catálogos de la matriz (consecuencias potenciales y criterios de materialidad), las consecuencias de
        // cada riesgo y su criterio de materialidad. Los archivos anteriores reciben la lista sugerida.
        (6, """
            CREATE TABLE IF NOT EXISTS catalog_item (
                id        TEXT PRIMARY KEY,
                kind      INTEGER NOT NULL,
                position  INTEGER NOT NULL,
                name      TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS risk_consequence (
                risk_id   TEXT NOT NULL REFERENCES risk(id) ON DELETE CASCADE,
                item_id   TEXT NOT NULL,
                position  INTEGER NOT NULL,
                PRIMARY KEY (risk_id, item_id)
            );
            ALTER TABLE risk ADD COLUMN materiality_id TEXT;
            INSERT INTO catalog_item (id, kind, position, name)
                SELECT lower(hex(randomblob(16))), kind, position, name FROM (
                    SELECT 1 AS kind, 0 AS position, 'Accidente laboral' AS name
                    UNION ALL SELECT 1, 1, 'Pérdida de rentabilidad'
                    UNION ALL SELECT 1, 2, 'Pérdida de reputación'
                    UNION ALL SELECT 1, 3, 'Sanción regulatoria'
                    UNION ALL SELECT 1, 4, 'Interrupción de la operación'
                    UNION ALL SELECT 1, 5, 'Pérdida o fuga de información'
                    UNION ALL SELECT 1, 6, 'Fraude'
                    UNION ALL SELECT 1, 7, 'Daño ambiental'
                    UNION ALL SELECT 2, 0, 'Impacto económico significativo'
                    UNION ALL SELECT 2, 1, 'Incumplimiento legal o regulatorio'
                    UNION ALL SELECT 2, 2, 'Afecta la reputación'
                    UNION ALL SELECT 2, 3, 'Afecta la seguridad de las personas'
                    UNION ALL SELECT 2, 4, 'Afecta la continuidad del negocio')
                WHERE NOT EXISTS (SELECT 1 FROM catalog_item);
            """),

        // v7: varias empresas, cada una con sus unidades de negocio, y la empresa y unidad de cada riesgo. Los archivos
        // anteriores reciben una empresa (la del proyecto, con su logo) con la unidad "General", y todos sus riesgos.
        (7, """
            CREATE TABLE IF NOT EXISTS company (
                id        TEXT PRIMARY KEY,
                position  INTEGER NOT NULL,
                name      TEXT NOT NULL,
                logo      BLOB
            );
            CREATE TABLE IF NOT EXISTS business_unit (
                id          TEXT PRIMARY KEY,
                company_id  TEXT NOT NULL REFERENCES company(id) ON DELETE CASCADE,
                position    INTEGER NOT NULL,
                name        TEXT NOT NULL
            );
            ALTER TABLE risk ADD COLUMN company_id TEXT;
            ALTER TABLE risk ADD COLUMN unit_id TEXT;
            INSERT INTO company (id, position, name, logo)
                SELECT lower(hex(randomblob(16))), 0, CASE WHEN trim(company) = '' THEN 'Empresa' ELSE trim(company) END, company_logo
                FROM project WHERE NOT EXISTS (SELECT 1 FROM company);
            INSERT INTO business_unit (id, company_id, position, name)
                SELECT lower(hex(randomblob(16))), id, 0, 'General' FROM company WHERE NOT EXISTS (SELECT 1 FROM business_unit);
            UPDATE risk SET company_id = (SELECT id FROM company ORDER BY position LIMIT 1),
                            unit_id = (SELECT id FROM business_unit ORDER BY position LIMIT 1)
                WHERE company_id IS NULL;
            """),

        // v8: acciones mitigantes de cada evaluación (descripción, responsable, fecha comprometida y estado
        // 0 pendiente / 1 en curso / 2 implementada / 3 descartada).
        (8, """
            CREATE TABLE IF NOT EXISTS mitigation_action (
                id             TEXT PRIMARY KEY,
                assessment_id  TEXT NOT NULL REFERENCES risk_assessment(id) ON DELETE CASCADE,
                position       INTEGER NOT NULL,
                description    TEXT NOT NULL,
                responsible    TEXT NOT NULL DEFAULT '',
                due_date       TEXT,
                status         INTEGER NOT NULL DEFAULT 0
            );
            """),
    ];

    /// <summary>Archivos con un esquema anterior a este no se abren (versiones de prueba sin migración).</summary>
    public const int OldestSupportedVersion = 3;

    public static int CurrentVersion => Steps[^1].Version;

    public static int GetVersion(SqliteConnection connection) =>
        Convert.ToInt32(Sql.Scalar(connection, "PRAGMA user_version;"), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Lleva la base desde <paramref name="fromVersion"/> hasta la versión vigente.</summary>
    public static void Migrate(SqliteConnection connection, int fromVersion)
    {
        foreach (var (version, sql) in Steps.Where(s => s.Version > fromVersion))
        {
            using var tx = connection.BeginTransaction();
            Sql.Execute(connection, sql, tx);
            Sql.Execute(connection, $"PRAGMA user_version = {version};", tx);
            tx.Commit();
        }
    }
}

internal static class Sql
{
    public static void Execute(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
