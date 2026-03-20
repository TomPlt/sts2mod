using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class PotionAnalytics
{
    private readonly IDbConnection _connection;

    public PotionAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<PotionPickRate> GetPotionPickRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                pc.PotionId,
                COUNT(*) AS TimesOffered,
                SUM(CASE WHEN pc.WasPicked = 1 THEN 1 ELSE 0 END) AS TimesPicked
            FROM PotionChoices pc
            JOIN Floors f ON pc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY pc.PotionId
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int timesOffered = (int)(long)row.TimesOffered;
            int timesPicked = (int)(long)row.TimesPicked;
            double pickRate = timesOffered > 0 ? (double)timesPicked / timesOffered : 0.0;

            return new PotionPickRate((string)row.PotionId, timesOffered, timesPicked, pickRate);
        }).ToList();
    }

    public List<PotionUsageTiming> GetPotionUsageTiming(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                pe.PotionId,
                f.MapPointType AS RoomType,
                COUNT(*) AS TimesUsed
            FROM PotionEvents pe
            JOIN Floors f ON pe.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            WHERE pe.Action = 'used'
            {(where.Length > 0 ? "AND " + where.Replace("WHERE ", "") : "")}
            GROUP BY pe.PotionId, f.MapPointType
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
            new PotionUsageTiming((string)row.PotionId, (string)row.RoomType, (int)(long)row.TimesUsed)
        ).ToList();
    }

    public List<PotionWasteRate> GetPotionWasteRate(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                pe.PotionId,
                SUM(CASE WHEN pe.Action = 'used' THEN 1 ELSE 0 END) AS TimesUsed,
                SUM(CASE WHEN pe.Action = 'discarded' THEN 1 ELSE 0 END) AS TimesDiscarded
            FROM PotionEvents pe
            JOIN Floors f ON pe.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY pe.PotionId
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int timesUsed = (int)(long)row.TimesUsed;
            int timesDiscarded = (int)(long)row.TimesDiscarded;
            int total = timesUsed + timesDiscarded;
            double wasteRate = total > 0 ? (double)timesDiscarded / total : 0.0;

            return new PotionWasteRate((string)row.PotionId, timesUsed, timesDiscarded, wasteRate);
        }).ToList();
    }

    private static (string Where, DynamicParameters Parameters) BuildWhereClause(AnalyticsFilter? filter)
    {
        if (filter is null)
            return ("", new DynamicParameters());

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (filter.Character is not null)
        {
            conditions.Add("r.Character = @Character");
            parameters.Add("Character", filter.Character);
        }
        if (filter.AscensionMin is not null)
        {
            conditions.Add("r.Ascension >= @AscensionMin");
            parameters.Add("AscensionMin", filter.AscensionMin);
        }
        if (filter.AscensionMax is not null)
        {
            conditions.Add("r.Ascension <= @AscensionMax");
            parameters.Add("AscensionMax", filter.AscensionMax);
        }
        if (filter.DateFrom is not null)
        {
            conditions.Add("r.StartTime >= @DateFrom");
            parameters.Add("DateFrom", filter.DateFrom.Value.ToString("o"));
        }
        if (filter.DateTo is not null)
        {
            conditions.Add("r.StartTime <= @DateTo");
            parameters.Add("DateTo", filter.DateTo.Value.ToString("o"));
        }
        if (filter.GameMode is not null)
        {
            conditions.Add("r.GameMode = @GameMode");
            parameters.Add("GameMode", filter.GameMode);
        }
        if (filter.ActIndex is not null)
        {
            conditions.Add("f.ActIndex = @ActIndex");
            parameters.Add("ActIndex", filter.ActIndex);
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        return (where, parameters);
    }
}
