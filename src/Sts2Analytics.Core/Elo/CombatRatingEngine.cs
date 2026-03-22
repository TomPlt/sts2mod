using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

public class CombatRatingEngine
{
    private readonly IDbConnection _connection;

    public CombatRatingEngine(IDbConnection connection)
    {
        _connection = connection;
    }

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM CombatGlicko2History ch
                JOIN CombatGlicko2Ratings cr ON ch.CombatGlicko2RatingId = cr.Id
                WHERE ch.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        if (unprocessedRunIds.Count == 0) return;

        var damageDistributions = PrecomputeDamageDistributions();
        var deckReconstructor = new DeckReconstructor(_connection);

        foreach (var runId in unprocessedRunIds)
        {
            ProcessRun(runId, deckReconstructor, damageDistributions);
        }
    }

    internal void ProcessRun(long runId, DeckReconstructor deckReconstructor,
        Dictionary<string, List<int>> damageDistributions)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Id, Character, Win, StartTime FROM Runs WHERE Id = @RunId",
            new { RunId = runId });

        if (run is null) return;

        // Get all combat floors for this run
        var combatFloors = _connection.Query<CombatFloorInfo>("""
            SELECT Id, ActIndex, EncounterId, DamageTaken
            FROM Floors
            WHERE RunId = @RunId AND EncounterId IS NOT NULL
            ORDER BY Id ASC
            """, new { RunId = runId }).ToList();

        if (combatFloors.Count == 0) return;

        using var transaction = _connection.BeginTransaction();
        try
        {
            // Apply inter-run decay before processing this run
            ApplyInterRunDecay(runId, transaction);

            foreach (var floor in combatFloors)
            {
                var poolContext = DerivePoolContext(floor.EncounterId, (int)floor.ActIndex);
                if (poolContext is null) continue;

                var deck = deckReconstructor.GetDeckAtFloor(runId, floor.Id);
                if (deck.Count == 0) continue;

                var score = ComputePercentileScore((int)floor.DamageTaken, poolContext, damageDistributions);
                var contexts = GetContexts(run.Character, poolContext);
                var poolEntityId = $"POOL.{poolContext}";

                foreach (var (character, context) in contexts)
                {
                    // Collect pre-update ratings for deck average calculation
                    var deckRatings = new List<(Glicko2Calculator.Glicko2Rating Rating, double Weight)>();

                    // Update each card in the deck
                    foreach (var cardId in deck)
                    {
                        var rating = GetOrCreateRating(cardId, character, context, transaction);

                        var currentRating = new Glicko2Calculator.Glicko2Rating(
                            rating.Rating, rating.RatingDeviation, rating.Volatility);

                        // Weight by 1/RD for the deck average (before update)
                        deckRatings.Add((currentRating, 1.0 / currentRating.RatingDeviation));

                        // Get pool entity as opponent for this card
                        var poolRating = GetOrCreateRating(poolEntityId, character, context, transaction);
                        var poolGlicko = new Glicko2Calculator.Glicko2Rating(
                            poolRating.Rating, poolRating.RatingDeviation, poolRating.Volatility);

                        var opponents = new (Glicko2Calculator.Glicko2Rating Rating, double Score)[]
                        {
                            (poolGlicko, score)
                        };

                        var newRating = Glicko2Calculator.UpdateRating(currentRating, opponents);
                        UpdateRating(rating, newRating, runId, floor.Id, run.StartTime, transaction);
                    }

                    // Update pool entity using 1/RD-weighted deck average as opponent
                    if (deckRatings.Count > 0)
                    {
                        var totalWeight = deckRatings.Sum(r => r.Weight);
                        var avgRating = deckRatings.Sum(r => r.Rating.Rating * r.Weight) / totalWeight;
                        var avgRd = deckRatings.Sum(r => r.Rating.RatingDeviation * r.Weight) / totalWeight;
                        var avgVol = deckRatings.Sum(r => r.Rating.Volatility * r.Weight) / totalWeight;

                        var poolRating = GetOrCreateRating(poolEntityId, character, context, transaction);
                        var currentPoolRating = new Glicko2Calculator.Glicko2Rating(
                            poolRating.Rating, poolRating.RatingDeviation, poolRating.Volatility);

                        var deckAvgOpponent = new Glicko2Calculator.Glicko2Rating(avgRating, avgRd, avgVol);
                        var poolOpponents = new (Glicko2Calculator.Glicko2Rating Rating, double Score)[]
                        {
                            (deckAvgOpponent, 1.0 - score)
                        };

                        var newPoolRating = Glicko2Calculator.UpdateRating(currentPoolRating, poolOpponents);
                        UpdateRating(poolRating, newPoolRating, runId, floor.Id, run.StartTime, transaction);
                    }
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    internal void ApplyInterRunDecay(long currentRunId, IDbTransaction transaction)
    {
        // Find all ratings that were last updated in a prior run and apply decay
        // for each run they missed
        var ratingsToDecay = _connection.Query<RatingInfo>("""
            SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId
            FROM CombatGlicko2Ratings
            WHERE LastUpdatedRunId IS NOT NULL AND LastUpdatedRunId < @CurrentRunId
            """, new { CurrentRunId = currentRunId }, transaction).ToList();

        foreach (var rating in ratingsToDecay)
        {
            var missedRuns = _connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Runs WHERE Id > @From AND Id < @To",
                new { From = rating.LastUpdatedRunId!.Value, To = currentRunId },
                transaction);

            if (missedRuns <= 0) continue;

            var current = new Glicko2Calculator.Glicko2Rating(
                rating.Rating, rating.RatingDeviation, rating.Volatility);

            for (int i = 0; i < missedRuns; i++)
                current = Glicko2Calculator.ApplyInactivityDecay(current);

            _connection.Execute("""
                UPDATE CombatGlicko2Ratings
                SET RatingDeviation = @Rd
                WHERE Id = @Id
                """,
                new { Rd = current.RatingDeviation, rating.Id },
                transaction);
        }
    }

    internal Dictionary<string, List<int>> PrecomputeDamageDistributions()
    {
        var rows = _connection.Query<DamageRow>("""
            SELECT EncounterId, ActIndex, DamageTaken
            FROM Floors
            WHERE EncounterId IS NOT NULL
            """).ToList();

        var distributions = new Dictionary<string, List<int>>();

        foreach (var row in rows)
        {
            var poolContext = DerivePoolContext(row.EncounterId, (int)row.ActIndex);
            if (poolContext is null) continue;

            if (!distributions.ContainsKey(poolContext))
                distributions[poolContext] = [];

            distributions[poolContext].Add((int)row.DamageTaken);
        }

        // Sort each distribution for binary search
        foreach (var list in distributions.Values)
            list.Sort();

        return distributions;
    }

    internal static double ComputePercentileScore(int actualDamage, string poolContext,
        Dictionary<string, List<int>> distributions)
    {
        if (!distributions.TryGetValue(poolContext, out var sorted) || sorted.Count == 0)
            return 0.5; // No data, neutral score

        // Score = fraction of historical fights where damage >= actual_damage
        // 0 damage -> ~1.0 (great), worst damage -> ~0.0 (bad)
        int countWorse = sorted.Count - LowerBound(sorted, actualDamage);
        return (double)countWorse / sorted.Count;
    }

    /// <summary>
    /// Returns index of first element >= value (like C++ lower_bound).
    /// </summary>
    private static int LowerBound(List<int> sorted, int value)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    public static string? DerivePoolContext(string encounterId, int actIndex)
    {
        var actNum = actIndex + 1;
        string? suffix = null;

        if (encounterId.EndsWith("_WEAK")) suffix = "weak";
        else if (encounterId.EndsWith("_NORMAL")) suffix = "normal";
        else if (encounterId.EndsWith("_ELITE")) suffix = "elite";
        else if (encounterId.EndsWith("_BOSS")) suffix = "boss";

        if (suffix is null) return null;

        return $"act{actNum}_{suffix}";
    }

    internal static List<(string Character, string Context)> GetContexts(string character, string poolContext)
    {
        return
        [
            ("ALL", poolContext),
            (character, poolContext),
            ("ALL", "overall"),
            (character, "overall"),
        ];
    }

    private RatingInfo GetOrCreateRating(string cardId, string character, string context, IDbTransaction transaction)
    {
        _connection.Execute(
            "INSERT OR IGNORE INTO CombatGlicko2Ratings (CardId, Character, Context) VALUES (@CardId, @Character, @Context)",
            new { CardId = cardId, Character = character, Context = context },
            transaction);

        return _connection.QueryFirst<RatingInfo>("""
            SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId
            FROM CombatGlicko2Ratings
            WHERE CardId = @CardId AND Character = @Character AND Context = @Context
            """,
            new { CardId = cardId, Character = character, Context = context },
            transaction);
    }

    private void UpdateRating(RatingInfo current, Glicko2Calculator.Glicko2Rating newRating,
        long runId, long floorId, string timestamp, IDbTransaction transaction)
    {
        _connection.Execute("""
            UPDATE CombatGlicko2Ratings
            SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                GamesPlayed = @Games, LastUpdatedRunId = @RunId
            WHERE Id = @Id
            """,
            new
            {
                Rating = newRating.Rating,
                Rd = newRating.RatingDeviation,
                Vol = newRating.Volatility,
                Games = current.GamesPlayed + 1,
                RunId = runId,
                current.Id
            },
            transaction);

        _connection.Execute("""
            INSERT INTO CombatGlicko2History
                (CombatGlicko2RatingId, RunId, FloorId, RatingBefore, RatingAfter, RdBefore, RdAfter,
                 VolatilityBefore, VolatilityAfter, Timestamp)
            VALUES (@RatingId, @RunId, @FloorId, @RatingBefore, @RatingAfter, @RdBefore, @RdAfter,
                    @VolBefore, @VolAfter, @Timestamp)
            """,
            new
            {
                RatingId = current.Id,
                RunId = runId,
                FloorId = floorId,
                RatingBefore = current.Rating,
                RatingAfter = newRating.Rating,
                RdBefore = current.RatingDeviation,
                RdAfter = newRating.RatingDeviation,
                VolBefore = current.Volatility,
                VolAfter = newRating.Volatility,
                Timestamp = timestamp
            },
            transaction);
    }

    private record RunInfo(long Id, string Character, long Win, string StartTime);
    private record CombatFloorInfo(long Id, long ActIndex, string EncounterId, long DamageTaken);
    private record DamageRow(string EncounterId, long ActIndex, long DamageTaken);

    private record RatingInfo
    {
        public long Id { get; init; }
        public double Rating { get; init; }
        public double RatingDeviation { get; init; }
        public double Volatility { get; init; }
        public int GamesPlayed { get; init; }
        public long? LastUpdatedRunId { get; init; }
    }
}
