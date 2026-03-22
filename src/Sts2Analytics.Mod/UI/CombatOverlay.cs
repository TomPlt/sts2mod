using System.Collections.Generic;
using Godot;

namespace SpireOracle.UI;

/// <summary>
/// Shows a compact overlay at the top of the combat screen with encounter Elo, pool Elo, and deck Elo.
/// Auto-hides after a few seconds.
/// </summary>
public static class CombatOverlay
{
    private const string OverlayName = "SpireOracleCombatOverlay";
    private static PanelContainer? _panel;

    public static void Show(List<string> lines, double encounterElo, double deckElo)
    {
        Hide(); // Remove any existing overlay

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        _panel = new PanelContainer();
        _panel.Name = OverlayName;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.88f);
        style.BorderColor = encounterElo > deckElo
            ? new Color(0.95f, 0.3f, 0.3f, 0.8f)  // red border — encounter is stronger
            : new Color(0.3f, 0.85f, 0.3f, 0.8f);  // green border — deck is stronger
        style.BorderWidthBottom = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 10;
        _panel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        var isFirst = true;
        foreach (var line in lines)
        {
            var label = new Label();
            label.Text = line;
            label.HorizontalAlignment = HorizontalAlignment.Center;

            if (isFirst)
            {
                // Title line (encounter name)
                label.AddThemeFontSizeOverride("font_size", 22);
                label.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
                isFirst = false;
            }
            else
            {
                label.AddThemeFontSizeOverride("font_size", 18);
                label.AddThemeColorOverride("font_color", Colors.White);
            }

            vbox.AddChild(label);
        }

        _panel.AddChild(vbox);

        // Position top-center
        _panel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        // Position: top-left area, below the top bar
        _panel.Position = new Vector2(20, 100);
        _panel.ZIndex = 100;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        tree.Root.AddChild(_panel);
    }

    public static void Hide()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _panel = null;
    }
}
