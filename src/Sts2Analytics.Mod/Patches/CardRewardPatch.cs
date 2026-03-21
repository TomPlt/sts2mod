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

            int overlaysAdded = 0;
            foreach (var child in cardRow.GetChildren())
            {
                if (child is NGridCardHolder holder)
                {
                    var cardId = holder.CardModel?.Id.ToString();
                    if (cardId == null) continue;

                    var stats = DataLoader.GetCard(cardId);
                    if (stats == null) continue;

                    OverlayFactory.AddOverlay(holder, stats, DataLoader.SkipElo);
                    overlaysAdded++;

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

            // Don't show skip line for screens with no analytics (e.g. boss malus choices)
            if (overlaysAdded == 0) return;

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
                container.Position = new Vector2(0, -80);
                container.Alignment = BoxContainer.AlignmentMode.Center;

                // Skip overall rating
                var skipOverall = new Label();
                skipOverall.Text = $"Skip: {DataLoader.SkipElo:F0}";
                skipOverall.AddThemeFontSizeOverride("font_size", 28);
                skipOverall.AddThemeColorOverride("font_color", Colors.White);
                container.AddChild(skipOverall);

                // Per-act skip ratings
                var act1Skip = DataLoader.GetSkipElo(actIndex: 0);
                var act2Skip = DataLoader.GetSkipElo(actIndex: 1);
                var act3Skip = DataLoader.GetSkipElo(actIndex: 2);

                var skipActs = new Label();
                skipActs.Text = $"  A1: {act1Skip:F0}  A2: {act2Skip:F0}  A3: {act3Skip:F0}";
                skipActs.AddThemeFontSizeOverride("font_size", 28);
                skipActs.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
                container.AddChild(skipActs);

                // Separator
                var sep = new Label();
                sep.Text = "  ";
                sep.AddThemeFontSizeOverride("font_size", 28);
                container.AddChild(sep);

                // Dashboard button
                var dashBtn = new Button();
                dashBtn.Text = "Dashboard";
                dashBtn.AddThemeFontSizeOverride("font_size", 22);
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
                    OS.ShellOpen("http://localhost:5202/elo");
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
