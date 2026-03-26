using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SpireOracle.Data;

public record RefEventOption(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("effect")] string Effect,
    [property: JsonPropertyName("notes")] string? Notes = null);

public record RefEvent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("condition")] string? Condition,
    [property: JsonPropertyName("options")] List<RefEventOption> Options);

public record RefEnemy(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("hp")] string? Hp = null,
    [property: JsonPropertyName("moves")] List<string>? Moves = null,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("monsters")] List<string>? Monsters = null);

public record RefAct(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("easyPool")] List<string>? EasyPool = null,
    [property: JsonPropertyName("hardPool")] List<string>? HardPool = null,
    [property: JsonPropertyName("elites")] List<object>? Elites = null,
    [property: JsonPropertyName("bosses")] List<object>? Bosses = null,
    [property: JsonPropertyName("events")] List<RefEvent>? Events = null);

public record RefData(
    [property: JsonPropertyName("acts")] List<RefAct> Acts,
    [property: JsonPropertyName("sharedEvents")] List<RefEvent>? SharedEvents = null);
