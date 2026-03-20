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
            foreach (var profileDir in Directory.GetDirectories(steamIdDir, "profile*"))
            {
                var historyDir = Path.Combine(profileDir, "saves", "history");
                if (Directory.Exists(historyDir))
                    results.Add(historyDir);
            }
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
