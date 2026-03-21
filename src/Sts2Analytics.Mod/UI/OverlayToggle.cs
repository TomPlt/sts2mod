using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

public partial class OverlayToggle : Node
{
    private MapIntelPanel? _mapPanel;
    private bool _mapVisible;
    private double _pollTimer;
    private const double PollInterval = 0.5; // check every 500ms

    public override void _Ready()
    {
        _mapPanel = new MapIntelPanel();
        GetTree().Root.CallDeferred("add_child", _mapPanel);
    }

    public override void _Process(double delta)
    {
        _pollTimer += delta;
        if (_pollTimer < PollInterval) return;
        _pollTimer = 0;

        if (!DataLoader.IsLoaded || _mapPanel == null) return;

        var mapScreen = FindMapScreen();
        if (mapScreen != null && mapScreen.Visible)
        {
            if (!_mapVisible)
            {
                _mapVisible = true;
                var (character, actIndex) = DetectCurrentContext();
                GD.Print($"[SpireOracle] Map detected, showing intel for {character} Act {actIndex + 1}");
                _mapPanel.UpdateForContext(character, actIndex);
            }
        }
        else
        {
            if (_mapVisible)
            {
                _mapVisible = false;
                _mapPanel.Visible = false;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.F2)
        {
            ModEntry.OverlayEnabled = !ModEntry.OverlayEnabled;
            OverlayFactory.SetAllOverlaysVisible(ModEntry.OverlayEnabled);

            // Also toggle map panel
            if (_mapPanel != null && _mapVisible)
                _mapPanel.Visible = ModEntry.OverlayEnabled;

            GD.Print($"[SpireOracle] Overlay {(ModEntry.OverlayEnabled ? "enabled" : "disabled")}");
            GetViewport().SetInputAsHandled();
        }
    }

    private Control? FindMapScreen()
    {
        // Search for the map screen node in the scene tree
        // STS2 uses Godot nodes — look for controls with "Map" in name
        var root = GetTree().Root;
        return FindNodeByPattern(root, "Map");
    }

    private static Control? FindNodeByPattern(Node node, string pattern)
    {
        // BFS with depth limit to find a visible Control with pattern in name
        var queue = new System.Collections.Generic.Queue<(Node node, int depth)>();
        queue.Enqueue((node, 0));
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth > 6) continue;
            if (current is Control ctrl
                && current.Name.ToString().Contains(pattern, System.StringComparison.OrdinalIgnoreCase)
                && !current.Name.ToString().Contains("SpireOracle")) // skip our own nodes
                return ctrl;
            foreach (var child in current.GetChildren())
                queue.Enqueue((child, depth + 1));
        }
        return null;
    }

    private static (string character, int actIndex) DetectCurrentContext()
    {
        // TODO: Read actual character/act from STS2 game state once we discover the API
        // For now, return first character with map intel data, act 0
        var characters = DataLoader.GetMapIntelCharacters();
        return (characters.Count > 0 ? characters[0] : "CHARACTER.IRONCLAD", 0);
    }
}
