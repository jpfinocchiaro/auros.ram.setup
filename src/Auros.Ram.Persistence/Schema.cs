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
        (1, """
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS project (
                id                   INTEGER PRIMARY KEY CHECK (id = 1),
                name                 TEXT NOT NULL,
                client               TEXT NOT NULL,
                consultancy          TEXT NOT NULL,
                date                 TEXT NOT NULL,
                status               INTEGER NOT NULL,
                consultancy_logo     BLOB,
                client_logo          BLOB,
                risk_scale_id        TEXT,
                value_scale_id       TEXT,
                difficulty_scale_id  TEXT
            );
            CREATE TABLE IF NOT EXISTS scale (
                id        TEXT PRIMARY KEY,
                position  INTEGER NOT NULL,
                name      TEXT NOT NULL,
                type      INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS level (
                id        TEXT PRIMARY KEY,
                scale_id  TEXT NOT NULL REFERENCES scale(id) ON DELETE CASCADE,
                name      TEXT NOT NULL,
                color     TEXT NOT NULL,
                ord       INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_level_scale ON level(scale_id);
            CREATE TABLE IF NOT EXISTS risk (
                id                        TEXT PRIMARY KEY,
                position                  INTEGER NOT NULL,
                title                     TEXT NOT NULL,
                short_description         TEXT NOT NULL,
                long_description          TEXT NOT NULL,
                risk_level_id             TEXT NOT NULL,
                value_level_id            TEXT NOT NULL,
                difficulty_level_id       TEXT NOT NULL,
                risk_justification        TEXT NOT NULL,
                value_justification       TEXT NOT NULL,
                difficulty_justification  TEXT NOT NULL,
                created_utc               TEXT NOT NULL,
                modified_utc              TEXT NOT NULL
            );
            """),
    ];

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
