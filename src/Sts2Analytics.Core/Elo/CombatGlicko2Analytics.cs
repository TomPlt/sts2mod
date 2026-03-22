using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Elo;

public class CombatGlicko2Analytics
{
    private readonly IDbConnection _connection;

    public CombatGlicko2Analytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<Glicko2RatingResult> GetRatings(string? character = null)
    {
        var where = character is not null ? "WHERE Character = @Character" : "";
        var sql = $"""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM CombatGlicko2Ratings
            {where}
            ORDER BY Rating DESC
            """;
        return _connection.Query<Glicko2RatingResult>(sql, new { Character = character }).ToList();
    }

    public List<Glicko2RatingResult> GetPoolRatings()
    {
        return _connection.Query<Glicko2RatingResult>("""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM CombatGlicko2Ratings
            WHERE CardId LIKE 'POOL.%'
            ORDER BY Context, Character
            """).ToList();
    }

    public List<Glicko2RatingResult> GetEncounterRatings()
    {
        return _connection.Query<Glicko2RatingResult>("""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM CombatGlicko2Ratings
            WHERE CardId LIKE 'ENC.%'
            ORDER BY Rating DESC
            """).ToList();
    }

    /// Compute deck Elo as 1/RD-weighted mean of card combat ratings.
    public static (double Mu, double Rd) ComputeDeckElo(
        IEnumerable<(double Rating, double Rd)> cardRatings)
    {
        double sumWeightedMu = 0;
        double sumWeight = 0;
        double sumPrecision = 0;

        foreach (var (rating, rd) in cardRatings)
        {
            if (rd <= 0) continue;
            var weight = 1.0 / rd;
            sumWeightedMu += rating * weight;
            sumWeight += weight;
            sumPrecision += 1.0 / (rd * rd);
        }

        if (sumWeight == 0) return (1500.0, 350.0);

        var deckMu = sumWeightedMu / sumWeight;
        var deckRd = 1.0 / Math.Sqrt(sumPrecision);
        return (deckMu, deckRd);
    }
}
