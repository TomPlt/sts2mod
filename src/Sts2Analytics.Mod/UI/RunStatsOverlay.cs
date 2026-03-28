using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Full-screen run stats overlay. Toggle with F6.
/// Shows per-combat breakdowns: top 3 played, damage, block for each fight.
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

        // Per-combat top 3 played
        var playedPerCombat = LiveRunDb.QueryGroupedStats(
            @"SELECT c.EncounterId || ' (F' || c.FloorIndex || ')', a.SourceId, COUNT(*) as cnt
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY c.Id, a.SourceId ORDER BY c.Id, cnt DESC", runId);

        // Per-combat top 3 damage
        var dmgPerCombat = LiveRunDb.QueryGroupedStats(
            @"SELECT c.EncounterId || ' (F' || c.FloorIndex || ')', a1.SourceId, SUM(a2.Amount) as total
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
              GROUP BY c.Id, a1.SourceId ORDER BY c.Id, total DESC", runId);

        // Per-combat top 3 block
        var blkPerCombat = LiveRunDb.QueryGroupedStats(
            @"SELECT c.EncounterId || ' (F' || c.FloorIndex || ')', a1.SourceId, SUM(a2.Amount) as total
              FROM CombatActions a1
              JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='BLOCK_GAINED'
                AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE(
                  (SELECT MIN(a3.Seq) FROM CombatActions a3
                   WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY c.Id, a1.SourceId ORDER BY c.Id, total DESC", runId);

        // HP per combat
        var hpPerCombat = LiveRunDb.QueryTopStats(
            @"SELECT c.EncounterId || ' (F' || c.FloorIndex || ')', t.StartingHp
              FROM Turns t JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND t.TurnNumber = 1
              ORDER BY c.Id", runId);

        // Overall totals
        var topCardsOverall = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 10", runId);

        // Group per-combat data: take top 3 per combat
        var playedByFight = GroupTop3(playedPerCombat);
        var dmgByFight = GroupTop3(dmgPerCombat);
        var blkByFight = GroupTop3(blkPerCombat);

        // Get ordered combat list
        var combats = playedByFight.Keys.ToList();

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
        vbox.AddThemeConstantOverride("separation", 16);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Title
        var title = new Label();
        title.Text = "Run Stats (F6 to close)";
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        // HP bar across combats
        if (hpPerCombat.Count > 0)
        {
            AddSection(vbox, "HP Over Run");
            var hpRow = new HBoxContainer();
            hpRow.AddThemeConstantOverride("separation", 2);
            var maxHp = hpPerCombat.Max(h => h.value);
            if (maxHp == 0) maxHp = 1;
            foreach (var (label, hp) in hpPerCombat)
            {
                var col = new VBoxContainer();
                col.AddThemeConstantOverride("separation", 1);
                col.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;

                var valLbl = new Label();
                valLbl.Text = $"{hp}";
                valLbl.AddThemeFontSizeOverride("font_size", 10);
                valLbl.AddThemeColorOverride("font_color", Colors.White);
                valLbl.HorizontalAlignment = HorizontalAlignment.Center;
                col.AddChild(valLbl);

                var bar = new ColorRect();
                bar.Color = hp > maxHp * 0.5f ? new Color(0.2f, 0.8f, 0.3f) :
                            hp > maxHp * 0.25f ? new Color(0.9f, 0.7f, 0.2f) :
                            new Color(0.9f, 0.25f, 0.2f);
                bar.CustomMinimumSize = new Vector2(30, Math.Max(2, 100f * hp / maxHp));
                col.AddChild(bar);

                hpRow.AddChild(col);
            }
            vbox.AddChild(hpRow);
        }

        // Per-combat breakdowns
        foreach (var combat in combats)
        {
            var combatSection = new VBoxContainer();
            combatSection.AddThemeConstantOverride("separation", 4);

            // Combat header
            var header = new Label();
            header.Text = FormatCombatName(combat);
            header.AddThemeFontSizeOverride("font_size", 18);
            header.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
            combatSection.AddChild(header);

            // Three columns: Played | Damage | Block
            var cols = new HBoxContainer();
            cols.AddThemeConstantOverride("separation", 30);

            var played = playedByFight.GetValueOrDefault(combat, new());
            var dmg = dmgByFight.GetValueOrDefault(combat, new());
            var blk = blkByFight.GetValueOrDefault(combat, new());

            AddMiniColumn(cols, "Played", played, new Color(0.5f, 0.5f, 0.7f));
            AddMiniColumn(cols, "Damage", dmg, new Color(0.85f, 0.25f, 0.2f));
            AddMiniColumn(cols, "Block", blk, new Color(0.2f, 0.5f, 0.85f));

            combatSection.AddChild(cols);
            combatSection.AddChild(new HSeparator());
            vbox.AddChild(combatSection);
        }

        // Overall top cards
        if (topCardsOverall.Count > 0)
        {
            AddSection(vbox, "Overall Top Cards");
            var maxVal = topCardsOverall.Max(c => c.value);
            if (maxVal == 0) maxVal = 1;
            foreach (var (cardId, count) in topCardsOverall)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);

                var nameLbl = new Label();
                nameLbl.Text = FormatCardName(cardId);
                nameLbl.CustomMinimumSize = new Vector2(160, 0);
                nameLbl.AddThemeFontSizeOverride("font_size", 14);
                nameLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
                row.AddChild(nameLbl);

                var bar = new ColorRect();
                bar.Color = new Color(0.5f, 0.5f, 0.7f);
                bar.CustomMinimumSize = new Vector2(300f * count / maxVal, 16);
                row.AddChild(bar);

                var valLbl = new Label();
                valLbl.Text = $"{count}";
                valLbl.AddThemeFontSizeOverride("font_size", 14);
                valLbl.AddThemeColorOverride("font_color", Colors.White);
                row.AddChild(valLbl);

                vbox.AddChild(row);
            }
        }

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

    private static Dictionary<string, List<(string label, int value)>> GroupTop3(
        List<(string group, string label, int value)> data)
    {
        var result = new Dictionary<string, List<(string, int)>>();
        foreach (var (group, label, value) in data)
        {
            if (!result.ContainsKey(group))
                result[group] = new List<(string, int)>();
            if (result[group].Count < 3)
                result[group].Add((label, value));
        }
        return result;
    }

    private static void AddSection(VBoxContainer parent, string title)
    {
        var lbl = new Label();
        lbl.Text = title;
        lbl.AddThemeFontSizeOverride("font_size", 20);
        lbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        parent.AddChild(lbl);
    }

    private static void AddMiniColumn(HBoxContainer parent, string header,
        List<(string label, int value)> stats, Color barColor)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        col.CustomMinimumSize = new Vector2(200, 0);

        var headerLbl = new Label();
        headerLbl.Text = header;
        headerLbl.AddThemeFontSizeOverride("font_size", 14);
        headerLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        col.AddChild(headerLbl);

        var maxVal = stats.Count > 0 ? stats.Max(s => s.value) : 1;
        if (maxVal == 0) maxVal = 1;

        foreach (var (cardId, value) in stats)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            var nameLbl = new Label();
            nameLbl.Text = FormatCardName(cardId);
            nameLbl.CustomMinimumSize = new Vector2(100, 0);
            nameLbl.AddThemeFontSizeOverride("font_size", 12);
            nameLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            row.AddChild(nameLbl);

            var bar = new ColorRect();
            bar.Color = barColor;
            bar.CustomMinimumSize = new Vector2(60f * value / maxVal, 12);
            row.AddChild(bar);

            var valLbl = new Label();
            valLbl.Text = $"{value}";
            valLbl.AddThemeFontSizeOverride("font_size", 12);
            valLbl.AddThemeColorOverride("font_color", Colors.White);
            row.AddChild(valLbl);

            col.AddChild(row);
        }

        if (stats.Count == 0)
        {
            var emptyLbl = new Label();
            emptyLbl.Text = "—";
            emptyLbl.AddThemeFontSizeOverride("font_size", 12);
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

    private static string FormatCombatName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var name = id;
        if (name.StartsWith("ENCOUNTER.")) name = name.Substring(10);
        // Keep floor info in parens
        var parenIdx = name.IndexOf(" (");
        var suffix = "";
        if (parenIdx > 0) { suffix = name.Substring(parenIdx); name = name.Substring(0, parenIdx); }
        foreach (var s in new[] { "_WEAK", "_NORMAL", "_ELITE", "_BOSS" })
            if (name.EndsWith(s)) { name = name.Substring(0, name.Length - s.Length); break; }
        name = string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
        return name + suffix;
    }
}
