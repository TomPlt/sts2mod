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
                container.Position = new Vector2(0, -40);
                container.Alignment = BoxContainer.AlignmentMode.Center;

                var label = new Label();
                label.Text = $"SKIP LINE: {DataLoader.SkipElo:F0}";
                label.AddThemeFontSizeOverride("font_size", 12);
                label.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f, 0.6f));
                container.AddChild(label);

                ui.AddChild(container);
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SpireOracle] Error in CardRewardPatch: {ex.Message}");
        }
    }
}
