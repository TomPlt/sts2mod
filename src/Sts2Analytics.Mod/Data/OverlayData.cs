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

public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("skipEloByAct")] Dictionary<string, double>? SkipEloByAct,
    [property: JsonPropertyName("cards")] List<CardStats> Cards);
