using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using SpireOracle.UI;

namespace SpireOracle.Data;

public static class DataLoader
{
    private static Dictionary<string, CardStats>? _cards;
    private static double _skipElo;
    private static Dictionary<string, double>? _skipEloByAct;
    private static double _skipOutcomeElo;
    private static Dictionary<string, double>? _skipOutcomeEloByAct;
    private static Dictionary<string, AncientStats>? _ancientChoices;
    private static Dictionary<string, MapIntelCharacter>? _mapIntel;
    private static Dictionary<string, RefAct>? _refActs;
    private static List<RefEvent>? _sharedEvents;
    private static Dictionary<string, RefEnemy>? _enemyIndex;
    private static Dictionary<string, PoolRating>? _encounterPools;
    private static Dictionary<string, PoolRating>? _encounterRatings;
    private static Dictionary<string, List<int>>? _damageDistributions;
    private static List<PlayerRunCount>? _playerRunCounts;

    public static bool IsLoaded => _cards != null;
    public static double SkipElo => _skipElo;

    public static double GetSkipElo(string? characterContext = null, int? actIndex = null)
    {
        if (_skipEloByAct == null) return _skipElo;

        if (characterContext != null && actIndex != null)
        {
            var key = $"{characterContext.Replace("CHARACTER.", "").ToLower()}_act{actIndex + 1}";
            if (_skipEloByAct.TryGetValue(key, out var elo)) return elo;
        }
        if (characterContext != null)
        {
            var key = characterContext.Replace("CHARACTER.", "").ToLower();
            if (_skipEloByAct.TryGetValue(key, out var elo)) return elo;
        }
        if (actIndex != null)
        {
            // Try any character's act rating as fallback
            var actKey = $"act{actIndex + 1}";
            foreach (var kvp in _skipEloByAct)
            {
                if (kvp.Key.EndsWith(actKey)) return kvp.Value;
            }
        }
        return _skipElo;
    }

    public static Dictionary<string, double>? SkipEloByAct => _skipEloByAct;
    public static double SkipOutcomeElo => _skipOutcomeElo;

    public static double GetSkipOutcomeElo(string? characterContext = null, int? actIndex = null)
    {
        if (_skipOutcomeEloByAct == null) return _skipOutcomeElo;

        if (characterContext != null && actIndex != null)
        {
            var key = $"{characterContext.Replace("CHARACTER.", "").ToLower()}_act{actIndex + 1}";
            if (_skipOutcomeEloByAct.TryGetValue(key, out var elo)) return elo;
        }
        if (characterContext != null)
        {
            var key = characterContext.Replace("CHARACTER.", "").ToLower();
            if (_skipOutcomeEloByAct.TryGetValue(key, out var elo)) return elo;
        }
        if (actIndex != null)
        {
            var actKey = $"act{actIndex + 1}";
            foreach (var kvp in _skipOutcomeEloByAct)
            {
                if (kvp.Key.EndsWith(actKey)) return kvp.Value;
            }
        }
        return _skipOutcomeElo;
    }

    public static bool Load(string modPath)
    {
        try
        {
            var filePath = Path.Combine(modPath, "overlay_data.json");
            if (!File.Exists(filePath))
            {
                DebugLogOverlay.LogErr($"[SpireOracle] overlay_data.json not found at: {filePath}");
                return false;
            }

            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<OverlayData>(json);

            if (data == null || data.Cards == null)
            {
                DebugLogOverlay.LogErr("[SpireOracle] Failed to deserialize overlay_data.json");
                return false;
            }

            _skipElo = data.SkipElo;
            _skipEloByAct = data.SkipEloByAct;
            _skipOutcomeElo = data.SkipOutcomeElo;
            _skipOutcomeEloByAct = data.SkipOutcomeEloByAct;
            _cards = new Dictionary<string, CardStats>(StringComparer.OrdinalIgnoreCase);

            foreach (var card in data.Cards)
            {
                if (!string.IsNullOrEmpty(card.CardId))
                {
                    _cards[card.CardId] = card;
                }
            }

            DebugLogOverlay.Log($"[SpireOracle] Loaded {_cards.Count} cards, skip Elo = {_skipElo:F0}");

            _ancientChoices = new Dictionary<string, AncientStats>(StringComparer.OrdinalIgnoreCase);
            if (data.AncientChoices != null)
            {
                foreach (var ac in data.AncientChoices)
                {
                    if (!string.IsNullOrEmpty(ac.ChoiceKey))
                        _ancientChoices[ac.ChoiceKey] = ac;
                }
            }
            DebugLogOverlay.Log($"[SpireOracle] Loaded {_ancientChoices.Count} ancient choices");

            _mapIntel = new Dictionary<string, MapIntelCharacter>(StringComparer.OrdinalIgnoreCase);
            if (data.MapIntel != null)
            {
                foreach (var mic in data.MapIntel)
                {
                    if (!string.IsNullOrEmpty(mic.Character))
                        _mapIntel[mic.Character] = mic;
                }
            }
            DebugLogOverlay.Log($"[SpireOracle] Loaded map intel for {_mapIntel.Count} characters");

            _encounterPools = data.EncounterPools ?? new Dictionary<string, PoolRating>();
            _encounterRatings = data.EncounterRatings ?? new Dictionary<string, PoolRating>();
            _damageDistributions = data.DamageDistributions ?? new Dictionary<string, List<int>>();
            _playerRunCounts = data.PlayerRunCounts ?? new List<PlayerRunCount>();
            DebugLogOverlay.Log($"[SpireOracle] Loaded {_encounterPools.Count} pool ratings, {_encounterRatings.Count} encounter ratings, {_damageDistributions.Count} damage distributions, {_playerRunCounts.Count} players");

            // Load reference data (events, encounters per act)
            _refActs = new Dictionary<string, RefAct>(StringComparer.OrdinalIgnoreCase);
            _sharedEvents = new List<RefEvent>();
            var refPath = Path.Combine(modPath, "sts2_reference.json");
            if (File.Exists(refPath))
            {
                try
                {
                    var refJson = File.ReadAllText(refPath);
                    var refData = JsonSerializer.Deserialize<RefData>(refJson);
                    if (refData?.Acts != null)
                    {
                        foreach (var act in refData.Acts)
                            _refActs[act.Name] = act;
                    }
                    _sharedEvents = refData?.SharedEvents ?? new List<RefEvent>();

                    // Build enemy index by parsing raw JSON (avoids Godot generic deserialization issues)
                    _enemyIndex = new Dictionary<string, RefEnemy>(StringComparer.OrdinalIgnoreCase);
                    using var refDoc = JsonDocument.Parse(refJson);
                    ParseEnemiesFromJson(refDoc.RootElement);

                    DebugLogOverlay.Log($"[SpireOracle] Loaded reference data: {_refActs.Count} acts, {_sharedEvents.Count} shared events, {_enemyIndex.Count} enemies");
                }
                catch (Exception refEx)
                {
                    DebugLogOverlay.LogErr($"[SpireOracle] Error loading reference data: {refEx.Message}");
                }
            }
            else
            {
                DebugLogOverlay.Log("[SpireOracle] sts2_reference.json not found, events will not be shown");
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Error loading data: {ex.Message}");
            return false;
        }
    }

    public static CardStats? GetCard(string cardId)
    {
        if (_cards == null) return null;
        return _cards.TryGetValue(cardId, out var stats) ? stats : null;
    }

    public static AncientStats? GetAncientChoice(string choiceKey) =>
        _ancientChoices?.TryGetValue(choiceKey, out var stats) == true ? stats : null;

    public static MapIntelCharacter? GetMapIntel(string character) =>
        _mapIntel?.TryGetValue(character, out var intel) == true ? intel : null;

    public static List<string> GetMapIntelCharacters() =>
        _mapIntel?.Keys.ToList() ?? new List<string>();

    public static RefAct? GetActReference(string actName) =>
        _refActs?.TryGetValue(actName, out var act) == true ? act : null;

    public static List<RefEvent> GetSharedEvents() =>
        _sharedEvents ?? new List<RefEvent>();

    public static PoolRating? GetPoolRating(string context) =>
        _encounterPools?.TryGetValue(context, out var rating) == true ? rating : null;

    public static PoolRating? GetEncounterRating(string encounterId) =>
        _encounterRatings?.TryGetValue(encounterId, out var rating) == true ? rating : null;

    public static Dictionary<string, PoolRating>? EncounterPools => _encounterPools;

    public static List<PlayerRunCount> GetPlayerRunCounts() =>
        _playerRunCounts ?? new List<PlayerRunCount>();

    public static RefEnemy? GetEnemyReference(string name) =>
        _enemyIndex?.TryGetValue(name, out var enemy) == true ? enemy : null;

    private static void ParseEnemiesFromJson(JsonElement root)
    {
        // Parse top-level "monsters" array
        if (root.TryGetProperty("monsters", out var monstersArr) && monstersArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in monstersArr.EnumerateArray())
                IndexEnemy(m);
        }

        // Parse elites/bosses from acts (override monsters with same name)
        if (root.TryGetProperty("acts", out var actsArr) && actsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var act in actsArr.EnumerateArray())
            {
                if (act.TryGetProperty("elites", out var elites) && elites.ValueKind == JsonValueKind.Array)
                    foreach (var e in elites.EnumerateArray())
                        IndexEnemy(e);
                if (act.TryGetProperty("bosses", out var bosses) && bosses.ValueKind == JsonValueKind.Array)
                    foreach (var b in bosses.EnumerateArray())
                        IndexEnemy(b);
            }
        }
    }

    private static void IndexEnemy(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        if (!el.TryGetProperty("name", out var nameProp)) return;
        var name = nameProp.GetString();
        if (string.IsNullOrEmpty(name)) return;

        string? hp = null;
        if (el.TryGetProperty("hp", out var hpProp) && hpProp.ValueKind == JsonValueKind.String)
            hp = hpProp.GetString();

        List<string>? moves = null;
        if (el.TryGetProperty("moves", out var movesProp) && movesProp.ValueKind == JsonValueKind.Array)
        {
            moves = new List<string>();
            foreach (var m in movesProp.EnumerateArray())
                if (m.ValueKind == JsonValueKind.String)
                    moves.Add(m.GetString()!);
        }

        string? notes = null;
        if (el.TryGetProperty("notes", out var notesProp) && notesProp.ValueKind == JsonValueKind.String)
            notes = notesProp.GetString();

        List<string>? monsters = null;
        if (el.TryGetProperty("monsters", out var monstersProp) && monstersProp.ValueKind == JsonValueKind.Array)
        {
            monsters = new List<string>();
            foreach (var m in monstersProp.EnumerateArray())
                if (m.ValueKind == JsonValueKind.String)
                    monsters.Add(m.GetString()!);
        }

        _enemyIndex![name] = new RefEnemy(name, hp, moves, notes, monsters);
    }

    /// <summary>
    /// Returns (mean, sampleSize) from the raw damage distribution for an encounter or pool.
    /// </summary>
    public static (double Mean, int SampleSize)? GetHistoricalDamage(string poolOrEncounterId)
    {
        if (_damageDistributions == null) return null;
        if (!_damageDistributions.TryGetValue(poolOrEncounterId, out var sorted) || sorted.Count == 0)
            return null;
        var mean = 0.0;
        foreach (var v in sorted) mean += v;
        mean /= sorted.Count;
        return (mean, sorted.Count);
    }

    /// <summary>
    /// Compute expected net damage from the damage distribution scaled by Elo matchup score.
    /// Returns (expected, low, high) where low/high are the 25th-75th percentile range.
    /// </summary>
    public static (double Expected, double Low, double High)? GetExpectedDamage(string poolOrEncounterId, double score)
    {
        if (_damageDistributions == null) return null;
        if (!_damageDistributions.TryGetValue(poolOrEncounterId, out var sorted) || sorted.Count == 0)
            return null;

        var mean = 0.0;
        foreach (var v in sorted) mean += v;
        mean /= sorted.Count;

        // Linear scaling: score 0.5 → mean, score 1.0 → 0, score 0.0 → 2*mean
        var expected = Math.Max(0, mean * 2.0 * (1.0 - score));

        // Range: shift the percentile window based on score
        // score 0.8 → look at 10th-40th percentile (good outcome range)
        // score 0.5 → look at 25th-75th percentile (average range)
        // score 0.2 → look at 60th-90th percentile (bad outcome range)
        var centerPct = 1.0 - score; // 0.0 = best, 1.0 = worst
        var lowPct = Math.Max(0, centerPct - 0.25);
        var highPct = Math.Min(1, centerPct + 0.25);

        var lowIdx = Math.Clamp((int)(lowPct * (sorted.Count - 1)), 0, sorted.Count - 1);
        var highIdx = Math.Clamp((int)(highPct * (sorted.Count - 1)), 0, sorted.Count - 1);

        return (expected, sorted[lowIdx], sorted[highIdx]);
    }
}
