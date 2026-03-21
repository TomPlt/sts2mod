using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class PathAnalytics
{
    private readonly IDbConnection _connection;

    public PathAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<EliteCorrelation> GetEliteCountCorrelation(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                sub.EliteCount,
                COUNT(*) AS TotalRuns,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM (
                SELECT f.RunId, COUNT(*) AS EliteCount
                FROM Floors f
                WHERE f.MapPointType = 'elite'
                GROUP BY f.RunId
            ) sub
            JOIN Runs r ON sub.RunId = r.Id
            {where}
            GROUP BY sub.EliteCount
            ORDER BY sub.EliteCount
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int eliteCount = (int)(long)row.EliteCount;
            int totalRuns = (int)(long)row.TotalRuns;
            int wins = (int)(long)row.Wins;
            double winRate = totalRuns > 0 ? (double)wins / totalRuns : 0.0;

            return new EliteCorrelation(eliteCount, totalRuns, wins, winRate);
        }).ToList();
    }

    public List<EliteCorrelationByAct> GetEliteCountCorrelationByAct(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                sub.ActIndex AS Act,
                sub.EliteCount,
                COUNT(*) AS TotalRuns,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM (
                SELECT f.RunId, f.ActIndex, COUNT(*) AS EliteCount
                FROM Floors f
                WHERE f.MapPointType = 'elite'
                GROUP BY f.RunId, f.ActIndex
            ) sub
            JOIN Runs r ON sub.RunId = r.Id
            {where}
            GROUP BY sub.ActIndex, sub.EliteCount
            ORDER BY sub.ActIndex, sub.EliteCount
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int act = (int)(long)row.Act;
            int eliteCount = (int)(long)row.EliteCount;
            int totalRuns = (int)(long)row.TotalRuns;
            int wins = (int)(long)row.Wins;
            double winRate = totalRuns > 0 ? (double)wins / totalRuns : 0.0;

            return new EliteCorrelationByAct(act, eliteCount, totalRuns, wins, winRate);
        }).ToList();
    }

    public List<PathPatternWinRate> GetPathPatternWinRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                sub.PathSignature,
                COUNT(*) AS TotalRuns,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM (
                SELECT
                    f.RunId,
                    f.ActIndex || ':' || GROUP_CONCAT(f.MapPointType, '-') AS PathSignature
                FROM Floors f
                GROUP BY f.RunId, f.ActIndex
            ) sub
            JOIN Runs r ON sub.RunId = r.Id
            {where}
            GROUP BY sub.PathSignature
            ORDER BY COUNT(*) DESC
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int totalRuns = (int)(long)row.TotalRuns;
            int wins = (int)(long)row.Wins;
            double winRate = totalRuns > 0 ? (double)wins / totalRuns : 0.0;

            return new PathPatternWinRate((string)row.PathSignature, totalRuns, wins, winRate);
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
