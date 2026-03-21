using Godot;

namespace SpireOracle.UI;

public static class MapIntelPanelManager
{
    private static MapIntelPanel? _panel;

    private static void EnsurePanel()
    {
        if (_panel != null && IsInstanceValid(_panel)) return;

        _panel = new MapIntelPanel();
        var tree = Engine.GetMainLoop() as SceneTree;
        tree?.Root.AddChild(_panel);
        GD.Print("[SpireOracle] MapIntelPanel created and added to scene root");
    }

    private static bool IsInstanceValid(GodotObject obj)
    {
        try { return GodotObject.IsInstanceValid(obj); }
        catch { return false; }
    }

    public static bool IsVisible => _panel != null && IsInstanceValid(_panel) && _panel.Visible;

    public static void Show(string character, int actIndex)
    {
        EnsurePanel();
        _panel?.UpdateForContext(character, actIndex);
    }

    public static void Hide()
    {
        if (_panel != null) _panel.Visible = false;
    }
}
