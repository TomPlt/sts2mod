---
name: dashboard
description: Import runs, export data, and start the Spire Oracle dashboard
user_invocable: true
---

Run the dashboard script to refresh all data and start the web server:

```bash
bash /home/tom/projects/sts2mod/scripts/dashboard.sh
```

This will:
1. Kill any existing dashboard process
2. Import new run data from STS2
3. Export fresh analytics for the web dashboard
4. Export mod overlay data to the game's mods folder
5. Start the dashboard at http://localhost:5202

After running, tell the user the dashboard is ready at http://localhost:5202
