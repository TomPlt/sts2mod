using Godot;

namespace SpireOracle.UI;

public partial class OverlayToggle : Node
{
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.F2)
        {
            ModEntry.OverlayEnabled = !ModEntry.OverlayEnabled;
            OverlayFactory.SetAllOverlaysVisible(ModEntry.OverlayEnabled);
            GD.Print($"[SpireOracle] Overlay {(ModEntry.OverlayEnabled ? "enabled" : "disabled")}");
            GetViewport().SetInputAsHandled();
        }
    }
}
