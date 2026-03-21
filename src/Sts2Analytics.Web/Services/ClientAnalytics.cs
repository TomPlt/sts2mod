using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Web.Services;

/// <summary>
/// Recomputes analytics client-side from raw choice data + filtered runs.
/// </summary>
public static class ClientAnalytics
{
    public static List<CardWinRate> ComputeCardWinRates(
        IReadOnlyList<CardChoiceExport> choices, HashSet<long> runIds, Dictionary<long, bool> runWins)
    {
        return choices
            .Where(c => runIds.Contains(c.RunId))
            .GroupBy(c => c.CardId)
            .Select(g =>
            {
                int picked = 0, skipped = 0, winsPicked = 0, winsSkipped = 0;
                foreach (var c in g)
                {
                    var win = runWins.GetValueOrDefault(c.RunId);
                    if (c.WasPicked) { picked++; if (win) winsPicked++; }
                    else { skipped++; if (win) winsSkipped++; }
                }
                double wrPicked = picked > 0 ? (double)winsPicked / picked : 0;
                double wrSkipped = skipped > 0 ? (double)winsSkipped / skipped : 0;
                return new CardWinRate(g.Key, picked, skipped, winsPicked, winsSkipped, wrPicked, wrSkipped, wrPicked - wrSkipped);
            })
            .ToList();
    }

    public static List<CardPickRate> ComputeCardPickRates(
        IReadOnlyList<CardChoiceExport> choices, HashSet<long> runIds)
    {
        return choices
            .Where(c => runIds.Contains(c.RunId))
            .GroupBy(c => c.CardId)
            .Select(g =>
            {
                int offered = g.Count();
                int picked = g.Count(c => c.WasPicked);
                return new CardPickRate(g.Key, offered, picked, offered > 0 ? (double)picked / offered : 0);
            })
            .ToList();
    }

    public static List<RelicWinRate> ComputeRelicWinRates(
        IReadOnlyList<RelicChoiceExport> choices, HashSet<long> runIds, Dictionary<long, bool> runWins)
    {
        return choices
            .Where(c => runIds.Contains(c.RunId))
            .GroupBy(c => c.RelicId)
            .Select(g =>
            {
                int picked = 0, skipped = 0, winsPicked = 0, winsSkipped = 0;
                foreach (var c in g)
                {
                    var win = runWins.GetValueOrDefault(c.RunId);
                    if (c.WasPicked) { picked++; if (win) winsPicked++; }
                    else { skipped++; if (win) winsSkipped++; }
                }
                double wrPicked = picked > 0 ? (double)winsPicked / picked : 0;
                double wrSkipped = skipped > 0 ? (double)winsSkipped / skipped : 0;
                return new RelicWinRate(g.Key, picked, skipped, wrPicked, wrSkipped);
            })
            .ToList();
    }

    public static List<RelicPickRate> ComputeRelicPickRates(
        IReadOnlyList<RelicChoiceExport> choices, HashSet<long> runIds)
    {
        return choices
            .Where(c => runIds.Contains(c.RunId))
            .GroupBy(c => c.RelicId)
            .Select(g =>
            {
                int offered = g.Count();
                int picked = g.Count(c => c.WasPicked);
                return new RelicPickRate(g.Key, offered, picked, offered > 0 ? (double)picked / offered : 0);
            })
            .ToList();
    }

    /// <summary>Returns (filteredRunIds, runWinMap) for the given filter.</summary>
    public static (HashSet<long> RunIds, Dictionary<long, bool> WinMap) FilterRuns(
        IReadOnlyList<RunExport> runs, FilterState filter)
    {
        var filtered = runs.Where(filter.MatchesRun).ToList();
        var ids = new HashSet<long>(filtered.Select(r => r.Id));
        var wins = filtered.ToDictionary(r => r.Id, r => r.Win);
        return (ids, wins);
    }
}
