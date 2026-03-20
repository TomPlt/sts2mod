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

public record EloRatingResult(string CardId, string Character, string Context, double Rating, long GamesPlayed);

public record EloHistoryResult(double RatingBefore, double RatingAfter, string Timestamp);

public record CardMatchupResult(string CardA, string CardB, int AWinsOverB, int BWinsOverA);

public record PotionPickRate(string PotionId, int TimesOffered, int TimesPicked, double PickRate);
public record PotionUsageTiming(string PotionId, string RoomType, int TimesUsed);
public record PotionWasteRate(string PotionId, int TimesUsed, int TimesDiscarded, double WasteRate);
public record PathPatternWinRate(string PathSignature, int TotalRuns, int Wins, double WinRate);
public record EliteCorrelation(int EliteCount, int TotalRuns, int Wins, double WinRate);
public record GoldEfficiency(string Category, int TotalSpent, int RunCount, double WinRate);
public record ShopPurchasePattern(string Category, int Count);
public record CardRemovalImpact(string CardId, int TimesRemoved, int WinsAfterRemoval, double WinRate);
public record DamageByEncounter(string EncounterId, double AvgDamage, int SampleSize);
public record TurnsByEncounter(string EncounterId, double AvgTurns, int SampleSize);
public record DeathFloor(int ActIndex, int FloorIndex, string? EncounterId, int DeathCount);
public record HpThreshold(int FloorIndex, int HpBucket, int TotalRuns, int Wins, double WinRate);
