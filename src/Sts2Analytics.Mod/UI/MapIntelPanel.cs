using System.Collections.Generic;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

public partial class MapIntelPanel : PanelContainer
{
    private VBoxContainer _content = null!;
    private string? _currentCharacter;
    private int _currentAct = -1;

    public override void _Ready()
    {
        Name = "SpireOracleMapIntel";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // Dark semi-transparent background with ember border
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        style.BorderColor = new Color(0.83f, 0.33f, 0.16f, 0.6f);
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
        style.ContentMarginTop = 12;
        style.ContentMarginBottom = 12;
        AddThemeStyleboxOverride("panel", style);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);
        AddChild(_content);

        // Position: top-right of screen
        SetAnchorsPreset(LayoutPreset.TopRight);
        Position = new Vector2(-320, 80);
        CustomMinimumSize = new Vector2(300, 0);
    }

    public void UpdateForContext(string character, int actIndex)
    {
        if (character == _currentCharacter && actIndex == _currentAct)
            return;

        _currentCharacter = character;
        _currentAct = actIndex;

        // Clear existing content
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var intel = DataLoader.GetMapIntel(character);
        if (intel == null)
        {
            AddNoDataLabel();
            return;
        }

        MapIntelAct? actData = null;
        foreach (var act in intel.Acts)
        {
            if (act.ActIndex == actIndex)
            {
                actData = act;
                break;
            }
        }

        if (actData == null)
        {
            AddNoDataLabel();
            return;
        }

        // Header
        var charName = character.Replace("CHARACTER.", "");
        var header = new Label();
        header.Text = $"Map Intel \u2014 {charName}, Act {actIndex + 1}";
        header.AddThemeFontSizeOverride("font_size", 22);
        header.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        _content.AddChild(header);

        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 8);
        _content.AddChild(sep);

        foreach (var pool in actData.Pools)
        {
            AddPoolRow(pool);
        }

        Visible = ModEntry.OverlayEnabled;
    }

    private void AddPoolRow(MapIntelPool pool)
    {
        var row = new HBoxContainer();

        var dot = new Label();
        dot.Text = "\u25cf";
        dot.AddThemeFontSizeOverride("font_size", 20);
        dot.AddThemeColorOverride("font_color", GetPoolColor(pool.Pool));
        row.AddChild(dot);

        var spacer = new Label();
        spacer.Text = " ";
        spacer.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(spacer);

        var poolLabel = new Label();
        poolLabel.Text = GetPoolDisplayName(pool.Pool);
        poolLabel.AddThemeFontSizeOverride("font_size", 20);
        poolLabel.AddThemeColorOverride("font_color", Colors.White);
        poolLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(poolLabel);

        var dmgLabel = new Label();
        dmgLabel.Text = $"~{pool.AvgDamage:F0} dmg";
        dmgLabel.AddThemeFontSizeOverride("font_size", 20);
        dmgLabel.AddThemeColorOverride("font_color", GetDamageColor(pool.AvgDamage));
        row.AddChild(dmgLabel);

        var sizeLabel = new Label();
        sizeLabel.Text = $"  (n={pool.SampleSize})";
        sizeLabel.AddThemeFontSizeOverride("font_size", 16);
        sizeLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
        row.AddChild(sizeLabel);

        _content.AddChild(row);

        // Encounter list
        var encounters = new Label();
        var names = new List<string>();
        foreach (var enc in pool.Encounters)
        {
            var name = enc.Replace("ENCOUNTER.", "");
            var suffixIdx = name.LastIndexOf('_');
            if (suffixIdx > 0)
            {
                var suffix = name.Substring(suffixIdx + 1);
                if (suffix is "WEAK" or "NORMAL" or "ELITE" or "BOSS")
                    name = name.Substring(0, suffixIdx);
            }
            name = name.Replace("_", " ");
            name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLower());
            names.Add(name);
        }
        encounters.Text = "  " + string.Join(", ", names);
        encounters.AddThemeFontSizeOverride("font_size", 16);
        encounters.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        encounters.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(encounters);
    }

    private void AddNoDataLabel()
    {
        var label = new Label();
        label.Text = "No map intel data available";
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        _content.AddChild(label);
        Visible = ModEntry.OverlayEnabled;
    }

    private static string GetPoolDisplayName(string pool) => pool switch
    {
        "weak" => "Easy Hallway",
        "normal" => "Hard Hallway",
        "elite" => "Elite",
        "boss" => "Boss",
        _ => pool
    };

    private static Color GetPoolColor(string pool) => pool switch
    {
        "weak" => new Color(0.3f, 0.85f, 0.3f),
        "normal" => new Color(0.95f, 0.85f, 0.2f),
        "elite" => new Color(0.95f, 0.3f, 0.3f),
        "boss" => new Color(0.7f, 0.3f, 0.9f),
        _ => Colors.White
    };

    private static Color GetDamageColor(double avg) => avg switch
    {
        < 5 => new Color(0.3f, 0.85f, 0.3f),
        < 10 => new Color(0.95f, 0.85f, 0.2f),
        < 18 => new Color(0.95f, 0.5f, 0.2f),
        _ => new Color(0.95f, 0.3f, 0.3f)
    };
}
