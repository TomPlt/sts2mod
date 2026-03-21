using System.Collections.Generic;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

public partial class MapIntelPanel : PanelContainer
{
    private VBoxContainer _content;
    private string? _currentCharacter;
    private int _currentAct = -1;

    public MapIntelPanel()
    {
        Name = "SpireOracleMapIntel";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

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

        // Position: bottom-left
        SetAnchorsPreset(LayoutPreset.BottomLeft);
        Position = new Vector2(20, -40);
        GrowVertical = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(340, 0);
        ZIndex = 10;
    }

    public void UpdateForContext(string character, int actIndex)
    {
        if (character == _currentCharacter && actIndex == _currentAct)
        {
            Visible = true;
            return;
        }

        _currentCharacter = character;
        _currentAct = actIndex;

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

        // Header
        var charName = character.Replace("CHARACTER.", "");
        var header = new Label();
        header.Text = $"{charName} \u2014 Act {actIndex + 1}";
        header.AddThemeFontSizeOverride("font_size", 24);
        header.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        _content.AddChild(header);

        // Find act data
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

        // Win rates
        AddStatRow("Overall Win Rate", $"{intel.WinRate:P0} ({intel.Wins}/{intel.Runs})",
            intel.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f));
        if (actData.Runs > 0)
        {
            AddStatRow($"Act {actIndex + 1} Win Rate", $"{actData.WinRate:P0} ({actData.Wins}/{actData.Runs})",
                actData.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f));
        }

        AddSeparator();

        // Damage summary
        var summaryHeader = new Label();
        summaryHeader.Text = "Expected Damage";
        summaryHeader.AddThemeFontSizeOverride("font_size", 20);
        summaryHeader.AddThemeColorOverride("font_color", Colors.White);
        _content.AddChild(summaryHeader);

        foreach (var pool in actData.Pools)
        {
            AddPoolSummaryRow(pool);

            if (pool.Pool is "elite" or "boss" && pool.EncounterDetails != null)
            {
                foreach (var enc in pool.EncounterDetails)
                {
                    AddEncounterRow(enc);
                }
            }
        }

        // Per-act elite count correlation
        if (actData.EliteWinRates != null && actData.EliteWinRates.Count > 0)
        {
            AddSeparator();
            var eliteHeader = new Label();
            eliteHeader.Text = $"Act {actIndex + 1} Elites \u2192 Win Rate";
            eliteHeader.AddThemeFontSizeOverride("font_size", 20);
            eliteHeader.AddThemeColorOverride("font_color", Colors.White);
            _content.AddChild(eliteHeader);

            foreach (var ec in actData.EliteWinRates)
            {
                if (ec.TotalRuns < 2) continue;
                AddStatRow($"  {ec.EliteCount} elites", $"{ec.WinRate:P0} ({ec.Wins}/{ec.TotalRuns})",
                    ec.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f), 17);
            }
        }

        Visible = true;
    }

    private void AddPoolSummaryRow(MapIntelPool pool)
    {
        var row = new HBoxContainer();

        var dot = new Label();
        dot.Text = "\u25cf ";
        dot.AddThemeFontSizeOverride("font_size", 18);
        dot.AddThemeColorOverride("font_color", GetPoolColor(pool.Pool));
        row.AddChild(dot);

        var poolLabel = new Label();
        poolLabel.Text = GetPoolDisplayName(pool.Pool);
        poolLabel.AddThemeFontSizeOverride("font_size", 18);
        poolLabel.AddThemeColorOverride("font_color", Colors.White);
        poolLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(poolLabel);

        var dmgLabel = new Label();
        dmgLabel.Text = $"~{pool.AvgDamage:F0} dmg";
        dmgLabel.AddThemeFontSizeOverride("font_size", 18);
        dmgLabel.AddThemeColorOverride("font_color", GetDamageColor(pool.AvgDamage));
        row.AddChild(dmgLabel);

        _content.AddChild(row);
    }

    private void AddEncounterRow(EncounterDamage enc)
    {
        var row = new HBoxContainer();

        var nameLabel = new Label();
        nameLabel.Text = $"    {FormatEncounterName(enc.EncounterId)}";
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        var dmgLabel = new Label();
        dmgLabel.Text = $"~{enc.AvgDamage:F0}";
        dmgLabel.AddThemeFontSizeOverride("font_size", 16);
        dmgLabel.AddThemeColorOverride("font_color", GetDamageColor(enc.AvgDamage));
        row.AddChild(dmgLabel);

        var maxLabel = new Label();
        maxLabel.Text = $" (max {enc.MaxDamage})";
        maxLabel.AddThemeFontSizeOverride("font_size", 14);
        maxLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
        row.AddChild(maxLabel);

        _content.AddChild(row);
    }

    private void AddStatRow(string label, string value, Color valueColor, int fontSize = 18)
    {
        var row = new HBoxContainer();
        var lbl = new Label();
        lbl.Text = label;
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        lbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(lbl);

        var val = new Label();
        val.Text = value;
        val.AddThemeFontSizeOverride("font_size", fontSize);
        val.AddThemeColorOverride("font_color", valueColor);
        row.AddChild(val);
        _content.AddChild(row);
    }

    private void AddSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 6);
        _content.AddChild(sep);
    }

    private void AddNoDataLabel()
    {
        var label = new Label();
        label.Text = "No map intel data available";
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        _content.AddChild(label);
        Visible = true;
    }

    private static string FormatEncounterName(string encounterId)
    {
        var name = encounterId.Replace("ENCOUNTER.", "");
        var suffixIdx = name.LastIndexOf('_');
        if (suffixIdx > 0)
        {
            var suffix = name.Substring(suffixIdx + 1);
            if (suffix is "WEAK" or "NORMAL" or "ELITE" or "BOSS")
                name = name.Substring(0, suffixIdx);
        }
        name = name.Replace("_", " ");
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLower());
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
