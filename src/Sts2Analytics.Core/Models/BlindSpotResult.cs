namespace Sts2Analytics.Core.Models;

public record BlindSpotResult(
    string CardId, string Context, string BlindSpotType,
    double Score, double PickRate, double ExpectedPickRate,
    double WinRateDelta, int GamesAnalyzed);
