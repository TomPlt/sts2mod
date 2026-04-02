using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

/// <summary>
/// Player stats overlay showing overall and per-character win rates and streaks. Toggle with F7.
/// </summary>
public static class PlayerStatsOverlay
{
    private const string OverlayName = "SpireOraclePlayerStats";
    private static Control? _panel;
    private static bool _visible;

    private static readonly Color AccentColor = new(0.83f, 0.33f, 0.16f);
    private static readonly Color HeaderColor = new(0.8f, 0.8f, 0.85f);
    private static readonly Color SubtextColor = new(0.5f, 0.5f, 0.6f);
    private static readonly Color WinColor = new(0.3f, 0.85f, 0.3f);
    private static readonly Color LossColor = new(0.85f, 0.25f, 0.2f);
    private static readonly Color NeutralColor = new(0.95f, 0.85f, 0.2f);
    private static readonly Color StreakColor = new(1f, 0.6f, 0.1f);
    private static readonly Color BarBgColor = new(0.15f, 0.15f, 0.2f);

    public static bool IsVisible => _visible;

    public static void Toggle()
    {
        if (_visible) Hide();
        else Show();
    }

    public static void Show()
    {
        Hide();
        if (!DataLoader.IsLoaded) return;

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        var players = DataLoader.GetPlayerRunCounts();
        if (players.Count == 0) return;

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
        vbox.AddThemeConstantOverride("separation", 24);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var title = new Label();
        title.Text = "Player Stats (F7 to close)";
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", AccentColor);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        foreach (var player in players)
            AddPlayerSection(vbox, player);

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

    private static void AddPlayerSection(VBoxContainer parent, PlayerRunCount player)
    {
        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 10);

        // Player name + overall stats
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 16);

        var nameLbl = new Label();
        nameLbl.Text = player.Name;
        nameLbl.AddThemeFontSizeOverride("font_size", 24);
        nameLbl.AddThemeColorOverride("font_color", AccentColor);
        nameRow.AddChild(nameLbl);

        var overallLbl = new Label();
        overallLbl.Text = $"{player.WinRate:P0}  ({player.Wins}W / {player.Runs - player.Wins}L / {player.Runs} total)";
        overallLbl.AddThemeFontSizeOverride("font_size", 20);
        overallLbl.AddThemeColorOverride("font_color", player.WinRate >= 0.5 ? WinColor : NeutralColor);
        nameRow.AddChild(overallLbl);

        var overallStreakLbl = new Label();
        overallStreakLbl.Text = $"streak {player.CurrentWinStreak} / best {player.MaxWinStreak}";
        overallStreakLbl.AddThemeFontSizeOverride("font_size", 20);
        overallStreakLbl.AddThemeColorOverride("font_color", player.CurrentWinStreak > 0 ? StreakColor : SubtextColor);
        nameRow.AddChild(overallStreakLbl);

        section.AddChild(nameRow);

        // Per-character breakdown
        var chars = player.ByCharacter;
        if (chars != null && chars.Count > 0)
        {
            var grid = new VBoxContainer();
            grid.AddThemeConstantOverride("separation", 6);

            // Header row
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 0);
            AddCell(headerRow, "Character", 180, 14, SubtextColor);
            AddCell(headerRow, "Win Rate", 90, 14, SubtextColor);
            AddCell(headerRow, "Record", 110, 14, SubtextColor);
            AddCell(headerRow, "", 200, 14, SubtextColor);
            AddCell(headerRow, "Current", 80, 14, SubtextColor);
            AddCell(headerRow, "Best", 80, 14, SubtextColor);
            grid.AddChild(headerRow);

            foreach (var c in chars)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 0);

                var charName = c.Character.Replace("CHARACTER.", "");
                AddCell(row, charName, 180, 16, HeaderColor);
                AddCell(row, $"{c.WinRate:P0}", 90, 16,
                    c.WinRate >= 0.6 ? WinColor : c.WinRate >= 0.4 ? NeutralColor : LossColor);
                AddCell(row, $"{c.Wins}W / {c.Runs - c.Wins}L", 110, 14, new Color(0.7f, 0.7f, 0.75f));

                AddWinBar(row, c.WinRate, 200);

                AddCell(row, c.CurrentWinStreak > 0 ? $"{c.CurrentWinStreak}" : "-", 80, 16,
                    c.CurrentWinStreak > 0 ? StreakColor : SubtextColor);
                AddCell(row, c.MaxWinStreak > 0 ? $"{c.MaxWinStreak}" : "-", 80, 16,
                    c.MaxWinStreak > 0 ? HeaderColor : SubtextColor);

                grid.AddChild(row);
            }

            section.AddChild(grid);
        }

        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 16);
        section.AddChild(sep);

        parent.AddChild(section);
    }

    private static void AddCell(HBoxContainer row, string text, float minWidth, int fontSize, Color color)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.CustomMinimumSize = new Vector2(minWidth, 0);
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        lbl.AddThemeColorOverride("font_color", color);
        row.AddChild(lbl);
    }

    private static void AddWinBar(HBoxContainer row, double winRate, float maxWidth)
    {
        var barContainer = new Control();
        barContainer.CustomMinimumSize = new Vector2(maxWidth + 10, 18);

        var bg = new ColorRect();
        bg.Color = BarBgColor;
        bg.Position = new Vector2(0, 3);
        bg.Size = new Vector2(maxWidth, 12);
        barContainer.AddChild(bg);

        var barWidth = (float)(maxWidth * Math.Min(winRate, 1.0));
        if (barWidth > 0)
        {
            var winBar = new ColorRect();
            winBar.Color = winRate >= 0.6 ? WinColor : winRate >= 0.4 ? NeutralColor : LossColor;
            winBar.Position = new Vector2(0, 3);
            winBar.Size = new Vector2(barWidth, 12);
            barContainer.AddChild(winBar);
        }

        row.AddChild(barContainer);
    }
}
