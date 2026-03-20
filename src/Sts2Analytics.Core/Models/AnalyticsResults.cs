namespace Sts2Analytics.Core.Models;

public record CardWinRate(
    string CardId, int TimesPicked, int TimesSkipped,
    int WinsWhenPicked, int WinsWhenSkipped,
    double WinRateWhenPicked, double WinRateWhenSkipped, double WinRateDelta);

public record CardPickRate(string CardId, int TimesOffered, int TimesPicked, double PickRate);

public record RelicWinRate(
    string RelicId, int TimesPicked, int TimesSkipped,
    double WinRateWhenPicked, double WinRateWhenSkipped);

public record RelicPickRate(string RelicId, int TimesOffered, int TimesPicked, double PickRate);

public record RunSummary(int TotalRuns, int Wins, int Losses, double WinRate,
    Dictionary<string, int> RunsByCharacter);

public record CardImpactScore(string CardId, double PickRate, double WinRateDelta, double ImpactScore);
