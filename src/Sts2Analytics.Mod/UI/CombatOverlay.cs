using System;
using System.Collections.Generic;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Combat overlay cycling: Stats → Actions → Hidden via F4.
/// Stats: name, HP, expected damage, historical, elo.
/// Actions: moveset and pattern notes.
/// </summary>
public static class CombatOverlay
{
    private const string OverlayName = "SpireOracleCombatOverlay";
    private static PanelContainer? _panel;

    private enum State { Stats, Actions, Hidden }
    private static State _state = State.Hidden;
    private static bool _inCombat;

    // Cached data for re-rendering on toggle
    private static List<string> _lines = new();
    private static double _encounterElo;
    private static double _deckElo;
    private static RefEnemy? _enemyRef;
    private static double _historicalAvg;
    private static int _sampleSize;

    public static bool IsInCombat => _inCombat;

    public static void Show(List<string> lines, double encounterElo, double deckElo,
        RefEnemy? enemyRef = null, double historicalAvg = 0, int sampleSize = 0)
    {
        _lines = lines;
        _encounterElo = encounterElo;
        _deckElo = deckElo;
        _enemyRef = enemyRef;
        _historicalAvg = historicalAvg;
        _sampleSize = sampleSize;
        _inCombat = true;
        _state = State.Stats;
        DebugLogOverlay.Log($"[SpireOracle] CombatOverlay.Show: enemyRef={enemyRef?.Name ?? "null"}, moves={enemyRef?.Moves?.Count ?? 0}, hist={sampleSize}");
        Render();
    }

    public static void Toggle()
    {
        if (!_inCombat) return;
        var prev = _state;
        _state = _state switch
        {
            State.Stats => State.Actions,
            State.Actions => State.Hidden,
            State.Hidden => State.Stats,
            _ => State.Stats
        };
        DebugLogOverlay.Log($"[SpireOracle] CombatOverlay.Toggle: {prev} -> {_state}, enemyRef={_enemyRef?.Name ?? "null"}");
        Render();
    }

    public static void Hide()
    {
        DestroyPanel();
        _inCombat = false;
        _state = State.Hidden;
        _lines = new List<string>();
        _enemyRef = null;
    }

    private static void Render()
    {
        DestroyPanel();
        if (_state == State.Hidden) return;

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        _panel = new PanelContainer();
        _panel.Name = OverlayName;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.88f);
        style.BorderColor = _state == State.Actions
            ? new Color(0.95f, 0.7f, 0.2f, 0.8f) // gold border for actions
            : _encounterElo > _deckElo
                ? new Color(0.95f, 0.3f, 0.3f, 0.8f)
                : new Color(0.3f, 0.85f, 0.3f, 0.8f);
        style.BorderWidthBottom = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 10;
        _panel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        if (_state == State.Stats)
            BuildStatsView(vbox);
        else
            BuildActionsView(vbox);

        _panel.AddChild(vbox);
        _panel.Position = new Vector2(20, 180);
        _panel.ZIndex = 100;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        tree.Root.AddChild(_panel);
    }

    private static void BuildStatsView(VBoxContainer vbox)
    {
        // Title
        if (_lines.Count > 0)
            AddLabel(vbox, _lines[0], 22, new Color(0.83f, 0.33f, 0.16f), HorizontalAlignment.Center);

        // HP (A10)
        if (_enemyRef?.Hp != null)
        {
            var hp = ParseA10Hp(_enemyRef.Hp);
            AddLabel(vbox, $"HP: {hp}", 16, Colors.White, HorizontalAlignment.Center);
        }

        // Multi-enemy
        if (_enemyRef?.Monsters != null && _enemyRef.Monsters.Count > 0)
            AddLabel(vbox, string.Join(" + ", _enemyRef.Monsters), 14,
                new Color(0.8f, 0.7f, 0.5f), HorizontalAlignment.Center);

        // Expected + historical + elo combined
        string? expected = null, elo = null;
        foreach (var line in _lines)
        {
            if (line.StartsWith("Expected:")) expected = line;
            else if (line.StartsWith("Encounter:")) elo = line;
        }

        var parts = new List<string>();
        if (expected != null) parts.Add(expected);
        if (_sampleSize > 0) parts.Add($"Avg: {_historicalAvg:F0} (n={_sampleSize})");
        if (parts.Count > 0)
            AddLabel(vbox, string.Join("  |  ", parts), 15, Colors.White, HorizontalAlignment.Center);

        if (elo != null)
            AddLabel(vbox, elo, 13, new Color(0.5f, 0.5f, 0.6f), HorizontalAlignment.Center);

        AddHintLabel(vbox, "[F4] actions");
    }

    private static void BuildActionsView(VBoxContainer vbox)
    {
        try
        {
            // Title
            var titleText = _lines.Count > 0 ? _lines[0] : "vs ???";
            AddLabel(vbox, titleText, 20, new Color(0.83f, 0.33f, 0.16f), HorizontalAlignment.Center);

            if (_enemyRef != null && (_enemyRef.Moves != null || _enemyRef.Notes != null))
            {
                var notes = _enemyRef.Notes ?? "";
                var patternParts = new List<string>();

                // Show each move with its damage/effect extracted from notes
                if (_enemyRef.Moves != null && _enemyRef.Moves.Count > 0)
                {
                    var usedRanges = new List<(int start, int end)>();

                    foreach (var move in _enemyRef.Moves)
                    {
                        var detail = ExtractMoveDetail(notes, move, usedRanges);
                        if (detail != null)
                        {
                            AddLabel(vbox, $"{move}: {detail}", 15,
                                new Color(0.9f, 0.9f, 0.95f), HorizontalAlignment.Left, autowrap: true, maxWidth: 420);
                        }
                        else
                        {
                            AddLabel(vbox, move, 15,
                                new Color(0.7f, 0.7f, 0.8f), HorizontalAlignment.Left);
                        }
                    }

                    // Collect remaining notes (pattern/behavior) that weren't move descriptions
                    var remaining = ExtractPatternNotes(notes, usedRanges);
                    if (remaining.Length > 0)
                        patternParts.Add(remaining);
                }
                else if (notes.Length > 0)
                {
                    patternParts.Add(notes);
                }

                // Pattern / behavior notes
                if (patternParts.Count > 0)
                {
                    vbox.AddChild(new HSeparator());
                    AddLabel(vbox, string.Join(" ", patternParts), 13,
                        new Color(0.7f, 0.7f, 0.8f), HorizontalAlignment.Left, autowrap: true, maxWidth: 420);
                }
            }
            else
            {
                AddLabel(vbox, "No moveset data available", 14,
                    new Color(0.5f, 0.5f, 0.6f), HorizontalAlignment.Center);
            }
        }
        catch (Exception ex)
        {
            AddLabel(vbox, $"Error: {ex.Message}", 12, new Color(0.95f, 0.3f, 0.3f), HorizontalAlignment.Left);
            DebugLogOverlay.LogErr($"[SpireOracle] CombatOverlay actions error: {ex.Message}");
        }

        AddHintLabel(vbox, "[F4] hide");
    }

    /// <summary>
    /// Extracts "MoveName: description" from notes. Returns the description or null.
    /// Records the character range used so we can extract remaining pattern text.
    /// </summary>
    private static string? ExtractMoveDetail(string notes, string moveName, List<(int start, int end)> usedRanges)
    {
        var searchFor = moveName + ":";
        var idx = notes.IndexOf(searchFor, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // Try with just the first word for multi-word moves
            var firstWord = moveName.Split(' ')[0];
            if (firstWord.Length >= 4 && firstWord != moveName)
            {
                searchFor = firstWord + ":";
                idx = notes.IndexOf(searchFor, StringComparison.OrdinalIgnoreCase);
            }
        }
        if (idx < 0) return null;

        var start = idx + searchFor.Length;
        // Find end: next ". " followed by a capital letter or move name, or end of string
        var end = start;
        while (end < notes.Length)
        {
            if (notes[end] == '.')
            {
                // Check if this is end of a sentence
                if (end + 1 >= notes.Length)
                    break;
                if (notes[end + 1] == ' ' && end + 2 < notes.Length && char.IsUpper(notes[end + 2]))
                    break;
                if (notes[end + 1] == ' ' && end + 2 < notes.Length && char.IsDigit(notes[end + 2]))
                    break;
            }
            end++;
        }

        usedRanges.Add((idx, end + 1)); // include the period
        return notes.Substring(start, end - start).Trim().TrimEnd('.');
    }

    /// <summary>
    /// Returns notes text with move-description ranges removed (pattern/behavior text only).
    /// </summary>
    private static string ExtractPatternNotes(string notes, List<(int start, int end)> usedRanges)
    {
        if (usedRanges.Count == 0) return notes.Trim();

        // Sort ranges and build remaining text
        usedRanges.Sort((a, b) => a.start.CompareTo(b.start));
        var result = new System.Text.StringBuilder();
        var pos = 0;
        foreach (var (start, end) in usedRanges)
        {
            if (start > pos)
                result.Append(notes, pos, start - pos);
            pos = Math.Min(end, notes.Length);
        }
        if (pos < notes.Length)
            result.Append(notes, pos, notes.Length - pos);

        return result.ToString().Trim().TrimStart('.').Trim();
    }

    private static void AddLabel(VBoxContainer vbox, string text, int fontSize, Color color,
        HorizontalAlignment align, bool autowrap = false, int maxWidth = 0)
    {
        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = align;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        if (autowrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (maxWidth > 0)
                label.CustomMinimumSize = new Vector2(maxWidth, 0);
        }
        vbox.AddChild(label);
    }

    private static void AddHintLabel(VBoxContainer vbox, string text)
    {
        AddLabel(vbox, text, 12, new Color(0.4f, 0.4f, 0.5f), HorizontalAlignment.Center);
    }

    private static string ParseA10Hp(string hp)
    {
        var parenIdx = hp.IndexOf('(');
        if (parenIdx >= 0)
        {
            var closeIdx = hp.IndexOf(')', parenIdx);
            if (closeIdx > parenIdx)
                return hp.Substring(parenIdx + 1, closeIdx - parenIdx - 1).Trim();
        }
        return hp;
    }

    private static void DestroyPanel()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _panel = null;
    }
}
