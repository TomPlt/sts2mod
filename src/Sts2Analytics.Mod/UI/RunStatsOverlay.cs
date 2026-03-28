using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Full-screen run stats overlay with graphs. Toggle with F6.
/// </summary>
public static class RunStatsOverlay
{
    private const string OverlayName = "SpireOracleRunStats";
    private static Control? _panel;
    private static bool _visible;

    public static bool IsVisible => _visible;

    public static void Toggle()
    {
        if (_visible) Hide();
        else Show();
    }

    public static void Show()
    {
        Hide();
        if (!LiveRunDb.IsInitialized || LiveRunDb.CurrentRunId <= 0) return;

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        var runId = LiveRunDb.CurrentRunId;

        // Query all data
        var dmgPerCombat = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, SUM(a.Amount)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='DAMAGE_DEALT'
                AND a.SourceId LIKE 'CHARACTER.%' AND a.TargetId NOT LIKE 'CHARACTER.%'
              GROUP BY c.Id ORDER BY c.Id", runId);

        var takenPerCombat = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, SUM(a.Amount)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='DAMAGE_TAKEN'
                AND a.SourceId LIKE 'CHARACTER.%'
              GROUP BY c.Id ORDER BY c.Id", runId);

        var hpPerCombat = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, t.StartingHp
              FROM Turns t JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND t.TurnNumber = 1
              ORDER BY c.Id", runId);

        var topCards = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 10", runId);

        // Build full-screen overlay
        _panel = new PanelContainer();
        _panel.Name = OverlayName;
        _panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.ZIndex = 100;

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.02f, 0.02f, 0.05f, 0.95f);
        bgStyle.ContentMarginLeft = 40;
        bgStyle.ContentMarginRight = 40;
        bgStyle.ContentMarginTop = 30;
        bgStyle.ContentMarginBottom = 30;
        _panel.AddThemeStyleboxOverride("panel", bgStyle);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 24);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Title
        var title = new Label();
        title.Text = "Run Stats (F6 to close)";
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        // Damage dealt per combat
        AddVerticalBarChart(vbox, "Damage Dealt Per Combat", dmgPerCombat,
            new Color(0.85f, 0.25f, 0.2f), 150);

        // Damage taken per combat
        AddVerticalBarChart(vbox, "Damage Taken Per Combat", takenPerCombat,
            new Color(0.9f, 0.6f, 0.2f), 150);

        // HP at start of each combat
        AddLineChartWithRects(vbox, "HP Over Run", hpPerCombat,
            new Color(0.2f, 0.8f, 0.3f), 150);

        // Top cards played
        AddHorizontalBars(vbox, "Top Cards Played", topCards,
            new Color(0.5f, 0.5f, 0.7f), 500);

        scroll.AddChild(vbox);
        _panel.AddChild(scroll);
        tree.Root.AddChild(_panel);
        _visible = true;
    }

    public static void Hide()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _panel = null;
        _visible = false;
    }

    private static void AddVerticalBarChart(VBoxContainer parent, string title,
        List<(string label, int value)> data, Color barColor, float maxHeight)
    {
        if (data.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 6);

        var titleLabel = new Label();
        titleLabel.Text = title;
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLabel);

        var maxVal = data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;

        // Chart area: bars aligned at bottom
        var chartRow = new HBoxContainer();
        chartRow.AddThemeConstantOverride("separation", 4);
        chartRow.CustomMinimumSize = new Vector2(0, maxHeight + 30);

        foreach (var (label, value) in data)
        {
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 2);
            col.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;

            // Value label
            var valLabel = new Label();
            valLabel.Text = $"{value}";
            valLabel.AddThemeFontSizeOverride("font_size", 11);
            valLabel.AddThemeColorOverride("font_color", Colors.White);
            valLabel.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(valLabel);

            // Bar
            var barHeight = maxHeight * value / maxVal;
            if (barHeight < 2) barHeight = 2;
            var bar = new ColorRect();
            bar.Color = barColor;
            bar.CustomMinimumSize = new Vector2(40, barHeight);
            col.AddChild(bar);

            // Encounter label
            var nameLabel = new Label();
            nameLabel.Text = FormatShortLabel(label);
            nameLabel.AddThemeFontSizeOverride("font_size", 9);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(nameLabel);

            chartRow.AddChild(col);
        }

        section.AddChild(chartRow);
        parent.AddChild(section);
    }

    private static void AddLineChartWithRects(VBoxContainer parent, string title,
        List<(string label, int value)> data, Color color, float maxHeight)
    {
        if (data.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 6);

        var titleLabel = new Label();
        titleLabel.Text = title;
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLabel);

        var maxVal = data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;

        // Use vertical bars to approximate a line chart
        var chartRow = new HBoxContainer();
        chartRow.AddThemeConstantOverride("separation", 2);
        chartRow.CustomMinimumSize = new Vector2(0, maxHeight + 30);

        foreach (var (label, value) in data)
        {
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 2);
            col.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;

            var valLabel = new Label();
            valLabel.Text = $"{value}";
            valLabel.AddThemeFontSizeOverride("font_size", 11);
            valLabel.AddThemeColorOverride("font_color", Colors.White);
            valLabel.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(valLabel);

            var barHeight = maxHeight * value / maxVal;
            if (barHeight < 2) barHeight = 2;
            var bar = new ColorRect();
            bar.Color = color;
            bar.CustomMinimumSize = new Vector2(30, barHeight);
            col.AddChild(bar);

            var nameLabel = new Label();
            nameLabel.Text = FormatShortLabel(label);
            nameLabel.AddThemeFontSizeOverride("font_size", 9);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(nameLabel);

            chartRow.AddChild(col);
        }

        section.AddChild(chartRow);
        parent.AddChild(section);
    }

    private static void AddHorizontalBars(VBoxContainer parent, string title,
        List<(string label, int value)> data, Color barColor, float maxWidth)
    {
        if (data.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLabel = new Label();
        titleLabel.Text = title;
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLabel);

        var maxVal = data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;

        foreach (var (cardId, count) in data)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var nameLabel = new Label();
            nameLabel.Text = FormatCardName(cardId);
            nameLabel.CustomMinimumSize = new Vector2(160, 0);
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            row.AddChild(nameLabel);

            var bar = new ColorRect();
            bar.Color = barColor;
            bar.CustomMinimumSize = new Vector2((maxWidth - 220) * count / maxVal, 18);
            row.AddChild(bar);

            var valLabel = new Label();
            valLabel.Text = $"{count}";
            valLabel.AddThemeFontSizeOverride("font_size", 14);
            valLabel.AddThemeColorOverride("font_color", Colors.White);
            row.AddChild(valLabel);

            section.AddChild(row);
        }

        parent.AddChild(section);
    }

    private static string FormatCardName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var name = id;
        if (name.StartsWith("CARD.")) name = name.Substring(5);
        if (name.StartsWith("ENCOUNTER.")) name = name.Substring(10);
        foreach (var suffix in new[] { "_WEAK", "_NORMAL", "_ELITE", "_BOSS" })
            if (name.EndsWith(suffix)) { name = name.Substring(0, name.Length - suffix.Length); break; }
        var upgrade = "";
        var plusIdx = name.IndexOf('+');
        if (plusIdx > 0) { upgrade = name.Substring(plusIdx); name = name.Substring(0, plusIdx); }
        name = string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
        return name + upgrade;
    }

    private static string FormatShortLabel(string id)
    {
        var name = FormatCardName(id);
        if (name.Length > 12) name = name.Substring(0, 12);
        return name;
    }
}
