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
            INSERT INTO project (id, name, company, date, status, company_logo, risk_scale_id, impact_scale_id, probability_scale_id)
            VALUES (1, $name, $company, $date, $status, $logo, $rs, $is, $ps);
            """))
        {
            cmd.Parameters.AddWithValue("$name", p.Name);
            cmd.Parameters.AddWithValue("$company", p.Company);
            cmd.Parameters.AddWithValue("$date", p.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$status", (int)p.Status);
            cmd.Parameters.AddWithValue("$logo", (object?)p.CompanyLogo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rs", Id(p.RiskScaleId));
            cmd.Parameters.AddWithValue("$is", Id(p.ImpactScaleId));
            cmd.Parameters.AddWithValue("$ps", Id(p.ProbabilityScaleId));
            cmd.ExecuteNonQuery();
        }

        using (var scaleCmd = Command(c, tx, "INSERT INTO scale (id, position, name, type) VALUES ($id, $pos, $name, $type);"))
        using (var levelCmd = Command(c, tx, "INSERT INTO level (id, scale_id, name, color, text_color, ord) VALUES ($id, $scale, $name, $color, $text, $ord);"))
        {
            var sId = scaleCmd.Parameters.Add("$id", SqliteType.Text);
            var sPos = scaleCmd.Parameters.Add("$pos", SqliteType.Integer);
            var sName = scaleCmd.Parameters.Add("$name", SqliteType.Text);
            var sType = scaleCmd.Parameters.Add("$type", SqliteType.Integer);
            var lId = levelCmd.Parameters.Add("$id", SqliteType.Text);
            var lScale = levelCmd.Parameters.Add("$scale", SqliteType.Text);
            var lName = levelCmd.Parameters.Add("$name", SqliteType.Text);
            var lColor = levelCmd.Parameters.Add("$color", SqliteType.Text);
            var lText = levelCmd.Parameters.Add("$text", SqliteType.Text);
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
                    lText.Value = l.TextColor;
                    lOrd.Value = l.Order;
                    levelCmd.ExecuteNonQuery();
                }
            }
        }

        using (var cmd = Command(c, tx, "INSERT OR REPLACE INTO heat_cell (impact_level_id, probability_level_id, risk_level_id) VALUES ($i, $p, $r);"))
        {
            var i = cmd.Parameters.Add("$i", SqliteType.Text);
            var pr = cmd.Parameters.Add("$p", SqliteType.Text);
            var r = cmd.Parameters.Add("$r", SqliteType.Text);
            foreach (var cell in p.HeatMap)
            {
                i.Value = cell.ImpactLevelId.ToString();
                pr.Value = cell.ProbabilityLevelId.ToString();
                r.Value = cell.RiskLevelId.ToString();
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = Command(c, tx, """
            INSERT INTO risk (id, position, title, short_description, long_description, impact_level_id, probability_level_id,
                              impact_justification, probability_justification, created_utc, modified_utc)
            VALUES ($id, $pos, $title, $short, $long, $il, $pl, $ij, $pj, $created, $modified);
            """))
        {
            var names = new[] { "$id", "$pos", "$title", "$short", "$long", "$il", "$pl", "$ij", "$pj", "$created", "$modified" };
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
                prm[5].Value = r.ImpactLevelId.ToString();
                prm[6].Value = r.ProbabilityLevelId.ToString();
                prm[7].Value = r.ImpactJustification;
                prm[8].Value = r.ProbabilityJustification;
                prm[9].Value = r.CreatedUtc.ToString("o", CultureInfo.InvariantCulture);
                prm[10].Value = r.ModifiedUtc.ToString("o", CultureInfo.InvariantCulture);
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
            SELECT name, company, date, status, company_logo, risk_scale_id, impact_scale_id, probability_scale_id
            FROM project WHERE id = 1;
            """))
        using (var r = cmd.ExecuteReader())
        {
            if (r.Read())
            {
                project.Name = r.GetString(0);
                project.Company = r.GetString(1);
                project.Date = DateOnly.ParseExact(r.GetString(2), DateFormat, CultureInfo.InvariantCulture);
                project.Status = (ProjectStatus)r.GetInt32(3);
                project.CompanyLogo = r.IsDBNull(4) ? null : (byte[])r[4];
                project.RiskScaleId = OptionalGuid(r, 5);
                project.ImpactScaleId = OptionalGuid(r, 6);
                project.ProbabilityScaleId = OptionalGuid(r, 7);
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

        using (var cmd = Command(c, null, "SELECT id, scale_id, name, color, text_color, ord FROM level ORDER BY ord, name;"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (byId.TryGetValue(Guid.Parse(r.GetString(1)), out var s))
                {
                    s.Levels.Add(new Level
                    {
                        Id = Guid.Parse(r.GetString(0)),
                        Name = r.GetString(2),
                        Color = r.GetString(3),
                        TextColor = r.GetString(4),
                        Order = r.GetInt32(5),
                    });
                }
            }
        }

        var heat = new List<HeatCell>();
        using (var cmd = Command(c, null, "SELECT impact_level_id, probability_level_id, risk_level_id FROM heat_cell;"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                heat.Add(new HeatCell(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2))));
            }
        }

        project.HeatMap = heat;

        var risks = new List<Risk>();
        using (var cmd = Command(c, null, """
            SELECT id, title, short_description, long_description, impact_level_id, probability_level_id,
                   impact_justification, probability_justification, created_utc, modified_utc
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
                    ImpactLevelId = Guid.Parse(r.GetString(4)),
                    ProbabilityLevelId = Guid.Parse(r.GetString(5)),
                    ImpactJustification = r.GetString(6),
                    ProbabilityJustification = r.GetString(7),
                    CreatedUtc = DateTime.Parse(r.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    ModifiedUtc = DateTime.Parse(r.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
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
