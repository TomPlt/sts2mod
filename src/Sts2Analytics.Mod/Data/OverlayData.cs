using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SpireOracle.Data;

public record CardStats(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("elo")] double Elo,
    [property: JsonPropertyName("pickRate")] double PickRate,
    [property: JsonPropertyName("winRatePicked")] double WinRatePicked,
    [property: JsonPropertyName("winRateSkipped")] double WinRateSkipped,
    [property: JsonPropertyName("delta")] double Delta);

public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("cards")] List<CardStats> Cards);
