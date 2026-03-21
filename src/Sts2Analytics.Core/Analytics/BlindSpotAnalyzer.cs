using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;
using static Sts2Analytics.Core.Analytics.BlindSpotConstants;

namespace Sts2Analytics.Core.Analytics;

public class BlindSpotAnalyzer
{
    private readonly IDbConnection _connection;

    public BlindSpotAnalyzer(IDbConnection connection) => _connection = connection;

    public List<BlindSpotResult> Analyze(string? character = null, int? actIndex = null)
    {
        var cardAnalytics = new CardAnalytics(_connection);
        var filter = new AnalyticsFilter(Character: character, ActIndex: actIndex);

        var pickRates = cardAnalytics.GetCardPickRates(filter).ToDictionary(c => c.CardId);
        var winRates = cardAnalytics.GetCardWinRates(filter).ToDictionary(c => c.CardId);

        // Build context string matching Glicko2Ratings convention
        string context;
        if (character != null && actIndex != null)
            context = $"{character}_ACT{actIndex + 1}";
        else if (character != null)
            context = character;
        else
            context = "overall";

        var ratings = _connection.Query<RatingRow>(
            "SELECT CardId, Rating, RatingDeviation FROM Glicko2Ratings WHERE Context = @Context",
            new { Context = context }).ToDictionary(r => r.CardId);

        var skipRating = ratings.TryGetValue("SKIP", out var skip) ? skip.Rating : 1500.0;

        var results = new List<BlindSpotResult>();

        foreach (var (cardId, pick) in pickRates)
        {
            if (cardId == "SKIP") continue;
            if (pick.TimesOffered < MinSampleSize) continue;
            if (!ratings.TryGetValue(cardId, out var rating)) continue;
            if (!winRates.TryGetValue(cardId, out var win)) continue;

            var confidenceWeight = Math.Max(0, 1.0 - rating.RatingDeviation / 350.0);
            if (confidenceWeight < MinConfidenceWeight) continue;

            var expectedPickRate = 1.0 / (1.0 + Math.Exp(-(rating.Rating - skipRating) / LogisticDivisor));
            var pickRateDeviation = pick.PickRate - expectedPickRate;
            var winRateDelta = win.WinRateDelta;

            var score = Math.Abs(pickRateDeviation) * Math.Abs(winRateDelta) * confidenceWeight;
            if (score < ScoreThreshold) continue;

            string? blindSpotType = null;
            if (pickRateDeviation > 0 && winRateDelta < 0)
                blindSpotType = "over_pick";
            else if (pickRateDeviation < 0 && winRateDelta > 0)
                blindSpotType = "under_pick";

            if (blindSpotType is null) continue;

            results.Add(new BlindSpotResult(
                cardId, context, blindSpotType, score,
                pick.PickRate, expectedPickRate, winRateDelta, pick.TimesOffered));
        }

        PersistResults(results, context);

        return results.OrderByDescending(r => r.Score).ToList();
    }

    public List<BlindSpotResult> AnalyzeAllContexts()
    {
        var results = new List<BlindSpotResult>();
        results.AddRange(Analyze()); // overall

        var characters = _connection.Query<string>(
            "SELECT DISTINCT Character FROM Runs").ToList();

        foreach (var character in characters)
        {
            results.AddRange(Analyze(character)); // per-character
            for (int act = 0; act < 3; act++)
                results.AddRange(Analyze(character, act)); // per-character-per-act
        }

        return results;
    }

    private void PersistResults(List<BlindSpotResult> results, string context)
    {
        _connection.Execute(
            "DELETE FROM BlindSpots WHERE Context = @Context",
            new { Context = context });

        foreach (var r in results)
        {
            _connection.Execute("""
                INSERT INTO BlindSpots (CardId, Context, BlindSpotType, Score,
                    PickRate, ExpectedPickRate, WinRateDelta, GamesAnalyzed, LastUpdated)
                VALUES (@CardId, @Context, @BlindSpotType, @Score,
                    @PickRate, @ExpectedPickRate, @WinRateDelta, @GamesAnalyzed, @LastUpdated)
                """, new {
                    r.CardId, r.Context, r.BlindSpotType, r.Score,
                    r.PickRate, r.ExpectedPickRate, r.WinRateDelta, r.GamesAnalyzed,
                    LastUpdated = DateTime.UtcNow.ToString("o")
                });
        }
    }

    private record RatingRow(string CardId, double Rating, double RatingDeviation);
}
