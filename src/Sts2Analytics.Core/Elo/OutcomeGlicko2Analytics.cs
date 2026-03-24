using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Elo;

public class OutcomeGlicko2Analytics
{
    private readonly IDbConnection _connection;

    public OutcomeGlicko2Analytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<Glicko2RatingResult> GetRatings(string? character = null)
    {
        var where = character is not null ? "WHERE Character = @Character" : "";
        var sql = $"""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM OutcomeGlicko2Ratings
            {where}
            ORDER BY Rating DESC
            """;
        return _connection.Query<Glicko2RatingResult>(sql, new { Character = character }).ToList();
    }
}
