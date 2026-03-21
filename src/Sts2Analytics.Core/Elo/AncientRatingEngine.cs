using System.Data;
using Dapper;
using static Sts2Analytics.Core.Elo.Glicko2Calculator;

namespace Sts2Analytics.Core.Elo;

public class AncientRatingEngine
{
    private readonly IDbConnection _connection;

    public AncientRatingEngine(IDbConnection connection) => _connection = connection;

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM AncientGlicko2History ah
                JOIN AncientGlicko2Ratings ar ON ah.AncientGlicko2RatingId = ar.Id
                WHERE ah.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        foreach (var runId in unprocessedRunIds)
            ProcessRun(runId);
    }

    private void ProcessRun(long runId)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Id, Character FROM Runs WHERE Id = @RunId",
            new { RunId = runId });
        if (run is null) return;

        var choices = _connection.Query<AncientChoiceRow>("""
            SELECT ac.TextKey, ac.WasChosen, f.ActIndex, f.Id as FloorId
            FROM AncientChoices ac
            JOIN Floors f ON ac.FloorId = f.Id
            WHERE f.RunId = @RunId
            ORDER BY f.Id
            """, new { RunId = runId }).ToList();

        if (choices.Count == 0) return;

        var choicesByFloor = choices.GroupBy(c => c.FloorId).ToList();

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var floorGroup in choicesByFloor)
            {
                var floorChoices = floorGroup.ToList();
                var picked = floorChoices.Where(c => (long)c.WasChosen != 0).ToList();
                var skipped = floorChoices.Where(c => (long)c.WasChosen == 0).ToList();

                if (picked.Count == 0 || skipped.Count == 0) continue;

                var actIndex = floorChoices[0].ActIndex;
                var timingContext = actIndex switch
                {
                    0 => "neow",
                    1 => "post_act1",
                    2 => "post_act2",
                    _ => $"post_act{actIndex}"
                };

                // Build matchups: picked beats skipped, skipped loses to picked
                var matchups = new Dictionary<string, List<(string Opponent, double Score)>>();

                foreach (var p in picked)
                {
                    if (!matchups.ContainsKey(p.TextKey))
                        matchups[p.TextKey] = new();
                    foreach (var s in skipped)
                        matchups[p.TextKey].Add((s.TextKey, 1.0));
                }

                foreach (var s in skipped)
                {
                    if (!matchups.ContainsKey(s.TextKey))
                        matchups[s.TextKey] = new();
                    foreach (var p in picked)
                        matchups[s.TextKey].Add((p.TextKey, 0.0));
                }

                // 3 contexts: (ALL, overall), (ALL, timing), (character, timing)
                var contexts = new[]
                {
                    ("ALL", "overall"),
                    ("ALL", timingContext),
                    (run.Character, timingContext)
                };

                foreach (var (character, context) in contexts)
                {
                    foreach (var (choiceKey, opponents) in matchups)
                    {
                        var rating = GetOrCreateRating(choiceKey, character, context, transaction);
                        var currentRating = new Glicko2Rating(rating.Rating, rating.RatingDeviation, rating.Volatility);

                        // Apply inactivity decay for runs where this choice was absent
                        if (rating.LastUpdatedRunId is not null)
                        {
                            var runsWithChoice = _connection.QueryFirstOrDefault<int>("""
                                SELECT COUNT(DISTINCT f.RunId) FROM AncientChoices ac
                                JOIN Floors f ON ac.FloorId = f.Id
                                WHERE f.RunId > @LastRunId AND f.RunId < @CurrentRunId
                                AND ac.TextKey = @ChoiceKey
                                """, new { LastRunId = rating.LastUpdatedRunId, CurrentRunId = runId,
                                    ChoiceKey = choiceKey }, transaction);
                            var totalRunsBetween = _connection.QueryFirstOrDefault<int>(
                                "SELECT COUNT(*) FROM Runs WHERE Id > @LastRunId AND Id < @CurrentRunId",
                                new { LastRunId = rating.LastUpdatedRunId, CurrentRunId = runId }, transaction);
                            var missedRuns = totalRunsBetween - runsWithChoice;
                            for (int i = 0; i < missedRuns; i++)
                                currentRating = ApplyInactivityDecay(currentRating);
                        }

                        var opponentRatings = opponents.Select(o =>
                        {
                            var oppRating = GetOrCreateRating(o.Opponent, character, context, transaction);
                            return (Rating: new Glicko2Rating(oppRating.Rating, oppRating.RatingDeviation, oppRating.Volatility),
                                    Score: o.Score);
                        }).ToArray();

                        var newRating = UpdateRating(currentRating, opponentRatings);

                        _connection.Execute("""
                            INSERT INTO AncientGlicko2History
                                (AncientGlicko2RatingId, RunId, RatingBefore, RatingAfter,
                                 RdBefore, RdAfter, VolatilityBefore, VolatilityAfter, Timestamp)
                            VALUES (@RatingId, @RunId, @RatingBefore, @RatingAfter,
                                    @RdBefore, @RdAfter, @VolBefore, @VolAfter, @Timestamp)
                            """, new {
                                RatingId = rating.Id, RunId = runId,
                                RatingBefore = currentRating.Rating, RatingAfter = newRating.Rating,
                                RdBefore = currentRating.RatingDeviation, RdAfter = newRating.RatingDeviation,
                                VolBefore = currentRating.Volatility, VolAfter = newRating.Volatility,
                                Timestamp = DateTime.UtcNow.ToString("o")
                            }, transaction);

                        _connection.Execute("""
                            UPDATE AncientGlicko2Ratings
                            SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                                GamesPlayed = GamesPlayed + 1, LastUpdatedRunId = @RunId
                            WHERE Id = @Id
                            """, new {
                                Rating = newRating.Rating, Rd = newRating.RatingDeviation,
                                Vol = newRating.Volatility, RunId = runId, Id = rating.Id
                            }, transaction);
                    }
                }
            }

            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private RatingInfo GetOrCreateRating(string choiceKey, string character, string context, IDbTransaction transaction)
    {
        var existing = _connection.QueryFirstOrDefault<RatingInfo>(
            "SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId FROM AncientGlicko2Ratings WHERE ChoiceKey = @ChoiceKey AND Character = @Character AND Context = @Context",
            new { ChoiceKey = choiceKey, Character = character, Context = context }, transaction);

        if (existing is not null) return existing;

        _connection.Execute(
            "INSERT INTO AncientGlicko2Ratings (ChoiceKey, Character, Context) VALUES (@ChoiceKey, @Character, @Context)",
            new { ChoiceKey = choiceKey, Character = character, Context = context }, transaction);

        var id = _connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);
        return new RatingInfo { Id = id, Rating = 1500.0, RatingDeviation = 350.0, Volatility = 0.06, GamesPlayed = 0 };
    }

    private record RunInfo(long Id, string Character);
    private record AncientChoiceRow(string TextKey, long WasChosen, long ActIndex, long FloorId);
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
