using System;
using System.Collections.Generic;
using Godot;

namespace SpireOracle.UI;

/// <summary>
/// In-game debug log panel (bottom-right). Toggle with F5.
/// Captures all [SpireOracle] log messages in a ring buffer.
/// </summary>
public static class DebugLogOverlay
{
    private const string OverlayName = "SpireOracleDebugLog";
    private const int MaxLines = 40;

    private static readonly List<string> _buffer = new();
    private static PanelContainer? _panel;
    private static VBoxContainer? _vbox;
    private static bool _visible;
    private static string _version = "?";

    public static bool IsVisible => _visible;

    public static void SetVersion(string version) => _version = version;

    /// <summary>
    /// Log a message — writes to Godot console AND captures in the ring buffer.
    /// Call this instead of GD.Print for SpireOracle messages.
    /// </summary>
    public static void Log(string message)
    {
        GD.Print(message);
        Append(message);
    }

    /// <summary>
    /// Log an error — writes to Godot error console AND captures in the ring buffer.
    /// </summary>
    public static void LogErr(string message)
    {
        GD.PrintErr(message);
        Append($"[ERR] {message}");
    }

    private static void Append(string message)
    {
        lock (_buffer)
        {
            _buffer.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (_buffer.Count > MaxLines)
                _buffer.RemoveAt(0);
        }

        if (_visible)
            RefreshContent();
    }

    public static void Toggle()
    {
        if (_visible)
            Hide();
        else
            Show();
    }

    public static void Show()
    {
        Hide();

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        _panel = new PanelContainer();
        _panel.Name = OverlayName;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.02f, 0.02f, 0.05f, 0.85f);
        style.BorderColor = new Color(0.3f, 0.3f, 0.4f, 0.6f);
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.CornerRadiusBottomLeft = 6;
        style.CornerRadiusBottomRight = 6;
        style.CornerRadiusTopLeft = 6;
        style.CornerRadiusTopRight = 6;
        style.ContentMarginLeft = 10;
        style.ContentMarginRight = 10;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        _panel.AddThemeStyleboxOverride("panel", style);

        _vbox = new VBoxContainer();
        _vbox.AddThemeConstantOverride("separation", 1);

        // Title
        var title = new Label();
        title.Text = $"SpireOracle v{_version} (F5)";
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        title.HorizontalAlignment = HorizontalAlignment.Right;
        _vbox.AddChild(title);

        _vbox.AddChild(new HSeparator());

        _panel.AddChild(_vbox);

        // Anchor bottom-right
        _panel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        _panel.Position = new Vector2(-10, -10);
        _panel.CustomMinimumSize = new Vector2(500, 0);
        _panel.ZIndex = 100;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        tree.Root.AddChild(_panel);
        _visible = true;

        RefreshContent();
    }

    public static void Hide()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _panel = null;
        _vbox = null;
        _visible = false;
    }

    private static void RefreshContent()
    {
        if (_vbox == null || !GodotObject.IsInstanceValid(_vbox)) return;

        // Remove old log lines (keep title + separator)
        while (_vbox.GetChildCount() > 2)
        {
            var child = _vbox.GetChild(2);
            _vbox.RemoveChild(child);
            child.QueueFree();
        }

        lock (_buffer)
        {
            foreach (var line in _buffer)
            {
                var label = new Label();
                label.Text = line;
                label.AddThemeFontSizeOverride("font_size", 12);
                label.HorizontalAlignment = HorizontalAlignment.Left;
                label.AutowrapMode = TextServer.AutowrapMode.Off;

                if (line.Contains("[ERR]"))
                    label.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
                else
                    label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));

                _vbox.AddChild(label);
            }
        }
    }
}
