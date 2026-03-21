namespace Sts2Analytics.Core.Parsing;

public static class SavePathDetector
{
    public static List<string> FindHistoryDirectories()
    {
        var results = new List<string>();

        // Check native Windows/Mac/Linux path
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AddSts2Paths(results, appData);

        // Check WSL paths (/mnt/c/Users/*/AppData/Roaming)
        if (Directory.Exists("/mnt/c/Users"))
        {
            foreach (var userDir in Directory.GetDirectories("/mnt/c/Users"))
            {
                var wslAppData = Path.Combine(userDir, "AppData", "Roaming");
                AddSts2Paths(results, wslAppData);
            }
        }

        return results;
    }

    private static void AddSts2Paths(List<string> results, string appDataDir)
    {
        var sts2Dir = Path.Combine(appDataDir, "SlayTheSpire2", "steam");
        if (!Directory.Exists(sts2Dir)) return;

        foreach (var steamIdDir in Directory.GetDirectories(sts2Dir))
        {
            // Only profile1 (main profile)
            var profile1 = Path.Combine(steamIdDir, "profile1", "saves", "history");
            if (Directory.Exists(profile1))
                results.Add(profile1);

            // Also modded profile1
            var moddedProfile1 = Path.Combine(steamIdDir, "modded", "profile1", "saves", "history");
            if (Directory.Exists(moddedProfile1))
                results.Add(moddedProfile1);
        }
    }

    public static string GetDefaultDbPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".sts2analytics");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "data.db");
    }
}
