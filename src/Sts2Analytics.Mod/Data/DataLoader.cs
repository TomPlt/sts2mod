using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace SpireOracle.Data;

public static class DataLoader
{
    private static Dictionary<string, CardStats>? _cards;
    private static double _skipElo;
    private static Dictionary<string, double>? _skipEloByAct;
    private static Dictionary<string, AncientStats>? _ancientChoices;
    private static Dictionary<string, MapIntelCharacter>? _mapIntel;
    private static Dictionary<string, RefAct>? _refActs;
    private static List<RefEvent>? _sharedEvents;
    private static Dictionary<string, PoolRating>? _encounterPools;
    private static Dictionary<string, PoolRating>? _encounterRatings;
    private static Dictionary<string, List<int>>? _damageDistributions;

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

    public static bool Load(string modPath)
    {
        try
        {
            var filePath = Path.Combine(modPath, "overlay_data.json");
            if (!File.Exists(filePath))
            {
                GD.PrintErr($"[SpireOracle] overlay_data.json not found at: {filePath}");
                return false;
            }

            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<OverlayData>(json);

            if (data == null || data.Cards == null)
            {
                GD.PrintErr("[SpireOracle] Failed to deserialize overlay_data.json");
                return false;
            }

            _skipElo = data.SkipElo;
            _skipEloByAct = data.SkipEloByAct;
            _cards = new Dictionary<string, CardStats>(StringComparer.OrdinalIgnoreCase);

            foreach (var card in data.Cards)
            {
                if (!string.IsNullOrEmpty(card.CardId))
                {
                    _cards[card.CardId] = card;
                }
            }

            GD.Print($"[SpireOracle] Loaded {_cards.Count} cards, skip Elo = {_skipElo:F0}");

            _ancientChoices = new Dictionary<string, AncientStats>(StringComparer.OrdinalIgnoreCase);
            if (data.AncientChoices != null)
            {
                foreach (var ac in data.AncientChoices)
                {
                    if (!string.IsNullOrEmpty(ac.ChoiceKey))
                        _ancientChoices[ac.ChoiceKey] = ac;
                }
            }
            GD.Print($"[SpireOracle] Loaded {_ancientChoices.Count} ancient choices");

            _mapIntel = new Dictionary<string, MapIntelCharacter>(StringComparer.OrdinalIgnoreCase);
            if (data.MapIntel != null)
            {
                foreach (var mic in data.MapIntel)
                {
                    if (!string.IsNullOrEmpty(mic.Character))
                        _mapIntel[mic.Character] = mic;
                }
            }
            GD.Print($"[SpireOracle] Loaded map intel for {_mapIntel.Count} characters");

            _encounterPools = data.EncounterPools ?? new Dictionary<string, PoolRating>();
            _encounterRatings = data.EncounterRatings ?? new Dictionary<string, PoolRating>();
            _damageDistributions = data.DamageDistributions ?? new Dictionary<string, List<int>>();
            GD.Print($"[SpireOracle] Loaded {_encounterPools.Count} pool ratings, {_encounterRatings.Count} encounter ratings, {_damageDistributions.Count} damage distributions");

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
                    GD.Print($"[SpireOracle] Loaded reference data: {_refActs.Count} acts, {_sharedEvents.Count} shared events");
                }
                catch (Exception refEx)
                {
                    GD.PrintErr($"[SpireOracle] Error loading reference data: {refEx.Message}");
                }
            }
            else
            {
                GD.Print("[SpireOracle] sts2_reference.json not found, events will not be shown");
            }

            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] Error loading data: {ex.Message}");
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
