using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Full-screen run stats overlay with temporal graphs. Toggle with F6.
/// Shows: damage per combat, HP over the run, top card usage per combat.
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

        // Query data
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

        var hpPerFloor = LiveRunDb.QueryTopStats(
            @"SELECT 'F' || c.FloorIndex, t.StartingHp
              FROM Turns t JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND t.TurnNumber = 1
              ORDER BY c.Id", runId);

        var topCards = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*)
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 10", runId);

        // Build overlay
        _panel = new PanelContainer();
        _panel.Name = OverlayName;

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.02f, 0.02f, 0.05f, 0.92f);
        bgStyle.ContentMarginLeft = 30;
        bgStyle.ContentMarginRight = 30;
        bgStyle.ContentMarginTop = 20;
        bgStyle.ContentMarginBottom = 20;
        _panel.AddThemeStyleboxOverride("panel", bgStyle);

        _panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.ZIndex = 100;

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 20);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Title
        var title = new Label();
        title.Text = "Run Stats (F6 to close)";
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        // Damage dealt per combat — bar chart
        AddBarChart(vbox, "Damage Dealt Per Combat", dmgPerCombat,
            new Color(0.85f, 0.25f, 0.2f), 600, 200);

        // Damage taken per combat — bar chart
        AddBarChart(vbox, "Damage Taken Per Combat", takenPerCombat,
            new Color(0.9f, 0.6f, 0.2f), 600, 200);

        // HP at start of each combat — line chart
        AddLineChart(vbox, "HP Over Run", hpPerFloor,
            new Color(0.2f, 0.8f, 0.3f), 600, 200);

        // Top cards played — horizontal bar chart
        AddHorizontalBars(vbox, "Top Cards Played", topCards,
            new Color(0.5f, 0.5f, 0.7f), 600);

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

    private static void AddBarChart(VBoxContainer parent, string title,
        List<(string label, int value)> data, Color barColor, float width, float height)
    {
        if (data.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLabel = new Label();
        titleLabel.Text = title;
        titleLabel.AddThemeFontSizeOverride("font_size", 18);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLabel);

        var chart = new ChartDrawer();
        chart.CustomMinimumSize = new Vector2(width, height);
        chart.Data = data;
        chart.BarColor = barColor;
        chart.ChartType = ChartDrawer.Type.Bar;
        section.AddChild(chart);

        parent.AddChild(section);
    }

    private static void AddLineChart(VBoxContainer parent, string title,
        List<(string label, int value)> data, Color lineColor, float width, float height)
    {
        if (data.Count == 0) return;

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);

        var titleLabel = new Label();
        titleLabel.Text = title;
        titleLabel.AddThemeFontSizeOverride("font_size", 18);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLabel);

        var chart = new ChartDrawer();
        chart.CustomMinimumSize = new Vector2(width, height);
        chart.Data = data;
        chart.BarColor = lineColor;
        chart.ChartType = ChartDrawer.Type.Line;
        section.AddChild(chart);

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
        titleLabel.AddThemeFontSizeOverride("font_size", 18);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        section.AddChild(titleLabel);

        var maxVal = data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;

        foreach (var (cardId, count) in data)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var name = FormatCardName(cardId);
            var nameLabel = new Label();
            nameLabel.Text = name;
            nameLabel.CustomMinimumSize = new Vector2(160, 0);
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            row.AddChild(nameLabel);

            var bar = new ColorRect();
            bar.Color = barColor;
            bar.CustomMinimumSize = new Vector2((maxWidth - 220) * count / maxVal, 16);
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
        // Strip common prefixes
        if (name.StartsWith("CARD.")) name = name.Substring(5);
        if (name.StartsWith("ENCOUNTER.")) name = name.Substring(10);
        // Remove pool suffixes
        foreach (var suffix in new[] { "_WEAK", "_NORMAL", "_ELITE", "_BOSS" })
            if (name.EndsWith(suffix)) { name = name.Substring(0, name.Length - suffix.Length); break; }
        // Upgrade suffix
        var upgrade = "";
        var plusIdx = name.IndexOf('+');
        if (plusIdx > 0) { upgrade = name.Substring(plusIdx); name = name.Substring(0, plusIdx); }
        // Title case
        name = string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
        return name + upgrade;
    }
}

/// <summary>
/// Custom Godot Control that draws bar or line charts via _Draw().
/// </summary>
public partial class ChartDrawer : Control
{
    public enum Type { Bar, Line }

    public List<(string label, int value)> Data { get; set; } = new();
    public Color BarColor { get; set; } = Colors.White;
    public Type ChartType { get; set; } = Type.Bar;

    public override void _Draw()
    {
        if (Data.Count == 0) return;

        var rect = GetRect();
        var w = rect.Size.X;
        var h = rect.Size.Y;
        var maxVal = Data.Max(d => d.value);
        if (maxVal == 0) maxVal = 1;

        var padding = 30f;
        var chartW = w - padding * 2;
        var chartH = h - padding * 2;

        // Axes
        var axisColor = new Color(0.3f, 0.3f, 0.4f);
        DrawLine(new Vector2(padding, padding), new Vector2(padding, h - padding), axisColor, 1);
        DrawLine(new Vector2(padding, h - padding), new Vector2(w - padding, h - padding), axisColor, 1);

        // Grid lines
        for (int i = 1; i <= 4; i++)
        {
            var y = h - padding - chartH * i / 4;
            DrawLine(new Vector2(padding, y), new Vector2(w - padding, y), new Color(0.2f, 0.2f, 0.25f), 1);
            var gridVal = maxVal * i / 4;
            DrawString(ThemeDB.FallbackFont, new Vector2(2, y + 4), $"{gridVal}", HorizontalAlignment.Left, -1, 10, new Color(0.5f, 0.5f, 0.6f));
        }

        if (ChartType == Type.Bar)
        {
            var barW = chartW / Data.Count * 0.7f;
            var gap = chartW / Data.Count * 0.3f;

            for (int i = 0; i < Data.Count; i++)
            {
                var (label, value) = Data[i];
                var barH = chartH * value / maxVal;
                var x = padding + i * (barW + gap) + gap / 2;
                var y = h - padding - barH;

                DrawRect(new Rect2(x, y, barW, barH), BarColor);

                // Value on top
                DrawString(ThemeDB.FallbackFont, new Vector2(x, y - 2), $"{value}", HorizontalAlignment.Left, -1, 10, Colors.White);

                // Label below
                var shortLabel = FormatShortLabel(label);
                DrawString(ThemeDB.FallbackFont, new Vector2(x, h - padding + 12), shortLabel, HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.6f, 0.7f));
            }
        }
        else // Line
        {
            var points = new List<Vector2>();
            for (int i = 0; i < Data.Count; i++)
            {
                var x = padding + chartW * i / Math.Max(Data.Count - 1, 1);
                var y = h - padding - chartH * Data[i].value / maxVal;
                points.Add(new Vector2(x, y));
            }

            // Draw filled area
            for (int i = 0; i < points.Count - 1; i++)
            {
                var fillColor = new Color(BarColor.R, BarColor.G, BarColor.B, 0.15f);
                var p1 = points[i];
                var p2 = points[i + 1];
                var bottom = h - padding;
                DrawPolygon(
                    new Vector2[] { p1, p2, new Vector2(p2.X, bottom), new Vector2(p1.X, bottom) },
                    new Color[] { fillColor, fillColor, fillColor, fillColor });
            }

            // Draw line
            for (int i = 0; i < points.Count - 1; i++)
                DrawLine(points[i], points[i + 1], BarColor, 2);

            // Draw dots + values
            for (int i = 0; i < points.Count; i++)
            {
                DrawCircle(points[i], 4, BarColor);
                DrawString(ThemeDB.FallbackFont, points[i] + new Vector2(4, -4), $"{Data[i].value}", HorizontalAlignment.Left, -1, 10, Colors.White);

                // Label below axis
                var x = padding + chartW * i / Math.Max(Data.Count - 1, 1);
                DrawString(ThemeDB.FallbackFont, new Vector2(x, h - padding + 12), Data[i].label, HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.6f, 0.7f));
            }
        }
    }

    private static string FormatShortLabel(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var name = id;
        if (name.StartsWith("ENCOUNTER.")) name = name.Substring(10);
        foreach (var suffix in new[] { "_WEAK", "_NORMAL", "_ELITE", "_BOSS" })
            if (name.EndsWith(suffix)) { name = name.Substring(0, name.Length - suffix.Length); break; }
        if (name.Length > 10) name = name.Substring(0, 10);
        return name;
    }
}
