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
    [property: JsonPropertyName("blindSpotWinRateDelta")] double BlindSpotWinRateDelta = 0);

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

public record MapIntelPool(
    [property: JsonPropertyName("pool")] string Pool,
    [property: JsonPropertyName("avgDamage")] double AvgDamage,
    [property: JsonPropertyName("sampleSize")] int SampleSize,
    [property: JsonPropertyName("encounters")] List<string> Encounters);

public record MapIntelAct(
    [property: JsonPropertyName("actIndex")] int ActIndex,
    [property: JsonPropertyName("pools")] List<MapIntelPool> Pools);

public record MapIntelCharacter(
    [property: JsonPropertyName("character")] string Character,
    [property: JsonPropertyName("acts")] List<MapIntelAct> Acts);

public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("skipEloByAct")] Dictionary<string, double>? SkipEloByAct,
    [property: JsonPropertyName("cards")] List<CardStats> Cards,
    [property: JsonPropertyName("ancientChoices")] List<AncientStats>? AncientChoices = null,
    [property: JsonPropertyName("mapIntel")] List<MapIntelCharacter>? MapIntel = null);
