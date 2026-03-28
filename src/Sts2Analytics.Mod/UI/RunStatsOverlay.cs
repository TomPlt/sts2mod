using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Full-screen run stats overlay with line charts. Toggle with F6.
/// Shows per-card damage/plays over combats as line plots.
/// </summary>
public static class RunStatsOverlay
{
    private const string OverlayName = "SpireOracleRunStats";
    private static Control? _panel;
    private static bool _visible;

    // Colors for different card lines
    private static readonly Color[] LineColors = new[]
    {
        new Color(0.85f, 0.25f, 0.2f),   // red
        new Color(0.2f, 0.6f, 0.9f),     // blue
        new Color(0.2f, 0.8f, 0.3f),     // green
        new Color(0.9f, 0.7f, 0.1f),     // yellow
        new Color(0.8f, 0.3f, 0.8f),     // purple
        new Color(0.9f, 0.5f, 0.1f),     // orange
        new Color(0.3f, 0.9f, 0.8f),     // cyan
        new Color(0.7f, 0.7f, 0.7f),     // gray
    };

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

        // Get ordered combat list
        var combatList = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, c.FloorIndex
              FROM Combats c WHERE c.RunId=@runId ORDER BY c.Id", runId);

        if (combatList.Count == 0) return;

        // Top 5 cards by total plays
        var topCards = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 5", runId);

        // Per-combat plays for each top card
        var playsPerCombat = LiveRunDb.QueryGroupedStats(
            @"SELECT a.SourceId, c.Id, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId, c.Id ORDER BY c.Id", runId);

        // Per-combat damage for each card
        var dmgPerCombat = LiveRunDb.QueryGroupedStats(
            @"SELECT a1.SourceId, CAST(c.Id AS TEXT), SUM(a2.Amount)
              FROM CombatActions a1
              JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='DAMAGE_DEALT'
                AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.TargetId NOT LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE(
                  (SELECT MIN(a3.Seq) FROM CombatActions a3
                   WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId, c.Id ORDER BY c.Id", runId);

        // HP per combat
        var hpPerCombat = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, t.StartingHp
              FROM Turns t JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND t.TurnNumber = 1
              ORDER BY c.Id", runId);

        // Combat IDs in order (for x-axis mapping)
        var combatIds = LiveRunDb.QueryTopStats(
            @"SELECT CAST(c.Id AS TEXT), c.Id
              FROM Combats c WHERE c.RunId=@runId ORDER BY c.Id", runId);
        var combatIdList = combatIds.Select(c => c.label).ToList();

        // Build overlay
        _panel = new PanelContainer();
        _panel.Name = OverlayName;
        _panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.ZIndex = 100;

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.02f, 0.02f, 0.05f, 0.95f);
        bgStyle.ContentMarginLeft = 40;
        bgStyle.ContentMarginRight = 40;
        bgStyle.ContentMarginTop = 20;
        bgStyle.ContentMarginBottom = 20;
        _panel.AddThemeStyleboxOverride("panel", bgStyle);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 30);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Title
        var title = new Label();
        title.Text = "Run Stats (F6 to close)";
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        // HP line chart
        AddLineChart(vbox, "HP Over Run", hpPerCombat, combatList,
            new Color(0.2f, 0.8f, 0.3f), 700, 200);

        // Card plays per combat — multi-line chart
        AddMultiLineChart(vbox, "Card Plays Per Combat", topCards, playsPerCombat,
            combatIdList, combatList, 700, 250);

        // Card damage per combat — multi-line chart
        AddMultiLineChart(vbox, "Card Damage Per Combat", topCards, dmgPerCombat,
            combatIdList, combatList, 700, 250);

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

    private static void AddLineChart(VBoxContainer parent, string chartTitle,
        List<(string label, int value)> data, List<(string label, int value)> combatLabels,
        Color color, float width, float height)
    {
        if (data.Count < 2) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLbl = new Label();
        titleLbl.Text = chartTitle;
        titleLbl.AddThemeFontSizeOverride("font_size", 20);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLbl);

        // Chart container
        var chart = new Control();
        chart.CustomMinimumSize = new Vector2(width, height);

        var maxVal = data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;

        var padding = 40f;
        var chartW = width - padding * 2;
        var chartH = height - padding;

        // Line2D
        var line = new Line2D();
        line.Width = 2;
        line.DefaultColor = color;
        for (int i = 0; i < data.Count; i++)
        {
            var x = padding + chartW * i / Math.Max(data.Count - 1, 1);
            var y = chartH - chartH * data[i].value / maxVal;
            line.AddPoint(new Vector2(x, y));
        }
        chart.AddChild(line);

        // Value labels + dots
        for (int i = 0; i < data.Count; i++)
        {
            var x = padding + chartW * i / Math.Max(data.Count - 1, 1);
            var y = chartH - chartH * data[i].value / maxVal;

            var dot = new ColorRect();
            dot.Color = color;
            dot.Position = new Vector2(x - 3, y - 3);
            dot.Size = new Vector2(6, 6);
            chart.AddChild(dot);

            var valLbl = new Label();
            valLbl.Text = $"{data[i].value}";
            valLbl.Position = new Vector2(x - 10, y - 18);
            valLbl.AddThemeFontSizeOverride("font_size", 11);
            valLbl.AddThemeColorOverride("font_color", Colors.White);
            chart.AddChild(valLbl);

            // X-axis label
            if (i < combatLabels.Count)
            {
                var xLbl = new Label();
                xLbl.Text = FormatShortName(combatLabels[i].label);
                xLbl.Position = new Vector2(x - 15, chartH + 4);
                xLbl.AddThemeFontSizeOverride("font_size", 9);
                xLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
                chart.AddChild(xLbl);
            }
        }

        section.AddChild(chart);
        parent.AddChild(section);
    }

    private static void AddMultiLineChart(VBoxContainer parent, string chartTitle,
        List<(string label, int value)> topCards,
        List<(string group, string label, int value)> perCombatData,
        List<string> combatIdList,
        List<(string label, int value)> combatLabels,
        float width, float height)
    {
        if (topCards.Count == 0 || combatIdList.Count < 2) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLbl = new Label();
        titleLbl.Text = chartTitle;
        titleLbl.AddThemeFontSizeOverride("font_size", 20);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLbl);

        // Legend
        var legend = new HBoxContainer();
        legend.AddThemeConstantOverride("separation", 16);
        for (int i = 0; i < topCards.Count && i < LineColors.Length; i++)
        {
            var legendItem = new HBoxContainer();
            legendItem.AddThemeConstantOverride("separation", 4);
            var swatch = new ColorRect();
            swatch.Color = LineColors[i];
            swatch.CustomMinimumSize = new Vector2(12, 12);
            legendItem.AddChild(swatch);
            var nameLbl = new Label();
            nameLbl.Text = FormatCardName(topCards[i].label);
            nameLbl.AddThemeFontSizeOverride("font_size", 12);
            nameLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            legendItem.AddChild(nameLbl);
            legend.AddChild(legendItem);
        }
        section.AddChild(legend);

        // Build per-card, per-combat value lookup
        var cardCombatValues = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (cardId, combatId, value) in perCombatData)
        {
            if (!cardCombatValues.ContainsKey(cardId))
                cardCombatValues[cardId] = new Dictionary<string, int>();
            cardCombatValues[cardId][combatId] = value;
        }

        // Find max value across all cards for Y scaling
        var maxVal = 1;
        foreach (var card in topCards)
        {
            if (cardCombatValues.TryGetValue(card.label, out var vals))
            {
                var cardMax = vals.Values.DefaultIfEmpty(0).Max();
                if (cardMax > maxVal) maxVal = cardMax;
            }
        }

        var padding = 40f;
        var chartW = width - padding * 2;
        var chartH = height - padding;

        var chart = new Control();
        chart.CustomMinimumSize = new Vector2(width, height);

        // Draw lines for each top card
        for (int ci = 0; ci < topCards.Count && ci < LineColors.Length; ci++)
        {
            var cardId = topCards[ci].label;
            var cardVals = cardCombatValues.GetValueOrDefault(cardId, new());

            var line = new Line2D();
            line.Width = 2;
            line.DefaultColor = LineColors[ci];

            for (int j = 0; j < combatIdList.Count; j++)
            {
                var val = cardVals.GetValueOrDefault(combatIdList[j], 0);
                var x = padding + chartW * j / Math.Max(combatIdList.Count - 1, 1);
                var y = chartH - chartH * val / maxVal;
                line.AddPoint(new Vector2(x, y));
            }
            chart.AddChild(line);

            // Dots at each point
            for (int j = 0; j < combatIdList.Count; j++)
            {
                var val = cardVals.GetValueOrDefault(combatIdList[j], 0);
                if (val == 0) continue;
                var x = padding + chartW * j / Math.Max(combatIdList.Count - 1, 1);
                var y = chartH - chartH * val / maxVal;

                var dot = new ColorRect();
                dot.Color = LineColors[ci];
                dot.Position = new Vector2(x - 2, y - 2);
                dot.Size = new Vector2(4, 4);
                chart.AddChild(dot);
            }
        }

        // X-axis labels
        for (int j = 0; j < combatIdList.Count && j < combatLabels.Count; j++)
        {
            var x = padding + chartW * j / Math.Max(combatIdList.Count - 1, 1);
            var xLbl = new Label();
            xLbl.Text = FormatShortName(combatLabels[j].label);
            xLbl.Position = new Vector2(x - 15, chartH + 4);
            xLbl.AddThemeFontSizeOverride("font_size", 9);
            xLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            chart.AddChild(xLbl);
        }

        // Y-axis labels
        for (int i = 0; i <= 4; i++)
        {
            var y = chartH - chartH * i / 4;
            var gridVal = maxVal * i / 4;
            var yLbl = new Label();
            yLbl.Text = $"{gridVal}";
            yLbl.Position = new Vector2(0, y - 6);
            yLbl.AddThemeFontSizeOverride("font_size", 10);
            yLbl.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
            chart.AddChild(yLbl);
        }

        section.AddChild(chart);
        parent.AddChild(section);
    }

    private static string FormatCardName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var name = id;
        if (name.StartsWith("CARD.")) name = name.Substring(5);
        var upgrade = "";
        var plusIdx = name.IndexOf('+');
        if (plusIdx > 0) { upgrade = name.Substring(plusIdx); name = name.Substring(0, plusIdx); }
        name = string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
        return name + upgrade;
    }

    private static string FormatShortName(string id)
    {
        var name = FormatCardName(id);
        if (name.StartsWith("Encounter ")) name = name.Substring(10);
        if (name.Length > 10) name = name.Substring(0, 10);
        return name;
    }
}
