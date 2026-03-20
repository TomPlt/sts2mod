using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class EconomyAnalytics
{
    private readonly IDbConnection _connection;

    public EconomyAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<GoldEfficiency> GetGoldEfficiency(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                CASE
                    WHEN sub.TotalSpent = 0 THEN '0'
                    WHEN sub.TotalSpent < 100 THEN '1-99'
                    WHEN sub.TotalSpent < 300 THEN '100-299'
                    WHEN sub.TotalSpent < 500 THEN '300-499'
                    ELSE '500+'
                END AS Category,
                SUM(sub.TotalSpent) AS TotalSpent,
                COUNT(*) AS RunCount,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM (
                SELECT f.RunId, SUM(f.GoldSpent) AS TotalSpent
                FROM Floors f
                GROUP BY f.RunId
            ) sub
            JOIN Runs r ON sub.RunId = r.Id
            {where}
            GROUP BY Category
            ORDER BY Category
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int totalSpent = (int)(long)row.TotalSpent;
            int runCount = (int)(long)row.RunCount;
            int wins = (int)(long)row.Wins;
            double winRate = runCount > 0 ? (double)wins / runCount : 0.0;

            return new GoldEfficiency((string)row.Category, totalSpent, runCount, winRate);
        }).ToList();
    }

    public List<ShopPurchasePattern> GetShopPurchasePatterns(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var cardsBoughtSql = $"""
            SELECT 'Cards Bought' AS Category, COUNT(*) AS Count
            FROM CardChoices cc
            JOIN Floors f ON cc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            WHERE cc.WasBought = 1
            {(where.Length > 0 ? "AND " + where.Replace("WHERE ", "") : "")}
            """;

        var relicsBoughtSql = $"""
            UNION ALL
            SELECT 'Relics Bought' AS Category, COUNT(*) AS Count
            FROM RelicChoices rc
            JOIN Floors f ON rc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            WHERE rc.WasBought = 1
            {(where.Length > 0 ? "AND " + where.Replace("WHERE ", "") : "")}
            """;

        var removalsSql = $"""
            UNION ALL
            SELECT 'Card Removals' AS Category, COUNT(*) AS Count
            FROM CardRemovals cr
            JOIN Floors f ON cr.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            """;

        var sql = cardsBoughtSql + "\n" + relicsBoughtSql + "\n" + removalsSql;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
            new ShopPurchasePattern((string)row.Category, (int)(long)row.Count)
        ).ToList();
    }

    public List<CardRemovalImpact> GetCardRemovalImpact(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                cr.CardId,
                COUNT(*) AS TimesRemoved,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS WinsAfterRemoval
            FROM CardRemovals cr
            JOIN Floors f ON cr.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY cr.CardId
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int timesRemoved = (int)(long)row.TimesRemoved;
            int winsAfterRemoval = (int)(long)row.WinsAfterRemoval;
            double winRate = timesRemoved > 0 ? (double)winsAfterRemoval / timesRemoved : 0.0;

            return new CardRemovalImpact((string)row.CardId, timesRemoved, winsAfterRemoval, winRate);
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
