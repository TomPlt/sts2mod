---
name: deploy
description: Build and deploy the SpireOracle mod to the STS2 game directory
user_invocable: true
---

Build the mod and deploy it to the game's mods folder. The game must be closed first or the copy will fail.

```bash
set -e
export PATH="$HOME/.dotnet:$PATH"
cd /home/tom/projects/sts2mod

GAME_DIR="/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/SpireOracle"

# Fail fast if game is running (DLL locked)
if ! cp --no-clobber /dev/null "$GAME_DIR/.deploy_test" 2>/dev/null; then
    echo "ERROR: Cannot write to game directory. Is STS2 running?" >&2
    exit 1
fi
rm -f "$GAME_DIR/.deploy_test"

echo "=== Building mod ==="
dotnet build src/Sts2Analytics.Mod -c Release

echo "=== Syncing shared run data ==="
DATA_REPO="$HOME/projects/spire-oracle-data"
if [ -d "$DATA_REPO/.git" ]; then
    git -C "$DATA_REPO" pull --ff-only -q
else
    git clone git@github.com:TomPlt/spire-oracle-data.git "$DATA_REPO"
fi

echo "=== Importing run data ==="
for player_dir in "$DATA_REPO"/runs/*/; do
    [ -d "$player_dir" ] && dotnet run --project src/Sts2Analytics.Cli -- import "$player_dir"
done

echo "=== Exporting mod overlay data ==="
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output mods/SpireOracle/overlay_data.json

echo "=== Deploying to game ==="
cp src/Sts2Analytics.Mod/bin/Release/net9.0/SpireOracle.dll mods/SpireOracle/

# Copy SQLite dependencies
for dll in Microsoft.Data.Sqlite.dll SQLitePCLRaw.core.dll SQLitePCLRaw.batteries_v2.dll SQLitePCLRaw.provider.e_sqlite3.dll; do
    [ -f "src/Sts2Analytics.Mod/bin/Release/net9.0/$dll" ] && cp "src/Sts2Analytics.Mod/bin/Release/net9.0/$dll" mods/SpireOracle/
done

# Copy native SQLite library for Windows
NATIVE_DIR="src/Sts2Analytics.Mod/bin/Release/net9.0/runtimes/win-x64/native"
if [ -d "$NATIVE_DIR" ]; then
    mkdir -p mods/SpireOracle/runtimes/win-x64/native
    cp "$NATIVE_DIR"/* mods/SpireOracle/runtimes/win-x64/native/
fi

cp data/reference/sts2_reference.json mods/SpireOracle/
cp mods/SpireOracle/SpireOracle.dll mods/SpireOracle/overlay_data.json mods/SpireOracle/mod_manifest.json mods/SpireOracle/sts2_reference.json mods/SpireOracle/config.json "$GAME_DIR/"
for dll in Microsoft.Data.Sqlite.dll SQLitePCLRaw.core.dll SQLitePCLRaw.batteries_v2.dll SQLitePCLRaw.provider.e_sqlite3.dll; do
    [ -f "mods/SpireOracle/$dll" ] && cp "mods/SpireOracle/$dll" "$GAME_DIR/"
done
if [ -d "mods/SpireOracle/runtimes" ]; then
    cp -r mods/SpireOracle/runtimes "$GAME_DIR/"
fi

echo "=== Done ==="
ls "$GAME_DIR/"
```

If the copy fails with "Permission denied", tell the user to close STS2 first and try `/deploy` again.
When the user asks to "redeploy" without changes, still run the FULL script above — never skip the export step.
