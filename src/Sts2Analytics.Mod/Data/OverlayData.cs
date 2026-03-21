using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SpireOracle.Data;

public record CardStats(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("elo")] double Elo,
    [property: JsonPropertyName("pickRate")] double PickRate,
    [property: JsonPropertyName("winRatePicked")] double WinRatePicked,
    [property: JsonPropertyName("winRateSkipped")] double WinRateSkipped,
    [property: JsonPropertyName("delta")] double Delta,
    [property: JsonPropertyName("eloAct1")] double EloAct1 = 0,
    [property: JsonPropertyName("eloAct2")] double EloAct2 = 0,
    [property: JsonPropertyName("eloAct3")] double EloAct3 = 0);

public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("skipEloByAct")] Dictionary<string, double>? SkipEloByAct,
    [property: JsonPropertyName("cards")] List<CardStats> Cards);
