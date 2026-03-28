using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Full-screen run stats overlay. Toggle with F6.
/// Shows aggregate bars + cumulative charts over combats.
/// </summary>
public static class RunStatsOverlay
{
    private const string OverlayName = "SpireOracleRunStats";
    private static Control? _panel;
    private static bool _visible;

    private static readonly Color[] LineColors = new[]
    {
        new Color(0.85f, 0.25f, 0.2f),
        new Color(0.2f, 0.6f, 0.9f),
        new Color(0.2f, 0.8f, 0.3f),
        new Color(0.9f, 0.7f, 0.1f),
        new Color(0.8f, 0.3f, 0.8f),
        new Color(0.9f, 0.5f, 0.1f),
        new Color(0.3f, 0.9f, 0.8f),
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

        // Combat list
        var combatList = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, c.FloorIndex FROM Combats c WHERE c.RunId=@runId ORDER BY c.Id", runId);
        var combatIds = LiveRunDb.QueryTopStats(
            @"SELECT CAST(c.Id AS TEXT), c.Id FROM Combats c WHERE c.RunId=@runId ORDER BY c.Id", runId);
        if (combatIds.Count == 0) return;
        var idList = combatIds.Select(c => c.label).ToList();

        // Top 6 cards
        var topCards = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*) FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED' GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 6", runId);

        // Per-combat data
        var playsRaw = LiveRunDb.QueryGroupedStats(
            @"SELECT a.SourceId, CAST(c.Id AS TEXT), COUNT(*) FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED' GROUP BY a.SourceId, c.Id ORDER BY c.Id", runId);
        var dmgRaw = LiveRunDb.QueryGroupedStats(
            @"SELECT a1.SourceId, CAST(c.Id AS TEXT), COALESCE(SUM(a2.Amount), 0)
              FROM CombatActions a1
              JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='DAMAGE_DEALT' AND a2.SourceId LIKE 'CHARACTER.%' AND a2.TargetId NOT LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE((SELECT MIN(a3.Seq) FROM CombatActions a3 WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId GROUP BY a1.SourceId, c.Id ORDER BY c.Id", runId);
        var hpPerCombat = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, t.StartingHp FROM Turns t JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND t.TurnNumber = 1 ORDER BY c.Id", runId);

        // Aggregates
        var topDamage = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, COALESCE(SUM(a2.Amount), 0) as total
              FROM CombatActions a1
              LEFT JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='DAMAGE_DEALT' AND a2.SourceId LIKE 'CHARACTER.%' AND a2.TargetId NOT LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE((SELECT MIN(a3.Seq) FROM CombatActions a3 WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId HAVING total > 0 ORDER BY total DESC LIMIT 10", runId);
        var topBlock = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, COALESCE(SUM(a2.Amount), 0) as total
              FROM CombatActions a1
              LEFT JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='BLOCK_GAINED' AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE((SELECT MIN(a3.Seq) FROM CombatActions a3 WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId HAVING total > 0 ORDER BY total DESC LIMIT 10", runId);

        // Summary
        var combatCount = LiveRunDb.QueryTopStats(@"SELECT 'c', COUNT(*) FROM Combats WHERE RunId=@runId", runId);
        var turnCount = LiveRunDb.QueryTopStats(@"SELECT 't', COUNT(*) FROM Turns t JOIN Combats c ON t.CombatId=c.Id WHERE c.RunId=@runId", runId);
        var totalDmg = LiveRunDb.QueryTopStats(@"SELECT 'd', COALESCE(SUM(a.Amount), 0) FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id WHERE c.RunId=@runId AND a.ActionType='DAMAGE_DEALT' AND a.SourceId LIKE 'CHARACTER.%' AND a.TargetId NOT LIKE 'CHARACTER.%'", runId);
        var totalTaken = LiveRunDb.QueryTopStats(@"SELECT 't', COALESCE(SUM(a.Amount), 0) FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id WHERE c.RunId=@runId AND a.ActionType='DAMAGE_TAKEN' AND a.SourceId LIKE 'CHARACTER.%'", runId);
        var totalBlockVal = LiveRunDb.QueryTopStats(@"SELECT 'b', COALESCE(SUM(a.Amount), 0) FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id WHERE c.RunId=@runId AND a.ActionType='BLOCK_GAINED' AND a.SourceId LIKE 'CHARACTER.%'", runId);

        var cumuPlays = BuildCumulative(topCards, playsRaw, idList);
        var cumuDmg = BuildCumulative(topCards, dmgRaw, idList);

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
        vbox.AddThemeConstantOverride("separation", 20);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Title
        var titleLbl = new Label();
        titleLbl.Text = "Run Stats (F6 to close)";
        titleLbl.AddThemeFontSizeOverride("font_size", 28);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(titleLbl);

        // Summary
        var c = combatCount.Count > 0 ? combatCount[0].value : 0;
        var t = turnCount.Count > 0 ? turnCount[0].value : 0;
        var d = totalDmg.Count > 0 ? totalDmg[0].value : 0;
        var tk = totalTaken.Count > 0 ? totalTaken[0].value : 0;
        var b = totalBlockVal.Count > 0 ? totalBlockVal[0].value : 0;
        var sumLbl = new Label();
        sumLbl.Text = $"Combats: {c}    Turns: {t}    Dealt: {d}    Taken: {tk}    Block: {b}";
        sumLbl.AddThemeFontSizeOverride("font_size", 18);
        sumLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
        sumLbl.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(sumLbl);

        // Aggregate bars
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 30);
        AddBarColumn(cols, "Most Played", topCards, new Color(0.5f, 0.5f, 0.7f), "x");
        AddBarColumn(cols, "Top Damage", topDamage, new Color(0.85f, 0.25f, 0.2f), " dmg");
        AddBarColumn(cols, "Top Block", topBlock, new Color(0.2f, 0.5f, 0.85f), " blk");
        vbox.AddChild(cols);

        vbox.AddChild(new HSeparator());

        // HP stem chart
        AddStemChart(vbox, "HP Over Run", hpPerCombat, combatList, new Color(0.2f, 0.8f, 0.3f));

        // Cumulative plays stem chart
        AddMultiStemChart(vbox, "Cumulative Card Plays", topCards, cumuPlays, idList, combatList);

        // Cumulative damage stem chart
        AddMultiStemChart(vbox, "Cumulative Card Damage", topCards, cumuDmg, idList, combatList);

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

    private static Dictionary<string, Dictionary<string, int>> BuildCumulative(
        List<(string label, int value)> topCards,
        List<(string group, string label, int value)> rawData,
        List<string> combatIds)
    {
        var perCombat = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (cardId, combatId, value) in rawData)
        {
            if (!perCombat.ContainsKey(cardId))
                perCombat[cardId] = new Dictionary<string, int>();
            perCombat[cardId][combatId] = value;
        }

        var result = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (cardId, _) in topCards)
        {
            var cardVals = perCombat.GetValueOrDefault(cardId, new());
            var cumulative = new Dictionary<string, int>();
            var running = 0;
            foreach (var cid in combatIds)
            {
                running += cardVals.GetValueOrDefault(cid, 0);
                cumulative[cid] = running;
            }
            result[cardId] = cumulative;
        }
        return result;
    }

    /// <summary>
    /// Single-series stem chart: vertical bars from bottom + value labels.
    /// Works inside Control tree (no Line2D).
    /// </summary>
    private static void AddStemChart(VBoxContainer parent, string title,
        List<(string label, int value)> data, List<(string label, int value)> combatLabels, Color color)
    {
        if (data.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLbl = new Label();
        titleLbl.Text = title;
        titleLbl.AddThemeFontSizeOverride("font_size", 20);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLbl);

        var maxVal = data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;
        var barH = 120f;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        row.CustomMinimumSize = new Vector2(0, barH + 30);

        for (int i = 0; i < data.Count; i++)
        {
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 1);
            col.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;

            var valLbl = new Label();
            valLbl.Text = $"{data[i].value}";
            valLbl.AddThemeFontSizeOverride("font_size", 10);
            valLbl.AddThemeColorOverride("font_color", Colors.White);
            valLbl.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(valLbl);

            var h = Math.Max(2, barH * data[i].value / maxVal);
            var barColor = data[i].value > maxVal * 0.5f ? color :
                           data[i].value > maxVal * 0.25f ? new Color(0.9f, 0.7f, 0.2f) :
                           new Color(0.9f, 0.25f, 0.2f);
            var bar = new ColorRect();
            bar.Color = barColor;
            bar.CustomMinimumSize = new Vector2(24, h);
            col.AddChild(bar);

            var nameLbl = new Label();
            nameLbl.Text = i < combatLabels.Count ? FormatShort(combatLabels[i].label) : $"{i+1}";
            nameLbl.AddThemeFontSizeOverride("font_size", 8);
            nameLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            nameLbl.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(nameLbl);

            row.AddChild(col);
        }

        section.AddChild(row);
        parent.AddChild(section);
    }

    /// <summary>
    /// Multi-series stem chart: grouped bars per combat, one color per card.
    /// </summary>
    private static void AddMultiStemChart(VBoxContainer parent, string title,
        List<(string label, int value)> topCards,
        Dictionary<string, Dictionary<string, int>> cumulativeData,
        List<string> combatIds, List<(string label, int value)> combatLabels)
    {
        if (topCards.Count == 0 || combatIds.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLbl = new Label();
        titleLbl.Text = title;
        titleLbl.AddThemeFontSizeOverride("font_size", 20);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLbl);

        // Legend
        var legend = new HBoxContainer();
        legend.AddThemeConstantOverride("separation", 12);
        for (int i = 0; i < topCards.Count && i < LineColors.Length; i++)
        {
            var item = new HBoxContainer();
            item.AddThemeConstantOverride("separation", 3);
            var swatch = new ColorRect();
            swatch.Color = LineColors[i];
            swatch.CustomMinimumSize = new Vector2(10, 10);
            item.AddChild(swatch);
            var lbl = new Label();
            lbl.Text = FormatCardName(topCards[i].label);
            lbl.AddThemeFontSizeOverride("font_size", 11);
            lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            item.AddChild(lbl);
            legend.AddChild(item);
        }
        section.AddChild(legend);

        // Find max
        var maxVal = 1;
        foreach (var (_, vals) in cumulativeData)
        {
            var m = vals.Values.DefaultIfEmpty(0).Max();
            if (m > maxVal) maxVal = m;
        }

        var barH = 150f;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.CustomMinimumSize = new Vector2(0, barH + 30);

        for (int j = 0; j < combatIds.Count; j++)
        {
            var combatCol = new VBoxContainer();
            combatCol.AddThemeConstantOverride("separation", 1);
            combatCol.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;

            // Grouped bars for this combat
            var bars = new HBoxContainer();
            bars.AddThemeConstantOverride("separation", 1);

            for (int ci = 0; ci < topCards.Count && ci < LineColors.Length; ci++)
            {
                var cardId = topCards[ci].label;
                var val = cumulativeData.GetValueOrDefault(cardId, new()).GetValueOrDefault(combatIds[j], 0);
                var h = Math.Max(1, barH * val / maxVal);

                var barCol = new VBoxContainer();
                barCol.AddThemeConstantOverride("separation", 0);
                barCol.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;

                if (val > 0)
                {
                    var valLbl = new Label();
                    valLbl.Text = $"{val}";
                    valLbl.AddThemeFontSizeOverride("font_size", 8);
                    valLbl.AddThemeColorOverride("font_color", LineColors[ci]);
                    valLbl.HorizontalAlignment = HorizontalAlignment.Center;
                    barCol.AddChild(valLbl);
                }

                var bar = new ColorRect();
                bar.Color = LineColors[ci];
                bar.CustomMinimumSize = new Vector2(8, h);
                barCol.AddChild(bar);

                bars.AddChild(barCol);
            }

            combatCol.AddChild(bars);

            // X label
            var xLbl = new Label();
            xLbl.Text = j < combatLabels.Count ? FormatShort(combatLabels[j].label) : $"{j+1}";
            xLbl.AddThemeFontSizeOverride("font_size", 8);
            xLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            xLbl.HorizontalAlignment = HorizontalAlignment.Center;
            combatCol.AddChild(xLbl);

            row.AddChild(combatCol);
        }

        section.AddChild(row);
        parent.AddChild(section);
    }

    private static void AddBarColumn(HBoxContainer parent, string header,
        List<(string label, int value)> stats, Color barColor, string suffix)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var headerLbl = new Label();
        headerLbl.Text = header;
        headerLbl.AddThemeFontSizeOverride("font_size", 18);
        headerLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        col.AddChild(headerLbl);

        var maxVal = stats.Count > 0 ? stats.Max(s => s.value) : 1;
        if (maxVal == 0) maxVal = 1;

        foreach (var (cardId, value) in stats)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var nameLbl = new Label();
            nameLbl.Text = FormatCardName(cardId);
            nameLbl.CustomMinimumSize = new Vector2(120, 0);
            nameLbl.AddThemeFontSizeOverride("font_size", 13);
            nameLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            row.AddChild(nameLbl);
            var bar = new ColorRect();
            bar.Color = barColor;
            bar.CustomMinimumSize = new Vector2(150f * value / maxVal, 14);
            row.AddChild(bar);
            var valLbl = new Label();
            valLbl.Text = $"{value}{suffix}";
            valLbl.AddThemeFontSizeOverride("font_size", 13);
            valLbl.AddThemeColorOverride("font_color", Colors.White);
            row.AddChild(valLbl);
            col.AddChild(row);
        }

        if (stats.Count == 0)
        {
            var e = new Label();
            e.Text = "No data";
            e.AddThemeFontSizeOverride("font_size", 13);
            e.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
            col.AddChild(e);
        }
        parent.AddChild(col);
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

    private static string FormatShort(string id)
    {
        var name = id;
        if (name.StartsWith("ENCOUNTER.")) name = name.Substring(10);
        foreach (var s in new[] { "_WEAK", "_NORMAL", "_ELITE", "_BOSS" })
            if (name.EndsWith(s)) { name = name.Substring(0, name.Length - s.Length); break; }
        if (name.Length > 8) name = name.Substring(0, 8);
        return name;
    }
}
