using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

public static class OverlayFactory
{
    private const string OverlayGroup = "spire_oracle_overlay";

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
        eloBadgeStyle.ContentMarginLeft = 8;
        eloBadgeStyle.ContentMarginRight = 8;
        eloBadgeStyle.ContentMarginTop = 4;
        eloBadgeStyle.ContentMarginBottom = 4;
        eloBadge.AddThemeStyleboxOverride("panel", eloBadgeStyle);

        var eloLabel = new Label();
        eloLabel.Text = $"{stats.Elo:F0}";
        eloLabel.AddThemeFontSizeOverride("font_size", 14);
        eloLabel.AddThemeColorOverride("font_color", Colors.White);
        eloBadge.AddChild(eloLabel);

        eloBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        eloBadge.Position = new Vector2(-60, 4);
        cardHolder.AddChild(eloBadge);

        // --- Recommendation Pill (bottom-center) ---
        var pill = new PanelContainer();
        pill.Name = "SpireOraclePill";
        pill.AddToGroup(OverlayGroup);

        var pillStyle = new StyleBoxFlat();
        pillStyle.CornerRadiusBottomLeft = 8;
        pillStyle.CornerRadiusBottomRight = 8;
        pillStyle.CornerRadiusTopLeft = 8;
        pillStyle.CornerRadiusTopRight = 8;
        pillStyle.ContentMarginLeft = 12;
        pillStyle.ContentMarginRight = 12;
        pillStyle.ContentMarginTop = 4;
        pillStyle.ContentMarginBottom = 4;

        var pillLabel = new Label();
        pillLabel.AddThemeFontSizeOverride("font_size", 13);

        if (stats.Elo > skipElo + 50)
        {
            pillStyle.BgColor = new Color(0.15f, 0.5f, 0.15f); // green
            pillLabel.Text = "\u25b2 PICK";
            pillLabel.AddThemeColorOverride("font_color", new Color(0.7f, 1.0f, 0.7f));
        }
        else if (stats.Elo < skipElo - 50)
        {
            pillStyle.BgColor = new Color(0.5f, 0.15f, 0.15f); // red
            pillLabel.Text = "\u25bc SKIP";
            pillLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.7f, 0.7f));
        }
        else
        {
            pillStyle.BgColor = new Color(0.5f, 0.4f, 0.1f); // gold
            pillLabel.Text = "\u2014 OK";
            pillLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.9f, 0.6f));
        }

        pill.AddThemeStyleboxOverride("panel", pillStyle);
        pill.AddChild(pillLabel);

        pill.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        pill.AnchorTop = 1f;
        pill.Position = new Vector2(0, -30);
        pill.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        cardHolder.AddChild(pill);

        // --- Detail Panel (below card, hidden by default) ---
        var detail = new PanelContainer();
        detail.Name = "SpireOracleDetail";
        detail.AddToGroup(OverlayGroup);
        detail.Visible = false;

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

        AddStatRow(vbox, "Elo", $"{stats.Elo:F0}");
        AddStatRow(vbox, "Pick Rate", $"{stats.PickRate:P1}");
        AddStatRow(vbox, "Win (Picked)", $"{stats.WinRatePicked:P1}");
        AddStatRow(vbox, "Win (Skipped)", $"{stats.WinRateSkipped:P1}");
        AddStatRow(vbox, "Delta", $"{stats.Delta:+0.0;-0.0;0.0}");

        detail.AddChild(vbox);

        detail.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        detail.AnchorTop = 1f;
        detail.Position = new Vector2(0, 10);
        cardHolder.AddChild(detail);
    }

    private static void AddStatRow(VBoxContainer parent, string label, string value)
    {
        var row = new HBoxContainer();

        var nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        var valueLabel = new Label();
        valueLabel.Text = value;
        valueLabel.AddThemeFontSizeOverride("font_size", 11);
        valueLabel.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(valueLabel);

        parent.AddChild(row);
    }

    public static void ShowDetail(Control cardHolder)
    {
        var detail = cardHolder.GetNodeOrNull<PanelContainer>("SpireOracleDetail");
        if (detail != null) detail.Visible = true;
    }

    public static void HideDetail(Control cardHolder)
    {
        var detail = cardHolder.GetNodeOrNull<PanelContainer>("SpireOracleDetail");
        if (detail != null) detail.Visible = false;
    }

    public static void RemoveOverlay(Control cardHolder)
    {
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
