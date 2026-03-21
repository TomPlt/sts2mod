using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
public static class CardRewardPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardRewardSelectionScreen __instance,
        IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> extraOptions)
    {
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;

        try
        {
            var cardRow = __instance.GetNodeOrNull<Control>("UI/CardRow");
            if (cardRow == null) return;

            foreach (var child in cardRow.GetChildren())
            {
                if (child is NGridCardHolder holder)
                {
                    var cardId = holder.CardModel?.Id.ToString();
                    if (cardId == null) continue;

                    var stats = DataLoader.GetCard(cardId);
                    if (stats == null) continue;

                    OverlayFactory.AddOverlay(holder, stats, DataLoader.SkipElo);

                    // Connect hover signals for detail panel
                    // The hitbox child handles mouse hover, not the holder itself
                    var hitbox = holder.Hitbox;
                    if (hitbox != null)
                    {
                        hitbox.Connect(Control.SignalName.MouseEntered,
                            Callable.From(() => OverlayFactory.ShowDetail(holder)));
                        hitbox.Connect(Control.SignalName.MouseExited,
                            Callable.From(() => OverlayFactory.HideDetail(holder)));
                    }
                    // Also connect focus signals for controller support
                    holder.Connect(Control.SignalName.FocusEntered,
                        Callable.From(() => OverlayFactory.ShowDetail(holder)));
                    holder.Connect(Control.SignalName.FocusExited,
                        Callable.From(() => OverlayFactory.HideDetail(holder)));
                }
            }

            // Add skip line reference
            var ui = __instance.GetNodeOrNull<Control>("UI");
            if (ui != null)
            {
                var existing = ui.GetNodeOrNull("SpireOracleSkipLine");
                existing?.QueueFree();

                var container = new HBoxContainer();
                container.Name = "SpireOracleSkipLine";
                container.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
                container.AnchorTop = 1f;
                container.Position = new Vector2(0, -120);
                container.Alignment = BoxContainer.AlignmentMode.Center;

                var label = new Label();
                label.Text = $"SKIP LINE: {DataLoader.SkipElo:F0}";
                label.AddThemeFontSizeOverride("font_size", 18);
                label.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f, 0.6f));
                container.AddChild(label);

                // Separator
                var sep = new Label();
                sep.Text = "  |  ";
                sep.AddThemeFontSizeOverride("font_size", 18);
                sep.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.3f));
                container.AddChild(sep);

                // Dashboard button
                var dashBtn = new Button();
                dashBtn.Text = "Open Dashboard";
                dashBtn.AddThemeFontSizeOverride("font_size", 16);
                dashBtn.AddThemeColorOverride("font_color", new Color(0.36f, 0.72f, 0.83f));
                var btnStyle = new StyleBoxFlat();
                btnStyle.BgColor = new Color(0.1f, 0.15f, 0.2f, 0.8f);
                btnStyle.BorderColor = new Color(0.36f, 0.72f, 0.83f, 0.3f);
                btnStyle.BorderWidthBottom = 1;
                btnStyle.BorderWidthTop = 1;
                btnStyle.BorderWidthLeft = 1;
                btnStyle.BorderWidthRight = 1;
                btnStyle.CornerRadiusBottomLeft = 4;
                btnStyle.CornerRadiusBottomRight = 4;
                btnStyle.CornerRadiusTopLeft = 4;
                btnStyle.CornerRadiusTopRight = 4;
                btnStyle.ContentMarginLeft = 12;
                btnStyle.ContentMarginRight = 12;
                btnStyle.ContentMarginTop = 4;
                btnStyle.ContentMarginBottom = 4;
                dashBtn.AddThemeStyleboxOverride("normal", btnStyle);
                var btnHover = (StyleBoxFlat)btnStyle.Duplicate();
                btnHover.BgColor = new Color(0.15f, 0.22f, 0.3f, 0.9f);
                dashBtn.AddThemeStyleboxOverride("hover", btnHover);
                dashBtn.Pressed += () =>
                {
                    OS.ShellOpen("http://localhost:5150");
                };
                container.AddChild(dashBtn);

                ui.AddChild(container);
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SpireOracle] Error in CardRewardPatch: {ex.Message}");
        }
    }
}
