using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Elo;

public class EloAnalytics
{
    private readonly IDbConnection _connection;

    public EloAnalytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<EloRatingResult> GetCardEloRatings(AnalyticsFilter? filter = null)
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
            SELECT CardId, Character, Context, Rating, GamesPlayed
            FROM EloRatings
            {where}
            ORDER BY Rating DESC
            """;

        return _connection.Query<EloRatingResult>(sql, parameters).ToList();
    }

    public List<EloHistoryResult> GetCardEloHistory(string cardId, string context = "overall")
    {
        var sql = """
            SELECT eh.RatingBefore, eh.RatingAfter, eh.Timestamp
            FROM EloHistory eh
            JOIN EloRatings er ON eh.EloRatingId = er.Id
            WHERE er.CardId = @CardId AND er.Context = @Context
            ORDER BY eh.Id ASC
            """;

        return _connection.Query<EloHistoryResult>(sql, new { CardId = cardId, Context = context }).ToList();
    }

    public CardMatchupResult GetCardMatchups(string cardA, string cardB)
    {
        // Count wins when A picked over B (both on same floor, A picked, B not picked)
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

    public List<EloRatingResult> GetSkipEloByContext()
    {
        return _connection.Query<EloRatingResult>("""
            SELECT CardId, Character, Context, Rating, GamesPlayed
            FROM EloRatings
            WHERE CardId = 'SKIP'
            ORDER BY Context
            """).ToList();
    }
}
