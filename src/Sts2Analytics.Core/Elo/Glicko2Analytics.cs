using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Elo;

public class Glicko2Analytics
{
    private readonly IDbConnection _connection;

    public Glicko2Analytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<Glicko2RatingResult> GetRatings(AnalyticsFilter? filter = null)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (filter?.Character is not null)
        {
            conditions.Add("Character = @Character");
            parameters.Add("Character", filter.Character);
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var sql = $"""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM Glicko2Ratings
            {where}
            ORDER BY Rating DESC
            """;

        return _connection.Query<Glicko2RatingResult>(sql, parameters).ToList();
    }

    public List<Glicko2HistoryResult> GetHistory(string cardId, string context = "overall")
    {
        return _connection.Query<Glicko2HistoryResult>("""
            SELECT gh.RatingBefore, gh.RatingAfter, gh.RdBefore, gh.RdAfter, gh.Timestamp
            FROM Glicko2History gh
            JOIN Glicko2Ratings gr ON gh.Glicko2RatingId = gr.Id
            WHERE gr.CardId = @CardId AND gr.Context = @Context
            ORDER BY gh.Id ASC
            """, new { CardId = cardId, Context = context }).ToList();
    }

    public int GetTrend(long ratingId, int lookback = 3)
    {
        var recentHistory = _connection.Query<(double RatingBefore, double RatingAfter)>("""
            SELECT RatingBefore, RatingAfter FROM Glicko2History
            WHERE Glicko2RatingId = @RatingId
            ORDER BY RunId DESC
            LIMIT @Lookback
            """, new { RatingId = ratingId, Lookback = lookback }).ToList();

        if (recentHistory.Count == 0) return 0;

        var netChange = recentHistory.Sum(h => h.RatingAfter - h.RatingBefore);
        return netChange switch
        {
            > 1.0 => 1,
            < -1.0 => -1,
            _ => 0
        };
    }

    public CardMatchupResult GetCardMatchups(string cardA, string cardB)
    {
        var aOverB = _connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM CardChoices a
            JOIN CardChoices b ON a.FloorId = b.FloorId
            WHERE a.CardId = @CardA AND a.WasPicked = 1
              AND b.CardId = @CardB AND b.WasPicked = 0
            """, new { CardA = cardA, CardB = cardB });

        var bOverA = _connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM CardChoices a
            JOIN CardChoices b ON a.FloorId = b.FloorId
            WHERE a.CardId = @CardB AND a.WasPicked = 1
              AND b.CardId = @CardA AND b.WasPicked = 0
            """, new { CardA = cardA, CardB = cardB });

        return new CardMatchupResult(cardA, cardB, aOverB, bOverA);
    }
}
