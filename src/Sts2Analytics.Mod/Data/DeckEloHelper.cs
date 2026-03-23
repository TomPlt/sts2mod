using System;
using System.Collections.Generic;

namespace SpireOracle.Data;

public static class DeckEloHelper
{
    /// <summary>
    /// Compute deck Elo as 1/RD-weighted mean of card combat ratings.
    /// If poolContext is provided (e.g. "act1_elite"), uses act-specific combat Elo per card.
    /// Falls back to overall combat Elo when act-specific is unavailable.
    /// </summary>
    public static (double Elo, double Rd) Compute(List<string> cardIds, string? poolContext = null)
    {
        double sumWM = 0, sumW = 0, sumP = 0;

        foreach (var cid in cardIds)
        {
            var cs = DataLoader.GetCard(cid);
            if (cs == null) continue;

            // Try act-specific combat Elo first
            double elo = 0, rd = 350;
            if (poolContext != null && cs.CombatByPool != null
                && cs.CombatByPool.TryGetValue(poolContext, out var poolElo)
                && poolElo.Elo > 0 && poolElo.Rd < 300)
            {
                elo = poolElo.Elo;
                rd = poolElo.Rd;
            }
            else if (cs.CombatElo > 0)
            {
                elo = cs.CombatElo;
                rd = cs.CombatRd;
            }
            else continue;

            if (rd <= 0) continue;
            var w = 1.0 / rd;
            sumWM += elo * w;
            sumW += w;
            sumP += 1.0 / (rd * rd);
        }

        if (sumW == 0) return (1500.0, 350.0);
        return (sumWM / sumW, 1.0 / Math.Sqrt(sumP));
    }

    /// <summary>
    /// Glicko-2 expected score: probability that deck "beats" the encounter.
    /// </summary>
    public static double GlickoExpectedScore(double deckElo, double oppElo, double oppRd)
    {
        const double scale = 173.7178;
        var phi = oppRd / scale;
        var g = 1.0 / Math.Sqrt(1.0 + 3.0 * phi * phi / (Math.PI * Math.PI));
        var mu1 = (deckElo - 1500) / scale;
        var mu2 = (oppElo - 1500) / scale;
        return 1.0 / (1.0 + Math.Exp(-g * (mu1 - mu2)));
    }
}
