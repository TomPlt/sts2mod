using Godot;
using SpireOracle.Data;
using System.Collections.Generic;

namespace SpireOracle.UI;

public static class OverlayFactory
{
    private const string OverlayGroup = "spire_oracle_overlay";

    // Track show timestamps per card holder to debounce flicker
    private static readonly System.Collections.Generic.Dictionary<ulong, bool> _hoverState = new();

    public static void AddOverlay(Control cardHolder, CardStats stats, double skipElo)
    {
        // Remove any existing overlay on this card
        RemoveOverlay(cardHolder);

        // --- Elo Badge (top-right) ---
        var eloBadge = new PanelContainer();
        eloBadge.Name = "SpireOracleEloBadge";
        eloBadge.AddToGroup(OverlayGroup);

        var eloBadgeStyle = new StyleBoxFlat();
        eloBadgeStyle.BgColor = stats.Elo >= 1650
            ? new Color(0.83f, 0.33f, 0.16f) // ember #d4552a
            : stats.Elo >= 1500
                ? new Color(0.14f, 0.19f, 0.27f) // grey #243044
                : new Color(0.16f, 0.10f, 0.10f); // dark red #2a1a1a
        eloBadgeStyle.CornerRadiusBottomLeft = 4;
        eloBadgeStyle.CornerRadiusBottomRight = 4;
        eloBadgeStyle.CornerRadiusTopLeft = 4;
        eloBadgeStyle.CornerRadiusTopRight = 4;
        eloBadgeStyle.ContentMarginLeft = 14;
        eloBadgeStyle.ContentMarginRight = 14;
        eloBadgeStyle.ContentMarginTop = 6;
        eloBadgeStyle.ContentMarginBottom = 6;
        eloBadge.AddThemeStyleboxOverride("panel", eloBadgeStyle);

        var eloLabel = new Label();
        eloLabel.Text = $"{stats.Elo:F0}";
        eloLabel.AddThemeFontSizeOverride("font_size", 28);
        eloLabel.AddThemeColorOverride("font_color", Colors.White);
        eloBadge.AddChild(eloLabel);

        // --- Info strip below card (Elo + Recommendation in one row) ---
        var strip = new HBoxContainer();
        strip.Name = "SpireOracleStrip";
        strip.AddToGroup(OverlayGroup);
        strip.AddThemeConstantOverride("separation", 10);
        strip.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        strip.AnchorTop = 1f;
        strip.Position = new Vector2(-30, 240);
        strip.Alignment = BoxContainer.AlignmentMode.Center;
        strip.ZIndex = 5;
        strip.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Elo badge inside the strip
        eloBadge.SetAnchorsPreset(Control.LayoutPreset.Center);
        eloBadge.Position = Vector2.Zero;
        eloBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        strip.AddChild(eloBadge);

        // No pill — just the Elo badge under the card

        cardHolder.AddChild(strip);

        // --- Blind Spot Badge (top-right corner) ---
        if (!string.IsNullOrEmpty(stats.BlindSpot))
        {
            var bsBadge = new PanelContainer();
            bsBadge.Name = "SpireOracleBlindSpot";
            bsBadge.AddToGroup(OverlayGroup);

            var bsStyle = new StyleBoxFlat();
            bsStyle.CornerRadiusBottomLeft = 4;
            bsStyle.CornerRadiusBottomRight = 4;
            bsStyle.CornerRadiusTopLeft = 4;
            bsStyle.CornerRadiusTopRight = 4;
            bsStyle.ContentMarginLeft = 6;
            bsStyle.ContentMarginRight = 6;
            bsStyle.ContentMarginTop = 2;
            bsStyle.ContentMarginBottom = 2;

            var isOverPick = stats.BlindSpot == "over_pick";
            bsStyle.BgColor = isOverPick
                ? new Color(0.94f, 0.27f, 0.27f) // red
                : new Color(0.96f, 0.62f, 0.04f); // amber

            bsBadge.AddThemeStyleboxOverride("panel", bsStyle);

            var bsLabel = new Label();
            bsLabel.Text = isOverPick ? "⚠ OVER-PICK" : "⚠ UNDER-PICK";
            bsLabel.AddThemeFontSizeOverride("font_size", 18);
            bsLabel.AddThemeColorOverride("font_color", isOverPick
                ? Colors.White
                : new Color(0.1f, 0.1f, 0.1f));
            bsBadge.AddChild(bsLabel);

            // Position top-right
            bsBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            bsBadge.Position = new Vector2(-10, -10);
            bsBadge.ZIndex = 5;
            bsBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardHolder.AddChild(bsBadge);
        }

        // --- Detail Panel (below card, hidden by default) ---
        var detail = new PanelContainer();
        detail.Name = "SpireOracleDetail";
        detail.AddToGroup(OverlayGroup);
        detail.Visible = false;
        detail.MouseFilter = Control.MouseFilterEnum.Ignore;

        var detailStyle = new StyleBoxFlat();
        detailStyle.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f); // dark bg
        detailStyle.BorderColor = new Color(0.83f, 0.33f, 0.16f); // ember border
        detailStyle.BorderWidthBottom = 2;
        detailStyle.BorderWidthTop = 2;
        detailStyle.BorderWidthLeft = 2;
        detailStyle.BorderWidthRight = 2;
        detailStyle.CornerRadiusBottomLeft = 6;
        detailStyle.CornerRadiusBottomRight = 6;
        detailStyle.CornerRadiusTopLeft = 6;
        detailStyle.CornerRadiusTopRight = 6;
        detailStyle.ContentMarginLeft = 12;
        detailStyle.ContentMarginRight = 12;
        detailStyle.ContentMarginTop = 8;
        detailStyle.ContentMarginBottom = 8;
        detail.AddThemeStyleboxOverride("panel", detailStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        AddStatRow(vbox, "Rating", $"{stats.Elo:F0} ±{stats.Rd:F0}");
        if (stats.CombatElo > 0)
            AddStatRow(vbox, "Combat", $"{stats.CombatElo:F0} ±{stats.CombatRd:F0}");
        AddStatRow(vbox, "Act 1", stats.EloAct1 > 0 ? $"{stats.EloAct1:F0} ±{stats.RdAct1:F0}" : "—");
        AddStatRow(vbox, "Act 2", stats.EloAct2 > 0 ? $"{stats.EloAct2:F0} ±{stats.RdAct2:F0}" : "—");
        AddStatRow(vbox, "Act 3", stats.EloAct3 > 0 ? $"{stats.EloAct3:F0} ±{stats.RdAct3:F0}" : "—");
        AddStatRow(vbox, "Pick Rate", $"{stats.PickRate:P1}");
        AddStatRow(vbox, "Win (Picked)", $"{stats.WinRatePicked:P1}");
        AddStatRow(vbox, "Win (Skipped)", $"{stats.WinRateSkipped:P1}");
        AddStatRow(vbox, "Delta", $"{stats.Delta:+0.0%;-0.0%;0.0%}");

        if (!string.IsNullOrEmpty(stats.BlindSpot))
        {
            // Separator
            var sep = new HSeparator();
            sep.AddThemeConstantOverride("separation", 6);
            vbox.AddChild(sep);

            var isOverPick = stats.BlindSpot == "over_pick";
            var bsColor = isOverPick
                ? new Color(0.94f, 0.27f, 0.27f) // red
                : new Color(0.96f, 0.62f, 0.04f); // amber
            var bsType = isOverPick ? "OVER-PICK" : "UNDER-PICK";
            var bsHint = isOverPick
                ? "You pick this too often — it hurts your win rate"
                : "You skip this too often — picking it wins more";

            AddColoredStatRow(vbox, "Blind Spot", bsType, bsColor);
            AddStatRow(vbox, "Your Pick%", $"{stats.BlindSpotPickRate:P0}");
            AddStatRow(vbox, "Win Delta", $"{stats.BlindSpotWinRateDelta:+0.0%;-0.0%;0.0%}");

            var hintLabel = new Label();
            hintLabel.Text = bsHint;
            hintLabel.AddThemeFontSizeOverride("font_size", 16);
            hintLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            vbox.AddChild(hintLabel);
        }

        detail.AddChild(vbox);

        detail.CustomMinimumSize = new Vector2(280, 0);
        detail.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        detail.GrowHorizontal = Control.GrowDirection.Both;
        detail.Position = new Vector2(-140, 15);
        detail.ZAsRelative = false;
        detail.ZIndex = 200;
        cardHolder.AddChild(detail);
    }

    private static void AddColoredStatRow(VBoxContainer parent, string label, string value, Color valueColor)
    {
        var row = new HBoxContainer();

        var nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        var valueLabel = new Label();
        valueLabel.Text = value;
        valueLabel.AddThemeFontSizeOverride("font_size", 20);
        valueLabel.AddThemeColorOverride("font_color", valueColor);
        row.AddChild(valueLabel);

        parent.AddChild(row);
    }

    private static void AddStatRow(VBoxContainer parent, string label, string value)
    {
        var row = new HBoxContainer();

        var nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        var valueLabel = new Label();
        valueLabel.Text = value;
        valueLabel.AddThemeFontSizeOverride("font_size", 20);
        valueLabel.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(valueLabel);

        parent.AddChild(row);
    }

    public static void AddAncientOverlay(Control choiceHolder, AncientStats stats)
    {
        RemoveOverlay(choiceHolder);

        var badge = new PanelContainer();
        badge.Name = "SpireOracleAncientBadge";
        badge.AddToGroup(OverlayGroup);

        var badgeStyle = new StyleBoxFlat();
        badgeStyle.BgColor = stats.Rating >= 1600
            ? new Color(0.83f, 0.33f, 0.16f) // ember - strong
            : stats.Rating >= 1450
                ? new Color(0.14f, 0.19f, 0.27f) // grey - average
                : new Color(0.16f, 0.10f, 0.10f); // dark red - weak
        badgeStyle.CornerRadiusBottomLeft = 6;
        badgeStyle.CornerRadiusBottomRight = 6;
        badgeStyle.CornerRadiusTopLeft = 6;
        badgeStyle.CornerRadiusTopRight = 6;
        badgeStyle.ContentMarginLeft = 8;
        badgeStyle.ContentMarginRight = 8;
        badgeStyle.ContentMarginTop = 4;
        badgeStyle.ContentMarginBottom = 4;
        badge.AddThemeStyleboxOverride("panel", badgeStyle);

        var label = new Label();
        label.Text = $"{stats.Rating:F0}";
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_color", Colors.White);
        badge.AddChild(label);

        badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        badge.Position = new Vector2(-10, -10);
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.ZIndex = 5;
        choiceHolder.AddChild(badge);

        // --- Detail Panel (hidden by default, shown on hover) ---
        var detail = new PanelContainer();
        detail.Name = "SpireOracleDetail";
        detail.AddToGroup(OverlayGroup);
        detail.Visible = false;
        detail.MouseFilter = Control.MouseFilterEnum.Ignore;

        var detailStyle = new StyleBoxFlat();
        detailStyle.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        detailStyle.BorderColor = new Color(0.83f, 0.33f, 0.16f);
        detailStyle.BorderWidthBottom = 2;
        detailStyle.BorderWidthTop = 2;
        detailStyle.BorderWidthLeft = 2;
        detailStyle.BorderWidthRight = 2;
        detailStyle.CornerRadiusBottomLeft = 6;
        detailStyle.CornerRadiusBottomRight = 6;
        detailStyle.CornerRadiusTopLeft = 6;
        detailStyle.CornerRadiusTopRight = 6;
        detailStyle.ContentMarginLeft = 12;
        detailStyle.ContentMarginRight = 12;
        detailStyle.ContentMarginTop = 8;
        detailStyle.ContentMarginBottom = 8;
        detail.AddThemeStyleboxOverride("panel", detailStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        AddStatRow(vbox, "Overall", $"{stats.Rating:F0} ±{stats.Rd:F0}");
        AddStatRow(vbox, "Neow", stats.RatingNeow > 0 ? $"{stats.RatingNeow:F0} ±{stats.RdNeow:F0}" : "—");
        AddStatRow(vbox, "Post Act 1", stats.RatingPostAct1 > 0 ? $"{stats.RatingPostAct1:F0} ±{stats.RdPostAct1:F0}" : "—");
        AddStatRow(vbox, "Post Act 2", stats.RatingPostAct2 > 0 ? $"{stats.RatingPostAct2:F0} ±{stats.RdPostAct2:F0}" : "—");

        detail.AddChild(vbox);

        detail.CustomMinimumSize = new Vector2(260, 0);
        detail.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        detail.GrowHorizontal = Control.GrowDirection.Both;
        detail.Position = new Vector2(-130, 5);
        detail.ZAsRelative = false;
        detail.ZIndex = 200;
        choiceHolder.AddChild(detail);
    }

    /// <summary>
    /// Append a combat forecast section to an existing detail panel on a card holder.
    /// Call after AddOverlay.
    /// </summary>
    public static void AddForecast(Control cardHolder, DamageForcast forecast)
    {
        var detail = cardHolder.GetNodeOrNull<PanelContainer>("SpireOracleDetail");
        if (detail == null) return;

        var vbox = detail.GetChildOrNull<VBoxContainer>(0);
        if (vbox == null) return;

        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(sep);

        var headerLabel = new Label();
        headerLabel.Text = "Combat Forecast";
        headerLabel.AddThemeFontSizeOverride("font_size", 18);
        headerLabel.AddThemeColorOverride("font_color", new Color(0.36f, 0.72f, 0.83f));
        vbox.AddChild(headerLabel);

        AddStatRow(vbox, "Deck Elo", $"{forecast.CurrentDeckElo:F0} → {forecast.NewDeckElo:F0}");

        var eloDeltaStr = forecast.EloDelta >= 0
            ? $"+{forecast.EloDelta:F0}"
            : $"{forecast.EloDelta:F0}";
        var eloColor = forecast.EloDelta >= 0
            ? new Color(0.3f, 0.85f, 0.3f) // green
            : new Color(0.95f, 0.3f, 0.3f); // red
        AddColoredStatRow(vbox, "Elo Change", eloDeltaStr, eloColor);

        var dmgDeltaStr = forecast.DmgDelta <= 0
            ? $"{forecast.DmgDelta:F1} HP"
            : $"+{forecast.DmgDelta:F1} HP";
        var dmgColor = forecast.DmgDelta <= 0
            ? new Color(0.3f, 0.85f, 0.3f) // green — less damage is good
            : new Color(0.95f, 0.3f, 0.3f); // red — more damage is bad
        AddColoredStatRow(vbox, "Next Fight", dmgDeltaStr, dmgColor);

        AddStatRow(vbox, "Exp. Damage", $"{forecast.NewExpectedDmg:F1} HP");
    }

    public static void ShowDetail(Control cardHolder)
    {
        var id = cardHolder.GetInstanceId();
        _hoverState[id] = true;

        var detail = cardHolder.GetNodeOrNull<PanelContainer>("SpireOracleDetail");
        if (detail != null) detail.Visible = true;
    }

    public static void HideDetail(Control cardHolder)
    {
        var id = cardHolder.GetInstanceId();
        _hoverState[id] = false;

        // Debounce: wait 150ms then check if still not hovered
        var tree = cardHolder.GetTree();
        if (tree == null) return;

        tree.CreateTimer(0.15).Timeout += () =>
        {
            // Only hide if mouse hasn't re-entered during the delay
            if (_hoverState.TryGetValue(id, out var hovering) && !hovering
                && GodotObject.IsInstanceValid(cardHolder))
            {
                var detail = cardHolder.GetNodeOrNull<PanelContainer>("SpireOracleDetail");
                if (detail != null) detail.Visible = false;
            }
        };
    }

    public static void RemoveOverlay(Control cardHolder)
    {
        _hoverState.Remove(cardHolder.GetInstanceId());
        var toRemove = new System.Collections.Generic.List<Node>();
        foreach (var child in cardHolder.GetChildren())
        {
            if (child.IsInGroup(OverlayGroup))
            {
                toRemove.Add(child);
            }
        }
        foreach (var node in toRemove)
        {
            cardHolder.RemoveChild(node);
            node.QueueFree();
        }
    }

    public static void SetAllOverlaysVisible(bool visible)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;

        foreach (var node in tree.GetNodesInGroup(OverlayGroup))
        {
            if (node is Control control)
            {
                control.Visible = visible;
            }
        }
    }
}
