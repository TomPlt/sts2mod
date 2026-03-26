SPIRE ORACLE
====================

Card and ancient analytics overlay for Slay the Spire 2.

FEATURES:
- Card reward screen: Pick Elo + Combat Elo ratings
- Combat overlay: Expected damage, encounter Elo vs deck Elo
- Deck viewer (D key): Deck strength per encounter type
- F4: Per-card combat ratings with act breakdown
- F3: Map intel with encounter damage stats
- F5: Debug console with mod version
- Ancient selection: Power (outcome Elo) + Popularity (pick Elo) ratings
- Ancient tooltips: Win rate (picked vs skipped), delta, per-character breakdown
- Cloud sync: Auto-upload runs, download shared ratings

INSTALL:
  Run setup.bat (or manually copy files to
  Steam\steamapps\common\Slay the Spire 2\mods\SpireOracle\)

CLOUD SYNC (optional):
  1. Get invited to github.com/TomPlt/spire-oracle-data
  2. Create a fine-grained GitHub token with Contents read/write
  3. Run setup.bat and enter your token when prompted
     (or create mods/SpireOracle/config.json manually)

FILES:
  SpireOracle.dll      - The mod
  mod_manifest.json    - Mod metadata
  overlay_data.json    - Analytics data (auto-updated via cloud sync)
  sts2_reference.json  - Game reference data
  config.json          - Cloud sync config (created by setup.bat)
  config.example.json  - Example config

SOURCE: github.com/TomPlt/sts2mod
