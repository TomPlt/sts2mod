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

echo "=== Exporting fresh overlay data ==="
gh workflow run export.yml --repo TomPlt/spire-oracle-data
echo "Waiting for export workflow..."
sleep 5
RUN_ID=$(gh run list --repo TomPlt/spire-oracle-data --workflow=export.yml --limit 1 --json databaseId --jq '.[0].databaseId')
gh run watch "$RUN_ID" --repo TomPlt/spire-oracle-data --exit-status

echo "=== Downloading latest overlay data ==="
gh release download latest --repo TomPlt/spire-oracle-data --pattern overlay_data.json --dir mods/SpireOracle/ --clobber

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
