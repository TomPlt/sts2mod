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

public record Glicko2RatingResult(
    string CardId, string Character, string Context,
    double Rating, double RatingDeviation, double Volatility, long GamesPlayed);

public record Glicko2HistoryResult(
    double RatingBefore, double RatingAfter,
    double RdBefore, double RdAfter,
    string Timestamp);

public record CardMatchupResult(string CardA, string CardB, int AWinsOverB, int BWinsOverA);

public record PotionPickRate(string PotionId, int TimesOffered, int TimesPicked, double PickRate);
public record PotionUsageTiming(string PotionId, string RoomType, int TimesUsed);
public record PotionWasteRate(string PotionId, int TimesUsed, int TimesDiscarded, double WasteRate);
public record PathPatternWinRate(string PathSignature, int TotalRuns, int Wins, double WinRate);
public record EliteCorrelation(int EliteCount, int TotalRuns, int Wins, double WinRate);
public record EliteCorrelationByAct(int Act, int EliteCount, int TotalRuns, int Wins, double WinRate);
public record RestSiteDecisionRate(string Choice, int Count, int Wins, double WinRate);
public record RestSiteHpBucket(string Choice, int HpBucketMin, int HpBucketMax, int Count, int Wins, double WinRate);
public record RestSiteUpgradeImpact(string CardId, int TimesUpgraded, int Wins, double WinRate);
public record RestSiteActBreakdown(int Act, string Choice, int Count, int Wins, double WinRate, double AvgHpPercent);
public record GoldEfficiency(string Category, int TotalSpent, int RunCount, double WinRate);
public record ShopPurchasePattern(string Category, int Count);
public record CardRemovalImpact(string CardId, int TimesRemoved, int WinsAfterRemoval, double WinRate);
public record DamageByEncounter(string EncounterId, double AvgDamage, int SampleSize);
public record TurnsByEncounter(string EncounterId, double AvgTurns, int SampleSize);
public record DeathFloor(int ActIndex, int FloorIndex, string? EncounterId, int DeathCount);
public record HpThreshold(int FloorIndex, int HpBucket, int TotalRuns, int Wins, double WinRate);

public record PoolRating(double Elo, double Rd);

public record ModCardStats(
    string CardId, double Elo, double Rd, double PickRate,
    double WinRatePicked, double WinRateSkipped, double Delta,
    double EloAct1, double RdAct1, double EloAct2, double RdAct2, double EloAct3, double RdAct3,
    string? BlindSpot = null, double BlindSpotScore = 0,
    double BlindSpotPickRate = 0, double BlindSpotWinRateDelta = 0,
    double CombatElo = 0, double CombatRd = 350,
    Dictionary<string, PoolRating>? CombatByPool = null);

public record ModAncientStats(
    string ChoiceKey, double Rating, double Rd,
    double RatingNeow, double RdNeow,
    double RatingPostAct1, double RdPostAct1,
    double RatingPostAct2, double RdPostAct2);

public record ModOverlayData(
    int Version, string ExportedAt, double SkipElo,
    Dictionary<string, double> SkipEloByAct,
    List<ModCardStats> Cards,
    List<ModAncientStats>? AncientChoices = null,
    List<MapIntelCharacter>? MapIntel = null,
    Dictionary<string, PoolRating>? EncounterPools = null,
    Dictionary<string, PoolRating>? EncounterRatings = null);

public record EncounterDamage(string EncounterId, double AvgDamage, double StdDev, int SampleSize, int MaxDamage);

public record MapIntelPool(string Pool, double AvgDamage, double StdDev, int SampleSize, List<string> Encounters,
    List<EncounterDamage>? EncounterDetails = null);

public record MapIntelAct(int ActIndex, List<MapIntelPool> Pools,
    int Runs = 0, int Wins = 0, double WinRate = 0,
    List<EliteCorrelation>? EliteWinRates = null);

public record MapIntelCharacter(string Character, int Runs, int Wins, double WinRate,
    List<MapIntelAct> Acts);
