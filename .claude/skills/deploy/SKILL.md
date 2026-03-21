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

echo "=== Building mod ==="
dotnet build src/Sts2Analytics.Mod -c Release

echo "=== Exporting mod overlay data ==="
dotnet run --project src/Sts2Analytics.Cli -- import
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output mods/SpireOracle/overlay_data.json

echo "=== Deploying to game ==="
cp src/Sts2Analytics.Mod/bin/Release/net9.0/SpireOracle.dll mods/SpireOracle/
cp mods/SpireOracle/SpireOracle.dll mods/SpireOracle/overlay_data.json mods/SpireOracle/mod_manifest.json "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/SpireOracle/"

echo "=== Done ==="
ls "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/SpireOracle/"
```

If the copy fails with "Permission denied", tell the user to close STS2 first and try `/deploy` again.
