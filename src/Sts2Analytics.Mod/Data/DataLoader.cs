using System;
using System.Collections.Generic;
using System.IO;
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
}
