using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class RunAnalytics
{
    private readonly IDbConnection _connection;

    public RunAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public RunSummary GetOverallWinRate(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var summarySql = $"""
            SELECT
                COUNT(*) AS TotalRuns,
                SUM(CASE WHEN Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM Runs r
            {where}
            """;

        var summary = _connection.QuerySingle(summarySql, parameters);
        int totalRuns = (int)(long)summary.TotalRuns;
        int wins = (int)(long)summary.Wins;
        int losses = totalRuns - wins;
        double winRate = totalRuns > 0 ? (double)wins / totalRuns : 0.0;

        var charSql = $"""
            SELECT r.Character, COUNT(*) AS RunCount
            FROM Runs r
            {where}
            GROUP BY r.Character
            """;

        var charRows = _connection.Query(charSql, parameters).ToList();
        var runsByCharacter = new Dictionary<string, int>();
        foreach (var row in charRows)
        {
            runsByCharacter[(string)row.Character] = (int)(long)row.RunCount;
        }

        return new RunSummary(totalRuns, wins, losses, winRate, runsByCharacter);
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

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        return (where, parameters);
    }
}
