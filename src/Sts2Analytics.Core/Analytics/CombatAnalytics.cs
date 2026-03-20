using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class CombatAnalytics
{
    private readonly IDbConnection _connection;

    public CombatAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<DamageByEncounter> GetDamageTakenByEncounter(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var baseCondition = "f.EncounterId IS NOT NULL";
        var fullWhere = where.Length > 0
            ? where + " AND " + baseCondition
            : "WHERE " + baseCondition;

        var sql = $"""
            SELECT
                f.EncounterId,
                AVG(CAST(f.DamageTaken AS REAL)) AS AvgDamage,
                COUNT(*) AS SampleSize
            FROM Floors f
            JOIN Runs r ON f.RunId = r.Id
            {fullWhere}
            GROUP BY f.EncounterId
            ORDER BY AvgDamage DESC
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
            new DamageByEncounter(
                (string)row.EncounterId,
                (double)row.AvgDamage,
                (int)(long)row.SampleSize)
        ).ToList();
    }

    public List<TurnsByEncounter> GetTurnsByEncounter(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var baseCondition = "f.EncounterId IS NOT NULL";
        var fullWhere = where.Length > 0
            ? where + " AND " + baseCondition
            : "WHERE " + baseCondition;

        var sql = $"""
            SELECT
                f.EncounterId,
                AVG(CAST(f.TurnsTaken AS REAL)) AS AvgTurns,
                COUNT(*) AS SampleSize
            FROM Floors f
            JOIN Runs r ON f.RunId = r.Id
            {fullWhere}
            GROUP BY f.EncounterId
            ORDER BY AvgTurns DESC
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
            new TurnsByEncounter(
                (string)row.EncounterId,
                (double)row.AvgTurns,
                (int)(long)row.SampleSize)
        ).ToList();
    }

    public List<DeathFloor> GetDeathFloorDistribution(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var baseCondition = "r.Win = 0";
        var fullWhere = where.Length > 0
            ? where + " AND " + baseCondition
            : "WHERE " + baseCondition;

        var sql = $"""
            SELECT
                f.ActIndex,
                f.FloorIndex,
                f.EncounterId,
                COUNT(*) AS DeathCount
            FROM Floors f
            JOIN Runs r ON f.RunId = r.Id
            {fullWhere}
            AND f.FloorIndex = (
                SELECT MAX(f2.FloorIndex)
                FROM Floors f2
                WHERE f2.RunId = f.RunId
                AND f2.ActIndex = (
                    SELECT MAX(f3.ActIndex)
                    FROM Floors f3
                    WHERE f3.RunId = f.RunId
                )
            )
            AND f.ActIndex = (
                SELECT MAX(f4.ActIndex)
                FROM Floors f4
                WHERE f4.RunId = f.RunId
            )
            GROUP BY f.ActIndex, f.FloorIndex, f.EncounterId
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
            new DeathFloor(
                (int)(long)row.ActIndex,
                (int)(long)row.FloorIndex,
                row.EncounterId as string,
                (int)(long)row.DeathCount)
        ).ToList();
    }

    public List<HpThreshold> GetHpThresholdAnalysis(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                f.FloorIndex,
                CASE
                    WHEN f.MaxHp = 0 THEN 0
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.25 THEN 25
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.50 THEN 50
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.75 THEN 75
                    ELSE 100
                END AS HpBucket,
                COUNT(*) AS TotalRuns,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM Floors f
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY f.FloorIndex, HpBucket
            ORDER BY f.FloorIndex, HpBucket
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int totalRuns = (int)(long)row.TotalRuns;
            int wins = (int)(long)row.Wins;
            double winRate = totalRuns > 0 ? (double)wins / totalRuns : 0.0;

            return new HpThreshold(
                (int)(long)row.FloorIndex,
                (int)(long)row.HpBucket,
                totalRuns,
                wins,
                winRate);
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
