# Slay the Spire 2 - Modding Resources

## Game Overview

- **Engine**: Godot 4.5.1 (custom fork called **MegaDot**)
- **Language**: C# with .NET 9.0
- **Developer**: Mega Crit
- **Early Access**: March 5, 2026
- **No STS1 mod compatibility** — mods must be written from scratch

## Modding Stack

| Technology | Purpose |
|---|---|
| C# / .NET 9.0 | Mod code |
| Godot.NET.Sdk | Build SDK |
| Lib.Harmony 2.4.2 | Runtime method patching |
| `sts2.dll` | Game reference assembly |
| `.pck` files | Godot packed resources (assets, scenes) |
| `mod_manifest.json` | Mod metadata |

### Decompilation & Assets
- **ILSpy / dnSpyEx** — decompile game code (no obfuscation)
- **GD RE Tools** — extract Godot resources
- **Godot 4.5.1** — package resources into `.pck` files
- **Spine Runtime for Godot** — skeletal animations

## Mod Structure

A compiled mod consists of:
- `.dll` — compiled C# library
- `.pck` — Godot packed resources
- `mod_manifest.json` — name, version, author, description

A mod project contains:
- `.csproj` targeting .NET 9.0 with Godot.NET.Sdk
- C# source using `[ModInitializer]` attribute with `ModLoaded` entry point
- `project.godot` and `export_presets.cfg`
- Asset files

## Installation

**Manual**: Drop `.dll` and `.pck` into `<game>/mods/` folder. Game auto-loads on startup.

**Steam Workshop**: Library > STS2 > Workshop > Subscribe. Enable in Mod Settings, restart.

**Mac path**: `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods`

**Notes**:
- Separate save files for modded vs unmodded play
- Multiplayer requires identical game + mod versions across players

## Community Libraries

### BaseLib-StS2
Foundational library that standardizes content additions.
- GitHub: https://github.com/Alchyr/BaseLib-StS2
- NuGet: `<PackageReference Include="Alchyr.Sts2.BaseLib" Version="*" />`
- Nexus: https://www.nexusmods.com/slaythespire2/mods/103
- Wiki: https://alchyr.github.io/BaseLib-Wiki/

### ModTemplate-StS2
Ready-to-use C# project scaffold for new mods.
- GitHub: https://github.com/Alchyr/ModTemplate-StS2

### ModConfig
API for mods to register configuration entries in-game.
- Nexus: https://www.nexusmods.com/slaythespire2/mods/27

## Example / Reference Mods

| Mod | Description | Source |
|---|---|---|
| BetterSpire2 | QoL pack (damage counter, auto-confirm, fast mode) | https://github.com/jdr1813/BetterSpire2 |
| STS2FirstMod | Tutorial/example mod | https://github.com/jiegec/STS2FirstMod |
| sts2-quickRestart | Modding introduction/guide | https://github.com/freude916/sts2-quickRestart |
| sts2_example_mod | Another example | https://github.com/lamali292/sts2_example_mod |

## Notable Nexus Mods

- **The Watcher** — ports Watcher from STS1 (83 cards, animations, VFX)
- **Together in Spire** — co-op overhaul, PVP duels, custom multiplayer relics
- **STS2 Plus** — QoL, combat clarity, custom rules
- **DevConsole With Unlocked Achievements** — dev console access
- **Skada Damage Meter** — multiplayer stat tracking

Nexus page: https://www.nexusmods.com/slaythespire2

## Community

- **Discord**: https://discord.com/invite/slaythespire — check `#modding-technical` channel (pinned resources)
- **Mega Crit FAQ**: https://www.megacrit.com/faq/
- **sts2mods.com**: https://sts2mods.com/how-to-install-mods-for-slay-the-spire-2/
- **slaythespire2.gg**: https://slaythespire2.gg/

## Official Modding Support

- Native mod loading (no third-party loader needed)
- Steam Workshop integration
- Built-in Harmony support with extensive hooks
- Unobfuscated game code
- Data-driven design (JSON payloads + Godot scene system)
- No formal API docs yet — community fills the gap via BaseLib Wiki and Discord
