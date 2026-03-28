using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Full-screen run stats overlay. Toggle with F6.
/// Shows aggregate stats across all combats in the current run.
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

        // Aggregate queries
        var topPlayed = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 10", runId);

        var topDamage = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, SUM(a2.Amount) as total
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
              GROUP BY a1.SourceId ORDER BY total DESC LIMIT 10", runId);

        var topBlock = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, SUM(a2.Amount) as total
              FROM CombatActions a1
              JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='BLOCK_GAINED'
                AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE(
                  (SELECT MIN(a3.Seq) FROM CombatActions a3
                   WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId ORDER BY total DESC LIMIT 10", runId);

        // Summary stats
        var combatCount = LiveRunDb.QueryTopStats(
            @"SELECT 'c', COUNT(*) FROM Combats WHERE RunId=@runId", runId);
        var turnCount = LiveRunDb.QueryTopStats(
            @"SELECT 't', COUNT(*) FROM Turns t JOIN Combats c ON t.CombatId=c.Id WHERE c.RunId=@runId", runId);
        var totalDmg = LiveRunDb.QueryTopStats(
            @"SELECT 'd', SUM(a.Amount) FROM CombatActions a
              JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='DAMAGE_DEALT'
                AND a.SourceId LIKE 'CHARACTER.%' AND a.TargetId NOT LIKE 'CHARACTER.%'", runId);
        var totalTaken = LiveRunDb.QueryTopStats(
            @"SELECT 't', SUM(a.Amount) FROM CombatActions a
              JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='DAMAGE_TAKEN'
                AND a.SourceId LIKE 'CHARACTER.%'", runId);
        var totalBlock = LiveRunDb.QueryTopStats(
            @"SELECT 'b', SUM(a.Amount) FROM CombatActions a
              JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='BLOCK_GAINED'
                AND a.SourceId LIKE 'CHARACTER.%'", runId);

        if (topPlayed.Count == 0) return;

        // Build overlay
        _panel = new PanelContainer();
        _panel.Name = OverlayName;
        _panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.ZIndex = 100;

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.02f, 0.02f, 0.05f, 0.95f);
        bgStyle.ContentMarginLeft = 60;
        bgStyle.ContentMarginRight = 60;
        bgStyle.ContentMarginTop = 30;
        bgStyle.ContentMarginBottom = 30;
        _panel.AddThemeStyleboxOverride("panel", bgStyle);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 20);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Title
        var title = new Label();
        title.Text = "Run Stats (F6 to close)";
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        // Summary line
        var combats = combatCount.Count > 0 ? combatCount[0].value : 0;
        var turns = turnCount.Count > 0 ? turnCount[0].value : 0;
        var dealt = totalDmg.Count > 0 ? totalDmg[0].value : 0;
        var taken = totalTaken.Count > 0 ? totalTaken[0].value : 0;
        var blocked = totalBlock.Count > 0 ? totalBlock[0].value : 0;

        var summary = new Label();
        summary.Text = $"Combats: {combats}    Turns: {turns}    Damage Dealt: {dealt}    Damage Taken: {taken}    Block: {blocked}";
        summary.AddThemeFontSizeOverride("font_size", 18);
        summary.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
        summary.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(summary);

        vbox.AddChild(new HSeparator());

        // Three columns side by side
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 40);
        cols.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        AddBarColumn(cols, "Most Played", topPlayed, new Color(0.5f, 0.5f, 0.7f), "x");
        AddBarColumn(cols, "Top Damage", topDamage, new Color(0.85f, 0.25f, 0.2f), " dmg");
        AddBarColumn(cols, "Top Block", topBlock, new Color(0.2f, 0.5f, 0.85f), " blk");

        vbox.AddChild(cols);

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

    private static void AddBarColumn(HBoxContainer parent, string header,
        List<(string label, int value)> stats, Color barColor, string suffix)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var headerLbl = new Label();
        headerLbl.Text = header;
        headerLbl.AddThemeFontSizeOverride("font_size", 22);
        headerLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        col.AddChild(headerLbl);

        var maxVal = stats.Count > 0 ? stats.Max(s => s.value) : 1;
        if (maxVal == 0) maxVal = 1;

        foreach (var (cardId, value) in stats)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var nameLbl = new Label();
            nameLbl.Text = FormatCardName(cardId);
            nameLbl.CustomMinimumSize = new Vector2(140, 0);
            nameLbl.AddThemeFontSizeOverride("font_size", 16);
            nameLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            row.AddChild(nameLbl);

            var bar = new ColorRect();
            bar.Color = barColor;
            bar.CustomMinimumSize = new Vector2(200f * value / maxVal, 18);
            row.AddChild(bar);

            var valLbl = new Label();
            valLbl.Text = $"{value}{suffix}";
            valLbl.AddThemeFontSizeOverride("font_size", 16);
            valLbl.AddThemeColorOverride("font_color", Colors.White);
            row.AddChild(valLbl);

            col.AddChild(row);
        }

        if (stats.Count == 0)
        {
            var emptyLbl = new Label();
            emptyLbl.Text = "No data";
            emptyLbl.AddThemeFontSizeOverride("font_size", 14);
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
            col.AddChild(emptyLbl);
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
}
