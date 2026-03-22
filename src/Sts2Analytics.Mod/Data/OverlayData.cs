using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SpireOracle.Data;

public record CardStats(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("elo")] double Elo,
    [property: JsonPropertyName("rd")] double Rd = 350,
    [property: JsonPropertyName("pickRate")] double PickRate = 0,
    [property: JsonPropertyName("winRatePicked")] double WinRatePicked = 0,
    [property: JsonPropertyName("winRateSkipped")] double WinRateSkipped = 0,
    [property: JsonPropertyName("delta")] double Delta = 0,
    [property: JsonPropertyName("eloAct1")] double EloAct1 = 0,
    [property: JsonPropertyName("rdAct1")] double RdAct1 = 350,
    [property: JsonPropertyName("eloAct2")] double EloAct2 = 0,
    [property: JsonPropertyName("rdAct2")] double RdAct2 = 350,
    [property: JsonPropertyName("eloAct3")] double EloAct3 = 0,
    [property: JsonPropertyName("rdAct3")] double RdAct3 = 350,
    [property: JsonPropertyName("blindSpot")] string? BlindSpot = null,
    [property: JsonPropertyName("blindSpotScore")] double BlindSpotScore = 0,
    [property: JsonPropertyName("blindSpotPickRate")] double BlindSpotPickRate = 0,
    [property: JsonPropertyName("blindSpotWinRateDelta")] double BlindSpotWinRateDelta = 0,
    [property: JsonPropertyName("combatElo")] double CombatElo = 0,
    [property: JsonPropertyName("combatRd")] double CombatRd = 350,
    [property: JsonPropertyName("combatByPool")] Dictionary<string, PoolRating>? CombatByPool = null);

public record PoolRating(
    [property: JsonPropertyName("elo")] double Elo,
    [property: JsonPropertyName("rd")] double Rd);

public record AncientStats(
    [property: JsonPropertyName("choiceKey")] string ChoiceKey,
    [property: JsonPropertyName("rating")] double Rating,
    [property: JsonPropertyName("rd")] double Rd = 350,
    [property: JsonPropertyName("ratingNeow")] double RatingNeow = 0,
    [property: JsonPropertyName("rdNeow")] double RdNeow = 350,
    [property: JsonPropertyName("ratingPostAct1")] double RatingPostAct1 = 0,
    [property: JsonPropertyName("rdPostAct1")] double RdPostAct1 = 350,
    [property: JsonPropertyName("ratingPostAct2")] double RatingPostAct2 = 0,
    [property: JsonPropertyName("rdPostAct2")] double RdPostAct2 = 350);

public record EncounterDamage(
    [property: JsonPropertyName("encounterId")] string EncounterId,
    [property: JsonPropertyName("avgDamage")] double AvgDamage,
    [property: JsonPropertyName("stdDev")] double StdDev = 0,
    [property: JsonPropertyName("sampleSize")] int SampleSize = 0,
    [property: JsonPropertyName("maxDamage")] int MaxDamage = 0);

public record MapIntelPool(
    [property: JsonPropertyName("pool")] string Pool,
    [property: JsonPropertyName("avgDamage")] double AvgDamage,
    [property: JsonPropertyName("stdDev")] double StdDev = 0,
    [property: JsonPropertyName("sampleSize")] int SampleSize = 0,
    [property: JsonPropertyName("encounters")] List<string>? Encounters = null,
    [property: JsonPropertyName("encounterDetails")] List<EncounterDamage>? EncounterDetails = null);

public record EliteWinRate(
    [property: JsonPropertyName("eliteCount")] int EliteCount,
    [property: JsonPropertyName("totalRuns")] int TotalRuns,
    [property: JsonPropertyName("wins")] int Wins,
    [property: JsonPropertyName("winRate")] double WinRate);

public record MapIntelAct(
    [property: JsonPropertyName("actIndex")] int ActIndex,
    [property: JsonPropertyName("pools")] List<MapIntelPool> Pools,
    [property: JsonPropertyName("runs")] int Runs = 0,
    [property: JsonPropertyName("wins")] int Wins = 0,
    [property: JsonPropertyName("winRate")] double WinRate = 0,
    [property: JsonPropertyName("eliteWinRates")] List<EliteWinRate>? EliteWinRates = null);

public record MapIntelCharacter(
    [property: JsonPropertyName("character")] string Character,
    [property: JsonPropertyName("runs")] int Runs = 0,
    [property: JsonPropertyName("wins")] int Wins = 0,
    [property: JsonPropertyName("winRate")] double WinRate = 0,
    [property: JsonPropertyName("acts")] List<MapIntelAct>? Acts = null);

public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("skipEloByAct")] Dictionary<string, double>? SkipEloByAct,
    [property: JsonPropertyName("cards")] List<CardStats> Cards,
    [property: JsonPropertyName("ancientChoices")] List<AncientStats>? AncientChoices = null,
    [property: JsonPropertyName("mapIntel")] List<MapIntelCharacter>? MapIntel = null,
    [property: JsonPropertyName("encounterPools")] Dictionary<string, PoolRating>? EncounterPools = null,
    [property: JsonPropertyName("encounterRatings")] Dictionary<string, PoolRating>? EncounterRatings = null);
