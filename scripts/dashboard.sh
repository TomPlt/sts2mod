#!/bin/bash
# SpireOracle Dashboard — import, export, and launch
set -e

export PATH="$HOME/.dotnet:$PATH"
cd /home/tom/projects/sts2mod

echo "=== Killing existing dashboard ==="
pkill -f "Sts2Analytics.Web" 2>/dev/null || true
sleep 1

echo "=== Importing run data ==="
dotnet run --project src/Sts2Analytics.Cli -- import

echo "=== Exporting dashboard data ==="
dotnet run --project src/Sts2Analytics.Cli -- export --output src/Sts2Analytics.Web/wwwroot/data.json

echo "=== Exporting mod overlay data ==="
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/SpireOracle/overlay_data.json"

echo "=== Starting dashboard on port 5202 ==="
nohup dotnet run --project src/Sts2Analytics.Web --urls "http://localhost:5202" > /tmp/dashboard.log 2>&1 &
sleep 4

if curl -s -o /dev/null -w "%{http_code}" http://localhost:5202 | grep -q 200; then
    echo "=== Dashboard running at http://localhost:5202 ==="
else
    echo "=== Dashboard may still be starting — check http://localhost:5202 ==="
fi
