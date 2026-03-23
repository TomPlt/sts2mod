using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireOracle.Data;

/// <summary>
/// Monte Carlo simulator for expected combat damage given a deck Elo and encounter pool.
/// Uses Glicko-2 expected score to scale per-encounter damage distributions.
/// </summary>
public static class CombatSimulator
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Simulate expected damage for a deck against a pool's encounters.
    /// </summary>
    /// <param name="deckElo">Deck's combat Elo rating</param>
    /// <param name="poolContext">Pool context key, e.g. "act1_normal"</param>
    /// <param name="character">Character key for map intel lookup</param>
    /// <param name="actIndex">Current act index (0-based)</param>
    /// <param name="simCount">Number of Monte Carlo samples</param>
    /// <returns>Mean expected damage and standard deviation, or null if insufficient data</returns>
    public static SimResult? Simulate(double deckElo, string poolContext, string character, int actIndex, int simCount = 200)
    {
        // Get encounter details from map intel
        var mapIntel = DataLoader.GetMapIntel(character);
        if (mapIntel?.Acts == null) return null;

        var act = mapIntel.Acts.FirstOrDefault(a => a.ActIndex == actIndex);
        if (act == null) return null;

        // Find the pool matching our context (e.g., "normal" from "act1_normal")
        var poolSuffix = poolContext.Contains('_') ? poolContext.Split('_')[1] : poolContext;
        var pool = act.Pools?.FirstOrDefault(p => p.Pool == poolSuffix);
        if (pool?.EncounterDetails == null || pool.EncounterDetails.Count == 0) return null;

        // Build weighted encounter list (weight by sample size for realistic sampling)
        var encounters = pool.EncounterDetails
            .Where(e => e.SampleSize > 0)
            .ToList();
        if (encounters.Count == 0) return null;

        var totalSamples = encounters.Sum(e => e.SampleSize);
        var damages = new double[simCount];

        for (var i = 0; i < simCount; i++)
        {
            // Sample a random encounter weighted by frequency
            var roll = Rng.Next(totalSamples);
            EncounterDamage? picked = null;
            var cumulative = 0;
            foreach (var enc in encounters)
            {
                cumulative += enc.SampleSize;
                if (roll < cumulative) { picked = enc; break; }
            }
            picked ??= encounters[^1];

            // Look up encounter Elo
            var encRating = DataLoader.GetEncounterRating(picked.EncounterId);
            var encElo = encRating?.Elo ?? DataLoader.GetPoolRating(poolContext)?.Elo ?? 1500.0;

            // Glicko-2 expected score: E = 1 / (1 + 10^((enc - deck) / 400))
            var expectedScore = 1.0 / (1.0 + Math.Pow(10.0, (encElo - deckElo) / 400.0));

            // Scale damage: strong deck takes less, weak deck takes more
            // At E=0.5 (equal), damage = avg. At E=1.0 (dominant), damage → 0. At E=0 (outmatched), damage → 2x avg.
            var damageScale = 2.0 * (1.0 - expectedScore);

            // Add some variance using the encounter's stddev
            var baseDmg = picked.AvgDamage * damageScale;
            var noise = picked.StdDev > 0 ? NextGaussian() * picked.StdDev * 0.5 : 0;
            damages[i] = Math.Max(0, baseDmg + noise);
        }

        var mean = damages.Average();
        var std = damages.Length > 1
            ? Math.Sqrt(damages.Sum(d => (d - mean) * (d - mean)) / damages.Length)
            : 0;

        return new SimResult(mean, std);
    }

    /// <summary>
    /// Compute deck Elo from a list of card IDs using their combat ratings.
    /// </summary>
    public static (double Elo, double Rd) ComputeDeckElo(IEnumerable<string> cardIds)
    {
        var ratings = new List<(double Rating, double Rd)>();
        foreach (var cid in cardIds)
        {
            var cs = DataLoader.GetCard(cid);
            if (cs != null && cs.CombatElo > 0)
                ratings.Add((cs.CombatElo, cs.CombatRd));
        }

        if (ratings.Count == 0) return (1500.0, 350.0);

        double sumWM = 0, sumW = 0, sumP = 0;
        foreach (var (r, rd) in ratings)
        {
            if (rd <= 0) continue;
            var w = 1.0 / rd;
            sumWM += r * w;
            sumW += w;
            sumP += 1.0 / (rd * rd);
        }

        if (sumW == 0) return (1500.0, 350.0);
        return (sumWM / sumW, 1.0 / Math.Sqrt(sumP));
    }

    /// <summary>
    /// Compute the delta in expected damage if a card is added to the deck,
    /// broken down by pool type (normal, elite, boss).
    /// </summary>
    public static MultiPoolForecast? ForecastCardPick(
        List<string> currentDeck, string candidateCardId,
        string character, int actIndex)
    {
        var (currentElo, _) = ComputeDeckElo(currentDeck);
        var newDeck = new List<string>(currentDeck) { candidateCardId };
        var (newElo, _) = ComputeDeckElo(newDeck);

        var pools = new[] { "normal", "elite", "boss" };
        var forecasts = new Dictionary<string, PoolForecast>();

        foreach (var pool in pools)
        {
            var poolContext = $"act{actIndex + 1}_{pool}";
            var currentSim = Simulate(currentElo, poolContext, character, actIndex);
            var newSim = Simulate(newElo, poolContext, character, actIndex);

            if (currentSim != null && newSim != null)
            {
                forecasts[pool] = new PoolForecast(
                    currentSim.Mean, newSim.Mean, newSim.Mean - currentSim.Mean);
            }
        }

        if (forecasts.Count == 0) return null;

        return new MultiPoolForecast(currentElo, newElo, newElo - currentElo, forecasts);
    }

    private static double NextGaussian()
    {
        // Box-Muller transform
        var u1 = 1.0 - Rng.NextDouble();
        var u2 = 1.0 - Rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}

public record SimResult(double Mean, double StdDev);

public record PoolForecast(double CurrentDmg, double NewDmg, double DmgDelta);

public record MultiPoolForecast(
    double CurrentDeckElo, double NewDeckElo, double EloDelta,
    Dictionary<string, PoolForecast> ByPool);

public record DamageForcast(
    double CurrentDeckElo, double NewDeckElo, double EloDelta,
    double CurrentExpectedDmg, double NewExpectedDmg, double DmgDelta);
