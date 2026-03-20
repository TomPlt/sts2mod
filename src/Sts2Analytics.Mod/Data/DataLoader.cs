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

    public static bool IsLoaded => _cards != null;
    public static double SkipElo => _skipElo;

    public static bool Load(string modPath)
    {
        try
        {
            var filePath = Path.Combine(modPath, "data.json");
            if (!File.Exists(filePath))
            {
                GD.PrintErr($"[SpireOracle] data.json not found at: {filePath}");
                return false;
            }

            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<OverlayData>(json);

            if (data == null || data.Cards == null)
            {
                GD.PrintErr("[SpireOracle] Failed to deserialize data.json");
                return false;
            }

            _skipElo = data.SkipElo;
            _cards = new Dictionary<string, CardStats>(StringComparer.OrdinalIgnoreCase);

            foreach (var card in data.Cards)
            {
                if (!string.IsNullOrEmpty(card.CardId))
                {
                    _cards[card.CardId] = card;
                }
            }

            GD.Print($"[SpireOracle] Loaded {_cards.Count} cards, skip Elo = {_skipElo:F0}");
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
}
