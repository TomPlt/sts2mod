#!/bin/bash
# SpireOracle Dashboard — import, export, and launch
set -e

export PATH="$HOME/.dotnet:$PATH"
cd /home/tom/projects/sts2mod

echo "=== Killing existing dashboard ==="
pkill -f "Sts2Analytics.Web" 2>/dev/null || true
sleep 1

echo "=== Syncing shared run data ==="
DATA_REPO="$HOME/projects/spire-oracle-data"
if [ -d "$DATA_REPO/.git" ]; then
    git -C "$DATA_REPO" pull --ff-only -q
else
    git clone git@github.com:TomPlt/spire-oracle-data.git "$DATA_REPO"
fi

echo "=== Importing run data ==="
# Import from shared data repo (per-player directories)
for player_dir in "$DATA_REPO"/runs/*/; do
    [ -d "$player_dir" ] && dotnet run --project src/Sts2Analytics.Cli -- import "$player_dir"
done

echo "=== Exporting dashboard data ==="
dotnet run --project src/Sts2Analytics.Cli -- export --output src/Sts2Analytics.Web/wwwroot/data.json

echo "=== Exporting mod overlay data ==="
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output mods/SpireOracle/overlay_data.json

echo "=== Starting dashboard on port 5202 ==="
nohup dotnet run --project src/Sts2Analytics.Web --urls "http://localhost:5202" > /tmp/dashboard.log 2>&1 &
sleep 4

if curl -s -o /dev/null -w "%{http_code}" http://localhost:5202 | grep -q 200; then
    echo "=== Dashboard running at http://localhost:5202 ==="
else
    echo "=== Dashboard may still be starting — check http://localhost:5202 ==="
fi
