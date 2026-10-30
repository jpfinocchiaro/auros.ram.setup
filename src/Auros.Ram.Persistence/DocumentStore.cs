using System.Globalization;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Microsoft.Data.Sqlite;

namespace Auros.Ram.Persistence;

/// <summary>Traduce el documento a las tablas de la base y viceversa.</summary>
internal static class DocumentStore
{
    private const string DateFormat = "yyyy-MM-dd";

    public static void Write(SqliteConnection c, RiskMatrixDocument doc)
    {
        using var tx = c.BeginTransaction();

        var p = doc.Project;
        using (var cmd = Command(c, tx, """
            INSERT INTO project (id, name, client, consultancy, date, status, consultancy_logo, client_logo,
                                 risk_scale_id, value_scale_id, difficulty_scale_id)
            VALUES (1, $name, $client, $consultancy, $date, $status, $clogo, $klogo, $rs, $vs, $ds);
            """))
        {
            cmd.Parameters.AddWithValue("$name", p.Name);
            cmd.Parameters.AddWithValue("$client", p.Client);
            cmd.Parameters.AddWithValue("$consultancy", p.Consultancy);
            cmd.Parameters.AddWithValue("$date", p.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$status", (int)p.Status);
            cmd.Parameters.AddWithValue("$clogo", (object?)p.ConsultancyLogo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$klogo", (object?)p.ClientLogo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rs", Id(p.RiskScaleId));
            cmd.Parameters.AddWithValue("$vs", Id(p.ValueScaleId));
            cmd.Parameters.AddWithValue("$ds", Id(p.DifficultyScaleId));
            cmd.ExecuteNonQuery();
        }

        using (var scaleCmd = Command(c, tx, "INSERT INTO scale (id, position, name, type) VALUES ($id, $pos, $name, $type);"))
        using (var levelCmd = Command(c, tx, "INSERT INTO level (id, scale_id, name, color, ord) VALUES ($id, $scale, $name, $color, $ord);"))
        {
            var sId = scaleCmd.Parameters.Add("$id", SqliteType.Text);
            var sPos = scaleCmd.Parameters.Add("$pos", SqliteType.Integer);
            var sName = scaleCmd.Parameters.Add("$name", SqliteType.Text);
            var sType = scaleCmd.Parameters.Add("$type", SqliteType.Integer);
            var lId = levelCmd.Parameters.Add("$id", SqliteType.Text);
            var lScale = levelCmd.Parameters.Add("$scale", SqliteType.Text);
            var lName = levelCmd.Parameters.Add("$name", SqliteType.Text);
            var lColor = levelCmd.Parameters.Add("$color", SqliteType.Text);
            var lOrd = levelCmd.Parameters.Add("$ord", SqliteType.Integer);

            for (var i = 0; i < doc.Scales.Count; i++)
            {
                var s = doc.Scales[i];
                sId.Value = s.Id.ToString();
                sPos.Value = i;
                sName.Value = s.Name;
                sType.Value = (int)s.Type;
                scaleCmd.ExecuteNonQuery();

                foreach (var l in s.Levels)
                {
                    lId.Value = l.Id.ToString();
                    lScale.Value = s.Id.ToString();
                    lName.Value = l.Name;
                    lColor.Value = l.Color;
                    lOrd.Value = l.Order;
                    levelCmd.ExecuteNonQuery();
                }
            }
        }

        using (var cmd = Command(c, tx, """
            INSERT INTO risk (id, position, title, short_description, long_description, risk_level_id, value_level_id,
                              difficulty_level_id, risk_justification, value_justification, difficulty_justification,
                              created_utc, modified_utc)
            VALUES ($id, $pos, $title, $short, $long, $rl, $vl, $dl, $rj, $vj, $dj, $created, $modified);
            """))
        {
            var names = new[] { "$id", "$pos", "$title", "$short", "$long", "$rl", "$vl", "$dl", "$rj", "$vj", "$dj", "$created", "$modified" };
            var prm = names.Select(n => cmd.Parameters.Add(n, SqliteType.Text)).ToArray();
            prm[1].SqliteType = SqliteType.Integer;
            for (var i = 0; i < doc.Risks.Count; i++)
            {
                var r = doc.Risks[i];
                prm[0].Value = r.Id.ToString();
                prm[1].Value = i;
                prm[2].Value = r.Title;
                prm[3].Value = r.ShortDescription;
                prm[4].Value = r.LongDescription;
                prm[5].Value = r.RiskLevelId.ToString();
                prm[6].Value = r.ValueLevelId.ToString();
                prm[7].Value = r.DifficultyLevelId.ToString();
                prm[8].Value = r.RiskJustification;
                prm[9].Value = r.ValueJustification;
                prm[10].Value = r.DifficultyJustification;
                prm[11].Value = r.CreatedUtc.ToString("o", CultureInfo.InvariantCulture);
                prm[12].Value = r.ModifiedUtc.ToString("o", CultureInfo.InvariantCulture);
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = Command(c, tx, "INSERT OR REPLACE INTO meta (key, value) VALUES ('app_version', $v);"))
        {
            cmd.Parameters.AddWithValue("$v", AppInfo.Version);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static RiskMatrixDocument Read(SqliteConnection c)
    {
        var project = new ProjectInfo();
        using (var cmd = Command(c, null, """
            SELECT name, client, consultancy, date, status, consultancy_logo, client_logo,
                   risk_scale_id, value_scale_id, difficulty_scale_id
            FROM project WHERE id = 1;
            """))
        using (var r = cmd.ExecuteReader())
        {
            if (r.Read())
            {
                project.Name = r.GetString(0);
                project.Client = r.GetString(1);
                project.Consultancy = r.GetString(2);
                project.Date = DateOnly.ParseExact(r.GetString(3), DateFormat, CultureInfo.InvariantCulture);
                project.Status = (ProjectStatus)r.GetInt32(4);
                project.ConsultancyLogo = r.IsDBNull(5) ? null : (byte[])r[5];
                project.ClientLogo = r.IsDBNull(6) ? null : (byte[])r[6];
                project.RiskScaleId = OptionalGuid(r, 7);
                project.ValueScaleId = OptionalGuid(r, 8);
                project.DifficultyScaleId = OptionalGuid(r, 9);
            }
        }

        var scales = new List<Scale>();
        var byId = new Dictionary<Guid, Scale>();
        using (var cmd = Command(c, null, "SELECT id, name, type FROM scale ORDER BY position;"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var s = new Scale { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), Type = (ScaleType)r.GetInt32(2) };
                scales.Add(s);
                byId[s.Id] = s;
            }
        }

        using (var cmd = Command(c, null, "SELECT id, scale_id, name, color, ord FROM level ORDER BY ord, name;"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (byId.TryGetValue(Guid.Parse(r.GetString(1)), out var s))
                {
                    s.Levels.Add(new Level { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(2), Color = r.GetString(3), Order = r.GetInt32(4) });
                }
            }
        }

        var risks = new List<Risk>();
        using (var cmd = Command(c, null, """
            SELECT id, title, short_description, long_description, risk_level_id, value_level_id, difficulty_level_id,
                   risk_justification, value_justification, difficulty_justification, created_utc, modified_utc
            FROM risk ORDER BY position;
            """))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                risks.Add(new Risk
                {
                    Id = Guid.Parse(r.GetString(0)),
                    Title = r.GetString(1),
                    ShortDescription = r.GetString(2),
                    LongDescription = r.GetString(3),
                    RiskLevelId = Guid.Parse(r.GetString(4)),
                    ValueLevelId = Guid.Parse(r.GetString(5)),
                    DifficultyLevelId = Guid.Parse(r.GetString(6)),
                    RiskJustification = r.GetString(7),
                    ValueJustification = r.GetString(8),
                    DifficultyJustification = r.GetString(9),
                    CreatedUtc = DateTime.Parse(r.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    ModifiedUtc = DateTime.Parse(r.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                });
            }
        }

        return new RiskMatrixDocument(project, scales, risks);
    }

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }

    private static object Id(Guid? id) => id?.ToString() ?? (object)DBNull.Value;

    private static Guid? OptionalGuid(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : Guid.Parse(r.GetString(i));
}
