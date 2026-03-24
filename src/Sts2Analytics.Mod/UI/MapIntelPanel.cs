using System.Collections.Generic;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

public partial class MapIntelPanel : PanelContainer
{
    private VBoxContainer _content;
    private ScrollContainer _scroll;
    private string? _currentCharacter;
    private int _currentAct = -1;
    private string? _currentActName;

    public MapIntelPanel()
    {
        Name = "SpireOracleMapIntel";
        MouseFilter = MouseFilterEnum.Pass;
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

        _scroll = new ScrollContainer();
        _scroll.MouseFilter = MouseFilterEnum.Pass;
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_scroll);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 3);
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_content);

        // Position: top-left, fixed size
        SetAnchorsPreset(LayoutPreset.TopLeft);
        Position = new Vector2(20, 80);
        Size = new Vector2(800, 700);
        ZIndex = 10;
    }

    public void UpdateForContext(string character, int actIndex, string actName = "")
    {
        if (character == _currentCharacter && actIndex == _currentAct && actName == _currentActName)
        {
            Visible = true;
            return;
        }

        _currentCharacter = character;
        _currentAct = actIndex;
        _currentActName = actName;

        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var intel = DataLoader.GetMapIntel(character);
        var refAct = !string.IsNullOrEmpty(actName)
            ? DataLoader.GetActReference(actName)
            : null;

        // Header
        var charName = character.Replace("CHARACTER.", "");
        var actDisplayName = refAct?.DisplayName ?? $"Act {actIndex + 1}";
        var header = new Label();
        header.Text = $"{charName} \u2014 {actDisplayName}";
        header.AddThemeFontSizeOverride("font_size", 22);
        header.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        _content.AddChild(header);

        // Player win rates for current character only
        var players = DataLoader.GetPlayerRunCounts();
        if (players.Count > 0)
        {
            AddSectionHeader($"{charName} Win Rates");
            foreach (var p in players)
            {
                var charStat = p.ByCharacter?.Find(c => c.Character == character);
                if (charStat != null)
                {
                    AddStatRow($"  {p.Name}", $"{charStat.WinRate:P0} ({charStat.Wins}/{charStat.Runs})",
                        charStat.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f), 16);
                }
                else
                {
                    AddStatRow($"  {p.Name}", $"{p.WinRate:P0} ({p.Wins}/{p.Runs})",
                        p.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f), 16);
                }
            }
            AddSeparator();
        }

        // Win rates from analytics
        if (intel != null)
        {
            AddStatRow("Overall Win Rate", $"{intel.WinRate:P0} ({intel.Wins}/{intel.Runs})",
                intel.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f));

            MapIntelAct? actData = null;
            if (intel.Acts != null)
            {
                foreach (var act in intel.Acts)
                    if (act.ActIndex == actIndex) { actData = act; break; }
            }

            if (actData != null)
            {
                if (actData.Runs > 0)
                    AddStatRow($"Survived Act {actIndex + 1}", $"{actData.WinRate:P0} ({actData.Wins}/{actData.Runs})",
                        actData.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f));

                AddSeparator();

                // Damage summary
                AddSectionHeader("Expected Damage");
                foreach (var pool in actData.Pools)
                {
                    AddPoolSummaryRow(pool, actIndex);
                    if (pool.EncounterDetails != null)
                        foreach (var enc in pool.EncounterDetails)
                            AddEncounterRow(enc);
                }

                // Elite win rate correlation
                if (actData.EliteWinRates != null && actData.EliteWinRates.Count > 0)
                {
                    AddSeparator();
                    AddSectionHeader($"Act {actIndex + 1} Elites \u2192 Win Rate");
                    foreach (var ec in actData.EliteWinRates)
                    {
                        if (ec.TotalRuns < 2) continue;
                        AddStatRow($"  {ec.EliteCount} elites", $"{ec.WinRate:P0} ({ec.Wins}/{ec.TotalRuns})",
                            ec.WinRate >= 0.5 ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.95f, 0.85f, 0.2f), 16);
                    }
                }
            }
        }

        // Reference data: encounter pools, elites, bosses, events
        if (refAct != null)
        {
            // Encounter pools
            if (refAct.EasyPool != null && refAct.EasyPool.Count > 0)
            {
                AddSeparator();
                AddSectionHeader("Encounter Pools");
                AddLabelRow("Easy:", string.Join(", ", refAct.EasyPool), new Color(0.3f, 0.85f, 0.3f));
            }
            if (refAct.HardPool != null && refAct.HardPool.Count > 0)
                AddLabelRow("Hard:", string.Join(", ", refAct.HardPool), new Color(0.95f, 0.85f, 0.2f));

            // Elites & Bosses (names only — damage stats already shown above)
            if (refAct.Elites != null && refAct.Elites.Count > 0)
                AddLabelRow("Elites:", string.Join(", ", refAct.Elites), new Color(0.95f, 0.3f, 0.3f));
            if (refAct.Bosses != null && refAct.Bosses.Count > 0)
                AddLabelRow("Bosses:", string.Join(", ", refAct.Bosses), new Color(0.7f, 0.3f, 0.9f));

            // Events
            if (refAct.Events != null && refAct.Events.Count > 0)
            {
                AddSeparator();
                AddSectionHeader($"Events ({refAct.Events.Count})");
                foreach (var evt in refAct.Events)
                {
                    var evtRow = new HBoxContainer();
                    var evtName = new Label();
                    evtName.Text = $"  \u2022 {evt.Name}";
                    evtName.AddThemeFontSizeOverride("font_size", 15);
                    evtName.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
                    evtName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    evtRow.AddChild(evtName);

                    if (evt.Condition != null)
                    {
                        var condLabel = new Label();
                        condLabel.Text = "\u26a0";
                        condLabel.AddThemeFontSizeOverride("font_size", 13);
                        condLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.2f));
                        condLabel.TooltipText = evt.Condition;
                        evtRow.AddChild(condLabel);
                    }
                    _content.AddChild(evtRow);

                    // Compact options
                    foreach (var opt in evt.Options)
                    {
                        var effectShort = opt.Effect.Length > 45 ? opt.Effect.Substring(0, 42) + "..." : opt.Effect;
                        var optLabel = new Label();
                        optLabel.Text = $"      {opt.Name}: {effectShort}";
                        optLabel.AddThemeFontSizeOverride("font_size", 13);
                        optLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.55f));
                        if (opt.Effect.Length > 45 || opt.Notes != null)
                            optLabel.TooltipText = opt.Effect + (opt.Notes != null ? $"\n\n{opt.Notes}" : "");
                        _content.AddChild(optLabel);
                    }
                }

                // Shared events count
                var shared = DataLoader.GetSharedEvents();
                if (shared.Count > 0)
                {
                    var sharedLabel = new Label();
                    sharedLabel.Text = $"  + {shared.Count} shared events";
                    sharedLabel.AddThemeFontSizeOverride("font_size", 14);
                    sharedLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
                    _content.AddChild(sharedLabel);
                }
            }
        }

        if (_content.GetChildCount() <= 1)
        {
            AddNoDataLabel();
            return;
        }

        Visible = true;
    }

    private void AddPoolSummaryRow(MapIntelPool pool, int actIndex)
    {
        var row = new HBoxContainer();

        var dot = new Label();
        dot.Text = "\u25cf ";
        dot.AddThemeFontSizeOverride("font_size", 17);
        dot.AddThemeColorOverride("font_color", GetPoolColor(pool.Pool));
        row.AddChild(dot);

        var poolRating = DataLoader.GetPoolRating($"act{actIndex + 1}_{pool.Pool}");
        var poolEloText = poolRating != null ? $" [{poolRating.Elo:F0}]" : "";
        var poolLabel = new Label();
        poolLabel.Text = $"{GetPoolDisplayName(pool.Pool)}{poolEloText}";
        poolLabel.AddThemeFontSizeOverride("font_size", 17);
        poolLabel.AddThemeColorOverride("font_color", Colors.White);
        poolLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(poolLabel);

        var dmgLabel = new Label();
        dmgLabel.Text = $"~{pool.AvgDamage:F0}\u00b1{pool.StdDev:F0} dmg  (n={pool.SampleSize})";
        dmgLabel.AddThemeFontSizeOverride("font_size", 17);
        dmgLabel.AddThemeColorOverride("font_color", GetDamageColor(pool.AvgDamage));
        row.AddChild(dmgLabel);

        _content.AddChild(row);
    }

    private void AddEncounterRow(EncounterDamage enc)
    {
        var row = new HBoxContainer();

        var nameLabel = new Label();
        var encRating = DataLoader.GetEncounterRating(enc.EncounterId);
        var eloText = encRating != null ? $" [{encRating.Elo:F0}]" : "";
        nameLabel.Text = $"    {FormatEncounterName(enc.EncounterId)}{eloText}";
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        var dmgLabel = new Label();
        dmgLabel.Text = $"~{enc.AvgDamage:F0}\u00b1{enc.StdDev:F0}  (n={enc.SampleSize})";
        dmgLabel.AddThemeFontSizeOverride("font_size", 15);
        dmgLabel.AddThemeColorOverride("font_color", GetDamageColor(enc.AvgDamage));
        row.AddChild(dmgLabel);

        _content.AddChild(row);
    }

    private void AddSectionHeader(string text)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", Colors.White);
        _content.AddChild(label);
    }

    private void AddStatRow(string label, string value, Color valueColor, int fontSize = 17)
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

    private void AddLabelRow(string label, string value, Color labelColor)
    {
        var lbl = new Label();
        lbl.Text = label;
        lbl.AddThemeFontSizeOverride("font_size", 16);
        lbl.AddThemeColorOverride("font_color", labelColor);
        _content.AddChild(lbl);

        var val = new Label();
        val.Text = $"  {value}";
        val.AddThemeFontSizeOverride("font_size", 14);
        val.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
        val.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(val);
    }

    private void AddSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 4);
        _content.AddChild(sep);
    }

    private void AddNoDataLabel()
    {
        var label = new Label();
        label.Text = "No map intel data available";
        label.AddThemeFontSizeOverride("font_size", 18);
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
