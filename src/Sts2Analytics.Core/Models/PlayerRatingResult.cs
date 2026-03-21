namespace Sts2Analytics.Core.Models;

public record PlayerRatingResult(
    string Context, double Rating, double RatingDeviation,
    double Volatility, int GamesPlayed);

public record PlayerRatingHistoryResult(
    string Context, long RunId,
    double RatingBefore, double RatingAfter,
    double RdBefore, double RdAfter,
    double VolatilityBefore, double VolatilityAfter,
    string Opponent, double OpponentRating, double Outcome);
