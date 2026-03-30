using System.Data;
using System.Text;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class CardAnalytics
{
    private readonly IDbConnection _connection;

    public CardAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<CardWinRate> GetCardWinRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            WITH RunCard AS (
                SELECT
                    CASE WHEN cc.UpgradeLevel > 0 THEN cc.CardId || '+' || cc.UpgradeLevel ELSE cc.CardId END AS CardId,
                    f.RunId,
                    MAX(cc.WasPicked) AS EverPicked,
                    r.Win
                FROM CardChoices cc
                JOIN Floors f ON cc.FloorId = f.Id
                JOIN Runs r ON f.RunId = r.Id
                {where}
                GROUP BY CASE WHEN cc.UpgradeLevel > 0 THEN cc.CardId || '+' || cc.UpgradeLevel ELSE cc.CardId END, f.RunId
            )
            SELECT
                CardId,
                SUM(EverPicked) AS TimesPicked,
                SUM(1 - EverPicked) AS TimesSkipped,
                SUM(CASE WHEN EverPicked = 1 AND Win = 1 THEN 1 ELSE 0 END) AS WinsWhenPicked,
                SUM(CASE WHEN EverPicked = 0 AND Win = 1 THEN 1 ELSE 0 END) AS WinsWhenSkipped
            FROM RunCard
            GROUP BY CardId
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
            double winRateDelta = winRateWhenPicked - winRateWhenSkipped;

            return new CardWinRate(
                (string)row.CardId, timesPicked, timesSkipped,
                winsWhenPicked, winsWhenSkipped,
                winRateWhenPicked, winRateWhenSkipped, winRateDelta);
        }).ToList();
    }

    public List<CardPickRate> GetCardPickRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                CASE WHEN cc.UpgradeLevel > 0 THEN cc.CardId || '+' || cc.UpgradeLevel ELSE cc.CardId END AS CardId,
                COUNT(*) AS TimesOffered,
                SUM(CASE WHEN cc.WasPicked = 1 THEN 1 ELSE 0 END) AS TimesPicked
            FROM CardChoices cc
            JOIN Floors f ON cc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY CASE WHEN cc.UpgradeLevel > 0 THEN cc.CardId || '+' || cc.UpgradeLevel ELSE cc.CardId END
            """;

        var rows = _connection.Query(sql, parameters).ToList();

        return rows.Select(row =>
        {
            int timesOffered = (int)(long)row.TimesOffered;
            int timesPicked = (int)(long)row.TimesPicked;
            double pickRate = timesOffered > 0 ? (double)timesPicked / timesOffered : 0.0;

            return new CardPickRate((string)row.CardId, timesOffered, timesPicked, pickRate);
        }).ToList();
    }

    public List<CardImpactScore> GetCardImpactScores(AnalyticsFilter? filter = null)
    {
        var winRates = GetCardWinRates(filter);
        var pickRates = GetCardPickRates(filter);

        var pickRateMap = pickRates.ToDictionary(p => p.CardId, p => p.PickRate);

        return winRates
            .Where(wr => pickRateMap.ContainsKey(wr.CardId))
            .Select(wr =>
            {
                var pickRate = pickRateMap[wr.CardId];
                var impactScore = pickRate * Math.Abs(wr.WinRateDelta);
                return new CardImpactScore(wr.CardId, pickRate, wr.WinRateDelta, impactScore);
            })
            .OrderByDescending(s => s.ImpactScore)
            .ToList();
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
