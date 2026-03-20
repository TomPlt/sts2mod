using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class RelicAnalytics
{
    private readonly IDbConnection _connection;

    public RelicAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<RelicWinRate> GetRelicWinRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                rc.RelicId,
                SUM(CASE WHEN rc.WasPicked = 1 THEN 1 ELSE 0 END) AS TimesPicked,
                SUM(CASE WHEN rc.WasPicked = 0 THEN 1 ELSE 0 END) AS TimesSkipped,
                SUM(CASE WHEN rc.WasPicked = 1 AND r.Win = 1 THEN 1 ELSE 0 END) AS WinsWhenPicked,
                SUM(CASE WHEN rc.WasPicked = 0 AND r.Win = 1 THEN 1 ELSE 0 END) AS WinsWhenSkipped
            FROM RelicChoices rc
            JOIN Floors f ON rc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rc.RelicId
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int timesPicked = (int)(long)row.TimesPicked;
            int timesSkipped = (int)(long)row.TimesSkipped;
            int winsWhenPicked = (int)(long)row.WinsWhenPicked;
            int winsWhenSkipped = (int)(long)row.WinsWhenSkipped;
            double winRateWhenPicked = timesPicked > 0 ? (double)winsWhenPicked / timesPicked : 0.0;
            double winRateWhenSkipped = timesSkipped > 0 ? (double)winsWhenSkipped / timesSkipped : 0.0;

            return new RelicWinRate(
                (string)row.RelicId, timesPicked, timesSkipped,
                winRateWhenPicked, winRateWhenSkipped);
        }).ToList();
    }

    public List<RelicPickRate> GetRelicPickRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                rc.RelicId,
                COUNT(*) AS TimesOffered,
                SUM(CASE WHEN rc.WasPicked = 1 THEN 1 ELSE 0 END) AS TimesPicked
            FROM RelicChoices rc
            JOIN Floors f ON rc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rc.RelicId
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int timesOffered = (int)(long)row.TimesOffered;
            int timesPicked = (int)(long)row.TimesPicked;
            double pickRate = timesOffered > 0 ? (double)timesPicked / timesOffered : 0.0;

            return new RelicPickRate((string)row.RelicId, timesOffered, timesPicked, pickRate);
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
