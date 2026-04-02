using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Full-screen run stats overlay with cumulative line charts. Toggle with F6.
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

        // Combat list for x-axis
        var combatList = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId, c.FloorIndex
              FROM Combats c WHERE c.RunId=@runId ORDER BY c.Id", runId);

        var combatIds = LiveRunDb.QueryTopStats(
            @"SELECT CAST(c.Id AS TEXT), c.Id
              FROM Combats c WHERE c.RunId=@runId ORDER BY c.Id", runId);

        if (combatIds.Count == 0) return;
        var idList = combatIds.Select(c => c.label).ToList();

        // Top 6 cards by total plays
        var topCards = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 6", runId);

        // Per-combat plays per card
        var playsRaw = LiveRunDb.QueryGroupedStats(
            @"SELECT a.SourceId, CAST(c.Id AS TEXT), COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId, c.Id ORDER BY c.Id", runId);

        // Per-combat damage per card
        var dmgRaw = LiveRunDb.QueryGroupedStats(
            @"SELECT a1.SourceId, CAST(c.Id AS TEXT), COALESCE(SUM(a2.Amount), 0)
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

        // Build cumulative data
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
        vbox.AddThemeConstantOverride("separation", 30);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Title
        var title = new Label();
        title.Text = "Run Stats (F6 to close)";
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        // Summary stats
        var combatCount = LiveRunDb.QueryTopStats(
            @"SELECT 'c', COUNT(*) FROM Combats WHERE RunId=@runId", runId);
        var turnCount = LiveRunDb.QueryTopStats(
            @"SELECT 't', COUNT(*) FROM Turns t JOIN Combats c ON t.CombatId=c.Id WHERE c.RunId=@runId", runId);
        var totalDmg = LiveRunDb.QueryTopStats(
            @"SELECT 'd', COALESCE(SUM(a.Amount), 0) FROM CombatActions a
              JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='DAMAGE_DEALT'
                AND a.SourceId LIKE 'CHARACTER.%' AND a.TargetId NOT LIKE 'CHARACTER.%'", runId);
        var totalTaken = LiveRunDb.QueryTopStats(
            @"SELECT 't', COALESCE(SUM(a.Amount), 0) FROM CombatActions a
              JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='DAMAGE_TAKEN'
                AND a.SourceId LIKE 'CHARACTER.%'", runId);
        var totalBlock = LiveRunDb.QueryTopStats(
            @"SELECT 'b', COALESCE(SUM(a.Amount), 0) FROM CombatActions a
              JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='BLOCK_GAINED'
                AND a.SourceId LIKE 'CHARACTER.%'", runId);

        var topDamage = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, COALESCE(SUM(a2.Amount), 0) as total
              FROM CombatActions a1
              LEFT JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='DAMAGE_DEALT'
                AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.TargetId NOT LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE(
                  (SELECT MIN(a3.Seq) FROM CombatActions a3
                   WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId HAVING total > 0 ORDER BY total DESC LIMIT 10", runId);

        var topBlock = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, COALESCE(SUM(a2.Amount), 0) as total
              FROM CombatActions a1
              LEFT JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='BLOCK_GAINED'
                AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE(
                  (SELECT MIN(a3.Seq) FROM CombatActions a3
                   WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId HAVING total > 0 ORDER BY total DESC LIMIT 10", runId);

        // Play counts per card (for per-play calculations)
        var allPlayCounts = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId", runId);
        var playCounts = allPlayCounts.ToDictionary(p => p.label, p => p.value);

        // Per-play damage (total dmg / plays), sorted desc
        var dmgPerPlay = topDamage
            .Where(d => playCounts.ContainsKey(d.label) && playCounts[d.label] > 0)
            .Select(d => (d.label, value: d.value / playCounts[d.label]))
            .OrderByDescending(d => d.value)
            .Take(10)
            .ToList();

        // Per-play block (total block / plays), sorted desc
        var blkPerPlay = topBlock
            .Where(b => playCounts.ContainsKey(b.label) && playCounts[b.label] > 0)
            .Select(b => (b.label, value: b.value / playCounts[b.label]))
            .OrderByDescending(b => b.value)
            .Take(10)
            .ToList();

        // Summary line
        var combats = combatCount.Count > 0 ? combatCount[0].value : 0;
        var turns = turnCount.Count > 0 ? turnCount[0].value : 0;
        var dealt = totalDmg.Count > 0 ? totalDmg[0].value : 0;
        var taken = totalTaken.Count > 0 ? totalTaken[0].value : 0;
        var blocked = totalBlock.Count > 0 ? totalBlock[0].value : 0;

        var summary = new Label();
        summary.Text = $"Combats: {combats}    Turns: {turns}    Dealt: {dealt}    Taken: {taken}    Block: {blocked}";
        summary.AddThemeFontSizeOverride("font_size", 18);
        summary.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
        summary.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(summary);

        // Per-play bar columns
        var perPlayCols = new HBoxContainer();
        perPlayCols.AddThemeConstantOverride("separation", 30);
        AddBarColumn(perPlayCols, "Dmg / Play", dmgPerPlay, new Color(0.85f, 0.25f, 0.2f), " dmg");
        AddBarColumn(perPlayCols, "Block / Play", blkPerPlay, new Color(0.2f, 0.5f, 0.85f), " blk");
        vbox.AddChild(perPlayCols);

        // Aggregate bar columns
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 30);
        AddBarColumn(cols, "Most Played", topCards, new Color(0.5f, 0.5f, 0.7f), "x");
        AddBarColumn(cols, "Top Damage", topDamage, new Color(0.85f, 0.25f, 0.2f), " dmg");
        AddBarColumn(cols, "Top Block", topBlock, new Color(0.2f, 0.5f, 0.85f), " blk");
        vbox.AddChild(cols);

        vbox.AddChild(new HSeparator());

        // HP line
        AddSingleLineChart(vbox, "HP Over Run", hpPerCombat, combatList,
            new Color(0.2f, 0.8f, 0.3f), 800, 180);

        // Cumulative plays
        AddMultiLineChart(vbox, "Cumulative Card Plays", topCards, cumuPlays,
            idList, combatList, 800, 250);

        // Cumulative damage
        AddMultiLineChart(vbox, "Cumulative Card Damage", topCards, cumuDmg,
            idList, combatList, 800, 250);

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

    /// <summary>
    /// Convert per-combat values into cumulative running totals per card.
    /// </summary>
    private static Dictionary<string, Dictionary<string, int>> BuildCumulative(
        List<(string label, int value)> topCards,
        List<(string group, string label, int value)> rawData,
        List<string> combatIds)
    {
        // Raw data: group=cardId, label=combatId, value=count
        var perCombat = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (cardId, combatId, value) in rawData)
        {
            if (!perCombat.ContainsKey(cardId))
                perCombat[cardId] = new Dictionary<string, int>();
            perCombat[cardId][combatId] = value;
        }

        // Build cumulative
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

    private static void AddSingleLineChart(VBoxContainer parent, string chartTitle,
        List<(string label, int value)> data, List<(string label, int value)> combatLabels,
        Color color, float width, float height)
    {
        if (data.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLbl = new Label();
        titleLbl.Text = chartTitle;
        titleLbl.AddThemeFontSizeOverride("font_size", 20);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLbl);

        var chart = new Control();
        chart.CustomMinimumSize = new Vector2(width, height);

        var maxVal = data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;
        var padding = 50f;
        var chartW = width - padding * 2;
        var chartH = height - padding;

        // Line
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

        // Dots + values + x labels
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

            if (i < combatLabels.Count)
            {
                var xLbl = new Label();
                xLbl.Text = FormatShort(combatLabels[i].label);
                xLbl.Position = new Vector2(x - 20, chartH + 4);
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
        Dictionary<string, Dictionary<string, int>> cumulativeData,
        List<string> combatIds, List<(string label, int value)> combatLabels,
        float width, float height)
    {
        if (topCards.Count == 0 || combatIds.Count == 0) return;

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
            var item = new HBoxContainer();
            item.AddThemeConstantOverride("separation", 4);
            var swatch = new ColorRect();
            swatch.Color = LineColors[i];
            swatch.CustomMinimumSize = new Vector2(12, 12);
            item.AddChild(swatch);
            var lbl = new Label();
            lbl.Text = FormatCardName(topCards[i].label);
            lbl.AddThemeFontSizeOverride("font_size", 12);
            lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            item.AddChild(lbl);
            legend.AddChild(item);
        }
        section.AddChild(legend);

        // Find max cumulative value
        var maxVal = 1;
        foreach (var (cardId, vals) in cumulativeData)
        {
            var cardMax = vals.Values.DefaultIfEmpty(0).Max();
            if (cardMax > maxVal) maxVal = cardMax;
        }

        var padding = 50f;
        var chartW = width - padding * 2;
        var chartH = height - padding;

        var chart = new Control();
        chart.CustomMinimumSize = new Vector2(width, height);

        // Draw lines
        for (int ci = 0; ci < topCards.Count && ci < LineColors.Length; ci++)
        {
            var cardId = topCards[ci].label;
            var cardVals = cumulativeData.GetValueOrDefault(cardId, new());

            var line = new Line2D();
            line.Width = 2;
            line.DefaultColor = LineColors[ci];

            for (int j = 0; j < combatIds.Count; j++)
            {
                var val = cardVals.GetValueOrDefault(combatIds[j], 0);
                var x = padding + chartW * j / Math.Max(combatIds.Count - 1, 1);
                var y = chartH - chartH * val / maxVal;
                line.AddPoint(new Vector2(x, y));
            }
            chart.AddChild(line);

            // End value label
            if (combatIds.Count > 0)
            {
                var lastVal = cardVals.GetValueOrDefault(combatIds[^1], 0);
                var lastX = padding + chartW;
                var lastY = chartH - chartH * lastVal / maxVal;
                var endLbl = new Label();
                endLbl.Text = $"{lastVal}";
                endLbl.Position = new Vector2(lastX + 4, lastY - 8);
                endLbl.AddThemeFontSizeOverride("font_size", 11);
                endLbl.AddThemeColorOverride("font_color", LineColors[ci]);
                chart.AddChild(endLbl);
            }
        }

        // X-axis labels
        for (int j = 0; j < combatIds.Count && j < combatLabels.Count; j++)
        {
            var x = padding + chartW * j / Math.Max(combatIds.Count - 1, 1);
            var xLbl = new Label();
            xLbl.Text = FormatShort(combatLabels[j].label);
            xLbl.Position = new Vector2(x - 20, chartH + 4);
            xLbl.AddThemeFontSizeOverride("font_size", 9);
            xLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            chart.AddChild(xLbl);
        }

        // Y-axis labels
        for (int i = 0; i <= 4; i++)
        {
            var y = chartH - chartH * i / 4;
            var yLbl = new Label();
            yLbl.Text = $"{maxVal * i / 4}";
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

    private static string FormatShort(string id)
    {
        var name = id;
        if (name.StartsWith("ENCOUNTER.")) name = name.Substring(10);
        foreach (var s in new[] { "_WEAK", "_NORMAL", "_ELITE", "_BOSS" })
            if (name.EndsWith(s)) { name = name.Substring(0, name.Length - s.Length); break; }
        if (name.Length > 10) name = name.Substring(0, 10);
        return name;
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
            var emptyLbl = new Label();
            emptyLbl.Text = "No data";
            emptyLbl.AddThemeFontSizeOverride("font_size", 13);
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
            col.AddChild(emptyLbl);
        }

        parent.AddChild(col);
    }
}
