using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class RestSiteAnalytics
{
    private readonly IDbConnection _connection;

    public RestSiteAnalytics(IDbConnection connection) => _connection = connection;

    public List<RestSiteDecisionRate> GetDecisionWinRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                rsc.Choice,
                COUNT(*) AS Count,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM RestSiteChoices rsc
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rsc.Choice
            ORDER BY COUNT(*) DESC
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int count = (int)(long)row.Count;
            int wins = (int)(long)row.Wins;
            return new RestSiteDecisionRate((string)row.Choice, count, wins,
                count > 0 ? (double)wins / count : 0);
        }).ToList();
    }

    public List<RestSiteHpBucket> GetDecisionsByHpBucket(AnalyticsFilter? filter = null)
    {
        var (conditions, parameters) = BuildConditions(filter);
        conditions.Add("f.MaxHp > 0");
        var where = "WHERE " + string.Join(" AND ", conditions);

        var sql = $"""
            SELECT
                rsc.Choice,
                CASE
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.25 THEN 0
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.50 THEN 25
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.75 THEN 50
                    ELSE 75
                END AS HpBucket,
                COUNT(*) AS Count,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM RestSiteChoices rsc
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rsc.Choice, HpBucket
            ORDER BY HpBucket, rsc.Choice
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int bucket = (int)(long)row.HpBucket;
            int count = (int)(long)row.Count;
            int wins = (int)(long)row.Wins;
            return new RestSiteHpBucket((string)row.Choice, bucket, bucket + 25, count, wins,
                count > 0 ? (double)wins / count : 0);
        }).ToList();
    }

    public List<RestSiteUpgradeImpact> GetUpgradeImpact(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                rsu.CardId,
                COUNT(*) AS TimesUpgraded,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM RestSiteUpgrades rsu
            JOIN RestSiteChoices rsc ON rsu.RestSiteChoiceId = rsc.Id
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rsu.CardId
            ORDER BY COUNT(*) DESC
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int count = (int)(long)row.TimesUpgraded;
            int wins = (int)(long)row.Wins;
            return new RestSiteUpgradeImpact((string)row.CardId, count, wins,
                count > 0 ? (double)wins / count : 0);
        }).ToList();
    }

    public List<RestSiteActBreakdown> GetActBreakdown(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                f.ActIndex AS Act,
                rsc.Choice,
                COUNT(*) AS Count,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins,
                AVG(CAST(f.CurrentHp AS REAL) / NULLIF(f.MaxHp, 0)) AS AvgHpPercent
            FROM RestSiteChoices rsc
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY f.ActIndex, rsc.Choice
            ORDER BY f.ActIndex, COUNT(*) DESC
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int count = (int)(long)row.Count;
            int wins = (int)(long)row.Wins;
            return new RestSiteActBreakdown(
                (int)(long)row.Act, (string)row.Choice, count, wins,
                count > 0 ? (double)wins / count : 0,
                (double)(row.AvgHpPercent ?? 0));
        }).ToList();
    }

    private static (List<string> Conditions, DynamicParameters Parameters) BuildConditions(AnalyticsFilter? filter)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();
        if (filter is null) return (conditions, parameters);

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

        return (conditions, parameters);
    }

    private static (string Where, DynamicParameters Parameters) BuildWhereClause(AnalyticsFilter? filter)
    {
        var (conditions, parameters) = BuildConditions(filter);
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        return (where, parameters);
    }
}
